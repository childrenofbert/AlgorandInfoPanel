using SkiaSharp;

namespace InfoPanel.Algorand;

internal static class SceneArt
{
    private static readonly SKColor Card = new(7, 16, 18, 230);
    private static readonly SKColor Teal = new(46, 230, 199);
    private static readonly SKColor Gold = new(245, 193, 108);
    private static readonly SKColor Red = new(255, 122, 110);
    private static readonly SKColor Ink = new(214, 228, 226);
    private static readonly SKColor Dim = new(140, 166, 164);
    private static readonly SKColor Track = new(46, 72, 74);

    public static void Luck(SKCanvas canvas, int width, int height, bool known, double delta, string label)
    {
        CardFill(canvas, width, height);
        using var typeface = Face();
        using var wordFont = new SKFont(typeface, 28);
        using var wordPaint = new SKPaint { IsAntialias = true, Color = known ? Ink : Dim };
        DrawCentered(canvas, known ? label : "Waiting", width / 2f, 52, wordFont, wordPaint);

        float left = 72;
        float right = width - 72;
        float y = height - 52;
        DrawTrack(canvas, left, right, y, 16);
        float mid = (left + right) / 2f;
        using (var tick = new SKPaint { IsAntialias = true, Color = Dim, StrokeWidth = 2 })
            canvas.DrawLine(mid, y - 16, mid, y + 16, tick);

        if (!known)
            return;

        float lean = Math.Clamp((float)(delta / 2d), -1f, 1f);
        float tip = mid + lean * (right - left) / 2f;
        var color = delta > 0.05 ? Teal : delta < -0.05 ? Red : Gold;
        using var fill = new SKPaint { IsAntialias = true, Color = color, StrokeCap = SKStrokeCap.Round, StrokeWidth = 16 };
        canvas.DrawLine(mid, y, tip, y, fill);
        canvas.DrawCircle(tip, y, 11, fill);
    }

    public static void Streak(SKCanvas canvas, int width, int height, int[] days, int streak)
    {
        CardFill(canvas, width, height);
        using var typeface = Face();
        using var wordFont = new SKFont(typeface, 22);
        using var dayFont = new SKFont(typeface, 13);
        using var wordPaint = new SKPaint { IsAntialias = true, Color = Ink };
        using var dayPaint = new SKPaint { IsAntialias = true, Color = Dim };
        using var todayPaint = new SKPaint { IsAntialias = true, Color = Gold };
        string word = streak == 1 ? "1 DAY" : streak.ToString() + " DAYS";
        canvas.DrawText(word, 28, 40, wordFont, wordPaint);

        if (days.Length == 0)
            return;

        float left = 36;
        float right = width - 36;
        float slot = (right - left) / days.Length;
        float cy = height - 58;
        float radius = Math.Min(14, slot * 0.32f);
        var today = DateTime.Today.AddDays(1 - days.Length);
        using var fill = new SKPaint { IsAntialias = true };
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };

