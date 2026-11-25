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
    Burst,
    Medal,
    Ribbon,
    Confetti,
    Ticket,
    Spotlight,
    Custom,
}

internal sealed class RewardPainter : IDisposable
{
    private readonly CelebrationArt _art = new();

    public void DrawIdle(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);
    }

    public void Draw(
        SKCanvas canvas,
        int width,
        int height,
        float t,
        FanfareKind kind,
        double algo,
        CelebrationLook look,
        string intro,
        string outro,
        CelebrationPicture? picture,
        double elapsedSeconds)
    {
        canvas.Clear(SKColors.Transparent);
        var motion = CelebrationMotion.At(intro, outro, t);
        if (motion.Alpha <= 0.01f)
            return;

        float cx = width / 2f;
        float cy = height / 2f;
        canvas.Save();
        canvas.Translate(motion.OffsetX, motion.OffsetY);
        canvas.Translate(cx, cy);
        canvas.Scale(Math.Max(0.05f, motion.Scale));
        canvas.Translate(-cx, -cy);

        if (look == CelebrationLook.Custom)
        {
            if (picture == null || !picture.Draw(canvas, width, height, motion.Alpha, elapsedSeconds))
                _art.DrawMissing(canvas, width, height, motion.Alpha);
        }
        else
        {
            _art.Draw(canvas, width, height, t, motion.Alpha, kind, algo, look);
        }

        canvas.Restore();
    }

    public void Dispose() => _art.Dispose();
}
