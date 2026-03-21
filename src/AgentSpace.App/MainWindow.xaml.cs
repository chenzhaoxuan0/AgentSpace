using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using AgentSpace.Core.Services;
using AgentSpace.App.Views;
using AgentSpace.Core.Models;
using AgentSpace.Core.Interop;
using System.Threading.Tasks;

namespace AgentSpace.App
{
    public partial class MainWindow : Window
    {
        private HotkeyHookService _hotkeyService;
        private WindowMaskingService _maskingService;
        private SelectionOverlayWindow _overlayWindow;

        public MainWindow()
        {
            InitializeComponent();
            _maskingService = new WindowMaskingService();
            _overlayWindow = new SelectionOverlayWindow();
            _overlayWindow.SelectionCompleted += OnSelectionCompleted;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _hotkeyService = new HotkeyHookService(hwnd);
            _hotkeyService.SelectionHotkeyPressed += OnHotkeyPressed;
            _hotkeyService.RouteHotkeyPressed += OnRouteHotkeyPressed;
            _hotkeyService.ArrangeHotkeyPressed += OnArrangeHotkeyPressed;
            _hotkeyService.Register();

            HwndSource source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            _hotkeyService?.ProcessMessage(msg, wParam);
            return IntPtr.Zero;
        }

        private void OnHotkeyPressed(object sender, EventArgs e)
        {
            _overlayWindow.Show();
            _overlayWindow.Activate();
        }

        private void OnRouteHotkeyPressed(object sender, EventArgs e)
        {
            // Find which container window holds the currently focused native window
            IntPtr foregroundHwnd = User32.GetForegroundWindow();
            
            ContainerWindow sourceContainer = null;
            foreach (Window window in Application.Current.Windows)
            {
                if (window is ContainerWindow cw && cw.TargetHwnd == foregroundHwnd)
                {
                    sourceContainer = cw;
                    break;
                }
            }

            if (sourceContainer != null)
            {
                sourceContainer.TriggerRouteIntent();
            }
        }

        private void OnArrangeHotkeyPressed(object sender, EventArgs e)
        {
            ArrangeAllWindows();
        }

        private void ArrangeAll_Click(object sender, RoutedEventArgs e)
        {
            ArrangeAllWindows();
        }

