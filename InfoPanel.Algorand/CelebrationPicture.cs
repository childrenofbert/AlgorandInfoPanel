using System.Runtime.InteropServices;
using SkiaSharp;

namespace InfoPanel.Algorand;

internal sealed class CelebrationPicture : IDisposable
{
    private readonly object _gate = new();
    private SKBitmap[] _frames = [];
    private int[] _delays = [];
    private int _totalMs;

    public bool HasFrames
    {
        get { lock (_gate) return _frames.Length > 0; }
    }

    public void Load(string? path)
    {
        SKBitmap[] next = [];
        int[] delays = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                Decode(path, out next, out delays);
        }
        catch
        {
            foreach (var frame in next)
                frame.Dispose();
            next = [];
            delays = [];
        }

        SKBitmap[] previous;
        lock (_gate)
        {
            previous = _frames;
            _frames = next;
            _delays = delays;
            _totalMs = 0;
            foreach (int delay in delays)
                _totalMs += delay;
            if (_totalMs <= 0 && next.Length > 0)
                _totalMs = next.Length * 100;
        }

        foreach (var frame in previous)
            frame.Dispose();
    }

    public bool Draw(SKCanvas canvas, int width, int height, float alpha, double seconds)
    {
        lock (_gate)
        {
            if (_frames.Length == 0 || alpha <= 0.01f)
                return false;

            var frame = _frames[IndexAt(seconds)];
            float fit = Math.Min(width * 0.92f / frame.Width, height * 0.88f / frame.Height);
            float w = frame.Width * fit;
            float h = frame.Height * fit;
            var dest = SKRect.Create((width - w) / 2f, (height - h) / 2f, w, h);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(255, 255, 255, Alpha(alpha)),
            };
            canvas.DrawBitmap(frame, dest, paint);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var frame in _frames)
                frame.Dispose();
            _frames = [];
        }
    }

    private int IndexAt(double seconds)
    {
        if (_frames.Length == 1)
            return 0;
        int ms = (int)(seconds * 1000d) % _totalMs;
        int cursor = 0;
        for (int i = 0; i < _frames.Length; i++)
        {
            cursor += Math.Max(20, _delays[i]);
            if (ms < cursor)
                return i;
        }

        return _frames.Length - 1;
    }

    private static void Decode(string path, out SKBitmap[] frames, out int[] delays)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null || codec.FrameCount <= 1)
        {
            using var still = SKBitmap.Decode(path) ?? throw new InvalidOperationException();
            frames = [Fit(still)];
            delays = [100];
            return;
        }

        int count = Math.Min(codec.FrameCount, 48);
        var native = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        frames = new SKBitmap[count];
        delays = new int[count];
        using var sheet = new SKBitmap(native);
        for (int i = 0; i < count; i++)
        {
            var info = codec.FrameInfo[i];
            delays[i] = Math.Max(20, info.Duration);
            sheet.Erase(SKColors.Transparent);
            var options = new SKCodecOptions(i);
            codec.GetPixels(native, sheet.GetPixels(), options);
            frames[i] = Fit(sheet);
        }
    }

    private static SKBitmap Fit(SKBitmap source)
    {
        const int maxWidth = 800;
        const int maxHeight = 250;
        if (source.Width <= maxWidth && source.Height <= maxHeight)
            return source.Copy();

        float scale = Math.Min(maxWidth / (float)source.Width, maxHeight / (float)source.Height);
        var sized = new SKBitmap(new SKImageInfo(
            Math.Max(1, (int)(source.Width * scale)),
            Math.Max(1, (int)(source.Height * scale)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        source.ScalePixels(sized, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return sized;
    }

    private static byte Alpha(float alpha) => (byte)Math.Clamp(alpha * 255f, 0f, 255f);
}

internal static class CelebrationFile
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InfoPanel", "plugins", "algorand-node.celebration.txt");

    public static string Load()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    public static void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, path);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // The next choice can still replace it.
        }
    }
}

internal static class PicturePicker
{
    public static string? Choose()
    {
        string? selected = null;
        var thread = new Thread(() => selected = Show())
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return selected;
    }

    private static string? Show()
    {
        var dialog = new OpenFileName
        {
            lpstrFilter = "Pictures\0*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp\0\0",
            lpstrFile = new string('\0', 520),
            nMaxFile = 520,
            lpstrTitle = "Celebration picture",
            Flags = 0x80000 | 0x1000 | 0x800 | 0x8,
        };
        dialog.lStructSize = Marshal.SizeOf(dialog);
        return GetOpenFileName(ref dialog) ? dialog.lpstrFile.Split('\0')[0] : null;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }
}
