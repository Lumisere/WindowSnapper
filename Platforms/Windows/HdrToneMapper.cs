using System.Buffers.Binary;

namespace WindowSnapper.Platforms.Windows;

internal sealed class HdrToneMapper
{
    private const float ScRgbNitsPerUnit = 80f;
    private const float DefaultHdrSdrWhiteNits = 203f;
    private const float SdrWhiteNits = 80f;
    private const int HistogramBins = 1024;

    // ST 2084 / PQ constants.
    private const float PqM1 = 0.1593017578125f;
    private const float PqM2 = 78.84375f;
    private const float PqC1 = 0.8359375f;
    private const float PqC2 = 18.8515625f;
    private const float PqC3 = 18.6875f;

    private readonly float _targetWhiteNits;
    private readonly float _sourcePeakNits;
    private readonly float _sourcePeakPq;
    private readonly float _maxLum;
    private readonly float _kneeStart;
    private readonly bool _needsRolloff;
    private readonly float _brightness;
    private readonly float _highlightCompression;
    private readonly float _outputKnee;
    private readonly float _outputCeiling;

    private HdrToneMapper(float targetWhiteNits, float sourcePeakNits)
    {
        _targetWhiteNits = targetWhiteNits;
        _sourcePeakNits = Math.Max(sourcePeakNits, targetWhiteNits);
        _sourcePeakPq = NitsToPq(_sourcePeakNits);

        var targetPq = NitsToPq(_targetWhiteNits);
        _maxLum = _sourcePeakPq > 0.000001f
            ? Math.Clamp(targetPq / _sourcePeakPq, 0f, 1f)
            : 1f;
        _kneeStart = Math.Clamp(1.5f * _maxLum - 0.5f, 0f, 0.9999f);
        _needsRolloff = _sourcePeakNits > _targetWhiteNits * 1.03f;
        if (_needsRolloff)
        {
            var peakRatio = Math.Max(1f, _sourcePeakNits / _targetWhiteNits);
            var severity = Math.Clamp(MathF.Log2(peakRatio) / 3f, 0f, 1f);
            _brightness = Lerp(0.94f, 0.88f, severity);
            _highlightCompression = Lerp(0.82f, 0.92f, severity);
        }
        else
        {
            _brightness = 1f;
            _highlightCompression = 0f;
        }
        _outputKnee = Lerp(0.65f, 0.32f, _highlightCompression);
        _outputCeiling = Lerp(1.0f, 0.72f, _highlightCompression);
    }

    public static HdrToneMapper Analyze(byte[] pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height * 8)
            return new HdrToneMapper(DefaultHdrSdrWhiteNits, DefaultHdrSdrWhiteNits);

        var histogram = new int[HistogramBins];
        var samples = 0;
        var maxNits = 0f;

        // A sparse scan is enough for exposure/peak selection and saves a lot of
        // work on 1440p/4K captures. The 99th percentile intentionally ignores
        // tiny single-pixel outliers.
        var pixelCount = width * height;
        var stride = pixelCount > 4_000_000 ? 8 : pixelCount > 1_000_000 ? 4 : 2;

        for (var y = 0; y < height; y += stride)
        {
            var row = y * width * 8;
            for (var x = 0; x < width; x += stride)
            {
                var offset = row + x * 8;
                var r = ReadHalf(pixels, offset);
                var g = ReadHalf(pixels, offset + 2);
                var b = ReadHalf(pixels, offset + 4);

                if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b))
                    continue;

                r = Math.Max(0f, r);
                g = Math.Max(0f, g);
                b = Math.Max(0f, b);

                var nits = Luminance(r, g, b) * ScRgbNitsPerUnit;
                if (!float.IsFinite(nits) || nits < 0f)
                    continue;

