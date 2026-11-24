using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace InfoPanel.Algorand;

internal sealed class PriceHistory : IDisposable
{
    private const long Scale = 100_000_000;
    private const int KeepMinutes = 31 * 24 * 60;

    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly List<Tick> _ticks = [];
    private readonly string _path;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly MinuteOrder _order = new();

    public PriceHistory()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("InfoPanel-Algorand", "1.2"));
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InfoPanel", "plugins");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "algorand-node.prices.bin");
        Load();
    }

    public async Task EnsureHistoryAsync(CancellationToken cancellationToken)
    {
        if (Covers(TimeSpan.FromDays(29)))
            return;

        var fetched = new List<Tick>(1200);
        await ReadKrakenAsync(fetched, cancellationToken);
        await ReadCoinbaseAsync(fetched, cancellationToken);
        if (fetched.Count == 0)
            return;

        lock (_gate)
        {
            foreach (var tick in fetched)
                Upsert(tick.Minute, tick.Scaled);
            Prune();
        }

        Save();
    }

    public void Add(DateTimeOffset when, decimal price)
    {
        if (price <= 0)
            return;
        long minute = when.ToUnixTimeSeconds() / 60;
        long scaled = (long)Math.Round(price * Scale, MidpointRounding.AwayFromZero);
        lock (_gate)
        {
            Upsert(minute, scaled);
            Prune();
        }
    }

    public decimal? Change(TimeSpan window)
    {
        long nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        long thenMinute = nowMinute - (long)window.TotalMinutes;
        lock (_gate)
        {
            int nowIndex = Floor(nowMinute);
            int thenIndex = Floor(thenMinute);
            if (nowIndex < 0 || thenIndex < 0)
                return null;
            long skew = thenMinute - _ticks[thenIndex].Minute;
            long allowed = Math.Max(15, (long)window.TotalMinutes / 10);
            if (skew > allowed)
                return null;
            decimal thenPrice = _ticks[thenIndex].Scaled / (decimal)Scale;
            decimal nowPrice = _ticks[nowIndex].Scaled / (decimal)Scale;
            if (thenPrice <= 0)
                return null;
            return (nowPrice - thenPrice) / thenPrice * 100m;
        }
    }

    public (long Unix, decimal Price)[] Window(TimeSpan window, bool hourly)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long startMinute = (now - (long)window.TotalSeconds) / 60;
        lock (_gate)
        {
            var raw = new List<Tick>();
            foreach (var tick in _ticks)
            {
                if (tick.Minute >= startMinute)
                    raw.Add(tick);
            }

            if (raw.Count == 0)
                return [];

            if (!hourly)
                return ToPoints(raw);

            var hours = new List<Tick>();
            long bucket = long.MinValue;
            Tick last = default;
            foreach (var tick in raw)
            {
                long hour = tick.Minute / 60;
                if (hour != bucket)
                {
                    if (bucket != long.MinValue)
                        hours.Add(last);
                    bucket = hour;
                }

                last = tick;
            }

            hours.Add(last);
            return ToPoints(hours);
        }
    }

    public void Save()
    {
        Tick[] copy;
        lock (_gate)
            copy = _ticks.ToArray();

        lock (_fileGate)
        {
            var tmp = _path + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x50524331);
                writer.Write(1);
                writer.Write(copy.Length);
                foreach (var tick in copy)
                {
                    writer.Write(tick.Minute);
                    writer.Write(tick.Scaled);
                }
            }

            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Dispose() => _http.Dispose();

    private bool Covers(TimeSpan span)
    {
        long nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        long need = nowMinute - (long)span.TotalMinutes;
        lock (_gate)
        {
            if (_ticks.Count == 0)
                return false;
            return _ticks[0].Minute <= need;
        }
    }

    private async Task ReadKrakenAsync(List<Tick> into, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await GetAsync("https://api.kraken.com/0/public/OHLC?pair=ALGOUSD&interval=60", cancellationToken);
            if (doc == null || !doc.RootElement.TryGetProperty("result", out var result))
                return;
            foreach (var property in result.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var candle in property.Value.EnumerateArray())
                {
                    if (candle.GetArrayLength() < 5)
                        continue;
                    if (!TryUnix(candle[0], out long unix) || !TryDecimal(candle[4], out decimal close) || close <= 0)
                        continue;
                    into.Add(new Tick { Minute = unix / 60, Scaled = ScaleOf(close) });
                }
            }
        }
        catch
        {
            // The other feed, or prices already saved, still fill the charts.
        }
    }

    private async Task ReadCoinbaseAsync(List<Tick> into, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await GetAsync("https://api.exchange.coinbase.com/products/ALGO-USD/candles?granularity=300", cancellationToken);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array)
                return;
            foreach (var candle in doc.RootElement.EnumerateArray())
            {
                if (candle.GetArrayLength() < 5)
                    continue;
                if (!TryUnix(candle[0], out long unix) || !TryDecimal(candle[4], out decimal close) || close <= 0)
                    continue;
                into.Add(new Tick { Minute = unix / 60, Scaled = ScaleOf(close) });
            }
        }
        catch
        {
            // Hourly history still covers the longer charts.
        }
    }

    private async Task<JsonDocument?> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private void Upsert(long minute, long scaled)
    {
        int index = _ticks.BinarySearch(new Tick { Minute = minute }, _order);
        if (index >= 0)
            _ticks[index] = new Tick { Minute = minute, Scaled = scaled };
        else
            _ticks.Insert(~index, new Tick { Minute = minute, Scaled = scaled });
    }

    private void Prune()
    {
        long nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        long cutoff = nowMinute - KeepMinutes;
        int remove = 0;
        while (remove < _ticks.Count && _ticks[remove].Minute < cutoff)
            remove++;
        if (remove > 0)
            _ticks.RemoveRange(0, remove);
    }

    private int Floor(long minute)
    {
        int index = _ticks.BinarySearch(new Tick { Minute = minute }, _order);
        if (index >= 0)
            return index;
        index = ~index - 1;
        return index >= 0 ? index : -1;
    }

    private void Load()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != 0x50524331 || reader.ReadInt32() != 1)
                return;
            int count = reader.ReadInt32();
            if (count < 0 || count > 100_000)
                return;
            var loaded = new List<Tick>(count);
            for (int i = 0; i < count; i++)
            {
                loaded.Add(new Tick { Minute = reader.ReadInt64(), Scaled = reader.ReadInt64() });
            }

            loaded.Sort(_order);
            lock (_gate)
            {
                _ticks.Clear();
                _ticks.AddRange(loaded);
                Prune();
            }
        }
        catch
        {
            lock (_gate)
                _ticks.Clear();
        }
    }

    private static (long Unix, decimal Price)[] ToPoints(List<Tick> ticks)
    {
        var points = new (long Unix, decimal Price)[ticks.Count];
        for (int i = 0; i < ticks.Count; i++)
            points[i] = (ticks[i].Minute * 60, ticks[i].Scaled / (decimal)Scale);
        return points;
    }

    private static long ScaleOf(decimal price) => (long)Math.Round(price * Scale, MidpointRounding.AwayFromZero);

    private static bool TryUnix(JsonElement element, out long unix)
    {
        unix = 0;
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt64(out unix);
        return element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out unix);
    }

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDecimal(out value);
        return element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private struct Tick
    {
        public long Minute;
        public long Scaled;
    }

    private sealed class MinuteOrder : IComparer<Tick>
    {
        public int Compare(Tick x, Tick y) => x.Minute.CompareTo(y.Minute);
    }
}
