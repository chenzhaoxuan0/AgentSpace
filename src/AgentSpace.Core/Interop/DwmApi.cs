using System;
using System.Runtime.InteropServices;

namespace AgentSpace.Core.Interop
{
    public static class DwmApi
    {
        public const int DWMWA_NCRENDERING_POLICY = 2;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;
        public const int DWMNCRP_USEWINDOWSTYLE = 0;
        public const int DWMNCRP_DISABLED = 1;
        public const int DWMNCRP_ENABLED = 2;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out AgentSpace.Core.Models.NativeRect attrValue, int attrSize);
    }
}
