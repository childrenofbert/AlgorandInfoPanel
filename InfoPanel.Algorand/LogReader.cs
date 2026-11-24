using System.Text;

namespace InfoPanel.Algorand;

internal static class LogReader
{
    private static readonly byte[] VoteMarker = "pseudonode.makeVotes: "u8.ToArray();
    private static readonly byte[] ProposalMarker = "pseudonode.makeProposals: "u8.ToArray();
    private static readonly byte[] TimeMarker = "\"time\":\""u8.ToArray();

    /// <summary>
    /// Reads complete new lines and returns the file position of the last line consumed.
    /// </summary>
    public static long Consume(string path, long start, Action<DateTimeOffset, int, int> onEvent, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return start;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (start < 0)
            return 0;
        if (start > stream.Length)
            return -1;

        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[256 * 1024];
        var carry = new List<byte>(4096);
        long consumed = start;

        while (!cancellationToken.IsCancellationRequested)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;

            long bufferStart = stream.Position - read;
            int lineStart = 0;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\n')
                    continue;

                if (carry.Count == 0)
                {
                    Parse(buffer.AsSpan(lineStart, i - lineStart), onEvent);
                }
                else
                {
                    carry.AddRange(buffer.AsSpan(lineStart, i - lineStart).ToArray());
                    Parse(CollectionsMarshalSpan(carry), onEvent);
                    carry.Clear();
                }

                lineStart = i + 1;
                consumed = bufferStart + lineStart;
            }

            if (lineStart < read)
                carry.AddRange(buffer.AsSpan(lineStart, read - lineStart).ToArray());
        }

        return consumed;
    }

    private static ReadOnlySpan<byte> CollectionsMarshalSpan(List<byte> carry)
    {
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(carry);
    }

    private static void Parse(ReadOnlySpan<byte> line, Action<DateTimeOffset, int, int> onEvent)
    {
        if (line.Length > 0 && line[^1] == (byte)'\r')
            line = line[..^1];
        if (line.Length == 0)
            return;

        bool votes = Contains(line, VoteMarker);
        bool proposals = !votes && Contains(line, ProposalMarker);
        if (!votes && !proposals)
            return;

        var marker = votes ? VoteMarker : ProposalMarker;
        int markerAt = IndexOf(line, marker);
        if (markerAt < 0)
            return;

        int numberAt = markerAt + marker.Length;
        int count = ReadInt(line, numberAt);
        if (count <= 0)
            return;

        int timeAt = IndexOf(line, TimeMarker);
        if (timeAt < 0)
            return;
        timeAt += TimeMarker.Length;
        int timeEnd = timeAt;
        while (timeEnd < line.Length && line[timeEnd] != (byte)'"')
            timeEnd++;
        if (timeEnd <= timeAt)
            return;

        var timeText = Encoding.UTF8.GetString(line[timeAt..timeEnd]);
        if (!DateTimeOffset.TryParse(timeText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
            return;

        if (votes)
            onEvent(when, count, 0);
        else
            onEvent(when, 0, count);
    }

    private static int ReadInt(ReadOnlySpan<byte> line, int start)
    {
        int value = 0;
        int i = start;
        if (i >= line.Length || line[i] < (byte)'0' || line[i] > (byte)'9')
            return 0;
        while (i < line.Length && line[i] >= (byte)'0' && line[i] <= (byte)'9')
        {
            value = value * 10 + (line[i] - (byte)'0');
            i++;
        }

        return value;
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, byte[] needle) => IndexOf(haystack, needle) >= 0;

    private static int IndexOf(ReadOnlySpan<byte> haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return -1;

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack[i] != needle[0])
                continue;
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }
}
