using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using ImageMagick.Drawing;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

internal static class Z7
{
    private const int E0 = 10;
    private const int R0 = 10;
    private const int A6 = 10;
    private const int A7 = 4;

    private const string K0 = "WSVC";
    private const string K1 = "WSVB";
    private const string K2 = "WSV9";
    private const string K3 = "WSW3";
    private const string K4 = "WSW4";
    private const string K5 = "WSW5";

    private readonly record struct L(
        string A,
        int B,
        int C,
        int D,
        int E,
        int F,
        int G,
        int H,
        int I,
        int J,
        bool K,
        bool M,
        bool N)
    {
        internal int O => E0 + E + R0;
        internal int P => A.Length + A6 + O + A7;
    }

    // New hotness. The numbers are weird on purpose; “cleaning them up” is how archaeology starts.
    private static readonly L B5 = new(K5, 24, 16, 192, 308, 4, 109, 16, 32, 16, true, true, false);
    private static readonly L B4 = new(K4, 24, 16, 144, 231, 3, 115, 12, 30, 10, true, false, false);
    private static readonly L B3 = new(K3, 24, 16, 96, 154, 2, 115, 9, 24, 8, true, false, false);
    private static readonly L B0 = new(K0, 12, 8, 48, 77, 4, 115, 5, 2, 4, true, false, true);
    private static readonly L B1 = new(K1, 12, 8, 48, 77, 4, 115, 5, 8, 4, false, false, true);
    private static readonly L B2 = new(K2, 12, 8, 48, 77, 4, 115, 5, 8, 4, false, false, true);
    private static readonly L[] B6 = { B5, B4, B3, B0, B1, B2 };

    private static readonly string A0 = new(new[]
    {
        (char)65,(char)66,(char)67,(char)68,(char)69,(char)70,(char)71,(char)72,
        (char)74,(char)75,(char)76,(char)77,(char)78,(char)80,(char)81,(char)82,
        (char)83,(char)84,(char)85,(char)86,(char)87,(char)88,(char)89,(char)90,
        (char)50,(char)51,(char)52,(char)53,(char)54,(char)55,(char)56,(char)57
    });

    private readonly record struct Q(byte[] X, byte[] R);

    internal static string A(string p) => p;
    internal static void B(CaptureSettings s, string p, CaptureIdentity i) { }

    internal static string D(CaptureSettings s, MagickImage image, string id, V5 box) =>
        I(s, id, H(image, box, B5.B, B5.C, B5.F, true, true), B5.E);

    internal static string D(CaptureSettings s, MagickImage exactImage, MagickImage descriptorReference, string id, V5 box)
    {
        var exact = H(exactImage, box, B5.B, B5.C, B5.F, true, true).X;
        var robust = H(descriptorReference, box, B5.B, B5.C, B5.F, true, true).R;
        return I(s, id, new Q(exact, robust), B5.E);
    }

    internal static void E(MagickImage image, string id, string proof, V5 box)
    {
        var rawId = new string(X9.I(id).Where(char.IsLetterOrDigit).ToArray());
        var rawProof = J(proof);
        if (rawId.Length != A6 || rawProof.Length != B5.O)
            return;

        var body = B5.A + rawId + rawProof;
        var check = N(SHA256.HashData(Encoding.ASCII.GetBytes(body)), A7);
        var payload = body + check;
        if (payload.Length != B5.P || payload.Length * 5 > B5.G * B5.H)
            return;

        var left = (int)Math.Ceiling(box.P13);
        var right = (int)Math.Floor(box.P15);
        var top = (int)Math.Ceiling(box.P14);
        var bottom = (int)Math.Floor(box.P16);
        if (right - left < B5.G || bottom - top < B5.H)
            return;

        var zero = MagickColor.FromRgba(17, 19, 24, 255);
        var one = MagickColor.FromRgba(40, 44, 52, 255);
        var d0 = new Drawables().StrokeColor(MagickColors.Transparent);
        var d1 = new Drawables().StrokeColor(MagickColors.Transparent);

        var bit = 0;
        foreach (var c in payload)
        {
            var v = A0.IndexOf(c);
            if (v < 0) return;
            for (var shift = 4; shift >= 0; shift--, bit++)
            {
                var row = bit / B5.G;
                var col = bit % B5.G;
                if (!W(left, right, top, bottom, col, row, B5.G, B5.H,
                        out var x0, out var y0, out var x1, out var y1))
                    continue;

                if (((v >> shift) & 1) == 0)
                    d0.FillColor(zero).Rectangle(x0, y0, x1, y1);
                else
                    d1.FillColor(one).Rectangle(x0, y0, x1, y1);
            }
        }

        d0.Draw(image);
        d1.Draw(image);
    }

