using WindowSnapper.Models;

namespace WindowSnapper.Services;

public readonly record struct CaptureIdentity(DateTimeOffset Timestamp, string Id, string VerificationCode)
{
    public static CaptureIdentity Create(CaptureSettings settings)
    {
        var now = DateTimeOffset.Now;
        var id = CaptureVerificationService.CreateCaptureId(now);
        var verificationCode = CaptureVerificationService.CreateVerificationCode(settings, id);
        return new CaptureIdentity(now, id, verificationCode);
    }
}
