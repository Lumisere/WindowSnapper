using WindowSnapper.Models;
#if WINDOWS
using WindowSnapper.Platforms.Windows;
#else
using WindowSnapper.Platforms.Linux;
#endif

namespace WindowSnapper.Services;

public sealed record CaptureResult(bool Success, string Message, string? FilePath = null);

public static class CaptureService
{
    public static void AbortPlatformSession()
    {
#if !WINDOWS
        WaylandScreenCastSession.Abort();
#endif
    }

    public static Task StopPlatformSessionAsync()
    {
#if WINDOWS
        return Task.CompletedTask;
#else
        return WaylandScreenCastSession.StopAsync();
#endif
    }

    public static async Task<CaptureResult> CaptureAsync(CaptureSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            return new CaptureResult(false, "Choose an output folder");

        try
        {
#if WINDOWS
            var target = WindowFinder.Resolve(settings.TargetMode, settings.TargetValue);
            if (target is null || target.Handle == 0)
                return new CaptureResult(false, "Target window not found");

            return await WindowsCaptureService.CaptureAsync(settings, target.Handle);
#else
            WindowInfo? target = null;
            if (!string.IsNullOrWhiteSpace(settings.TargetNativeId))
                target = WindowFinder.ResolveNativeId(settings.TargetNativeId);
            if (target is null && !string.IsNullOrWhiteSpace(settings.TargetValue))
                target = WindowFinder.Resolve(settings.TargetMode, settings.TargetValue);

            if (settings.Backend == CaptureBackend.Auto && PlatformInfo.IsWayland)
                return await LinuxCaptureService.CaptureAsync(settings, target);

            if (!CaptureBackendInfo.RequiresWindowTarget(settings.Backend))
                return await LinuxCaptureService.CaptureAsync(settings, null);

            if (!IsUsableLinuxTarget(target))
                return new CaptureResult(false, "Target window not found");

            return await LinuxCaptureService.CaptureAsync(settings, target);
#endif
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

#if !WINDOWS
    private static bool IsUsableLinuxTarget(WindowInfo? target) =>
        target is not null && (target.Handle != 0 || target.HasNativeId);
#endif
}