        for (int i = 0; i < days.Length; i++)
        {
            bool isToday = i == days.Length - 1;
            bool paid = days[i] > 0;
            float cx = left + slot * i + slot / 2f;
            if (paid)
            {
                fill.Color = Gold;
                canvas.DrawCircle(cx, cy, radius, fill);
            }
            else
            {
                ring.Color = isToday ? Gold : Track;
                canvas.DrawCircle(cx, cy, radius, ring);
            }

            string day = today.AddDays(i).Day.ToString();
            float textWidth = dayFont.MeasureText(day);
            canvas.DrawText(day, cx - textWidth / 2f, height - 22, dayFont, isToday ? todayPaint : dayPaint);
        }
    }

    public static void Share(SKCanvas canvas, int width, int height, bool known, string percent, float fill, string scale)
    {
        CardFill(canvas, width, height);
        using var typeface = Face();
        using var wordFont = new SKFont(typeface, 32);
        using var noteFont = new SKFont(typeface, 15);
        using var wordPaint = new SKPaint { IsAntialias = true, Color = known ? Ink : Dim };
        using var notePaint = new SKPaint { IsAntialias = true, Color = Dim };
        DrawCentered(canvas, known ? percent : "Waiting", width / 2f, 48, wordFont, wordPaint);

        float left = 48;
        float right = width - 48;
        float y = height - 46;
        DrawTrack(canvas, left, right, y, 12);
        if (!known)
            return;

        float tip = left + Math.Clamp(fill, 0f, 1f) * (right - left);
        using var bar = new SKPaint { IsAntialias = true, Color = Teal, StrokeCap = SKStrokeCap.Round, StrokeWidth = 12 };
        if (tip - left > 2)
            canvas.DrawLine(left, y, tip, y, bar);
        canvas.DrawCircle(Math.Max(left, tip), y, 8, bar);
        DrawCentered(canvas, scale, width / 2f, height - 18, noteFont, notePaint);
    }

    public static void Upgrade(SKCanvas canvas, int width, int height, float fill, bool alert, string center, string side)
    {
        CardFill(canvas, width, height);
        float size = Math.Min(height - 36, 150);
        float cx = 28 + size / 2f;
        float cy = height / 2f;
        var oval = new SKRect(cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f);
        using var track = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 12,
            Color = Track,
        };
        canvas.DrawArc(oval, -90, 360, false, track);
        if (fill > 0.01f)
        {
            using var arc = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 12,
                StrokeCap = SKStrokeCap.Round,
                Color = alert ? Red : Teal,
            };
            canvas.DrawArc(oval, -90, Math.Clamp(fill, 0f, 1f) * 360f, false, arc);
        }

        using var typeface = Face();
        using var centerFont = new SKFont(typeface, center.Length > 4 ? 18 : 26);
        using var sideFont = new SKFont(typeface, 24);
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        using var dim = new SKPaint { IsAntialias = true, Color = Dim };
        DrawCentered(canvas, center, cx, cy + 8, centerFont, ink);

        float textLeft = cx + size / 2f + 28;
        float textWidth = sideFont.MeasureText(side);
        float room = width - 28 - textLeft;
        var font = textWidth > room ? new SKFont(typeface, 18) : sideFont;
        canvas.DrawText(side, textLeft, cy + 8, font, side == "Waiting" ? dim : ink);
        if (!ReferenceEquals(font, sideFont))
            font.Dispose();
    }

    public static void Spark(SKCanvas canvas, int width, int height, (long Minute, long Micro)[] payouts, long fromMinute, long toMinute)
    {
        CardFill(canvas, width, height);
        using var typeface = Face();
        using var wordFont = new SKFont(typeface, 15);
        using var wordPaint = new SKPaint { IsAntialias = true, Color = Dim };
        canvas.DrawText("PAYOUTS", 28, 36, wordFont, wordPaint);

        float left = 36;
        float right = width - 36;
        float bottom = height - 28;
        float top = 52;
        using var baseLine = new SKPaint { IsAntialias = true, Color = Track, StrokeWidth = 2 };
        canvas.DrawLine(left, bottom, right, bottom, baseLine);

        if (payouts.Length == 0 || toMinute <= fromMinute)
        {
            using var font = new SKFont(typeface, 16);
            using var paint = new SKPaint { IsAntialias = true, Color = Dim };
            DrawCentered(canvas, "No payouts yet", width / 2f, (top + bottom) / 2f, font, paint);
            return;
        }

        long max = 1;
        foreach (var payout in payouts)
        {
            if (payout.Micro > max)
                max = payout.Micro;
        }

        using var mark = new SKPaint { IsAntialias = true, Color = Gold, StrokeCap = SKStrokeCap.Round, StrokeWidth = 3 };
        using var dot = new SKPaint { IsAntialias = true, Color = Gold };
        float span = right - left;
        float plot = bottom - top;
        foreach (var payout in payouts)
        {
            float x = left + (payout.Minute - fromMinute) / (float)(toMinute - fromMinute) * span;
            float stem = Math.Max(8, payout.Micro / (float)max * plot);
            canvas.DrawLine(x, bottom, x, bottom - stem, mark);
            canvas.DrawCircle(x, bottom - stem, 4.5f, dot);
        }
    }

    private static void CardFill(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);
        var card = new SKRoundRect(SKRect.Create(8, 8, width - 16, height - 16), 22, 22);
        using var fill = new SKPaint { IsAntialias = true, Color = Card };
        canvas.DrawRoundRect(card, fill);
    }

    private static void DrawTrack(SKCanvas canvas, float left, float right, float y, float thickness)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = Track, StrokeCap = SKStrokeCap.Round, StrokeWidth = thickness };
        canvas.DrawLine(left, y, right, y, paint);
    }

    private static void DrawCentered(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint)
    {
        float textWidth = font.MeasureText(text);
        canvas.DrawText(text, x - textWidth / 2f, baseline, font, paint);
    }

    private static SKTypeface Face() => SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.CreateDefault();
}
