using SkiaSharp;

namespace InfoPanel.Algorand;

internal sealed class CelebrationArt : IDisposable
{
    private readonly SKTypeface _typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    private readonly SKTypeface _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    public void Draw(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo, CelebrationLook look)
    {
        if (alpha <= 0.01f)
            return;

        switch (look)
        {
            case CelebrationLook.Burst:
                DrawBurst(canvas, width, height, t, alpha, kind, algo);
                break;
            case CelebrationLook.Medal:
                DrawMedal(canvas, width, height, t, alpha, kind, algo);
                break;
            case CelebrationLook.Ribbon:
                DrawRibbon(canvas, width, height, t, alpha, kind, algo);
                break;
            case CelebrationLook.Confetti:
                DrawConfetti(canvas, width, height, t, alpha, kind, algo);
                break;
            case CelebrationLook.Ticket:
                DrawTicket(canvas, width, height, t, alpha, kind, algo);
                break;
            case CelebrationLook.Spotlight:
                DrawSpotlight(canvas, width, height, t, alpha, kind, algo);
                break;
            default:
                DrawCard(canvas, width, height, t, alpha, kind, algo, PaletteFor(look));
                break;
        }
    }

    public void DrawMissing(SKCanvas canvas, int width, int height, float alpha)
    {
        Text(canvas, "Choose a celebration picture", width / 2f, height / 2f + 8, 28, new SKColor(214, 228, 226, A(alpha)), true);
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_typeface, SKTypeface.Default))
            _typeface.Dispose();
        if (!ReferenceEquals(_bold, SKTypeface.Default) && !ReferenceEquals(_bold, _typeface))
            _bold.Dispose();
    }

    private void DrawCard(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo, Palette palette)
    {
        float cx = width / 2f;
        float cy = height / 2f;
        if (palette.ShowCard)
        {
            var card = new SKRoundRect(SKRect.Create(width * 0.08f, height * 0.12f, width * 0.84f, height * 0.76f), 32, 32);
            using (var glow = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 6,
                Color = palette.Accent.WithAlpha(A(alpha * 0.35f)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8),
            })
                canvas.DrawRoundRect(card, glow);
            using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(7, 16, 18, A(alpha * 0.9f)) })
                canvas.DrawRoundRect(card, fill);
            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                Color = palette.Accent.WithAlpha(A(alpha * 0.7f)),
            };
            canvas.DrawRoundRect(card, stroke);
        }

        using var arc = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            StrokeCap = SKStrokeCap.Round,
            Color = palette.Highlight.WithAlpha(A(alpha * 0.9f)),
        };
        if (kind == FanfareKind.Reward)
        {
            float sweep = 340f * Ease(Math.Clamp(t / 0.62f, 0f, 1f));
            canvas.DrawArc(SKRect.Create(cx - 150, cy - 46, 300, 108), -90, sweep, false, arc);
            Sparks(canvas, cx, cy + 8, t, alpha, palette.Accent, palette.Highlight);
            Text(canvas, "BLOCK REWARD", cx, cy - 22, 15, palette.Accent.WithAlpha(A(alpha)), false);
            Text(canvas, Amount(algo, t), cx, cy + 22, 60, new SKColor(255, 255, 255, A(alpha)), true);
            Text(canvas, "ALGO", cx, cy + 50, 16, palette.Highlight.WithAlpha(A(alpha)), false);
        }
        else
        {
            canvas.DrawArc(SKRect.Create(cx - 120, cy - 78, 240, 90), 200, 140f * Ease(Math.Clamp(t / 0.5f, 0f, 1f)), false, arc);
            Text(canvas, "BLOCK CREATED", cx, cy + 12, 36, palette.Accent.WithAlpha(A(alpha)), true);
        }
    }

    private void DrawBurst(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo)
    {
        float cx = width / 2f;
        float cy = height / 2f + 6;
        float open = Ease(Math.Clamp(t / 0.45f, 0f, 1f));
        using (var glow = new SKPaint { IsAntialias = true })
        {
            glow.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                90,
                [new SKColor(245, 193, 108, A(alpha * 0.35f)), new SKColor(245, 193, 108, 0)],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(cx, cy, 90, glow);
        }

        using var ray = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 2.5f,
            Color = new SKColor(255, 226, 160, A(alpha * 0.9f)),
        };
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(open * 18f);
        for (int i = 0; i < 16; i++)
        {
            canvas.RotateDegrees(360f / 16f);
            float far = 70f + 150f * open;
            canvas.DrawLine(78, 0, far, 0, ray);
        }
        canvas.Restore();

        if (kind == FanfareKind.Reward)
        {
            Text(canvas, "BLOCK REWARD", cx, cy - 34, 14, new SKColor(255, 226, 160, A(alpha)), false);
            Text(canvas, Amount(algo, t), cx, cy + 18, 64, SKColors.White.WithAlpha(A(alpha)), true);
        }
        else
        {
            Text(canvas, "BLOCK CREATED", cx, cy + 10, 40, SKColors.White.WithAlpha(A(alpha)), true);
        }
    }

    private void DrawMedal(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo)
    {
        float cx = width / 2f;
        float cy = height / 2f + 4;
        float reveal = Ease(Math.Clamp(t / 0.4f, 0f, 1f));
        using var tail = new SKPaint { IsAntialias = true, Color = new SKColor(176, 42, 48, A(alpha)) };
        canvas.DrawRect(cx - 28, cy + 48, 22, 36 * reveal, tail);
        canvas.DrawRect(cx + 6, cy + 48, 22, 36 * reveal, tail);

        using (var coin = new SKPaint { IsAntialias = true })
        {
            coin.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx - 18, cy - 22),
                96,
                [new SKColor(255, 236, 176, A(alpha)), new SKColor(232, 176, 62, A(alpha)), new SKColor(122, 74, 16, A(alpha))],
                [0f, 0.62f, 1f],
                SKShaderTileMode.Clamp);
            canvas.DrawCircle(cx, cy, 78, coin);
        }

        using var ring = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            Color = new SKColor(92, 52, 12, A(alpha * 0.8f)),
        };
        canvas.DrawCircle(cx, cy, 68, ring);
        if (kind == FanfareKind.Reward)
        {
            Text(canvas, Amount(algo, t), cx, cy + 8, 32, new SKColor(62, 36, 8, A(alpha)), true);
            Text(canvas, "ALGO", cx, cy + 32, 13, new SKColor(92, 52, 12, A(alpha)), false);
        }
        else
        {
            Text(canvas, "CREATED", cx, cy + 8, 26, new SKColor(62, 36, 8, A(alpha)), true);
        }
    }

    private void DrawRibbon(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo)
    {
        float open = Ease(Math.Clamp(t / 0.35f, 0f, 1f));
        float cx = width / 2f;
        float ribbonWidth = (width - 80) * open;
        float left = cx - ribbonWidth / 2f;
        float top = height * 0.30f;
        float ribbonHeight = height * 0.40f;
        var band = new SKRoundRect(SKRect.Create(left, top, ribbonWidth, ribbonHeight), 8, 8);
        using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(20, 110, 102, A(alpha)) })
            canvas.DrawRoundRect(band, fill);
        using (var edge = new SKPaint { IsAntialias = true, Color = new SKColor(8, 58, 54, A(alpha)) })
        {
            canvas.DrawRect(left, top, 18, ribbonHeight, edge);
            canvas.DrawRect(left + ribbonWidth - 18, top, 18, ribbonHeight, edge);
        }

        if (open > 0.45f)
        {
            if (kind == FanfareKind.Reward)
            {
                Text(canvas, "BLOCK REWARD", cx, top + 28, 13, new SKColor(186, 242, 230, A(alpha)), false);
                Text(canvas, Amount(algo, t), cx, top + ribbonHeight * 0.62f, 48, SKColors.White.WithAlpha(A(alpha)), true);
            }
            else
            {
                Text(canvas, "BLOCK CREATED", cx, top + ribbonHeight * 0.58f, 34, SKColors.White.WithAlpha(A(alpha)), true);
            }
        }
    }

    private void DrawConfetti(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo)
    {
        var colors = new[]
        {
            new SKColor(46, 230, 199),
            new SKColor(245, 193, 108),
            new SKColor(255, 122, 110),
            new SKColor(170, 220, 255),
        };
        using var paint = new SKPaint { IsAntialias = true };
        for (int i = 0; i < 42; i++)
        {
            float x = 16 + (i * 97 % (width - 32));
            float fall = (i * 53 % height) + t * (90 + (i % 6) * 28);
            float y = fall % (height + 24) - 16;
            paint.Color = colors[i % colors.Length].WithAlpha(A(alpha * (i % 3 == 0 ? 1f : 0.75f)));
            canvas.Save();
            canvas.Translate(x, y);
            canvas.RotateDegrees((i * 28 + t * 80) % 180);
            canvas.DrawRoundRect(-6, -2.5f, i % 2 == 0 ? 12 : 8, 5, 2, 2, paint);
            canvas.Restore();
        }

        var pill = new SKRoundRect(SKRect.Create(width * 0.22f, height * 0.28f, width * 0.56f, height * 0.46f), 24, 24);
        using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(7, 16, 18, A(alpha * 0.82f)) })
            canvas.DrawRoundRect(pill, fill);
        float cx = width / 2f;
        if (kind == FanfareKind.Reward)
        {
            Text(canvas, Amount(algo, t), cx, height * 0.50f, 52, SKColors.White.WithAlpha(A(alpha)), true);
            Text(canvas, "ALGO", cx, height * 0.66f, 16, new SKColor(245, 193, 108, A(alpha)), false);
        }
        else
        {
            Text(canvas, "BLOCK CREATED", cx, height * 0.54f, 32, SKColors.White.WithAlpha(A(alpha)), true);
        }
    }

    private void DrawTicket(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo)
    {
        float left = width * 0.12f;
        float top = height * 0.16f;
        float w = width * 0.76f;
        float h = height * 0.68f;
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRoundRect(new SKRoundRect(SKRect.Create(left, top, w, h), 16, 16));
        path.AddCircle(left, top + h / 2f, 14);
        path.AddCircle(left + w, top + h / 2f, 14);
        using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(246, 241, 228, A(alpha)) };
        canvas.DrawPath(path, fill);

        using var dash = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash([4, 6], 0),
            Color = new SKColor(176, 156, 124, A(alpha)),
        };
        canvas.DrawLine(left + 54, top + 16, left + 54, top + h - 16, dash);

        float cx = left + w * 0.58f;
        var ink = new SKColor(28, 36, 34, A(alpha));
        if (kind == FanfareKind.Reward)
        {
            Text(canvas, "PAYOUT", cx, top + 36, 13, new SKColor(120, 96, 64, A(alpha)), false);
            Text(canvas, Amount(algo, t), cx, top + h * 0.58f, 48, ink, true);
            Text(canvas, "ALGO", cx, top + h * 0.78f, 14, new SKColor(120, 96, 64, A(alpha)), false);
        }
        else
        {
            Text(canvas, "BLOCK CREATED", cx, top + h * 0.56f, 28, ink, true);
        }
    }

    private void DrawSpotlight(SKCanvas canvas, int width, int height, float t, float alpha, FanfareKind kind, double algo)
    {
        float cx = width / 2f;
        float cy = height / 2f;
        using (var beam = new SKPaint { IsAntialias = true })
        {
            beam.Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                480,
                [new SKColor(255, 244, 214, A(alpha)), new SKColor(245, 193, 108, A(alpha * 0.22f)), new SKColor(245, 193, 108, 0)],
                [0f, 0.34f, 1f],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, beam);
        }

        var ink = new SKColor(42, 28, 8, A(alpha));
        if (kind == FanfareKind.Reward)
        {
            Text(canvas, Amount(algo, t), cx, cy + 8, 58, ink, true);
            Text(canvas, "BLOCK REWARD", cx, cy + 36, 14, new SKColor(92, 62, 18, A(alpha)), false);
        }
        else
        {
            Text(canvas, "BLOCK CREATED", cx, cy + 8, 36, ink, true);
        }
    }

    private void Sparks(SKCanvas canvas, float cx, float cy, float t, float alpha, SKColor accent, SKColor highlight)
    {
        using var paint = new SKPaint { IsAntialias = true };
        float travel = Ease(Math.Clamp(t / 0.7f, 0f, 1f));
        for (int i = 0; i < 18; i++)
        {
            double angle = (i / 18.0) * Math.PI * 2.0 - Math.PI / 2.0;
            float radius = 24f + 126f * travel;
            float x = cx + (float)Math.Cos(angle) * radius;
            float y = cy + (float)Math.Sin(angle) * radius * 0.36f;
            paint.Color = (i % 2 == 0 ? highlight : accent).WithAlpha(A(alpha * (i % 3 == 0 ? 1f : 0.55f)));
            canvas.DrawCircle(x, y, i % 3 == 0 ? 3.2f : 2f, paint);
        }
    }

    private void Text(SKCanvas canvas, string text, float cx, float baseline, float size, SKColor color, bool bold)
    {
        using var font = new SKFont(bold ? _bold : _typeface, size);
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        float width = font.MeasureText(text);
        canvas.DrawText(text, cx - width / 2f, baseline, font, paint);
    }

    private static string Amount(double algo, float t)
    {
        float shown = (float)(algo * Ease(Math.Clamp(t / 0.5f, 0f, 1f)));
        return shown.ToString("+0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    private static byte A(float alpha) => (byte)Math.Clamp(alpha * 255f, 0f, 255f);

    private static Palette PaletteFor(CelebrationLook look) => look switch
    {
        CelebrationLook.Gold => new(new SKColor(245, 193, 108), new SKColor(255, 244, 214), true),
        CelebrationLook.Ice => new(new SKColor(170, 220, 255), SKColors.White, true),
        CelebrationLook.Ember => new(new SKColor(255, 122, 46), new SKColor(255, 214, 140), true),
        CelebrationLook.Plain => new(new SKColor(46, 230, 199), new SKColor(245, 193, 108), false),
        _ => new(new SKColor(46, 230, 199), new SKColor(245, 193, 108), true),
    };

    private readonly record struct Palette(SKColor Accent, SKColor Highlight, bool ShowCard);
}
