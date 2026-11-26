namespace InfoPanel.Algorand;

internal sealed class RoundClock
{
    private DateTimeOffset? _at;
    private long _eventRound;
    private long _seenRound;
    private double _pace;

    public void Note(long eventRound, long lastRound, double pace)
    {
        if (eventRound <= 0 || lastRound < eventRound)
            return;

        bool changed = eventRound != _eventRound
            || lastRound != _seenRound
            || Math.Abs(pace - _pace) > 0.05;
        if (!changed && _at.HasValue)
            return;

        double ageSeconds = (lastRound - eventRound) * pace;
        _at = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(ageSeconds);
        _eventRound = eventRound;
        _seenRound = lastRound;
        _pace = pace;
    }

    public string Text(string format) =>
        _at is DateTimeOffset at
            ? AlgoFormat.Age(DateTimeOffset.UtcNow - at, format)
            : "—";
}
