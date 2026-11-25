using InfoPanel.Plugins;
using InfoPanel.Plugins.Graphics;
using SkiaSharp;

namespace InfoPanel.Algorand;

public sealed class AlgorandPlugin : BasePlugin, IPluginImageProvider, IPluginConfigurable
{
    private static readonly (Period Period, string Id, string Name)[] Periods =
    [
        (Period.Hour, "hour", "past hour"),
        (Period.Day, "day", "past 24 hours"),
        (Period.Week, "week", "past 7 days"),
        (Period.Month, "month", "past 30 days"),
        (Period.Quarter, "quarter", "past 90 days"),
        (Period.Year, "year", "past 365 days"),
    ];

    private static readonly string[] LookLabels =
    [
        "Teal and gold", "Gold", "Ice", "Ember", "Plain",
        "Burst", "Medal", "Ribbon", "Confetti", "Ticket", "Spotlight", "Custom picture",
    ];
    private static readonly string[] IntroLabels = ["Fade", "Pop", "Rise", "Drop", "Zoom", "Slide in", "Snap"];
    private static readonly string[] OutroLabels = ["Fade", "Shrink", "Fall", "Zoom out", "Slide out", "Snap"];
    private static readonly string[] LengthLabels = ["Short", "Normal", "Long"];
    private static readonly string[] TimeFormats = ["12h 28m", "12h 28m 10s", "748 min", "12:28:10"];

    private readonly PluginText _block = new("block", "Current block", "—");
    private readonly PluginText _status = new("status", "Status", "Stopped");
    private readonly PluginSensor _stake = new("stake", "Current stake", 0, "ALGO");
    private readonly PluginText _heartbeat = new("heartbeat-age", "Time since heartbeat", "—");
    private readonly PluginText _price = new("price", "ALGO price", "—");
    private readonly PluginText _changeHour = new("price-change-hour", "Change past hour", "—");
    private readonly PluginText _changeDay = new("price-change-day", "Change past 24 hours", "—");
    private readonly PluginText _changeWeek = new("price-change-week", "Change past 7 days", "—");
    private readonly PluginText _changeMonth = new("price-change-month", "Change past 30 days", "—");
    private readonly PluginText _reward = new("last-reward", "Last reward", "—");
    private readonly PluginText _sinceBlock = new("since-block", "Time since last block", "—");
    private readonly PluginText _keyDays = new("key-days", "Key days left", "—");
    private readonly PluginSensor[] _votes = CreateCounters("votes", "Votes");
    private readonly PluginSensor[] _proposed = CreateCounters("proposed", "Blocks proposed");
    private readonly PluginSensor[] _created = CreateCounters("created", "Blocks created");

    private readonly NodeClient _node = new();
    private readonly StatsStore _stats = new();
    private readonly RewardPainter _painter = new();
    private readonly CelebrationPicture _picture = new();
    private readonly PriceHistory _prices = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _priceGate = new();

    private IPluginImageWriter? _rewardWriter;
    private readonly Dictionary<string, IPluginImageWriter> _chartWriters = [];
    private DateTime _lastChartUtc = DateTime.MinValue;
    private string _dataFolder = "";
    private bool _celebration = true;
    private string _lookLabel = "Teal and gold";
    private string _introLabel = "Fade";
    private string _outroLabel = "Fade";
    private string _lengthLabel = "Normal";
    private string _timeFormat = "12h 28m";
    private string _typedPicture = "";
    private string _chosenPicture = CelebrationFile.Load();
    private CelebrationLook _look = CelebrationLook.TealGold;

    private string? _activeDataDir;
    private Task? _scanTask;
    private Task? _priceTask;
    private volatile bool _scanDone;
    private DateTime _lastPollUtc = DateTime.MinValue;
    private DateTime _lastSaveUtc = DateTime.MinValue;
    private long _seenRound;
    private double _roundSeconds = 2.9;
    private DateTime _roundSampleUtc;
    private long _roundSample;
    private DateTimeOffset? _heartbeatUtc;
    private long _heartbeatRound;
    private long _heartbeatAtRound;
    private double _heartbeatPace;
    private List<PartKey> _keys = [];
    private string _priceText = "—";

