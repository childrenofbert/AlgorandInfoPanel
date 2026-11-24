using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfoPanel.Algorand;

internal sealed class NodeClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public NodeClient()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("InfoPanel-Algorand", "1.0"));
    }

    public static string FindDataDirectory(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            string full = Path.GetFullPath(overridePath.Trim());
            if (Directory.Exists(full))
                return full;
        }

        string root = Environment.GetEnvironmentVariable("FUNC_DATA_DIR") is { Length: > 0 } env
            ? env
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "func");

        string parent = root;
        string pointer = Path.Combine(root, "algorand.data");
        if (File.Exists(pointer))
        {
            string text = File.ReadAllText(pointer).Trim();
            if (text.Length > 0)
                parent = text;
        }

        return Path.Combine(parent, "algorand");
    }

    public static bool ServiceIsRunning()
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "sc.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("query");
            start.ArgumentList.Add("Algorand Node");
            using var process = Process.Start(start);
            if (process == null)
                return false;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return output.Contains("RUNNING", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public async Task<NodeSnapshot?> ReadAsync(string dataDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dataDirectory))
            return null;

        string? token = ReadToken(dataDirectory);
        int port = ReadPort(dataDirectory);
        if (string.IsNullOrEmpty(token) || port <= 0)
            return null;

        string root = $"http://127.0.0.1:{port}";
        bool health = await GetOkAsync(root + "/health", token, cancellationToken);
        bool ready = health && await GetOkAsync(root + "/ready", token, cancellationToken);
        using var status = await GetJsonAsync(root + "/v2/status", token, cancellationToken);
        if (status == null)
            return new NodeSnapshot(health, ready, 0, false, [], port, 0);

        long round = status.RootElement.TryGetProperty("last-round", out var lastRound) && lastRound.TryGetInt64(out long parsedRound)
            ? parsedRound
            : 0;
        bool catchingUp = status.RootElement.TryGetProperty("catchup-time", out var catchup)
            && catchup.TryGetInt64(out long catchupNs)
            && catchupNs > 0;
        var parts = await ReadParticipationAsync(root, token, cancellationToken);
        long heartbeat = await ReadLatestHeartbeatAsync(root, token, parts, cancellationToken);
        return new NodeSnapshot(health, ready, round, catchingUp, parts, port, heartbeat);
    }

    public async Task<BlockReward?> ReadBlockAsync(string dataDirectory, long round, CancellationToken cancellationToken)
    {
        string? token = ReadToken(dataDirectory);
        int port = ReadPort(dataDirectory);
        if (string.IsNullOrEmpty(token) || port <= 0)
            return null;

        using var doc = await GetJsonAsync($"http://127.0.0.1:{port}/v2/blocks/{round}?format=json", token, cancellationToken);
        if (doc == null)
            return null;

        JsonElement block = doc.RootElement;
        if (block.TryGetProperty("block", out var inner))
            block = inner;

        string? proposer = FirstString(block, "prp", "proposer");
        long payout = FirstInt(block, "pp", "proposer-payout");
        return new BlockReward(proposer, payout);
    }

    public async Task<decimal?> ReadPriceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await GetPublicJsonAsync("https://api.coinbase.com/v2/prices/ALGO-USD/spot", cancellationToken);
            if (doc != null
                && doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("amount", out var amount)
                && decimal.TryParse(amount.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var coinbase))
            {
                return coinbase;
            }
        }
        catch
        {
            // Fall through to the backup quote.
        }

        try
        {
            using var doc = await GetPublicJsonAsync("https://api.kraken.com/0/public/Ticker?pair=ALGOUSD", cancellationToken);
            if (doc == null || !doc.RootElement.TryGetProperty("result", out var result))
                return null;
            foreach (var pair in result.EnumerateObject())
            {
                if (!pair.Value.TryGetProperty("c", out var close) || close.GetArrayLength() == 0)
                    continue;
                if (decimal.TryParse(close[0].GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var kraken))
                    return kraken;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<List<PartKey>> ReadParticipationAsync(string root, string token, CancellationToken cancellationToken)
    {
        var list = new List<PartKey>();
        using var doc = await GetJsonAsync(root + "/v2/participation", token, cancellationToken);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string address = item.TryGetProperty("address", out var addr) ? addr.GetString() ?? "" : "";
            if (address.Length == 0)
                continue;
            list.Add(new PartKey(
                address,
                GetLong(item, "effective-first-valid"),
                GetLong(item, "effective-last-valid"),
                GetLong(item, "last-vote")));
        }

        return list;
    }

    private async Task<long> ReadLatestHeartbeatAsync(string root, string token, IReadOnlyList<PartKey> keys, CancellationToken cancellationToken)
    {
        long newest = 0;
        foreach (var key in keys)
        {
            if (key.Address.Length == 0)
                continue;
            using var doc = await GetJsonAsync(root + "/v2/accounts/" + Uri.EscapeDataString(key.Address), token, cancellationToken);
            if (doc == null)
                continue;
            long value = GetLong(doc.RootElement, "last-heartbeat");
            if (value > newest)
                newest = value;
        }

        return newest;
    }

    private async Task<bool> GetOkAsync(string url, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Algo-API-Token", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Algo-API-Token", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<JsonDocument?> GetPublicJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? ReadToken(string dataDirectory)
    {
        foreach (var name in new[] { "algod.admin.token", "algod.token" })
        {
            try
            {
                string path = Path.Combine(dataDirectory, name);
                if (!File.Exists(path))
                    continue;
                string token = File.ReadAllText(path).Trim();
                if (token.Length > 0)
                    return token;
            }
            catch
            {
                // The next file may still be readable.
            }
        }

        return null;
    }

    private static int ReadPort(string dataDirectory)
    {
        try
        {
            string configPath = Path.Combine(dataDirectory, "config.json");
            if (File.Exists(configPath))
            {
                var match = Regex.Match(File.ReadAllText(configPath), "\"EndpointAddress\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success && TryPort(match.Groups[1].Value, out int fromConfig))
                    return fromConfig;
            }
        }
        catch
        {
            // Fall through to algod.net.
        }

        try
        {
            string netPath = Path.Combine(dataDirectory, "algod.net");
            if (File.Exists(netPath) && TryPort(File.ReadAllText(netPath).Trim(), out int fromNet))
                return fromNet;
        }
        catch
        {
            return 0;
        }

        return 0;
    }

    private static bool TryPort(string endpoint, out int port)
    {
        port = 0;
        int colon = endpoint.LastIndexOf(':');
        if (colon < 0 || colon == endpoint.Length - 1)
            return false;
        return int.TryParse(endpoint[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;
    }

    private static long GetLong(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.TryGetInt64(out long number) ? number : 0;
    }

    private static string? FirstString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static long FirstInt(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.TryGetInt64(out long number))
                return number;
        }

        return 0;
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record NodeSnapshot(
    bool HealthOk,
    bool Ready,
    long LastRound,
    bool CatchingUp,
    IReadOnlyList<PartKey> Keys,
    int Port,
    long LastHeartbeat);

internal readonly record struct PartKey(string Address, long FirstValid, long LastValid, long LastVote);

internal readonly record struct BlockReward(string? Proposer, long PayoutMicro);
