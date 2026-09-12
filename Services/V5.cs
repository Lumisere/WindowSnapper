namespace WindowSnapper.Services;

internal readonly record struct V5(
    double P0,
    double P1,
    double P2,
    double P3,
    double P4,
    double P5,
    double P6,
    double P7,
    double P8,
    double P9,
    double P10,
    double P11,
    double P12,
    double P13,
    double P14,
    double P15,
    double P16,
    double P17,
    double P18,
    double P19,
    double P20)
{
    // New toast math. Touch the geometry casually and old screenshots will come back to haunt you.
    internal static V5 A(uint imageWidth, uint imageHeight, double configuredScale, int carrierRows = 12)
    {
        const double w0 = 430;
        const double h0 = 108;
        const double m0 = 18;
        const int carrierColumns = 115;
        carrierRows = Math.Clamp(carrierRows, 5, 16);

        var s = Math.Clamp(double.IsFinite(configuredScale) && configuredScale > 0
            ? configuredScale
            : 1.0, 0.4, 2.0);

        var dw = w0 * s;
        var dh = h0 * s;
        var f = Math.Min(1.0, Math.Min(
            Math.Max(0.12, (imageWidth - m0 * s * 2) / dw),
            Math.Max(0.12, (imageHeight - m0 * s * 2) / dh)));

        var w = dw * f;
        var h = dh * f;
        var m = Math.Max(3, m0 * s * f);
        var l = m;
        var t = Math.Max(m, imageHeight - m - h);
        var r = l + w;
        var b = t + h;
        var border = Math.Max(0.7, s * f);
        var radius = Math.Max(2, 11 * s * f);
        var rail = Math.Max(1, 5 * s * f);
        var cl = 19 * s * f;
        var ct = 10 * s * f;
        var ts = Math.Max(5, 12.5 * s * f);
        var ms = Math.Max(4.5, 10.5 * s * f);
        var tb = t + ct + ts * 0.92;
        var mb = t + ct + ts * 1.20 + 3 * s * f + ms * 0.92;

        var codeLeft = l + 19 * s * f;
        var available = Math.Max(1, r - 10 * s * f - codeLeft);
        var cw = Math.Max(0.25, available / carrierColumns);
        var codeRight = codeLeft + cw * carrierColumns;
        var desiredCodeHeight = 42.0 * s * f;
        var codeTop = Math.Max(mb + Math.Max(3, 4 * s * f), b - Math.Max(14.0, desiredCodeHeight));
        var codeBottom = b - Math.Max(1, border);
        var ch = Math.Max(0.5, (codeBottom - codeTop) / carrierRows);

        return new V5(l, t, r, b, border, radius, rail, cl, ct, ts, ms, tb, mb,
            codeLeft, codeTop, codeRight, codeBottom, cw, ch, f, s);
    }

    // Museum exhibit. Looks weird, stays weird, because old screenshots still need it.
    internal static V5 B(uint imageWidth, uint imageHeight, double configuredToastScale)
    {
        const double w0 = 344;
        const double h0 = 82;
        const double m0 = 18;

        var s = Math.Clamp(double.IsFinite(configuredToastScale) && configuredToastScale > 0
            ? configuredToastScale
            : 1.0, 0.8, 2.0);

        var dw = w0 * s;
        var dh = h0 * s;
        var f = Math.Min(1.0, Math.Min(
            Math.Max(0.12, (imageWidth - m0 * 2) / dw),
            Math.Max(0.12, (imageHeight - m0 * 2) / dh)));

        var w = dw * f;
        var h = dh * f;
        var m = Math.Max(4, m0 * f);
        var l = m;
        var t = Math.Max(m, imageHeight - m - h);
        var r = l + w;
        var b = t + h;
        var border = Math.Max(0.7, f);
        var radius = Math.Max(2, 11 * f);
        var rail = Math.Max(1, 5 * f);
        var cl = 19 * f;
        var ct = 10 * f;
        var ts = Math.Max(5, 12.5 * s * f);
        var ms = Math.Max(4.5, 10.5 * s * f);
        var tb = t + ct + ts * 0.92;
        var mb = t + ct + ts * 1.20 + 3 * f + ms * 0.92;

        var codeLeft = l + 19 * f;
        var available = Math.Max(1, r - 10 * f - codeLeft);
        var cw = Math.Max(0.25, Math.Min(2.6 * f, available / 115.0));
        var codeRight = codeLeft + cw * 115;
        var codeTop = b - Math.Max(10.0, 14.0 * f);
        var codeBottom = b - Math.Max(1, border);
        var ch = Math.Max(0.5, (codeBottom - codeTop) / 5.0);

        return new V5(l, t, r, b, border, radius, rail, cl, ct, ts, ms, tb, mb,
            codeLeft, codeTop, codeRight, codeBottom, cw, ch, f, s);
    }
}
