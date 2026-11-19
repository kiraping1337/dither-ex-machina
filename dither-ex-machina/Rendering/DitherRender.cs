namespace dither_ex_machina.Rendering;

public static class DitherRenderer
{
    public static byte[] Render(byte[] grayscale, int width, int height, RenderSettings settings,
        IDitherEffect effect, IReadOnlyDictionary<string, double> effectParams,
        (byte[] r, byte[] g, byte[] b)? gradientLut, CancellationToken token)
        => new RenderSession().Render(grayscale, width, height, settings, effect, effectParams, gradientLut, token);
}

internal sealed class RenderSession
{
    private const int TileSize = 2048;
    private const long ToneCacheLimit = 384L * 1024 * 1024;
    private readonly record struct ToneKey(byte[] Source, int Width, int Height, int Median, int Blur,
        int Samples, double Center, double Steepness, int Smooth, bool Tiled);
    private readonly record struct EffectKey(IDitherEffect Effect, double Shadow, double Highlight, bool Invert);
    private readonly record struct Tile(int X, int Y, int W, int H, int Left, int Top, int SourceW, int SourceH);

    private ToneKey toneKey;
    private byte[]? filtered;
    private Tile[] tiles = [];
    private double[]?[]? tones;
    private EffectKey effectKey;
    private Dictionary<string, double>? previousParameters;
    private byte[]? dithered;
    private byte[]? blurred;
    private int blurRadius;
    private byte[]? glow;
    private (double Threshold, double Intensity) glowKey;

    public byte[] Render(byte[] source, int width, int height, RenderSettings settings,
        IDitherEffect effect, IReadOnlyDictionary<string, double> parameters,
        (byte[] r, byte[] g, byte[] b)? gradient, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var sequential = effect as ISequentialDitherEffect;
        int samples = sequential != null ? 1 : settings.Supersample;
        bool tiled = sequential == null && (width > TileSize || height > TileSize);
        var key = new ToneKey(source, width, height, settings.MedianRadius, settings.BlurRadius,
            samples, settings.ContrastCenter, settings.ContrastSteepness, settings.PostContrastSmoothRadius, tiled);
        if (key != toneKey)
        {
            toneKey = default;
            tones = null;
            filtered = null;
            dithered = blurred = glow = null;
            var input = source;
            if (key.Median > 0) input = ImageOps.MedianFilter(input, width, height, key.Median);
            token.ThrowIfCancellationRequested();
            if (key.Blur > 0) input = ImageOps.BoxBlur(input, width, height, key.Blur);
            token.ThrowIfCancellationRequested();
            tiles = CreateTiles(key).ToArray();
            long toneBytes = tiles.Sum(t => (long)t.SourceW * t.SourceH * samples * samples * sizeof(double));
            tones = toneBytes <= ToneCacheLimit ? new double[]?[tiles.Length] : null;
            filtered = input;
            toneKey = key;
        }

        var nextEffect = new EffectKey(effect, settings.ShadowCutoff, settings.HighlightCutoff, settings.Invert);
        if (dithered == null || nextEffect != effectKey || previousParameters == null ||
            previousParameters.Count != parameters.Count ||
            !parameters.All(p => previousParameters.TryGetValue(p.Key, out double v) && v == p.Value))
        {
            var compute = effect.Prepare(parameters);
            byte[] output = sequential == null ? new byte[checked(width * height)] : Array.Empty<byte>();
            for (int i = 0; i < tiles.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var tile = tiles[i];
                var tone = tones?[i] ?? PrepareTone(filtered!, key, tile, token);
                if (tones != null) tones[i] = tone;
                if (sequential != null)
                {
                    var clamped = new double[tone.Length];
                    Parallel.For(0, height, y =>
                    {
                        int end = (y + 1) * width;
                        for (int j = y * width; j < end; j++)
                            clamped[j] = tone[j] < nextEffect.Shadow ? 0 : tone[j] > nextEffect.Highlight ? 1 : tone[j];
                    });
                    output = sequential.ComputeFullImage(clamped, width, height, parameters, token);
                    if (settings.Invert)
                        Parallel.For(0, height, y =>
                        {
                            for (int j = y * width; j < (y + 1) * width; j++) output[j] = (byte)(255 - output[j]);
                        });
                }
                else
                    RenderPixels(tone, key, tile, nextEffect, compute, parameters, output, token);
            }
            token.ThrowIfCancellationRequested();
            dithered = output;
            effectKey = nextEffect;
            previousParameters = new(parameters);
            blurred = glow = null;
        }

        byte[] gray = dithered;
        if (settings.GlowEnabled && settings.GlowRadius > 0 && settings.GlowIntensity > 0)
        {
            if (blurred == null || blurRadius != settings.GlowRadius)
            {
                blurred = BlurTiles(gray, width, height, settings.GlowRadius, token);
                blurRadius = settings.GlowRadius;
                glow = null;
            }
            var blend = (settings.GlowThreshold, settings.GlowIntensity);
            if (glow == null || glowKey != blend)
            {
                glow = ImageOps.BlendGlow(gray, blurred, blend.GlowThreshold, blend.GlowIntensity);
                glowKey = blend;
            }
            gray = glow;
        }
        token.ThrowIfCancellationRequested();
        return gradient is { } lut ? ImageOps.ApplyGradientLut(gray, width, height, lut.r, lut.g, lut.b)
            : ImageOps.GrayToBgra(gray, width, height);
    }

