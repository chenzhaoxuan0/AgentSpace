namespace AgentSpace.Core.Models
{
    public static class AppSettings
    {
        public static bool ShowFullWindowDuringResize { get; set; } = true;
        
        // Defaults to Ctrl + Shift + V
        public static uint RouteHotkeyModifiers { get; set; } = 0x0002 | 0x0004; // MOD_CONTROL | MOD_SHIFT
        public static uint RouteHotkeyKey { get; set; } = 0x56; // VK_V
    }
}
