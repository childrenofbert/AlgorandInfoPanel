using SkiaSharp;

namespace InfoPanel.Algorand;

internal enum FanfareKind
{
    None,
    Reward,
    Created,
}

internal enum CelebrationLook
{
    TealGold,
    Gold,
    Ice,
    Ember,
    Plain,
}

internal sealed class RewardPainter : IDisposable
{
    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    private readonly SKTypeface _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    public void DrawIdle(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);
    }

    public void Draw(SKCanvas canvas, int width, int height, float t, FanfareKind kind, double algo, CelebrationLook look)
    {
        canvas.Clear(SKColors.Transparent);
        float fade = Fade(t);
        if (fade <= 0.01f)
            return;

        var palette = PaletteFor(look);
        float cx = width / 2f;
        float cy = height / 2f;
        float scale = 0.94f + 0.06f * fade;

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(scale);
        canvas.Translate(-cx, -cy);

        if (palette.ShowCard)
            DrawCard(canvas, width, height, fade, palette);

        using var arc = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            StrokeCap = SKStrokeCap.Round,
            Color = palette.Highlight.WithAlpha((byte)(220 * fade)),
        };

        if (kind == FanfareKind.Reward)
        {
            var oval = SKRect.Create(cx - 150, cy - 46, 300, 108);
            float sweep = 340f * Ease(Math.Clamp(t / 0.62f, 0f, 1f));
            canvas.DrawArc(oval, -90, sweep, false, arc);
            DrawSparks(canvas, cx, cy + 8, t, fade, palette);
            DrawLabel(canvas, "BLOCK REWARD", cx, cy - 22, 15, palette.Accent.WithAlpha((byte)(220 * fade)), false);
            float shown = (float)(algo * Ease(Math.Clamp(t / 0.5f, 0f, 1f)));
            DrawLabel(canvas, shown.ToString("+0.000", System.Globalization.CultureInfo.InvariantCulture), cx, cy + 22, 60, new SKColor(255, 255, 255, (byte)(255 * fade)), true);
            DrawLabel(canvas, "ALGO", cx, cy + 50, 16, palette.Highlight.WithAlpha((byte)(230 * fade)), false);
        }
        else
        {
            var smile = SKRect.Create(cx - 120, cy - 78, 240, 90);
            float sweep = 140f * Ease(Math.Clamp(t / 0.5f, 0f, 1f));
            canvas.DrawArc(smile, 200, sweep, false, arc);
            DrawLabel(canvas, "BLOCK CREATED", cx, cy + 12, 36, palette.Accent.WithAlpha((byte)(255 * fade)), true);
        }

        canvas.Restore();
    }

    private static void DrawCard(SKCanvas canvas, int width, int height, float fade, Palette palette)
    {
        var card = new SKRoundRect(SKRect.Create(width * 0.08f, height * 0.12f, width * 0.84f, height * 0.76f), 32, 32);
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6,
            Color = palette.Accent.WithAlpha((byte)(80 * fade)),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8),
        })
            canvas.DrawRoundRect(card, glow);

        using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(7, 16, 18, (byte)(230 * fade)) })
            canvas.DrawRoundRect(card, fill);

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = palette.Accent.WithAlpha((byte)(160 * fade)),
        };
        canvas.DrawRoundRect(card, stroke);
    }

    private void DrawSparks(SKCanvas canvas, float cx, float cy, float t, float fade, Palette palette)
    {
        using var paint = new SKPaint { IsAntialias = true };
        float travel = Ease(Math.Clamp(t / 0.7f, 0f, 1f));
        for (int i = 0; i < 18; i++)
        {
            double angle = (i / 18.0) * Math.PI * 2.0 - Math.PI / 2.0;
            float radius = 24f + 126f * travel;
            float x = cx + (float)Math.Cos(angle) * radius;
            float y = cy + (float)Math.Sin(angle) * radius * 0.36f;
            byte alpha = (byte)(255 * fade * (0.35f + 0.65f * (i % 3 == 0 ? 1f : 0.55f)));
            paint.Color = (i % 2 == 0 ? palette.Highlight : palette.Accent).WithAlpha(alpha);
            canvas.DrawCircle(x, y, i % 3 == 0 ? 3.2f : 2f, paint);
        }
    }

    private void DrawLabel(SKCanvas canvas, string text, float cx, float baseline, float size, SKColor color, bool bold)
    {
        using var font = new SKFont(bold ? _bold : _typeface, size);
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        float width = font.MeasureText(text);
        canvas.DrawText(text, cx - width / 2f, baseline, font, paint);
    }

    private static float Fade(float t)
    {
        if (t < 0.08f)
            return t / 0.08f;
        if (t > 0.82f)
            return Math.Clamp((1f - t) / 0.18f, 0f, 1f);
        return 1f;
    }

    private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    private static Palette PaletteFor(CelebrationLook look) => look switch
    {
        CelebrationLook.Gold => new(new SKColor(245, 193, 108), new SKColor(255, 244, 214), true),
        CelebrationLook.Ice => new(new SKColor(170, 220, 255), SKColors.White, true),
        CelebrationLook.Ember => new(new SKColor(255, 122, 46), new SKColor(255, 214, 140), true),
        CelebrationLook.Plain => new(new SKColor(46, 230, 199), new SKColor(245, 193, 108), false),
        _ => new(new SKColor(46, 230, 199), new SKColor(245, 193, 108), true),
    };

    private readonly record struct Palette(SKColor Accent, SKColor Highlight, bool ShowCard);

    public void Dispose()
    {
        if (!ReferenceEquals(_typeface, SKTypeface.Default))
            _typeface.Dispose();
        if (!ReferenceEquals(_bold, SKTypeface.Default) && !ReferenceEquals(_bold, _typeface))
            _bold.Dispose();
    }
}
