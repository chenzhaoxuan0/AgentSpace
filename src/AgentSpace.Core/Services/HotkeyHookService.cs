using System;
using System.Runtime.InteropServices;
using AgentSpace.Core.Interop;
using AgentSpace.Core.Models;

namespace AgentSpace.Core.Services
{
    public class HotkeyHookService : IDisposable
    {
        private readonly IntPtr _hwnd;
        private const int SELECTION_HOTKEY_ID = 9000;
        private const int ROUTE_HOTKEY_ID = 9001;

        // Modifiers: MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_SPACE = 0x20; // Space key

        public event EventHandler SelectionHotkeyPressed;
        public event EventHandler RouteHotkeyPressed;

        public HotkeyHookService(IntPtr windowHandle)
        {
            _hwnd = windowHandle;
        }

        public void Register()
        {
            bool selSuccess = User32.RegisterHotKey(_hwnd, SELECTION_HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_SPACE);
            if (!selSuccess)
            {
                Console.WriteLine("Failed to register hotkey Ctrl+Shift+Space");
            }

            bool routeSuccess = User32.RegisterHotKey(_hwnd, ROUTE_HOTKEY_ID, AppSettings.RouteHotkeyModifiers, AppSettings.RouteHotkeyKey);
            if (!routeSuccess)
            {
                Console.WriteLine("Failed to register Route hotkey");
            }
        }

        public void ReRegisterRouteHotkey()
        {
            User32.UnregisterHotKey(_hwnd, ROUTE_HOTKEY_ID);
            bool routeSuccess = User32.RegisterHotKey(_hwnd, ROUTE_HOTKEY_ID, AppSettings.RouteHotkeyModifiers, AppSettings.RouteHotkeyKey);
            if (!routeSuccess)
            {
                Console.WriteLine("Failed to register Route hotkey");
            }
        }

        public void Unregister()
        {
            User32.UnregisterHotKey(_hwnd, SELECTION_HOTKEY_ID);
            User32.UnregisterHotKey(_hwnd, ROUTE_HOTKEY_ID);
        }

        public void ProcessMessage(int msg, IntPtr wParam)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == SELECTION_HOTKEY_ID)
                {
                    SelectionHotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
                else if (id == ROUTE_HOTKEY_ID)
                {
                    RouteHotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void Dispose()
        {
            Unregister();
        }
    }
}
