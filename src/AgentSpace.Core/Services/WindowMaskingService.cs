using System;
using System.Collections.Generic;
using AgentSpace.Core.Interop;
using AgentSpace.Core.Models;

namespace AgentSpace.Core.Services
{
    public class WindowMaskingService : IDisposable
    {
        private readonly Dictionary<IntPtr, CapturedWindowInfo> _capturedWindows = new Dictionary<IntPtr, CapturedWindowInfo>();
        
        private User32.WinEventDelegate _winEventDelegate;
        private IntPtr _hLocationChangeHook;
        private IntPtr _hMinimizeStartHook;
        private IntPtr _hMinimizeEndHook;
        private IntPtr _hForegroundHook;

        public event Action<IntPtr> NativeWindowBoundsChanged;
        public event Action<IntPtr> NativeWindowMinimized;
        public event Action<IntPtr> NativeWindowRestored;

        public WindowMaskingService()
        {
            // Keeping the delegate reference alive to prevent GC
            _winEventDelegate = new User32.WinEventDelegate(WinEventProc);
            
            _hLocationChangeHook = User32.SetWinEventHook(
                User32.EVENT_OBJECT_LOCATIONCHANGE, User32.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventDelegate, 0, 0, User32.WINEVENT_OUTOFCONTEXT);

            _hMinimizeStartHook = User32.SetWinEventHook(
                User32.EVENT_SYSTEM_MINIMIZESTART, User32.EVENT_SYSTEM_MINIMIZESTART,
                IntPtr.Zero, _winEventDelegate, 0, 0, User32.WINEVENT_OUTOFCONTEXT);

            _hMinimizeEndHook = User32.SetWinEventHook(
                User32.EVENT_SYSTEM_MINIMIZEEND, User32.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, _winEventDelegate, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
                
            _hForegroundHook = User32.SetWinEventHook(
                User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventDelegate, 0, 0, User32.WINEVENT_OUTOFCONTEXT);
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != User32.OBJID_WINDOW) return;
            
            if (_capturedWindows.ContainsKey(hwnd))
            {
                if (eventType == User32.EVENT_SYSTEM_MINIMIZESTART)
                {
                    NativeWindowMinimized?.Invoke(hwnd);
                }
                else if (eventType == User32.EVENT_SYSTEM_MINIMIZEEND)
                {
                    NativeWindowRestored?.Invoke(hwnd);
                    NativeWindowBoundsChanged?.Invoke(hwnd);
                }
                else
                {
                    NativeWindowBoundsChanged?.Invoke(hwnd);
                }
            }
        }

        public void Dispose()
        {
            if (_hLocationChangeHook != IntPtr.Zero) User32.UnhookWinEvent(_hLocationChangeHook);
            if (_hMinimizeStartHook != IntPtr.Zero) User32.UnhookWinEvent(_hMinimizeStartHook);
            if (_hMinimizeEndHook != IntPtr.Zero) User32.UnhookWinEvent(_hMinimizeEndHook);
            if (_hForegroundHook != IntPtr.Zero) User32.UnhookWinEvent(_hForegroundHook);
        }

        public IntPtr GetTopLevelWindowFromPoint(NativePoint point)
        {
            IntPtr hwnd = User32.WindowFromPhysicalPoint(point);
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;

            IntPtr topLevelHwnd = User32.GetAncestor(hwnd, User32.GA_ROOT);
            return topLevelHwnd != IntPtr.Zero ? topLevelHwnd : hwnd;
        }

        public void MaskWindow(IntPtr hwnd, NativeRect screenMaskRect)
        {
            if (hwnd == IntPtr.Zero) return;

            if (User32.GetWindowRect(hwnd, out NativeRect targetWindowRect))
            {
                // Calculate region relative to the target window
                int relativeLeft = screenMaskRect.Left - targetWindowRect.Left;
                int relativeTop = screenMaskRect.Top - targetWindowRect.Top;
                int relativeRight = screenMaskRect.Right - targetWindowRect.Left;
                int relativeBottom = screenMaskRect.Bottom - targetWindowRect.Top;

                // Get original NC policy
                int originalPolicy = 0;
                DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_NCRENDERING_POLICY, out originalPolicy, sizeof(int));

                // Disable NC rendering to remove DWM shadow and DWM Titlebar
                int policy = DwmApi.DWMNCRP_DISABLED;
                DwmApi.DwmSetWindowAttribute(hwnd, DwmApi.DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

                _capturedWindows[hwnd] = new CapturedWindowInfo
                {
                    Handle = hwnd,
                    OriginalWindowRect = targetWindowRect,
                    RelativeMaskRect = new NativeRect { Left = relativeLeft, Top = relativeTop, Right = relativeRight, Bottom = relativeBottom },
                    OriginalNcRenderingPolicy = originalPolicy
                };

                // Force window to update frame
                User32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_FRAMECHANGED | User32.SWP_NOACTIVATE);

                IntPtr hRgn = Gdi32.CreateRectRgn(relativeLeft, relativeTop, relativeRight, relativeBottom);
                // System owns the region handle after SetWindowRgn
                User32.SetWindowRgn(hwnd, hRgn, true);
            }
        }