    private FanfareKind _fanfare = FanfareKind.None;
    private DateTime _fanfareStartUtc;
    private double _fanfareSeconds = 4.2;
    private double _fanfareAlgo;
    private bool _idleDrawn;

    public AlgorandPlugin()
        : base("algorand-node", "Algorand", "Live block, status, votes, proposals, rewards, and ALGO price from the node on this PC.")
    {
    }

    public override TimeSpan UpdateInterval =>
        _fanfare == FanfareKind.None ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(50);

    public IReadOnlyList<PluginImageDescriptor> ImageDescriptors { get; } =
    [
        new PluginImageDescriptor("reward", "Reward celebration", 800, 250),
        new PluginImageDescriptor("price-24h", "Price past 24 hours", 800, 200),
        new PluginImageDescriptor("price-7d", "Price past 7 days", 800, 200),
        new PluginImageDescriptor("price-30d", "Price past 30 days", 800, 200),
        new PluginImageDescriptor("created-7d", "Blocks created past 7 days", 800, 200),
        new PluginImageDescriptor("created-30d", "Blocks created past 30 days", 800, 200),
    ];

    public IReadOnlyList<PluginConfigProperty> ConfigProperties =>
    [
        new PluginConfigProperty
        {
            Key = "look",
            DisplayName = "Celebration look",
            Description = "The picture that plays when you are paid. Custom picture uses the file you choose below.",
            Type = PluginConfigType.Choice,
            Value = _lookLabel,
            Options = LookLabels,
        },
        new PluginConfigProperty
        {
            Key = "intro",
            DisplayName = "Entrance",
            Description = "How the celebration comes onto the screen.",
            Type = PluginConfigType.Choice,
            Value = _introLabel,
            Options = IntroLabels,
        },
        new PluginConfigProperty
        {
            Key = "outro",
            DisplayName = "Exit",
            Description = "How the celebration leaves the screen.",
            Type = PluginConfigType.Choice,
            Value = _outroLabel,
            Options = OutroLabels,
        },
        new PluginConfigProperty
        {
            Key = "length",
            DisplayName = "Celebration length",
            Description = "How long the reward animation stays on screen.",
            Type = PluginConfigType.Choice,
            Value = _lengthLabel,
            Options = LengthLabels,
        },
        new PluginConfigProperty
        {
            Key = "dataFolder",
            DisplayName = "Node data folder",
            Description = "Folder that contains algod.token. Leave blank to use FUNC's folder.",
            Type = PluginConfigType.String,
            Value = _dataFolder,
        },
        new PluginConfigProperty
        {
            Key = "celebration",
            DisplayName = "Reward celebration",
            Description = "Play the animation when the network accepts your block. Test celebration still plays if this is off.",
            Type = PluginConfigType.Boolean,
            Value = _celebration,
        },
        new PluginConfigProperty
        {
            Key = "timeFormat",
            DisplayName = "Time format",
            Description = "How the heartbeat and last-block clocks are written. Every copy uses this style.",
            Type = PluginConfigType.Choice,
            Value = _timeFormat,
            Options = TimeFormats,
        },
        new PluginConfigProperty
        {
            Key = "picture",
            DisplayName = "Celebration picture",
            Description = "Full path to a PNG, JPG, or GIF. Leave blank to use Choose celebration picture.",
            Type = PluginConfigType.String,
            Value = _typedPicture,
        },
    ];

    public void OnImageBuffersReady(IReadOnlyDictionary<string, IPluginImageWriter> writers)
    {
        _rewardWriter = null;
        _chartWriters.Clear();
        foreach (var pair in writers)
        {
            if (pair.Key == "reward")
                _rewardWriter = pair.Value;
            else
                _chartWriters[pair.Key] = pair.Value;
        }

        _idleDrawn = false;
        _lastChartUtc = DateTime.MinValue;
        DrawFrame();
    }

    public override void Initialize()
    {
    }

