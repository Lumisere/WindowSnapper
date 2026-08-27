using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class CaptureBackendInfo
{
    public static bool RequiresWindowTarget(CaptureBackend backend)
    {
        if (PlatformInfo.IsWindows)
            return true;

        if (backend == CaptureBackend.Auto)
            return !PlatformInfo.IsWayland;

        if (PlatformInfo.IsWayland
            && backend is CaptureBackend.LinuxSwayGrim
                or CaptureBackend.LinuxHyprlandGrim
                or CaptureBackend.LinuxHyprshot
                or CaptureBackend.LinuxGrimblast)
        {
            return false;
        }

        return backend is not (CaptureBackend.WaylandPortal or CaptureBackend.LinuxGrimSlurp);
    }

    public static bool HasOptionalWindowTarget(CaptureBackend backend) =>
        PlatformInfo.IsLinux && PlatformInfo.IsWayland && backend == CaptureBackend.Auto && PlatformInfo.HasX11;
}
