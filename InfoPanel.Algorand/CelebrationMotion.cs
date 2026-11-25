namespace InfoPanel.Algorand;

internal readonly record struct MotionPose(float Alpha, float Scale, float OffsetX, float OffsetY);

internal static class CelebrationMotion
{
    public static MotionPose At(string intro, string outro, float t)
    {
        const float introEnd = 0.14f;
        const float outroStart = 0.78f;
        if (t < introEnd)
            return Enter(intro, Ease(t / introEnd));
        if (t > outroStart)
            return Exit(outro, Ease((t - outroStart) / (1f - outroStart)));
        return new MotionPose(1f, 1f, 0f, 0f);
    }

    private static MotionPose Enter(string intro, float p) => intro switch
    {
        "Pop" => new(p, 0.55f + 0.45f * BackOut(p), 0f, 0f),
        "Rise" => new(p, 1f, 0f, (1f - p) * 46f),
        "Drop" => new(p, 1f, 0f, (1f - p) * -52f),
        "Zoom" => new(p, 1.45f - 0.45f * p, 0f, 0f),
        "Slide in" => new(Math.Min(1f, p * 1.5f), 1f, (1f - p) * -220f, 0f),
        "Snap" => new(1f, 1f, 0f, 0f),
        _ => new(p, 0.97f + 0.03f * p, 0f, 0f),
    };

    private static MotionPose Exit(string outro, float p) => outro switch
    {
        "Shrink" => new(1f - p, 1f - 0.4f * p, 0f, 0f),
        "Fall" => new(1f - p, 1f, 0f, p * 56f),
        "Zoom out" => new(1f - p, 1f + 0.4f * p, 0f, 0f),
        "Slide out" => new(1f - p * 0.25f, 1f, p * 240f, 0f),
        "Snap" => new(1f, 1f, 0f, 0f),
        _ => new(1f - p, 1f, 0f, 0f),
    };

    private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    private static float BackOut(float t)
    {
        const float c = 1.35f;
        float u = t - 1f;
        return 1f + (c + 1f) * u * u * u + c * u * u;
    }
}
