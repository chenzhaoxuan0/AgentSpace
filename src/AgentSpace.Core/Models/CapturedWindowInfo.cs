using System;

namespace AgentSpace.Core.Models
{
    public class CapturedWindowInfo
    {
        public IntPtr Handle { get; set; }
        public NativeRect OriginalWindowRect { get; set; }
        public NativeRect RelativeMaskRect { get; set; }
        public NativeRect LastKnownTargetRect { get; set; }
        public int OriginalNcRenderingPolicy { get; set; }
    }
}