        public void RestoreMaskedWindow(IntPtr hwnd)
        {
            if (_capturedWindows.TryGetValue(hwnd, out var capturedWin))
            {
                User32.SetWindowRgn(capturedWin.Handle, IntPtr.Zero, true);

                int policy = capturedWin.OriginalNcRenderingPolicy;
                DwmApi.DwmSetWindowAttribute(capturedWin.Handle, DwmApi.DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
                
                User32.SetWindowPos(capturedWin.Handle, IntPtr.Zero, 0, 0, 0, 0, 
                    User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_FRAMECHANGED | User32.SWP_NOACTIVATE);

                _capturedWindows.Remove(hwnd);
            }
        }

        public void RestoreAllMaskedWindows()
        {
            // Create a list to avoid modifying the collection while iterating
            var keys = new List<IntPtr>(_capturedWindows.Keys);
            foreach (var key in keys)
            {
                RestoreMaskedWindow(key);
            }
        }

        public void SyncWindowPosition(IntPtr hwnd, int newContainerLeft, int newContainerTop)
        {
            if (!_capturedWindows.TryGetValue(hwnd, out var capturedWin)) return;
            
            // We know exactly where the container is locally, but we need to move the native window 
            // so that its cropped relative region matches up perfectly with the new container x/y.

            int newNativeLeft = newContainerLeft - capturedWin.RelativeMaskRect.Left;
            int newNativeTop = newContainerTop - capturedWin.RelativeMaskRect.Top;

            User32.SetWindowPos(hwnd, IntPtr.Zero, newNativeLeft, newNativeTop, 0, 0, 
                User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }

        public void ClearMaskRegion(IntPtr hwnd)
        {
            if (_capturedWindows.ContainsKey(hwnd))
            {
                User32.SetWindowRgn(hwnd, IntPtr.Zero, true);
            }
        }

        public NativeRect? GetCurrentVisibleRegion(IntPtr hwnd)
        {
            if (_capturedWindows.TryGetValue(hwnd, out var capturedWin))
            {
                // The RelativeMaskRect is in screen coordinates, updated every drag/resize.
                return capturedWin.RelativeMaskRect;
            }
            return null;
        }

        public void UpdateMaskRegion(IntPtr hwnd, NativeRect screenMaskRect, bool forceUpdate = false)
        {
            if (!_capturedWindows.TryGetValue(hwnd, out var capturedWin)) return;

            if (User32.GetWindowRect(hwnd, out NativeRect targetWindowRect))
            {
                int relativeLeft = screenMaskRect.Left - targetWindowRect.Left;
                int relativeTop = screenMaskRect.Top - targetWindowRect.Top;
                int relativeRight = screenMaskRect.Right - targetWindowRect.Left;
                int relativeBottom = screenMaskRect.Bottom - targetWindowRect.Top;

                // If native window is minimized, ignore region updates entirely
                if (User32.IsIconic(hwnd))
                {
                    return;
                }

                bool sizeChanged = capturedWin.LastKnownTargetRect.Right - capturedWin.LastKnownTargetRect.Left != targetWindowRect.Right - targetWindowRect.Left ||
                                   capturedWin.LastKnownTargetRect.Bottom - capturedWin.LastKnownTargetRect.Top != targetWindowRect.Bottom - targetWindowRect.Top;

                // Stop infinite EVENT_OBJECT_LOCATIONCHANGE loops by ignoring effectively identical crops
                // If the Container WPF moved the native window relative to the container, it's identical
                if (!forceUpdate && !sizeChanged && 
                    capturedWin.RelativeMaskRect.Left == relativeLeft &&
                    capturedWin.RelativeMaskRect.Top == relativeTop &&
                    capturedWin.RelativeMaskRect.Right == relativeRight &&
                    capturedWin.RelativeMaskRect.Bottom == relativeBottom)
                {
                    return;
                }

                capturedWin.LastKnownTargetRect = targetWindowRect;

                // Update the tracked relative rect so dragging still works perfectly
                capturedWin.RelativeMaskRect = new NativeRect 
                { 
                    Left = relativeLeft, 
                    Top = relativeTop, 
                    Right = relativeRight, 
                    Bottom = relativeBottom 
                };

                IntPtr hRgn = Gdi32.CreateRectRgn(relativeLeft, relativeTop, relativeRight, relativeBottom);
                User32.SetWindowRgn(hwnd, hRgn, true);
            }
        }

        public void ScaleNativeWindow(IntPtr hwnd, double scaleMultiplier)
        {
            if (!_capturedWindows.TryGetValue(hwnd, out var capturedWin)) return;

            if (User32.GetWindowRect(hwnd, out NativeRect targetWindowRect))
            {
                int newWidth = (int)(targetWindowRect.Width * scaleMultiplier);
                int newHeight = (int)(targetWindowRect.Height * scaleMultiplier);

                User32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, newWidth, newHeight, 
                    User32.SWP_NOMOVE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
            }
        }
    }
}