    public override void Load(List<IPluginContainer> containers)
    {
        var node = new PluginContainer("node", "Node");
        node.Entries.Add(_block);
        node.Entries.Add(_status);
        node.Entries.Add(_stake);
        node.Entries.Add(_heartbeat);
        node.Entries.Add(_price);
        node.Entries.Add(_reward);
        node.Entries.Add(_sinceBlock);
        node.Entries.Add(_keyDays);
        containers.Add(node);

        containers.Add(Folder("votes", "Votes", _votes));
        containers.Add(Folder("proposed", "Blocks proposed", _proposed));
        containers.Add(Folder("created", "Blocks created", _created));

        var price = new PluginContainer("price", "Price");
        price.Entries.Add(_changeHour);
        price.Entries.Add(_changeDay);
        price.Entries.Add(_changeWeek);
        price.Entries.Add(_changeMonth);
        containers.Add(price);

        if (_stats.HasReward)
            _reward.Value = AlgoFormat.Reward(_stats.LastRewardMicro);
        _picture.Load(ActivePicture());
    }

    public void ApplyConfig(string key, object? value)
    {
        switch (key)
        {
            case "look":
                if (value is string look && LookLabels.Contains(look))
                {
                    _lookLabel = look;
                    _look = LookFrom(look);
                }
                break;
            case "intro":
                if (value is string intro && IntroLabels.Contains(intro))
                    _introLabel = intro;
                break;
            case "outro":
                if (value is string outro && OutroLabels.Contains(outro))
                    _outroLabel = outro;
                break;
            case "picture":
                _typedPicture = value as string ?? "";
                _picture.Load(ActivePicture());
                break;
            case "length":
                if (value is string length && LengthLabels.Contains(length))
                    _lengthLabel = length;
                break;
            case "dataFolder":
                _dataFolder = value as string ?? "";
                break;
            case "celebration":
                _celebration = value is bool flag ? flag : Convert.ToBoolean(value);
                break;
            case "timeFormat":
                if (value is string format && TimeFormats.Contains(format))
                    _timeFormat = format;
                break;
        }
    }

    public override async Task UpdateAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        EnsureScan();

        if (DateTime.UtcNow - _lastPollUtc >= TimeSpan.FromSeconds(2))
        {
            await PollAsync(linked.Token);
            _lastPollUtc = DateTime.UtcNow;
        }

        RefreshCounts();
        RefreshClocks();
        _price.Value = _priceText;
        RefreshPriceChanges();
        DrawFrame();