    internal static CaptureIntegrityResult C(CaptureSettings s, string p, string? idText, string? codeText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                return new(false, "Choose an existing screenshot file.");

            using var image = new MagickImage(p);
            image.AutoOrient();
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = 8;

            var id = X9.I(idText);
            var code = J(codeText);
            var automatic = false;
            string? payload = null;
            V5? decodedBox = null;
            L? decodedLayout = null;
            double? decodedScale = null;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(code))
            {
                if (V(image, out var autoId, out var autoCode, out var autoScale, out var autoPayload, out var autoBox, out var autoLayout))
                {
                    id = autoId;
                    code = autoCode;
                    payload = autoPayload;
                    decodedBox = autoBox;
                    decodedLayout = autoLayout;
                    decodedScale = autoScale;
                    automatic = true;
                }
                else if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(code))
                {
                    return new(false,
                        "Could not read the automatic WindowSnapper proof from this image. The watermark may be from an older version, cropped/edited, resized, or too heavily recompressed. Use the manual ID/code fallback if needed.");
                }
            }

            if (string.IsNullOrWhiteSpace(id))
                return new(false, "Enter the ID shown in the screenshot watermark.");
            if (!X9.G(id, out var capturedAt))
                return new(false, "That capture ID is not a valid WindowSnapper ID.");

            var layout = decodedLayout ?? R(code.Length);
            if (layout is null)
            {
                var manual = X9.F(s, id, codeText);
                return manual.IsValid
                    ? new(true, "Manual watermark verified: this ID/code pair was issued by this WindowSnapper installation. Upload the image for whole-image integrity verification.", id, manual.CapturedAt)
                    : new(false, automatic
                        ? "The automatic proof was incomplete or damaged."
                        : "The manual ID/code pair does not verify on this WindowSnapper installation.");
            }

            var proofLayout = layout.Value;
            var e0 = code[..E0];
            var d0 = code.Substring(E0, proofLayout.E);
            var r0 = code[(E0 + proofLayout.E)..];
            if (!P(d0, proofLayout.D, out var signedDescriptor))
                return new(false, "The whole-image proof descriptor is malformed.", id, capturedAt);

            var expectedMac = T(s, id, signedDescriptor);
            if (!L0(r0, expectedMac))
                return new(false, "Not verified: the image proof was not issued by this WindowSnapper installation.", id, capturedAt);

            if (!CaptureVerificationService.TryGetContentProofScale(id, out var scale))
                return new(false, "This capture ID does not contain a valid watermark layout marker.", id, capturedAt);

            var box = decodedBox ?? G0(image, scale, proofLayout);
            var q = H(image, box, proofLayout.B, proofLayout.C, proofLayout.F, proofLayout.K, proofLayout.M);
            var difference = U(signedDescriptor, q.R, proofLayout);
            var distance = difference.A;
            var exactContent = L0(e0, S(s, id, q.X));
            var carrierExact = payload is not null && Y(image, box, payload, proofLayout, strict: true, out _);

            if (exactContent && distance == 0 && carrierExact)
                return new(true, "Verified", id, capturedAt, BitDifference: 0, ChangedCells: 0, LargestChangedCluster: 0);

            var carrierErrors = 0;
            var carrierReadable = payload is null ||
                (Y(image, box, payload, proofLayout, strict: false, out carrierErrors) &&
                 carrierErrors <= proofLayout.J);

            // A cluster is sus. Codec confetti usually isn’t.
            var localizedEdit = proofLayout.A switch
            {
                K5 => difference.C >= 7,
                K4 => difference.C >= 10,
                _ => false
            };

            // PNG has no excuse. Same-size pixels c3? Somebody touched it.
            var losslessPixelEdit = automatic
                && image.Format == MagickFormat.Png
                && decodedScale is { } apparentScale
                && Math.Abs(apparentScale - scale) <= 0.02
                && !exactContent;

            if (losslessPixelEdit)
                return new(false, "Altered / Not Verified",
                    id, capturedAt, BitDifference: distance, ChangedCells: difference.B,
                    LargestChangedCluster: difference.C, ExactPixelMismatch: true);

            if (carrierReadable && distance <= proofLayout.I && !localizedEdit)
                return new(true, "Verified", id, capturedAt, BitDifference: distance,
                    ChangedCells: difference.B, LargestChangedCluster: difference.C);

            return new(false, "Altered / Not Verified", id, capturedAt, BitDifference: distance,
                ChangedCells: difference.B, LargestChangedCluster: difference.C);
        }
        catch (Exception ex)
        {
            return new(false, $"Could not verify screenshot: {ex.Message}");
        }
    }

    private static L? R(int proofLength)
    {
        if (proofLength == B5.O)
            return B5;
        if (proofLength == B4.O)
            return B4;
        if (proofLength == B3.O)
            return B3;
        if (proofLength == B0.O)
            return B0;
        return null;
    }

    private static V5 G0(MagickImage image, double scale, L layout) =>
        layout.N
            ? V5.B(image.Width, image.Height, scale)
            : V5.A(image.Width, image.Height, scale, layout.H);

    private static bool V(MagickImage image, out string id, out string code, out double matchedScale,
        out string payload, out V5 boxOut, out L layoutOut)
    {
        id = string.Empty;
        code = string.Empty;
        payload = string.Empty;
        matchedScale = 1.0;
        boxOut = default;
        layoutOut = default;

        byte[] rgb;
        using (var px = image.GetPixelsUnsafe())
            rgb = px.ToByteArray(0, 0, image.Width, image.Height, PixelMapping.RGB) ?? Array.Empty<byte>();
        if (rgb.Length == 0) return false;

        var w = checked((int)image.Width);
        var h = checked((int)image.Height);

        foreach (var layout in B6)
        {
            var minScale = layout.N ? 0.8 : 0.4;
            // Chat apps resize by cursed decimals. Walk the ruler instead of guessing.
            var scaleStep = layout.N ? 0.05 : 0.01;
            var scaleSteps = (int)Math.Round((2.0 - minScale) / scaleStep);
            for (var si = 0; si <= scaleSteps; si++)
            {
                var scale = minScale + si * scaleStep;
                var box = G0(image, scale, layout);
                var left = (int)Math.Ceiling(box.P13);
                var right = (int)Math.Floor(box.P15);
                var top = (int)Math.Ceiling(box.P14);
                var bottom = (int)Math.Floor(box.P16);
                if (right - left < layout.G || bottom - top < layout.H)
                    continue;

                var chars = new char[layout.P];
                var failed = false;
                for (var ci = 0; ci < chars.Length && !failed; ci++)
                {
                    var v = 0;
                    for (var k = 0; k < 5; k++)
                    {
                        var bit = ci * 5 + k;
                        var row = bit / layout.G;
                        var col = bit % layout.G;
                        if (!W(left, right, top, bottom, col, row, layout.G, layout.H,
                                out var x0, out var y0, out var x1, out var y1))
                        {
                            failed = true;
                            break;
                        }

                        if (!F0(rgb, w, h, x0, y0, x1, y1, out var observed))
                        {
                            failed = true;
                            break;
                        }

                        v = (v << 1) | observed;
                    }

                    if (!failed)
                    {
                        chars[ci] = A0[v & 31];
                        // Bad scales faceplant early. Nice of them.
                        if (ci < layout.A.Length && chars[ci] != layout.A[ci])
                            failed = true;
                    }
                }

                if (failed) continue;
                var candidatePayload = new string(chars);
                if (!candidatePayload.StartsWith(layout.A, StringComparison.Ordinal))
                    continue;

                var idStart = layout.A.Length;
                var proofStart = idStart + A6;
                var checkStart = proofStart + layout.O;
                if (candidatePayload.Length != layout.P || checkStart + A7 != candidatePayload.Length)
                    continue;

                var rawId = candidatePayload.Substring(idStart, A6);
                var rawCode = candidatePayload.Substring(proofStart, layout.O);
                var check = candidatePayload.Substring(checkStart, A7);
                var body = candidatePayload.Substring(0, checkStart);
                if (!L0(check, N(SHA256.HashData(Encoding.ASCII.GetBytes(body)), A7)))
                    continue;

                var candidateId = $"{rawId[..5]}-{rawId[5..]}";
                if (!X9.Q(candidateId, out var encodedScale))
                    continue;

                // Old proofs get old math. History refuses to die.
                if (layout.N && Math.Abs(encodedScale - scale) > 0.001)
                    continue;

                id = candidateId;
                code = rawCode;
                payload = candidatePayload;
                matchedScale = scale;
                boxOut = box;
                layoutOut = layout;
                return true;
            }
        }

        return false;
    }

    private static bool F0(byte[] rgb, int width, int height, int x0, int y0, int x1, int y1, out int bit)
    {
        bit = 0;
        x0 = Math.Clamp(x0, 0, width - 1);
        x1 = Math.Clamp(x1, x0, width - 1);
        y0 = Math.Clamp(y0, 0, height - 1);
        y1 = Math.Clamp(y1, y0, height - 1);

        long sr = 0, sg = 0, sb = 0, count = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var o = (y * width + x) * 3;
                sr += rgb[o];
                sg += rgb[o + 1];
                sb += rgb[o + 2];
                count++;
            }
        }

        if (count == 0)
            return false;

        var r = (int)(sr / count);
        var g = (int)(sg / count);
        var b = (int)(sb / count);
        var z0 = (r - 17) * (r - 17) + (g - 19) * (g - 19) + (b - 24) * (b - 24);
        var z1 = (r - 40) * (r - 40) + (g - 44) * (g - 44) + (b - 52) * (b - 52);
        bit = z1 < z0 ? 1 : 0;
        return true;
    }

    private static bool W(int left, int right, int top, int bottom, int col, int row,
        int columns, int rows, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = left + col * (right - left) / columns;
        var xn = left + (col + 1) * (right - left) / columns;
        y0 = top + row * (bottom - top) / rows;
        var yn = top + (row + 1) * (bottom - top) / rows;
        x1 = xn - 1;
        y1 = yn - 1;
        return col >= 0 && col < columns && row >= 0 && row < rows && xn > x0 && yn > y0;
    }

    private static Q H(MagickImage image, V5 box, int a1, int a2, int a3, bool a4 = false, bool a5 = false)
    {
        byte[] rgb;
        using (var px = image.GetPixelsUnsafe())
            rgb = px.ToByteArray(0, 0, image.Width, image.Height, PixelMapping.RGB)
                ?? throw new InvalidOperationException("Could not read screenshot pixels for verification.");

        var w = checked((int)image.Width);
        var h = checked((int)image.Height);
        var x0 = Math.Clamp((int)Math.Floor(box.P13), 0, w);
        var x1 = Math.Clamp((int)Math.Ceiling(box.P15), x0, w);
        var y0 = Math.Clamp((int)Math.Floor(box.P14), 0, h);
        var y1 = Math.Clamp((int)Math.Ceiling(box.P16), y0, h);

        var a6 = a4 ? 3 : 0;
        var dx0 = Math.Max(0, x0 - a6);
        var dx1 = Math.Min(w, x1 + a6);
        var dy0 = Math.Max(0, y0 - a6);
        var dy1 = Math.Min(h, y1 + a6);

        using var exact = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> hdr = stackalloc byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(hdr[0..4], w);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[4..8], h);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[8..12], x0);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[12..16], x1);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[16..20], y0);
        BinaryPrimitives.WriteInt32LittleEndian(hdr[20..24], y1);
        exact.AppendData(hdr);

        var zero = new byte[Math.Max(3, (x1 - x0) * 3)];
        var a7 = checked(a1 * a2);
        var sr = new ulong[a7];
        var sg = new ulong[a7];
        var sb = new ulong[a7];
        var sy = new ulong[a7];
        var sy2 = new ulong[a7];
        var a8 = new ulong[a7];
        var a9 = new uint[a7];
        var counts = new uint[a7];
        var xb = new int[w];
        var b0 = new bool[w];
        for (var x = 0; x < w; x++)
        {
            var col = Math.Min(a1 - 1, (int)((long)x * a1 / Math.Max(1, w)));
            xb[x] = col;
            if (a5)
            {
                var cellX0 = (int)((long)col * w / a1);
                var cellX1 = (int)((long)(col + 1) * w / a1);
                var inset = Math.Max(1, (cellX1 - cellX0) / 4);
                b0[x] = x >= cellX0 + inset && x < cellX1 - inset;
            }
        }

        for (var y = 0; y < h; y++)
        {
            var row = y * w * 3;
            var b1 = y >= y0 && y < y1 && x1 > x0;
            var b2 = y >= dy0 && y < dy1 && dx1 > dx0;
            if (b1)
            {
                if (x0 > 0) exact.AppendData(rgb, row, x0 * 3);
                exact.AppendData(zero, 0, (x1 - x0) * 3);
                if (x1 < w) exact.AppendData(rgb, row + x1 * 3, (w - x1) * 3);
            }
            else
            {
                exact.AppendData(rgb, row, w * 3);
            }

            var by = Math.Min(a2 - 1, (int)((long)y * a2 / Math.Max(1, h)));
            var baseCell = by * a1;
            var yIsCenter = false;
            if (a5)
            {
                var cellY0 = (int)((long)by * h / a2);
                var cellY1 = (int)((long)(by + 1) * h / a2);
                var inset = Math.Max(1, (cellY1 - cellY0) / 4);
                yIsCenter = y >= cellY0 + inset && y < cellY1 - inset;
            }
            for (var x = 0; x < w; x++)
            {
                if (b2 && x >= dx0 && x < dx1)
                    continue;

                var c = baseCell + xb[x];
                counts[c]++;
                var o = row + x * 3;
                var pr = rgb[o];
                var pg = rgb[o + 1];
                var pb = rgb[o + 2];
                sr[c] += pr;
                sg[c] += pg;
                sb[c] += pb;
                var py = (54 * pr + 183 * pg + 19 * pb) >> 8;
                sy[c] += (uint)py;
                sy2[c] += (uint)(py * py);
                if (a5 && yIsCenter && b0[x])
                {
                    a8[c] += (uint)py;
                    a9[c]++;
                }
            }
        }

        a3 = a3 switch
        {
            2 => 2,
            3 => 3,
            _ => 4
        };
        var descriptor = new byte[(a7 * a3 + 7) / 8];
        var b3 = 0;
        for (var i = 0; i < a7; i++)
        {
            var n = Math.Max(1u, counts[i]);
            var r = (int)Math.Min(255UL, sr[i] / n);
            var g = (int)Math.Min(255UL, sg[i] / n);
            var b = (int)Math.Min(255UL, sb[i] / n);
            var luma = (54 * r + 183 * g + 19 * b) >> 8;
            var lq = Math.Clamp((luma * 3 + 127) / 255, 0, 3);
            var value = lq;

            if (a5)
            {
                // Four tiny opinions per cell. Future-you gets no more hints than that.
                var b4 = (double)sy[i] / n;
                var luma8 = Math.Clamp((int)Math.Round(b4), 0, 255);
                var q3 = Math.Clamp((luma8 * 7 + 127) / 255, 0, 7);
                var gray = q3 ^ (q3 >> 1);

                var centerN = a9[i];
                var outerN = n > centerN ? n - centerN : 0;
                var b5 = centerN == 0 ? b4 : (double)a8[i] / centerN;
                var b6 = outerN == 0 ? b4 : (double)(sy[i] - a8[i]) / outerN;
                // One unit of mercy for codec noise.
                var centerVsEdge = b5 >= b6 + 1.0 ? 1 : 0;
                value = (gray << 1) | centerVsEdge;
            }
            else if (a3 == 3)
            {
                var b4 = (double)sy[i] / n;
                var variance = Math.Max(0.0, (double)sy2[i] / n - b4 * b4);
                var b7 = variance >= 18.0 * 18.0 ? 1 : 0;
                value = (lq << 1) | b7;
            }
            else if (a3 == 4)
            {
                var warm = r >= b ? 1 : 0;
                var green = g * 2 >= r + b ? 1 : 0;
                value = (lq << 2) | (warm << 1) | green;
            }

            for (var shift = a3 - 1; shift >= 0; shift--, b3++)
            {
                if (((value >> shift) & 1) != 0)
                    descriptor[b3 >> 3] |= (byte)(1 << (7 - (b3 & 7)));
            }
        }

        return new Q(exact.GetHashAndReset(), descriptor);
    }

    private static string I(CaptureSettings s, string id, Q q, int descriptorChars)
    {
        var e = S(s, id, q.X);
        var d = N(q.R, descriptorChars);
        var r = T(s, id, q.R);
        return $"{e}-{d}-{r}";
    }

    private static string S(CaptureSettings s, string id, byte[] digest)
    {
        var k = X9.H(s);
        return M(k, O(0, id), digest, E0);
    }

    private static string T(CaptureSettings s, string id, byte[] descriptor)
    {
        var k = X9.H(s);
        return M(k, O(1, id), descriptor, R0);
    }

    private static string M(byte[] key, byte[] prefix, byte[] digest, int count)
    {
        var data = new byte[prefix.Length + digest.Length];
        Buffer.BlockCopy(prefix, 0, data, 0, prefix.Length);
        Buffer.BlockCopy(digest, 0, data, prefix.Length, digest.Length);
        using var h = new HMACSHA256(key);
        return N(h.ComputeHash(data), count);
    }

    private static string N(ReadOnlySpan<byte> d, int n)
    {
        var o = new StringBuilder(n);
        var bits = 0;
        var acc = 0;
        foreach (var x in d)
        {
            acc = (acc << 8) | x;
            bits += 8;
            while (bits >= 5 && o.Length < n)
            {
                bits -= 5;
                o.Append(A0[(acc >> bits) & 31]);
            }
            if (o.Length == n) break;
        }
        if (o.Length < n && bits > 0)
            o.Append(A0[(acc << (5 - bits)) & 31]);
        while (o.Length < n) o.Append('A');
        return o.ToString();
    }

    private static bool P(string s, int byteCount, out byte[] b)
    {
        b = new byte[byteCount];
        var acc = 0;
        var bits = 0;
        var o = 0;
        foreach (var c in s)
        {
            var v = A0.IndexOf(c);
            if (v < 0) { b = Array.Empty<byte>(); return false; }
            acc = (acc << 5) | v;
            bits += 5;
            while (bits >= 8 && o < b.Length)
            {
                bits -= 8;
                b[o++] = (byte)((acc >> bits) & 0xFF);
            }
        }
        return o == b.Length;
    }

    private static string J(string? v)
    {
        var x = (v ?? string.Empty).Trim().ToUpperInvariant();
        if (x.StartsWith("VERIFY ", StringComparison.OrdinalIgnoreCase)) x = x[7..].Trim();
        if (x.StartsWith("V ", StringComparison.OrdinalIgnoreCase)) x = x[2..].Trim();
        return new string(x.Where(char.IsLetterOrDigit).ToArray());
    }

    private static bool L0(string a, string b)
    {
        var x = Encoding.ASCII.GetBytes(a);
        var y = Encoding.ASCII.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }

    private readonly record struct D0(int A, int B, int C);

    private static D0 U(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, L layout)
    {
        if (a.Length != b.Length)
            return new D0(int.MaxValue, int.MaxValue, int.MaxValue);

        var c0 = 0;
        for (var i = 0; i < a.Length; i++)
            c0 += System.Numerics.BitOperations.PopCount((uint)(a[i] ^ b[i]));

        var a7 = checked(layout.B * layout.C);
        var c3 = new bool[a7];
        var c1 = 0;
        for (var cell = 0; cell < a7; cell++)
        {
            var firstBit = cell * layout.F;
            var differs = false;
            for (var k = 0; k < layout.F; k++)
            {
                var bit = firstBit + k;
                var mask = 1 << (7 - (bit & 7));
                var av = (a[bit >> 3] & mask) != 0;
                var bv = (b[bit >> 3] & mask) != 0;
                if (av != bv)
                {
                    differs = true;
                    break;
                }
            }

            if (differs)
            {
                c3[cell] = true;
                c1++;
            }
        }

        var c2 = 0;
        var c4 = new bool[a7];
        var c5 = new Queue<int>();
        for (var start = 0; start < a7; start++)
        {
            if (!c3[start] || c4[start])
                continue;

            c4[start] = true;
            c5.Enqueue(start);
            var cluster = 0;
            while (c5.Count > 0)
            {
                var cell = c5.Dequeue();
                cluster++;
                var cx = cell % layout.B;
                var cy = cell / layout.B;
                for (var oy = -1; oy <= 1; oy++)
                {
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;
                        var nx = cx + ox;
                        var ny = cy + oy;
                        if (nx < 0 || nx >= layout.B || ny < 0 || ny >= layout.C)
                            continue;
                        var next = ny * layout.B + nx;
                        if (!c3[next] || c4[next])
                            continue;
                        c4[next] = true;
                        c5.Enqueue(next);
                    }
                }
            }

            c2 = Math.Max(c2, cluster);
        }

        return new D0(c0, c1, c2);
    }

    private static bool Y(MagickImage image, V5 box, string payload, L layout, bool strict, out int errors)
    {
        errors = 0;
        if (payload.Length != layout.P || payload.Length * 5 > layout.G * layout.H)
            return false;

        byte[] rgb;
        using (var px = image.GetPixelsUnsafe())
            rgb = px.ToByteArray(0, 0, image.Width, image.Height, PixelMapping.RGB) ?? Array.Empty<byte>();
        if (rgb.Length == 0) return false;

        var w = checked((int)image.Width);
        var h = checked((int)image.Height);
        var left = (int)Math.Ceiling(box.P13);
        var right = (int)Math.Floor(box.P15);
        var top = (int)Math.Ceiling(box.P14);
        var bottom = (int)Math.Floor(box.P16);
        if (right - left < layout.G || bottom - top < layout.H)
            return false;

        var bit = 0;
        foreach (var c in payload)
        {
            var v = A0.IndexOf(c);
            if (v < 0) return false;
            for (var shift = 4; shift >= 0; shift--, bit++)
            {
                var expected = (v >> shift) & 1;
                var row = bit / layout.G;
                var col = bit % layout.G;
                if (!W(left, right, top, bottom, col, row, layout.G, layout.H,
                        out var x0, out var y0, out var x1, out var y1))
                    return false;

                if (!F1(rgb, w, h, x0, y0, x1, y1, out var r, out var g, out var b))
                    return false;

                var er = expected == 0 ? 17 : 40;
                var eg = expected == 0 ? 19 : 44;
                var eb = expected == 0 ? 24 : 52;

                if (strict)
                {
                    if (r != er || g != eg || b != eb)
                        errors++;
                }
                else
                {
                    var z0 = (r - 17) * (r - 17) + (g - 19) * (g - 19) + (b - 24) * (b - 24);
                    var z1 = (r - 40) * (r - 40) + (g - 44) * (g - 44) + (b - 52) * (b - 52);
                    var observed = z1 < z0 ? 1 : 0;
                    if (observed != expected) errors++;
                }
            }
        }

        return strict ? errors == 0 : true;
    }

    private static bool F1(byte[] rgb, int w, int h, int x0, int y0, int x1, int y1,
        out int r, out int g, out int b)
    {
        r = g = b = 0;
        x0 = Math.Clamp(x0, 0, w - 1);
        x1 = Math.Clamp(x1, x0, w - 1);
        y0 = Math.Clamp(y0, 0, h - 1);
        y1 = Math.Clamp(y1, y0, h - 1);

        long sr = 0, sg = 0, sb = 0, count = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var o = (y * w + x) * 3;
                sr += rgb[o];
                sg += rgb[o + 1];
                sb += rgb[o + 2];
                count++;
            }
        }

        if (count == 0)
            return false;

        r = (int)(sr / count);
        g = (int)(sg / count);
        b = (int)(sb / count);
        return true;
    }

    private static byte[] O(int n, string id)
    {
        ReadOnlySpan<byte> a = n == 0
            ? new byte[] { 0x6C, 0x29, 0x5A, 0x7F, 0x32 }
            : new byte[] { 0x6C, 0x29, 0x4D, 0x7F, 0x32 };
        var t = new byte[a.Length + id.Length];
        for (var i = 0; i < a.Length; i++) t[i] = (byte)(a[i] ^ (0x1A + i));
        Encoding.ASCII.GetBytes(id.AsSpan(), t.AsSpan(a.Length));
        return t;
    }
}
