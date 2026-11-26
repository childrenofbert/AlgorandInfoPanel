using System.Globalization;

namespace InfoPanel.Algorand;

internal enum Period
{
    Hour,
    Day,
    Week,
    Month,
    Quarter,
    Year,
}

internal sealed class StatsStore
{
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly Dictionary<long, Bucket> _minutes = new();
    private readonly string _path;

    private long _logPosition;
    private bool _archiveDone;
    private bool _hasReward;
    private long _lastRewardMicro;
    private long _lastCreatedUnix;

    public StatsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InfoPanel", "plugins");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "algorand-node.history.bin");
        Load();
    }

    public long LogPosition
    {
        get { lock (_gate) return _logPosition; }
    }

    public bool ArchiveDone
    {
        get { lock (_gate) return _archiveDone; }
    }

    public bool HasReward
    {
        get { lock (_gate) return _hasReward; }
    }

    public long LastRewardMicro
    {
        get { lock (_gate) return _lastRewardMicro; }
    }

    public long LastCreatedUnix
    {
        get { lock (_gate) return _lastCreatedUnix; }
    }

    public void Add(DateTimeOffset when, int votes, int proposals, int created, long rewardMicro)
    {
        if (votes == 0 && proposals == 0 && created == 0)
            return;

        long minute = when.ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            if (!_minutes.TryGetValue(minute, out var bucket))
                bucket = default;

            bucket.Votes += votes;
            bucket.Proposals += proposals;
            bucket.Created += created;
            if (created > 0)
            {
                bucket.RewardMicro += rewardMicro;
                _lastRewardMicro = rewardMicro;
                _hasReward = true;
                long unix = when.ToUnixTimeSeconds();
                if (unix > _lastCreatedUnix)
                    _lastCreatedUnix = unix;
            }

            _minutes[minute] = bucket;
        }
    }

    public void SetLogPosition(long position)
    {
        lock (_gate)
            _logPosition = position;
    }

    public void MarkArchiveDone()
    {
        lock (_gate)
            _archiveDone = true;
    }

    public int Count(Period period, DateTimeOffset now, CountKind kind)
    {
        var window = period switch
        {
            Period.Hour => TimeSpan.FromHours(1),
            Period.Day => TimeSpan.FromHours(24),
            Period.Week => TimeSpan.FromDays(7),
            Period.Month => TimeSpan.FromDays(30),
            Period.Quarter => TimeSpan.FromDays(90),
            Period.Year => TimeSpan.FromDays(365),
            _ => TimeSpan.FromHours(1),
        };
        return Count(window, now, kind);
    }

    public int Count(TimeSpan window, DateTimeOffset now, CountKind kind)
    {
        long start = (now - window).ToUnixTimeSeconds() / 60;
        long end = now.ToUnixTimeSeconds() / 60;
        int total = 0;
        lock (_gate)
        {
            foreach (var pair in _minutes)
            {
                if (pair.Key < start || pair.Key > end)
                    continue;
                total += kind switch
                {
                    CountKind.Votes => pair.Value.Votes,
                    CountKind.Proposals => pair.Value.Proposals,
                    _ => pair.Value.Created,
                };
            }
        }

        return total;
    }

    public (long Minute, long Micro)[] Payouts(TimeSpan window, DateTimeOffset now)
    {
        long start = (now - window).ToUnixTimeSeconds() / 60;
        long end = now.ToUnixTimeSeconds() / 60;
        var found = new List<(long Minute, long Micro)>();
        lock (_gate)
        {
            foreach (var pair in _minutes)
            {
                if (pair.Key < start || pair.Key > end || pair.Value.RewardMicro <= 0)
                    continue;
                found.Add((pair.Key, pair.Value.RewardMicro));
            }
        }

        found.Sort((a, b) => a.Minute.CompareTo(b.Minute));
        return found.ToArray();
    }

    public int[] CreatedByDay(int days, DateTimeOffset now)
    {
        if (days < 1)
            days = 1;
        var today = now.ToLocalTime().Date;
        var start = today.AddDays(1 - days);
        var counts = new int[days];
        lock (_gate)
        {
            foreach (var pair in _minutes)
            {
                if (pair.Value.Created == 0)
                    continue;
                var day = DateTimeOffset.FromUnixTimeSeconds(pair.Key * 60).ToLocalTime().Date;
                int index = (int)(day - start).TotalDays;
                if ((uint)index < (uint)days)
                    counts[index] += pair.Value.Created;
            }
        }

        return counts;
    }

    public long SumReward(TimeSpan window, DateTimeOffset now)
    {
        long start = (now - window).ToUnixTimeSeconds() / 60;
        long end = now.ToUnixTimeSeconds() / 60;
        long total = 0;
        lock (_gate)
        {
            foreach (var pair in _minutes)
            {
                if (pair.Key < start || pair.Key > end || pair.Value.Created == 0)
                    continue;
                total += pair.Value.RewardMicro;
            }
        }

        return total;
    }

    public TimeSpan HistoryLength(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_minutes.Count == 0)
                return TimeSpan.Zero;
            long oldest = long.MaxValue;
            foreach (long key in _minutes.Keys)
            {
                if (key < oldest)
                    oldest = key;
            }

            long nowMinute = now.ToUnixTimeSeconds() / 60;
            return TimeSpan.FromMinutes(Math.Max(0, nowMinute - oldest));
        }
    }

    public int PayoutStreak(DateTimeOffset now)
    {
        var counts = CreatedByDay(400, now);
        int index = counts.Length - 1;
        if (index >= 0 && counts[index] == 0)
            index--;
        int streak = 0;
        for (int i = index; i >= 0; i--)
        {
            if (counts[i] == 0)
                break;
            streak++;
        }

        return streak;
    }

    public void Save()
    {
        lock (_fileGate)
            SaveCore();
    }

    private void SaveCore()
    {
        Bucket[] minutes;
        long[] keys;
        long logPosition;
        bool archiveDone;
        bool hasReward;
        long lastReward;
        long lastCreatedUnix;
        lock (_gate)
        {
            keys = new long[_minutes.Count];
            minutes = new Bucket[_minutes.Count];
            int i = 0;
            foreach (var pair in _minutes)
            {
                keys[i] = pair.Key;
                minutes[i] = pair.Value;
                i++;
            }

            logPosition = _logPosition;
            archiveDone = _archiveDone;
            hasReward = _hasReward;
            lastReward = _lastRewardMicro;
            lastCreatedUnix = _lastCreatedUnix;
        }

        var tmp = _path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x414C4731); // ALG1
            writer.Write(2);
            writer.Write(logPosition);
            writer.Write(archiveDone);
            writer.Write(hasReward);
            writer.Write(lastReward);
            writer.Write(lastCreatedUnix);
            writer.Write(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                writer.Write(keys[i]);
                writer.Write(minutes[i].Votes);
                writer.Write(minutes[i].Proposals);
                writer.Write(minutes[i].Created);
                writer.Write(minutes[i].RewardMicro);
            }
        }

        File.Move(tmp, _path, overwrite: true);
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != 0x414C4731)
                return;
            int version = reader.ReadInt32();
            if (version is < 1 or > 2)
                return;

            _logPosition = reader.ReadInt64();
            _archiveDone = reader.ReadBoolean();
            _hasReward = reader.ReadBoolean();
            _lastRewardMicro = reader.ReadInt64();
            if (version >= 2)
                _lastCreatedUnix = reader.ReadInt64();
            int count = reader.ReadInt32();
            if (count < 0 || count > 2_000_000)
                return;

            for (int i = 0; i < count; i++)
            {
                long key = reader.ReadInt64();
                var bucket = new Bucket
                {
                    Votes = reader.ReadInt32(),
                    Proposals = reader.ReadInt32(),
                    Created = reader.ReadInt32(),
                    RewardMicro = reader.ReadInt64(),
                };
                _minutes[key] = bucket;
            }

            if (_lastCreatedUnix == 0)
                _lastCreatedUnix = LatestCreatedMinute() * 60;
        }
        catch
        {
            _minutes.Clear();
            _logPosition = 0;
            _archiveDone = false;
            _hasReward = false;
            _lastRewardMicro = 0;
            _lastCreatedUnix = 0;
        }
    }

    private long LatestCreatedMinute()
    {
        long latest = 0;
        foreach (var pair in _minutes)
        {
            if (pair.Value.Created > 0 && pair.Key > latest)
                latest = pair.Key;
        }

        return latest;
    }

    private struct Bucket
    {
        public int Votes;
        public int Proposals;
        public int Created;
        public long RewardMicro;
    }
}

