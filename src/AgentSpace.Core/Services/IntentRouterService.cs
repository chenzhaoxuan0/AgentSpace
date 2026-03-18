using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Automation;
using AgentSpace.Core.Interop;
using AgentSpace.Core.Models;
using System.Text;

namespace AgentSpace.Core.Services
{
    public class IntentRouterService
    {
        public IntentRouterService()
        {
        }

        public class TargetWindowInfo
        {
            public IntPtr Hwnd { get; set; }
            public string Title { get; set; }
            public NativeRect Bounds { get; set; }
        }

        public List<TargetWindowInfo> GetAvailableTargetWindows(IntPtr sourceHwnd)
        {
            var targets = new List<TargetWindowInfo>();
            
            User32.EnumWindows((hwnd, lParam) =>
            {
                if (hwnd == sourceHwnd) return true;
                
                if (User32.IsWindowVisible(hwnd))
                {
                    int exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
                    bool isToolWindow = (exStyle & User32.WS_EX_TOOLWINDOW) != 0;
                    bool isAppWindow = (exStyle & User32.WS_EX_APPWINDOW) != 0;
                    
                    IntPtr owner = User32.GetWindow(hwnd, User32.GW_OWNER);

                    // Typical Alt-Tab logic
                    if (isToolWindow && !isAppWindow) return true;
                    if (owner != IntPtr.Zero && !isAppWindow) return true;

                    // Check if it's a root window
                    IntPtr root = User32.GetAncestor(hwnd, User32.GA_ROOT);
                    if (root != hwnd) return true;

                    // Windows 10/11 UWP cloaking check (filters out suspended background apps)
                    if (DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    {
                        return true;
                    }

                    // Exclude windows with no title (often invisible background apps)
                    var sb = new StringBuilder(256);
                    User32.GetWindowText(hwnd, sb, sb.Capacity);
                    string title = sb.ToString().Trim();
                    
                    if (string.IsNullOrEmpty(title)) return true;
                    
                    // Exclude specific system windows
                    if (title == "Program Manager" || 
                        title == "AgentSpace Controller" || 
                        title.Contains("Overwolf") || 
                        title == "Settings") return true;

                    if (User32.GetWindowRect(hwnd, out NativeRect rect))
                    {
                        // Try DWM extended frame bounds for Win10/11 drop shadow exclusion
                        if (DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeRect dwmRect, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeRect))) == 0)
                        {
                            rect = dwmRect;
                        }

                        // Exclude tiny hidden windows / offscreen windows
                        if (rect.Right - rect.Left > 10 && rect.Bottom - rect.Top > 10)
                        {
                            targets.Add(new TargetWindowInfo
                            {
                                Hwnd = hwnd,
                                Title = title,
                                Bounds = rect
                            });
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return targets;
        }

        public async Task<bool> RouteIntentAsync(IntPtr sourceHwnd, IntPtr targetHwnd)
        {
            try
            {
                // 1. Focus the source window
                if (!FocusWindow(sourceHwnd)) return false;

                // Wait a tiny bit for the OS to actually switch focus
                await Task.Delay(100);

                // 2. Clear clipboard, then send Ctrl+C
                System.Windows.Clipboard.Clear();
                SendCtrlC();

                // 3. Wait for clipboard to populate (with a timeout fallback)
                string copiedText = await WaitForClipboardTextAsync(TimeSpan.FromSeconds(2));
                if (string.IsNullOrEmpty(copiedText))
                {
                    return false;
                }

                // 4. Focus the target window
                if (!FocusWindow(targetHwnd)) return false;

                // Wait for focus shift to target
                await Task.Delay(200);

                // Try to find the inner focus element using UIA to ensure a textbox is active
                EnsureInnerFocusReady(targetHwnd);

                // 5. Send Ctrl+V
                SendCtrlV();

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureInnerFocusReady(IntPtr targetHwnd)
        {
            try
            {
                var targetRoot = AutomationElement.FromHandle(targetHwnd);
                if (targetRoot == null) return;

                // 1. Check if the currently focused element is ALREADY a valid text input inside our target window.
                var focusedElement = AutomationElement.FocusedElement;
                if (focusedElement != null)
                {
                    if (IsDescendant(targetRoot, focusedElement) && IsTextInput(focusedElement))
                    {
                        // It's already correctly focused inside a text box.
                        return;
                    }
                }

                // 2. We need to find the first available text input and force focus it!
                var condition = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)
                );
                
                var firstInput = targetRoot.FindFirst(TreeScope.Descendants, condition);
                if (firstInput != null)
                {
                    firstInput.SetFocus();
                }
            }
            catch { /* Best effort, ignore UIACOM errors */ }
        }

        private bool IsDescendant(AutomationElement root, AutomationElement element)
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = element;
            while (current != null)
            {
                if (current == root) return true;
                current = walker.GetParent(current);
            }
            return false;
        }

        private bool IsTextInput(AutomationElement element)
        {
            return element.Current.ControlType == ControlType.Edit || 
                   element.Current.ControlType == ControlType.Document;
        }

        private bool FocusWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            
            // If it's iconic (minimized), we technically should restore it first, 
            // but for AgentSpace containers the target is usually visible on screen.
            return User32.SetForegroundWindow(hwnd);
        }

        private void SendCtrlC()
        {
            User32.keybd_event(User32.VK_CONTROL, 0, 0, UIntPtr.Zero);
            User32.keybd_event(User32.VK_C, 0, 0, UIntPtr.Zero);
            User32.keybd_event(User32.VK_C, 0, User32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            User32.keybd_event(User32.VK_CONTROL, 0, User32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void SendCtrlV()
        {
            User32.keybd_event(User32.VK_CONTROL, 0, 0, UIntPtr.Zero);
            User32.keybd_event(User32.VK_V, 0, 0, UIntPtr.Zero);
            User32.keybd_event(User32.VK_V, 0, User32.KEYEVENTF_KEYUP, UIntPtr.Zero);
            User32.keybd_event(User32.VK_CONTROL, 0, User32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private async Task<string> WaitForClipboardTextAsync(TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                string text = null;
                // Clipboard access must be on STA thread, but since we are calling from UI thread 
                // typically, we might need to dispatch it. We will try a direct catch first.
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        text = System.Windows.Clipboard.GetText();
                    }
                }
                catch (System.Runtime.InteropServices.COMException) 
                { 
                    // Clipboard is locked by another process, ignore and retry 
                }

                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }

                await Task.Delay(50);
            }
            return null;
        }
    }
}
