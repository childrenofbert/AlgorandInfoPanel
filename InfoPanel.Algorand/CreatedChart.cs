using SkiaSharp;

namespace InfoPanel.Algorand;

internal static class CreatedChart
{
    private static readonly SKColor Bar = new(46, 230, 199);
    private static readonly SKColor Today = new(245, 193, 108);
    private static readonly SKColor Empty = new(46, 72, 74);
    private static readonly SKColor Label = new(214, 228, 226);

    public static void Draw(SKCanvas canvas, int width, int height, int[] days)
    {
        canvas.Clear(SKColors.Transparent);
        var card = new SKRoundRect(SKRect.Create(8, 8, width - 16, height - 16), 22, 22);
        using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(7, 16, 18, 230) })
            canvas.DrawRoundRect(card, fill);

        using var typeface = SKTypeface.FromFamilyName("Segoe UI");

        if (days.Length == 0)
            return;

        int max = 0;
        foreach (int count in days)
        {
            if (count > max)
                max = count;
        }

        float left = 28;
        float right = width - 28;
        float top = 36;
        float bottom = height - 34;
        float slot = Math.Max(1, (right - left) / days.Length);
        float barWidth = Math.Max(2, slot - 4);
        float span = Math.Max(1, bottom - top);
        var today = DateTime.Today.AddDays(1 - days.Length);

        using var countFont = new SKFont(typeface ?? SKTypeface.Default, barWidth >= 16 ? 13 : 11);
        using var dayFont = new SKFont(typeface ?? SKTypeface.Default, 11);
        using var countPaint = new SKPaint { IsAntialias = true, Color = Label };
        using var dayPaint = new SKPaint { IsAntialias = true, Color = new SKColor(140, 166, 164) };
        using var barPaint = new SKPaint { IsAntialias = true };

        for (int i = 0; i < days.Length; i++)
        {
            bool isToday = i == days.Length - 1;
            int count = days[i];
            float heightPx = count == 0 || max == 0 ? 3 : Math.Max(3, count / (float)max * span);
            float x = left + i * slot + (slot - barWidth) / 2f;
            float y = bottom - heightPx;
            barPaint.Color = count == 0 ? Empty : isToday ? Today : Bar;

            var rect = new SKRoundRect(SKRect.Create(x, y, barWidth, heightPx), 3, 3);
            canvas.DrawRoundRect(rect, barPaint);

            if (count > 0 && barWidth >= 12)
            {
                string text = count.ToString();
                float textWidth = countFont.MeasureText(text);
                canvas.DrawText(text, x + (barWidth - textWidth) / 2f, y - 6, countFont, countPaint);
            }

            if (barWidth >= 14)
            {
                string day = today.AddDays(i).Day.ToString();
                float textWidth = dayFont.MeasureText(day);
                canvas.DrawText(day, x + (barWidth - textWidth) / 2f, height - 16, dayFont, dayPaint);
            }
        }
    }
}
