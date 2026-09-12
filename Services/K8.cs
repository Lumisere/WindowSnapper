using System.Security.Cryptography;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

internal static class K8
{
    private static readonly object A0 = new();
    private static byte[]? A1;
    private static bool A2;

    internal static byte[] A(CaptureSettings s)
    {
        lock (A0)
        {
            if (A1 is { Length: 32 })
            {
                s.CaptureVerificationSecret = A2 ? string.Empty : Convert.ToHexString(A1);
                return A1.ToArray();
            }

            var p = B();
            byte[]? k = null;
            var persisted = false;
            try
            {
                if (File.Exists(p))
                {
                    var b = File.ReadAllBytes(p);
                    if (b.Length == 32)
                    {
                        k = b;
                        persisted = true;
                    }
                }
            }
            catch { }

            if (k is null && C(s.CaptureVerificationSecret, out var old))
                k = old;
            k ??= RandomNumberGenerator.GetBytes(32);

            if (!persisted)
            {
                try
                {
                    var d = Path.GetDirectoryName(p);
                    if (!string.IsNullOrWhiteSpace(d)) Directory.CreateDirectory(d);
                    var t = p + ".tmp";
                    using (var f = new FileStream(t, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        f.Write(k, 0, k.Length);
                        f.Flush(true);
                    }
                    File.Move(t, p, true);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(p, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    persisted = true;
                }
                catch { }
            }

            A2 = persisted;
            s.CaptureVerificationSecret = persisted ? string.Empty : Convert.ToHexString(k);
            A1 = k.ToArray();
            return k.ToArray();
        }
    }

    private static string B()
    {
        var r = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(r)) r = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(r, "WindowSnapper", ".state2");
    }

    private static bool C(string? x, out byte[] b)
    {
        b = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(x) || x.Length != 64) return false;
        try
        {
            b = Convert.FromHexString(x);
            return b.Length == 32;
        }
        catch
        {
            b = Array.Empty<byte>();
            return false;
        }
    }
}