    private static IEnumerable<Tile> CreateTiles(ToneKey key)
    {
        if (!key.Tiled)
        {
            yield return new Tile(0, 0, key.Width, key.Height, 0, 0, key.Width, key.Height);
            yield break;
        }
        int overlap = Math.Max(8, 5 + (key.Smooth + key.Samples - 1) / key.Samples);
        for (int y = 0; y < key.Height; y += TileSize)
        for (int x = 0; x < key.Width; x += TileSize)
        {
            int w = Math.Min(TileSize, key.Width - x), h = Math.Min(TileSize, key.Height - y);
            int left = Math.Max(0, x - overlap), top = Math.Max(0, y - overlap);
            yield return new(x, y, w, h, left, top,
                Math.Min(key.Width - left, w + 2 * overlap), Math.Min(key.Height - top, h + 2 * overlap));
        }
    }

    private static double[] PrepareTone(byte[] source, ToneKey key, Tile tile, CancellationToken token)
    {
        byte[] input = key.Tiled ? CopyRegion(source, key.Width, tile.Left, tile.Top, tile.SourceW, tile.SourceH) : source;
        int sw = checked(tile.SourceW * key.Samples), sh = checked(tile.SourceH * key.Samples);
        var curve = new ImageOps.ContrastCurve(key.Center, key.Steepness);
        double[] tone;
        //при неточном масштабе сохраняем округление билинейной интерполяции
        if (key.Samples == 1 && sw * (1.0 / sw) == 1 && sh * (1.0 / sh) == 1)
        {
            var lut = new double[256];
            for (int i = 0; i < lut.Length; i++) lut[i] = curve.Apply(i * (1.0 / 255.0));
            tone = new double[input.Length];
            Parallel.For(0, sh, new ParallelOptions { CancellationToken = token }, y =>
            {
                for (int i = y * sw; i < (y + 1) * sw; i++) tone[i] = lut[input[i]];
            });
        }
        else
        {
            tone = ImageOps.UpsampleBilinear(input, tile.SourceW, tile.SourceH, sw, sh);
            Parallel.For(0, sh, new ParallelOptions { CancellationToken = token }, y =>
            {
                for (int i = y * sw; i < (y + 1) * sw; i++) tone[i] = curve.Apply(tone[i]);
            });
        }
        return key.Smooth > 0 ? ImageOps.BoxBlurDouble(tone, sw, sh, key.Smooth) : tone;
    }

    private static void RenderPixels(double[] tone, ToneKey key, Tile tile, EffectKey effect,
        Func<EffectPixelContext, byte> compute, IReadOnlyDictionary<string, double> parameters,
        byte[] output, CancellationToken token)
    {
        int ss = key.Samples, stride = tile.SourceW * ss, count = ss * ss;
        var options = new ParallelOptions { CancellationToken = token };
        Parallel.For(0, tile.H, options, y =>
        {
            var ctx = new EffectPixelContext { Sw = key.Width * ss, Sh = key.Height * ss, Params = parameters };
            int py = tile.Y + y;
            for (int x = tile.X; x < tile.X + tile.W; x++)
            {
                int sum = 0;
                for (int dy = 0; dy < ss; dy++)
                {
                    ctx.Sy = py * ss + dy;
                    int start = (ctx.Sy - tile.Top * ss) * stride + (x - tile.Left) * ss;
                    for (int dx = 0; dx < ss; dx++)
                    {
                        double v = tone[start + dx];
                        byte color;
                        if (v < effect.Shadow) color = 0;
                        else if (v > effect.Highlight) color = 255;
                        else
                        {
                            ctx.Sx = x * ss + dx;
                            ctx.Brightness = v;
                            double shadowFade = effect.Shadow <= 0 ? 1 : Math.Clamp((v - effect.Shadow) * 12.5, 0, 1);
                            double highlightFade = effect.Highlight >= 1 ? 1 : Math.Clamp((effect.Highlight - v) * 12.5, 0, 1);
                            ctx.Fade = Math.Min(shadowFade, highlightFade);
                            color = compute(ctx);
                        }
                        sum += effect.Invert ? 255 - color : color;
                    }
                }
                output[py * key.Width + x] = (byte)(sum / count);
            }
        });
    }

    private static byte[] CopyRegion(byte[] source, int stride, int x, int y, int width, int height)
    {
        var region = new byte[width * height];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(source, (y + row) * stride + x, region, row * width, width);
        return region;
    }

    private byte[] BlurTiles(byte[] source, int width, int height, int radius, CancellationToken token)
    {
        if (tiles.Length == 1) return ImageOps.BoxBlur(source, width, height, radius);
        //размываем тайлы отдельно, чтобы сохранить прежний Glow на стыках
        var result = new byte[source.Length];
        foreach (var tile in tiles)
        {
            token.ThrowIfCancellationRequested();
            var region = CopyRegion(source, width, tile.X, tile.Y, tile.W, tile.H);
            var blur = ImageOps.BoxBlur(region, tile.W, tile.H, radius);
            for (int row = 0; row < tile.H; row++)
                Buffer.BlockCopy(blur, row * tile.W, result, (tile.Y + row) * width + tile.X, tile.W);
        }
        return result;
    }
}
