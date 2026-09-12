using WindowSnapper.Models;

namespace WindowSnapper.Services;

public readonly record struct CaptureIntegrityResult(
    bool IsValid,
    string Message,
    string? CaptureId = null,
    DateTimeOffset? CapturedAt = null,
    string? SignaturePath = null,
    int? BitDifference = null,
    int? ChangedCells = null,
    int? LargestChangedCluster = null,
    bool ExactPixelMismatch = false);

public static class CaptureIntegrityService
{
    public static string SignaturePathFor(string imagePath) => Z7.A(imagePath);

    // Old callers still poke this. Let them. The real proof moved out ages ago.
    public static void SealFile(CaptureSettings settings, string imagePath, CaptureIdentity identity) =>
        Z7.B(settings, imagePath, identity);

    public static Task<CaptureIntegrityResult> VerifyFileAsync(
        CaptureSettings settings,
        string imagePath,
        string? captureId,
        string? verificationCode) =>
        Task.Run(() => Z7.C(settings, imagePath, captureId, verificationCode));

    public static Task<CaptureIntegrityResult> VerifyFileAsync(CaptureSettings settings, string imagePath) =>
        VerifyFileAsync(settings, imagePath, null, null);
}
