using SkiaSharp;

namespace InfoPanel.Algorand;

internal static class PriceChart
{
    private static readonly SKColor Up = new(46, 230, 199);
    private static readonly SKColor Down = new(255, 122, 110);

    public static void Draw(SKCanvas canvas, int width, int height, string title, (long Unix, decimal Price)[] points, long fromUnix, long toUnix)
    {
        canvas.Clear(SKColors.Transparent);
        var card = new SKRoundRect(SKRect.Create(8, 8, width - 16, height - 16), 22, 22);
        using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(7, 16, 18, 230) })
            canvas.DrawRoundRect(card, fill);

        using var typeface = SKTypeface.FromFamilyName("Segoe UI");
        using var font = new SKFont(typeface ?? SKTypeface.Default, 15);
        using var titlePaint = new SKPaint { IsAntialias = true, Color = new SKColor(170, 196, 194) };
        canvas.DrawText(title, 28, 34, font, titlePaint);

        if (points.Length < 2 || toUnix <= fromUnix)
        {
            DrawMessage(canvas, width, height, "Waiting for price history");
            return;
        }

        decimal min = points[0].Price;
        decimal max = points[0].Price;
        foreach (var point in points)
        {
            if (point.Price < min) min = point.Price;
            if (point.Price > max) max = point.Price;
        }

        decimal pad = Math.Max((max - min) * 0.12m, points[^1].Price * 0.002m);
        min -= pad;
        max += pad;
        if (max <= min)
            max = min + 1;

        var line = points[^1].Price >= points[0].Price ? Up : Down;
        float left = 24;
        float right = width - 24;
        float top = 48;
        float bottom = height - 22;
        float spanX = Math.Max(1, right - left);
        float spanY = Math.Max(1, bottom - top);
        long spanT = toUnix - fromUnix;

        using var path = new SKPath();
        bool started = false;
        float firstX = left;
        float lastX = left;
        foreach (var point in points)
        {
            float x = left + (point.Unix - fromUnix) / (float)spanT * spanX;
            float y = bottom - (float)((point.Price - min) / (max - min)) * spanY;
            x = Math.Clamp(x, left, right);
            y = Math.Clamp(y, top, bottom);
            if (!started)
            {
                path.MoveTo(x, y);
                firstX = x;
                started = true;
            }
            else
            {
                path.LineTo(x, y);
            }

            lastX = x;
        }

        using var fillPath = new SKPath(path);
        fillPath.LineTo(lastX, bottom);
        fillPath.LineTo(firstX, bottom);
        fillPath.Close();

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, top),
            new SKPoint(0, bottom),
            [line.WithAlpha(70), line.WithAlpha(0)],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = shader,
        };
        canvas.DrawPath(fillPath, fillPaint);

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = line,
        };
        canvas.DrawPath(path, stroke);
    }

    private static void DrawMessage(SKCanvas canvas, int width, int height, string message)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI");
        using var font = new SKFont(typeface ?? SKTypeface.Default, 16);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(170, 196, 194) };
        float textWidth = font.MeasureText(message);
        canvas.DrawText(message, (width - textWidth) / 2f, height / 2f + 6, font, paint);
    }
}