        private void ArrangeAllWindows()
        {
            // Collect all container windows
            var containers = new List<(IntPtr Hwnd, double DipLeft, double DipTop, double DipWidth, double DipHeight)>();
            foreach (Window window in Application.Current.Windows)
            {
                if (window is ContainerWindow cw)
                {
                    containers.Add((cw.TargetHwnd, cw.Left, cw.Top, cw.Width, cw.Height));
                }
            }

            if (containers.Count == 0)
                return;

            // Get screen dimensions in physical pixels
            int screenW = (int)(SystemParameters.PrimaryScreenWidth * GetDpiScaleX());
            int screenH = (int)(SystemParameters.PrimaryScreenHeight * GetDpiScaleY());
            int gap = AppSettings.ArrangeGap;
            double dpiScaleX = GetDpiScaleX();
            double dpiScaleY = GetDpiScaleY();

            ArrangeService.ArrangeAll(
                containers,
                screenW,
                screenH,
                gap,
                dpiScaleX,
                dpiScaleY,
                (hwnd, dipX, dipY, dipW, dipH) =>
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is ContainerWindow cw && cw.TargetHwnd == hwnd)
                        {
                            cw.Left = dipX;
                            cw.Top = dipY;
                            cw.Width = dipW;
                            cw.Height = dipH;
                            break;
                        }
                    }
                });
        }

        private double GetDpiScaleX()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
        }

        private double GetDpiScaleY()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source != null ? source.CompositionTarget.TransformToDevice.M22 : 1.0;
        }

        private async void OnSelectionCompleted(NativeRect selectionRect)
        {
            int centerX = selectionRect.Left + (selectionRect.Right - selectionRect.Left) / 2;
            int centerY = selectionRect.Top + (selectionRect.Bottom - selectionRect.Top) / 2;
            NativePoint targetPoint = new NativePoint(centerX, centerY);
            
            IntPtr mainHwnd = new WindowInteropHelper(this).Handle;
            IntPtr overlayHwnd = new WindowInteropHelper(_overlayWindow).Handle;
            IntPtr targetHwnd = IntPtr.Zero;

            // Robust polling: Yield up to 10 times to allow the overlay window to completely hide 
            // before we probe HWNDs. On the very first run, WPF takes longer to detach the visuals.
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(50);
                targetHwnd = _maskingService.GetTopLevelWindowFromPoint(targetPoint);
                
                // If we found a valid window that is NEITHER our MainWindow NOR our fading OverlayWindow, we are good.
                if (targetHwnd != IntPtr.Zero && targetHwnd != mainHwnd && targetHwnd != overlayHwnd)
                {
                    break;
                }
            }
            
            if (targetHwnd != IntPtr.Zero && targetHwnd != mainHwnd && targetHwnd != overlayHwnd)
            {
                _maskingService.MaskWindow(targetHwnd, selectionRect);

                // Convert Physical Pixels (from Win32) to WPF Device Independent Pixels (DIPs) for High-DPI screens
                PresentationSource source = PresentationSource.FromVisual(this);
                double scaleX = source != null ? source.CompositionTarget.TransformFromDevice.M11 : 1.0;
                double scaleY = source != null ? source.CompositionTarget.TransformFromDevice.M22 : 1.0;

                Rect wpfRect = new Rect(selectionRect.Left * scaleX, 
                                      selectionRect.Top * scaleY, 
                                      (selectionRect.Right - selectionRect.Left) * scaleX, 
                                      (selectionRect.Bottom - selectionRect.Top) * scaleY);
                                      
                var newContainer = new ContainerWindow(targetHwnd, _maskingService, wpfRect);
                newContainer.Show();
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                RestoreAll_Click(this, null);
            }
        }

        private void RestoreAll_Click(object sender, RoutedEventArgs e)
        {
            var windowsToClose = new System.Collections.Generic.List<ContainerWindow>();
            foreach (Window window in Application.Current.Windows)
            {
                if (window is ContainerWindow cw)
                {
                    windowsToClose.Add(cw);
                }
            }
            foreach (var cw in windowsToClose)
            {
                cw.Close();
            }
            
            _maskingService?.RestoreAllMaskedWindows();
        }

        private void UnmaskOnResize_Checked(object sender, RoutedEventArgs e)
        {
            AppSettings.ShowFullWindowDuringResize = true;
        }

        private void UnmaskOnResize_Unchecked(object sender, RoutedEventArgs e)
        {
            AppSettings.ShowFullWindowDuringResize = false;
        }

        private void RouteHotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Ignore modifiers only presses
            if (e.Key == System.Windows.Input.Key.LeftCtrl || e.Key == System.Windows.Input.Key.RightCtrl ||
                e.Key == System.Windows.Input.Key.LeftShift || e.Key == System.Windows.Input.Key.RightShift ||
                e.Key == System.Windows.Input.Key.LeftAlt || e.Key == System.Windows.Input.Key.RightAlt ||
                e.Key == System.Windows.Input.Key.LWin || e.Key == System.Windows.Input.Key.RWin)
            {
                return;
            }

            e.Handled = true;

            // Extract modifiers
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            uint win32Modifiers = 0;
            string modifierText = "";

            if ((modifiers & System.Windows.Input.ModifierKeys.Alt) != 0)
            {
                win32Modifiers |= 0x0001; // MOD_ALT
                modifierText += "Alt + ";
            }
            if ((modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                win32Modifiers |= 0x0002; // MOD_CONTROL
                modifierText += "Ctrl + ";
            }
            if ((modifiers & System.Windows.Input.ModifierKeys.Shift) != 0)
            {
                win32Modifiers |= 0x0004; // MOD_SHIFT
                modifierText += "Shift + ";
            }

            // Extract key
            var key = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
            uint win32Key = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);

            // Update UI
            RouteHotkeyTextBox.Text = $"{modifierText}{key}";

            // Update Settings
            AppSettings.RouteHotkeyModifiers = win32Modifiers;
            AppSettings.RouteHotkeyKey = win32Key;

            // Re-apply binding
            _hotkeyService?.ReRegisterRouteHotkey();

            // Remove focus
            System.Windows.Input.Keyboard.ClearFocus();
            this.Focus();
        }

        private void ArrangeGapTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Only allow digits and backspace
            if (!char.IsDigit((char)e.Key) &&
                e.Key != System.Windows.Input.Key.Back &&
                e.Key != System.Windows.Input.Key.Delete &&
                e.Key != System.Windows.Input.Key.Left &&
                e.Key != System.Windows.Input.Key.Right &&
                e.Key != System.Windows.Input.Key.Home &&
                e.Key != System.Windows.Input.Key.End &&
                e.Key != System.Windows.Input.Key.Tab)
            {
                e.Handled = true;
                return;
            }
        }

        private void ArrangeGapTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(ArrangeGapTextBox.Text, out int gap))
            {
                AppSettings.ArrangeGap = Math.Max(0, Math.Min(gap, 200)); // clamp to 0-200
                ArrangeGapTextBox.Text = AppSettings.ArrangeGap.ToString();
            }
            else
            {
                ArrangeGapTextBox.Text = AppSettings.ArrangeGap.ToString();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // First, close all ContainerWindows explicitly so they disappear
            RestoreAll_Click(this, null);

            _maskingService?.RestoreAllMaskedWindows();
            _maskingService?.Dispose();
            _hotkeyService?.Dispose();
            _overlayWindow?.Close();
            base.OnClosed(e);
        }
    }
}
