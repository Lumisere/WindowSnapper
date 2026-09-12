using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

internal static class X9
{
    private const int A0 = 32;
    private const int A1 = 10;
    private const int A2 = 15;
    private static readonly string A3 = new(new[]
    {
        (char)65,(char)66,(char)67,(char)68,(char)69,(char)70,(char)71,(char)72,
        (char)74,(char)75,(char)76,(char)77,(char)78,(char)80,(char)81,(char)82,
        (char)83,(char)84,(char)85,(char)86,(char)87,(char)88,(char)89,(char)90,
        (char)50,(char)51,(char)52,(char)53,(char)54,(char)55,(char)56,(char)57
    });

    internal static string A() => Convert.ToHexString(RandomNumberGenerator.GetBytes(A0));

    internal static string B(int n = 8) => N(RandomNumberGenerator.GetBytes(Math.Max(5, n)), n);

    internal static string C(DateTimeOffset t)
    {
        var n = checked((ulong)t.ToUniversalTime().ToUnixTimeSeconds());
        var x = M(n, 7) + B(3);
        return $"{x[..5]}-{x[5..]}";
    }

    internal static string C(DateTimeOffset t, double toastScale)
    {
        var n = checked((ulong)t.ToUniversalTime().ToUnixTimeSeconds());
        var q = Math.Clamp(double.IsFinite(toastScale) && toastScale > 0 ? toastScale : 1.0, 0.8, 2.0);
        var si = Math.Clamp((int)Math.Round((q - 0.8) / 0.05), 0, 24);
        var x = M(n, 7) + A3[si] + B(2);
        return $"{x[..5]}-{x[5..]}";
    }

    internal static bool Q(string id, out double scale)
    {
        scale = 1.0;
        var r = new string(I(id).Where(char.IsLetterOrDigit).ToArray());
        if (r.Length != 10) return false;
        var si = A3.IndexOf(r[7]);
        if (si is < 0 or > 24) return false;
        scale = 0.8 + si * 0.05;
        return true;
    }

    internal static void D(CaptureSettings s) => _ = K8.A(s);

    internal static byte[] H(CaptureSettings s) => K8.A(s);

    internal static string E(CaptureSettings s, string id)
    {
        var k = H(s);
        var c = I(id);
        using var h = new HMACSHA256(k);
        var d = h.ComputeHash(Encoding.UTF8.GetBytes(c));
        if (c.StartsWith("WS1-", StringComparison.Ordinal))
        {
            var z = N(d, A2);
            return $"{z[..5]}-{z[5..10]}-{z[10..15]}";
        }

        var y = N(d, A1);
        return $"{y[..5]}-{y[5..10]}";
    }

    internal static CaptureVerificationResult F(CaptureSettings s, string? a, string? b)
    {
        var i = I(a);
        var q = J(b);
        if (string.IsNullOrWhiteSpace(i))
            return new(false, P(2));
        if (string.IsNullOrWhiteSpace(q))
            return new(false, P(3));
        if (q.Length == 26)
            return new(false, "This VERIFY code is bound to the screenshot content. Choose the image in Settings and verify the ID, code, and image together.");
        if (!G(i, out var t))
            return new(false, P(4));

        var e = Encoding.ASCII.GetBytes(J(E(s, i)));
        var u = Encoding.ASCII.GetBytes(q);
        var ok = e.Length == u.Length && CryptographicOperations.FixedTimeEquals(e, u);
        return ok ? new(true, P(5), t) : new(false, P(6));
    }

    internal static bool G(string id, out DateTimeOffset t)
    {
        t = default;
        var x = I(id);
        if (x.StartsWith("WS1-", StringComparison.OrdinalIgnoreCase))
        {
            if (x.Length < 31) return false;
            var p = x.IndexOf('-', 4);
            if (p < 0) return false;
            return DateTimeOffset.TryParseExact(x[4..p], "yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t);
        }

        var r = new string(x.Where(char.IsLetterOrDigit).ToArray());
        if (r.Length != 10 || !r.All(c => A3.Contains(c)) || !O(r.AsSpan(0, 7), out var n))
            return false;
        try
        {
            t = DateTimeOffset.FromUnixTimeSeconds(checked((long)n));
            return t >= new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                && t < new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
        catch
        {
            t = default;
            return false;
        }
    }

    internal static string I(string? v)
    {
        var x = (v ?? string.Empty).Trim();
        if (x.StartsWith("ID ", StringComparison.OrdinalIgnoreCase)) x = x[3..].Trim();
        x = x.ToUpperInvariant();
        if (x.StartsWith("WS1-", StringComparison.Ordinal)) return x;
        var r = new string(x.Where(char.IsLetterOrDigit).ToArray());
        return r.Length == 10 && r.All(c => A3.Contains(c)) ? $"{r[..5]}-{r[5..10]}" : x;
    }

    private static string J(string? v)
    {
        var x = (v ?? string.Empty).Trim().ToUpperInvariant();
        if (x.StartsWith("VERIFY ", StringComparison.OrdinalIgnoreCase)) x = x[7..].Trim();
        return new string(x.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string M(ulong v, int n)
    {
        var c = new char[n];
        for (var i = n - 1; i >= 0; i--)
        {
            c[i] = A3[(int)(v & 31)];
            v >>= 5;
        }
        if (v != 0) throw new ArgumentOutOfRangeException(nameof(n));
        return new string(c);
    }

    private static string N(ReadOnlySpan<byte> d, int n)
    {
        var o = new StringBuilder(n);
        var b = 0;
        var c = 0;
        foreach (var z in d)
        {
            b = (b << 8) | z;
            c += 8;
            while (c >= 5 && o.Length < n)
            {
                c -= 5;
                o.Append(A3[(b >> c) & 31]);
            }
            if (o.Length == n) break;
        }
        if (o.Length < n && c > 0) o.Append(A3[(b << (5 - c)) & 31]);
        while (o.Length < n) o.Append('A');
        return o.ToString();
    }

    private static bool O(ReadOnlySpan<char> s, out ulong v)
    {
        v = 0;
        foreach (var c in s)
        {
            var i = A3.IndexOf(char.ToUpperInvariant(c));
            if (i < 0) { v = 0; return false; }
            v = (v << 5) | (uint)i;
        }
        return true;
    }

    private static string P(int n) => n switch
    {
        1 => "Could not initialize the watermark verification key.",
        2 => "Enter the capture ID shown in the watermark.",
        3 => "Enter the VERIFY code shown in the watermark.",
        4 => "That capture ID is not a valid WindowSnapper ID.",
        5 => "Verified: this ID was issued by this WindowSnapper installation.",
        _ => "Not verified: the ID/code pair does not match this WindowSnapper installation."
    };
}