                nits = Math.Min(nits, 10_000f);
                maxNits = Math.Max(maxNits, nits);
                histogram[HistogramIndex(nits)]++;
                samples++;
            }
        }

        if (samples == 0)
            return new HdrToneMapper(DefaultHdrSdrWhiteNits, DefaultHdrSdrWhiteNits);

        var percentileNits = HistogramPercentile(histogram, samples, 0.99f);

        // On an SDR desktop, FP16 WGC frames generally top out around the
        // scRGB 1.0 / 80-nit reference. HDR desktops place ordinary SDR white
        // much higher. This keeps SDR-only systems from being dimmed by the HDR
        // path while retaining Windows' common 203-nit HDR reference white.
        var targetWhite = percentileNits <= 110f && maxNits <= 140f
            ? SdrWhiteNits
            : DefaultHdrSdrWhiteNits;

        var sourcePeak = Math.Max(targetWhite, percentileNits);
        return new HdrToneMapper(targetWhite, sourcePeak);
    }

    public void Map(ref float r, ref float g, ref float b)
    {
        r = Sanitize(r);
        g = Sanitize(g);
        b = Sanitize(b);

        var inputLuminance = Luminance(r, g, b);
        if (inputLuminance <= 0.0000001f)
        {
            r = g = b = 0f;
            return;
        }

        var inputNits = inputLuminance * ScRgbNitsPerUnit;
        var outputNits = _needsRolloff ? MapLuminance(inputNits) : inputNits;

        // Preserve hue by mapping luminance once and applying the same ratio to
        // all channels. The second factor converts absolute scRGB nits into a
        // normal SDR [0,1] linear-light image where 1.0 is SDR reference white.
        var scale = outputNits / inputNits * (ScRgbNitsPerUnit / _targetWhiteNits);
        r *= scale * _brightness;
        g *= scale * _brightness;
        b *= scale * _brightness;

        CompressHighlights(ref r, ref g, ref b);
        CompressGamut(ref r, ref g, ref b);
    }

    private float MapLuminance(float inputNits)
    {
        inputNits = Math.Clamp(inputNits, 0f, _sourcePeakNits);
        if (_sourcePeakPq <= 0.000001f)
            return 0f;

        var e1 = Math.Clamp(NitsToPq(inputNits) / _sourcePeakPq, 0f, 1f);
        float e2;

        if (e1 < _kneeStart)
        {
            e2 = e1;
        }
        else
        {
            var t = (e1 - _kneeStart) / (1f - _kneeStart);
            var t2 = t * t;
            var t3 = t2 * t;

            // BT.2390 Hermite shoulder. Black-level lift is intentionally zero
            // for screenshots, so only the highlight shoulder is required.
            e2 = (2f * t3 - 3f * t2 + 1f) * _kneeStart
               + (t3 - 2f * t2 + t) * (1f - _kneeStart)
               + (-2f * t3 + 3f * t2) * _maxLum;
        }

        return PqToNits(Math.Clamp(e2 * _sourcePeakPq, 0f, 1f));
    }

    private void CompressHighlights(ref float r, ref float g, ref float b)
    {
        if (_highlightCompression <= 0.0001f)
            return;

        var y = Luminance(r, g, b);
        if (y <= _outputKnee || y <= 0.000001f)
            return;

        var t = Math.Clamp((y - _outputKnee) / (1f - _outputKnee), 0f, 1f);
        var ceilingRange = Math.Max(0.0001f, _outputCeiling - _outputKnee);
        var fullRange = Math.Max(0.0001f, 1f - _outputKnee);
        var endSlope = ceilingRange / fullRange;

        // Cubic shoulder with a unit slope at the knee and a flat slope at the
        // configured ceiling. Midtones barely move; only the upper range folds
        // down, which avoids bringing back crushed shadows.
        var quadratic = 3f * endSlope - 2f;
        var cubic = 1f - 2f * endSlope;
        var shaped = t + quadratic * t * t + cubic * t * t * t;
        var mappedY = _outputKnee + fullRange * shaped;
        mappedY = Math.Min(mappedY, _outputCeiling);

        var ratio = mappedY / y;
        r *= ratio;
        g *= ratio;
        b *= ratio;
    }

    private static void CompressGamut(ref float r, ref float g, ref float b)
    {
        r = Math.Max(0f, r);
        g = Math.Max(0f, g);
        b = Math.Max(0f, b);

        var maxChannel = Math.Max(r, Math.Max(g, b));
        if (maxChannel <= 1f)
            return;

        var y = Math.Clamp(Luminance(r, g, b), 0f, 1f);
        var excess = maxChannel - 1f;
        var desaturation = Math.Clamp(excess / (excess + 0.35f), 0f, 0.72f);

        r = Lerp(r, y, desaturation);
        g = Lerp(g, y, desaturation);
        b = Lerp(b, y, desaturation);

        maxChannel = Math.Max(r, Math.Max(g, b));
        if (maxChannel > 1f)
        {
            var normalize = 1f / maxChannel;
            r *= normalize;
            g *= normalize;
            b *= normalize;
        }
    }

    public static byte LinearToSrgbByte(float value, int x, int y, int channel)
    {
        value = Math.Clamp(value, 0f, 1f);
        var srgb = value <= 0.0031308f
            ? value * 12.92f
            : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

        // Tiny deterministic dither before 8-bit quantization. This is most
        // noticeable in HDR skies, fog and gradients after the range is folded
        // down to SDR.
        var hash = unchecked((uint)(x * 0x1f123bb5) ^ (uint)(y * 0x05491333) ^ (uint)(channel * 0x68bc21eb));
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        var noise = ((hash & 1023u) / 1023f - 0.5f) / 255f;

        return (byte)Math.Clamp((int)MathF.Round((srgb + noise) * 255f), 0, 255);
    }

    private static float Luminance(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Sanitize(float value) => float.IsFinite(value) ? Math.Max(0f, value) : 0f;

    private static int HistogramIndex(float nits)
    {
        const float logMax = 13.287856f; // log2(10001)
        var normalized = MathF.Log2(nits + 1f) / logMax;
        return Math.Clamp((int)(normalized * (HistogramBins - 1)), 0, HistogramBins - 1);
    }

    private static float HistogramPercentile(int[] histogram, int samples, float percentile)
    {
        var wanted = Math.Max(1, (int)MathF.Ceiling(samples * percentile));
        var total = 0;
        var index = histogram.Length - 1;

        for (var i = 0; i < histogram.Length; i++)
        {
            total += histogram[i];
            if (total < wanted)
                continue;

            index = i;
            break;
        }

        const float logMax = 13.287856f;
        var normalized = (index + 0.5f) / HistogramBins;
        return MathF.Pow(2f, normalized * logMax) - 1f;
    }

    private static float NitsToPq(float nits)
    {
        var y = Math.Clamp(nits / 10_000f, 0f, 1f);
        var p = MathF.Pow(y, PqM1);
        return MathF.Pow((PqC1 + PqC2 * p) / (1f + PqC3 * p), PqM2);
    }

    private static float PqToNits(float pq)
    {
        var p = MathF.Pow(Math.Clamp(pq, 0f, 1f), 1f / PqM2);
        var numerator = Math.Max(p - PqC1, 0f);
        var denominator = Math.Max(PqC2 - PqC3 * p, 0.000001f);
        return MathF.Pow(numerator / denominator, 1f / PqM1) * 10_000f;
    }

    private static float ReadHalf(byte[] data, int offset)
    {
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        var value = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(value) ? value : 0f;
    }
}