internal enum CountKind
{
    Votes,
    Proposals,
    Created,
}

internal static class AlgoFormat
{
    public static string Price(decimal price)
    {
        return price >= 1m
            ? price.ToString("'$'0.00", CultureInfo.InvariantCulture)
            : price.ToString("'$'0.0000", CultureInfo.InvariantCulture);
    }

    public static string Reward(long microAlgo)
    {
        decimal algo = microAlgo / 1_000_000m;
        return algo.ToString("0.000' ALGO'", CultureInfo.InvariantCulture);
    }

    public static string Share(decimal pct)
    {
        string digits = pct >= 1m ? "0.00" : "0.0000";
        return pct.ToString(digits, CultureInfo.InvariantCulture) + "%";
    }

    public static string OnlineStake(long microAlgo)
    {
        decimal algo = microAlgo / 1_000_000m;
        if (algo >= 1_000_000_000m)
            return (algo / 1_000_000_000m).ToString("0.00", CultureInfo.InvariantCulture) + "B ALGO";
        if (algo >= 1_000_000m)
            return (algo / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M ALGO";
        long whole = (long)Math.Round(algo, 0, MidpointRounding.AwayFromZero);
        return whole.ToString(CultureInfo.InvariantCulture) + " ALGO";
    }

    public static string Behind(long seconds)
    {
        if (seconds <= 0)
            return "0 s";
        if (seconds < 90)
            return seconds.ToString(CultureInfo.InvariantCulture) + " s";
        return Age(TimeSpan.FromSeconds(seconds), "12h 28m");
    }

    public static string Upgrade(long lastRound, long nextRound, bool supported, double roundSeconds, string version)
    {
        if (nextRound <= lastRound || roundSeconds <= 0)
            return "None scheduled";
        double days = (nextRound - lastRound) * roundSeconds / 86400d;
        string when = days >= 2
            ? $"{Math.Floor(days):0} days"
            : days >= 1d / 24d
                ? $"{Math.Max(1, Math.Floor(days * 24)):0} hours"
                : "soon";
        string name = version.Length is > 0 and <= 18 && !version.Contains("://", StringComparison.Ordinal)
            ? version + " "
            : "";
        return supported ? name + "in " + when : "Stops in " + when;
    }

    public static string Expected(double blocks) => blocks.ToString("0.0", CultureInfo.InvariantCulture);

    public static string Luck(double blocks)
    {
        if (blocks > 0.05)
            return "Ahead " + blocks.ToString("0.0", CultureInfo.InvariantCulture);
        if (blocks < -0.05)
            return "Behind " + Math.Abs(blocks).ToString("0.0", CultureInfo.InvariantCulture);
        return "On pace";
    }

    public static string WinRate(int created, int proposed)
    {
        if (proposed <= 0)
            return "—";
        decimal pct = created / (decimal)proposed * 100m;
        return pct.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    public static string Percent(decimal pct)
    {
        return pct.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
    }

    public static string Age(TimeSpan age, string style)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        long totalSeconds = (long)age.TotalSeconds;
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;

        return style switch
        {
            "12h 28m 10s" => Words(hours, minutes, seconds, withSeconds: true),
            "748 min" => (totalSeconds / 60).ToString(CultureInfo.InvariantCulture) + " min",
            "12:28:10" => string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}"),
            _ => Words(hours, minutes, seconds, withSeconds: false),
        };
    }

    private static string Words(long hours, long minutes, long seconds, bool withSeconds)
    {
        if (hours > 0)
            return withSeconds ? $"{hours}h {minutes}m {seconds}s" : $"{hours}h {minutes}m";
        if (minutes > 0)
            return withSeconds ? $"{minutes}m {seconds}s" : $"{minutes}m";
        return $"{seconds}s";
    }
}
