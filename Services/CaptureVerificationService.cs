using WindowSnapper.Models;

namespace WindowSnapper.Services;

public readonly record struct CaptureVerificationResult(
    bool IsValid,
    string Message,
    DateTimeOffset? CapturedAt = null);

public static class CaptureVerificationService
{
    public static string CreateSecret() => X9.A();
    public static string CreateNonce(int characterCount = 8) => X9.B(characterCount);
    public static string CreateCaptureId(DateTimeOffset timestamp) => X9.C(timestamp);
    public static string CreateCaptureId(DateTimeOffset timestamp, double toastScale) => X9.C(timestamp, toastScale);
    internal static bool TryGetContentProofScale(string captureId, out double scale) => X9.Q(captureId, out scale);
    public static void EnsureSecret(CaptureSettings settings) => X9.D(settings);
    public static string CreateVerificationCode(CaptureSettings settings, string captureId) => X9.E(settings, captureId);
    public static CaptureVerificationResult Verify(CaptureSettings settings, string? captureIdText, string? verificationCodeText) =>
        X9.F(settings, captureIdText, verificationCodeText);
    public static bool TryParseTimestamp(string captureId, out DateTimeOffset timestamp) => X9.G(captureId, out timestamp);
}