        if (DateTime.UtcNow - _lastSaveUtc >= TimeSpan.FromSeconds(20))
        {
            _stats.Save();
            _lastSaveUtc = DateTime.UtcNow;
        }
    }

    public override void Update() => throw new NotSupportedException();

    public override void Close()
    {
        _lifetime.Cancel();
        try
        {
            Task.WaitAll(new[] { _scanTask, _priceTask }.OfType<Task>().ToArray(), TimeSpan.FromSeconds(2));
        }
        catch
        {
            // The background work stops with the plugin.
        }

        try
        {
            _stats.Save();
        }
        catch
        {
            // A failed save must not block shutdown.
        }

        _painter.Dispose();
        _picture.Dispose();
        _prices.Dispose();
        _node.Dispose();
        _lifetime.Dispose();
    }

    private void EnsureScan()
    {
        if (_scanTask != null)
            return;

        string dataDir = NodeClient.FindDataDirectory(_dataFolder);
        _activeDataDir = dataDir;
        _priceTask = Task.Run(() => RefreshPriceAsync(_lifetime.Token), CancellationToken.None);
        _scanTask = Task.Run(() => ScanLogs(dataDir, _lifetime.Token), CancellationToken.None);
    }

    private void ScanLogs(string dataDir, CancellationToken cancellationToken)
    {
        try
        {
            string archive = Path.Combine(dataDir, "node.archive.log");
            string log = Path.Combine(dataDir, "node.log");
            if (!_stats.ArchiveDone && File.Exists(archive))
            {
                LogReader.Consume(archive, 0, RememberLogEvent, cancellationToken);
                _stats.MarkArchiveDone();
                _stats.Save();
            }

            long position = _stats.LogPosition;
            long length = File.Exists(log) ? new FileInfo(log).Length : 0;
            if (position > length)
            {
                LogReader.Consume(archive, position, RememberLogEvent, cancellationToken);
                position = 0;
            }

            position = LogReader.Consume(log, position, RememberLogEvent, cancellationToken);
            if (position >= 0)
                _stats.SetLogPosition(position);
            _stats.Save();
        }
        catch
        {
            // Live readings still work if the log cannot be read.
        }
        finally
        {
            _scanDone = true;
        }
    }

    private void RememberLogEvent(DateTimeOffset when, int votes, int proposals)
    {
        _stats.Add(when, votes, proposals, 0, 0);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        string dataDir = _activeDataDir ?? NodeClient.FindDataDirectory(_dataFolder);
        bool serviceRunning = NodeClient.ServiceIsRunning();
        NodeSnapshot? snapshot = null;
        try
        {
            snapshot = await _node.ReadAsync(dataDir, cancellationToken);
        }
        catch
        {
            snapshot = null;
        }

        if (snapshot == null || !snapshot.HealthOk)
        {
            _status.Value = serviceRunning ? "Syncing" : "Stopped";
            return;
        }

        _status.Value = snapshot.Ready && !snapshot.CatchingUp ? "Synced" : "Syncing";
        if (snapshot.LastRound > 0)
            _block.Value = snapshot.LastRound.ToString();
        if (snapshot.StakeKnown)
            _stake.Value = (float)Math.Round(snapshot.StakeMicro / 1_000_000m, 0, MidpointRounding.AwayFromZero);

        _keys = snapshot.Keys.ToList();
        UpdateKeyDays(snapshot.LastRound);
        NoteRoundPace(snapshot.LastRound);
        NoteHeartbeat(snapshot.LastHeartbeat, snapshot.LastRound);

        if (_scanDone)
            TailLog(dataDir, cancellationToken);

        if (_seenRound == 0)
        {
            _seenRound = snapshot.LastRound;
            return;
        }

        if (snapshot.LastRound <= _seenRound)
            return;

        long from = Math.Max(_seenRound + 1, snapshot.LastRound - 6);
        for (long round = from; round <= snapshot.LastRound; round++)
        {
            BlockReward? block;
            try
            {
                block = await _node.ReadBlockAsync(dataDir, round, cancellationToken);
            }
            catch
            {
                continue;
            }

            if (block?.Proposer == null || !IsOurs(block.Value.Proposer))
                continue;

            _stats.Add(DateTimeOffset.UtcNow, 0, 0, 1, block.Value.PayoutMicro);
            _reward.Value = AlgoFormat.Reward(block.Value.PayoutMicro);
            StartFanfare(block.Value.PayoutMicro, force: false);
        }

        _seenRound = snapshot.LastRound;
    }

    private void TailLog(string dataDir, CancellationToken cancellationToken)
    {
        string log = Path.Combine(dataDir, "node.log");
        string archive = Path.Combine(dataDir, "node.archive.log");
        long position = _stats.LogPosition;
        long length = File.Exists(log) ? new FileInfo(log).Length : 0;
        if (position > length)
        {
            LogReader.Consume(archive, position, RememberLogEvent, cancellationToken);
            position = 0;
        }

        long next = LogReader.Consume(log, position, RememberLogEvent, cancellationToken);
        if (next >= 0)
            _stats.SetLogPosition(next);
    }

    private void UpdateKeyDays(long round)
    {
        PartKey? best = null;
        foreach (var key in _keys)
        {
            if (round < key.FirstValid || round > key.LastValid)
                continue;
            if (best == null || key.LastValid > best.Value.LastValid)
                best = key;
        }

        if (best == null)
        {
            _keyDays.Value = "—";
            return;
        }

        double days = Math.Max(0, best.Value.LastValid - round) * _roundSeconds / 86400d;
        _keyDays.Value = $"{Math.Floor(days):0} days";
    }

    private void NoteRoundPace(long round)
    {
        var now = DateTime.UtcNow;
        if (_roundSample > 0 && round > _roundSample)
        {
            double seconds = (now - _roundSampleUtc).TotalSeconds / (round - _roundSample);
            if (seconds is > 0.5 and < 10)
                _roundSeconds = seconds;
        }

        _roundSample = round;
        _roundSampleUtc = now;
    }

    private void NoteHeartbeat(long heartbeatRound, long lastRound)
    {
        if (heartbeatRound <= 0 || lastRound < heartbeatRound)
            return;

        bool changed = heartbeatRound != _heartbeatRound
            || lastRound != _heartbeatAtRound
            || Math.Abs(_roundSeconds - _heartbeatPace) > 0.05;
        if (!changed && _heartbeatUtc.HasValue)
            return;

        double ageSeconds = (lastRound - heartbeatRound) * _roundSeconds;
        _heartbeatUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(ageSeconds);
        _heartbeatRound = heartbeatRound;
        _heartbeatAtRound = lastRound;
        _heartbeatPace = _roundSeconds;
    }

    private void RefreshClocks()
    {
        _heartbeat.Value = _heartbeatUtc is DateTimeOffset heartbeat
            ? AlgoFormat.Age(DateTimeOffset.UtcNow - heartbeat, _timeFormat)
            : "—";

        long createdUnix = _stats.LastCreatedUnix;
        _sinceBlock.Value = createdUnix > 0
            ? AlgoFormat.Age(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(createdUnix), _timeFormat)
            : "—";
    }

    private bool IsOurs(string proposer)
    {
        foreach (var key in _keys)
        {
            if (string.Equals(key.Address, proposer, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    [PluginAction("Choose celebration picture")]
    public void ChooseCelebrationPicture()
    {
        string? path = PicturePicker.Choose();
        if (string.IsNullOrWhiteSpace(path))
            return;
        CelebrationFile.Save(path);
        _chosenPicture = path;
        _picture.Load(ActivePicture());
        _lookLabel = "Custom picture";
        _look = CelebrationLook.Custom;
    }

    [PluginAction("Clear celebration picture")]
    public void ClearCelebrationPicture()
    {
        CelebrationFile.Clear();
        _chosenPicture = "";
        _typedPicture = "";
        _picture.Load(ActivePicture());
    }

    [PluginAction("Test celebration")]
    public void TestCelebration()
    {
        long micro = _stats.HasReward && _stats.LastRewardMicro > 0
            ? _stats.LastRewardMicro
            : 8_097_000;
        StartFanfare(micro, force: true);
    }

    private void StartFanfare(long payoutMicro, bool force)
    {
        if (!_celebration && !force)
            return;

        double rewardSeconds = _lengthLabel switch
        {
            "Short" => 2.4,
            "Long" => 7,
            _ => 4.2,
        };

        _fanfareAlgo = payoutMicro / 1_000_000d;
        _fanfareSeconds = payoutMicro > 0 ? rewardSeconds : Math.Max(1.6, rewardSeconds * 0.45);
        _fanfareStartUtc = DateTime.UtcNow;
        _idleDrawn = false;
        _fanfare = payoutMicro > 0 ? FanfareKind.Reward : FanfareKind.Created;
    }

    private void RefreshCounts()
    {
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < Periods.Length; i++)
        {
            var period = Periods[i].Period;
            _votes[i].Value = _stats.Count(period, now, CountKind.Votes);
            _proposed[i].Value = _stats.Count(period, now, CountKind.Proposals);
            _created[i].Value = _stats.Count(period, now, CountKind.Created);
        }
    }

    private async Task RefreshPriceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _prices.EnsureHistoryAsync(cancellationToken);
        }
        catch
        {
            // Saved prices still cover whatever was downloaded before.
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var price = await _node.ReadPriceAsync(cancellationToken);
                if (price is decimal amount)
                {
                    _prices.Add(DateTimeOffset.UtcNow, amount);
                    string text = AlgoFormat.Price(amount);
                    lock (_priceGate)
                        _priceText = text;
                    _prices.Save();
                }
            }
            catch
            {
                // Keep the last price.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void RefreshPriceChanges()
    {
        _changeHour.Value = FormatChange(_prices.Change(TimeSpan.FromHours(1)));
        _changeDay.Value = FormatChange(_prices.Change(TimeSpan.FromHours(24)));
        _changeWeek.Value = FormatChange(_prices.Change(TimeSpan.FromDays(7)));
        _changeMonth.Value = FormatChange(_prices.Change(TimeSpan.FromDays(30)));
    }

    private static string FormatChange(decimal? pct) => pct is decimal value ? AlgoFormat.Percent(value) : "—";

    private void DrawFrame()
    {
        if (DateTime.UtcNow - _lastChartUtc >= TimeSpan.FromSeconds(2))
        {
            DrawCharts();
            _lastChartUtc = DateTime.UtcNow;
        }

        if (_rewardWriter == null)
            return;

        if (_fanfare == FanfareKind.None)
        {
            if (_idleDrawn)
                return;
            using var idle = new SKCanvas(_rewardWriter.Bitmap);
            _painter.DrawIdle(idle, _rewardWriter.Width, _rewardWriter.Height);
            _rewardWriter.Invalidate();
            _idleDrawn = true;
            return;
        }

        float t = (float)((DateTime.UtcNow - _fanfareStartUtc).TotalSeconds / _fanfareSeconds);
        using var canvas = new SKCanvas(_rewardWriter.Bitmap);
        if (t >= 1f)
        {
            _fanfare = FanfareKind.None;
            _painter.DrawIdle(canvas, _rewardWriter.Width, _rewardWriter.Height);
            _rewardWriter.Invalidate();
            _idleDrawn = true;
            return;
        }

        double elapsed = (DateTime.UtcNow - _fanfareStartUtc).TotalSeconds;
        _painter.Draw(
            canvas,
            _rewardWriter.Width,
            _rewardWriter.Height,
            t,
            _fanfare,
            _fanfareAlgo,
            _look,
            _introLabel,
            _outroLabel,
            _picture,
            elapsed);
        _rewardWriter.Invalidate();
    }

    private void DrawCharts()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        DrawOneChart("price-24h", "PAST 24 HOURS", TimeSpan.FromHours(24), hourly: false, now);
        DrawOneChart("price-7d", "PAST 7 DAYS", TimeSpan.FromDays(7), hourly: true, now);
        DrawOneChart("price-30d", "PAST 30 DAYS", TimeSpan.FromDays(30), hourly: true, now);
        DrawCreatedChart("created-7d", 7);
        DrawCreatedChart("created-30d", 30);
    }

    private void DrawOneChart(string id, string title, TimeSpan window, bool hourly, long now)
    {
        if (!_chartWriters.TryGetValue(id, out var writer))
            return;
        var points = _prices.Window(window, hourly);
        using var canvas = new SKCanvas(writer.Bitmap);
        PriceChart.Draw(canvas, writer.Width, writer.Height, title, points, now - (long)window.TotalSeconds, now);
        writer.Invalidate();
    }

    private void DrawCreatedChart(string id, int days)
    {
        if (!_chartWriters.TryGetValue(id, out var writer))
            return;
        var counts = _stats.CreatedByDay(days, DateTimeOffset.Now);
        using var canvas = new SKCanvas(writer.Bitmap);
        CreatedChart.Draw(canvas, writer.Width, writer.Height, counts);
        writer.Invalidate();
    }

    private string ActivePicture()
    {
        if (File.Exists(_typedPicture))
            return _typedPicture;
        return File.Exists(_chosenPicture) ? _chosenPicture : "";
    }

    private static CelebrationLook LookFrom(string label) => label switch
    {
        "Gold" => CelebrationLook.Gold,
        "Ice" => CelebrationLook.Ice,
        "Ember" => CelebrationLook.Ember,
        "Plain" => CelebrationLook.Plain,
        "Burst" => CelebrationLook.Burst,
        "Medal" => CelebrationLook.Medal,
        "Ribbon" => CelebrationLook.Ribbon,
        "Confetti" => CelebrationLook.Confetti,
        "Ticket" => CelebrationLook.Ticket,
        "Spotlight" => CelebrationLook.Spotlight,
        "Custom picture" => CelebrationLook.Custom,
        _ => CelebrationLook.TealGold,
    };

    private static PluginSensor[] CreateCounters(string prefix, string title)
    {
        var sensors = new PluginSensor[Periods.Length];
        for (int i = 0; i < Periods.Length; i++)
        {
            var period = Periods[i];
            sensors[i] = new PluginSensor($"{prefix}-{period.Id}", $"{title} {period.Name}", 0);
        }

        return sensors;
    }

    private static PluginContainer Folder(string id, string name, PluginSensor[] sensors)
    {
        var container = new PluginContainer(id, name);
        foreach (var sensor in sensors)
            container.Entries.Add(sensor);
        return container;
    }
}
