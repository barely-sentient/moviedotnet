using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieDotNet.Core.Media;

/// <summary>A camera, screen, window or microphone capture source.</summary>
public sealed record CaptureDevice(
    string Name,
    bool IsVideo,
    bool IsScreen = false,
    bool IsWindow = false);

/// <summary>
/// Records a webcam and/or microphone through ffmpeg's platform capture
/// backends. The process keeps stdin open so <see cref="StopAsync"/> can ask
/// ffmpeg to close the file cleanly instead of killing it mid-write.
/// </summary>
public sealed partial class CaptureRecorder : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Text.StringBuilder _log = new();
    private bool _stopped;

    private CaptureRecorder(Process process, string outputPath, bool hasVideo)
    {
        _process = process;
        OutputPath = outputPath;
        HasVideo = hasVideo;
        StartedAt = DateTime.Now;
    }

    /// <summary>Gets the destination recording path.</summary>
    public string OutputPath { get; }

    /// <summary>Gets whether this recording includes a video stream.</summary>
    public bool HasVideo { get; }

    /// <summary>Gets the local time at which recording started.</summary>
    public DateTime StartedAt { get; }

    /// <summary>Gets the elapsed recording time.</summary>
    public TimeSpan Elapsed => DateTime.Now - StartedAt;

    /// <summary>Gets whether the ffmpeg process is still running.</summary>
    public bool IsRecording => !_process.HasExited;

    /// <summary>Gets a task that completes when ffmpeg exits.</summary>
    public Task Completion => _exited.Task;

    /// <summary>Gets the ffmpeg exit code, or <c>-1</c> while it is running.</summary>
    public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

    /// <summary>Gets the captured ffmpeg diagnostic log.</summary>
    public string Log => _log.ToString();

    [GeneratedRegex("\"(?<name>.+)\"\\s+\\((?<kind>video|audio)\\)")]
    private static partial Regex DevicePattern();

    /// <summary>Enumerates the capture devices ffmpeg can open.</summary>
    public static async Task<IReadOnlyList<CaptureDevice>> ListDevicesAsync(CancellationToken cancellation = default)
    {
        if (Ffmpeg.Exe is null || !OperatingSystem.IsWindows())
        {
            return [];
        }

        var info = new ProcessStartInfo(Ffmpeg.Exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[]
        {
            "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"
        })
        {
            info.ArgumentList.Add(argument);
        }

        var devices = new List<CaptureDevice>();

        try
        {
            using var process = new Process { StartInfo = info };
            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);

            foreach (var line in stderr.Split('\n'))
            {
                var match = DevicePattern().Match(line);

                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value;

                if (name.Contains("Alternative name", StringComparison.Ordinal))
                {
                    continue;
                }

                devices.Add(new CaptureDevice(name, match.Groups["kind"].Value == "video"));
            }
        }
        catch
        {
            // DirectShow enumeration is best-effort. The screen and window
            // sources below do not depend on it.
        }

        // FFmpeg's gdigrab backend captures the desktop directly and can also
        // capture a single top-level window by title; neither is enumerated
        // through DirectShow.
        devices.Add(new CaptureDevice("Entire desktop", IsVideo: true, IsScreen: true));

        foreach (var title in ListWindowTitles())
        {
            devices.Add(new CaptureDevice(title, IsVideo: true, IsWindow: true));
        }

        return devices
            .DistinctBy(d => (d.Name, d.IsVideo, d.IsScreen, d.IsWindow))
            .ToList();
    }

    /// <summary>Lists visible top-level windows that can be captured by region.</summary>
    private static IReadOnlyList<string> ListWindowTitles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var titles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Callback(IntPtr handle, IntPtr _)
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
            {
                return true;
            }

            var length = GetWindowTextLength(handle);

            if (length <= 0)
            {
                return true;
            }

            var builder = new StringBuilder(length + 1);

            if (GetWindowText(handle, builder, builder.Capacity) <= 0)
            {
                return true;
            }

            var title = builder.ToString().Trim();

            if (title.Length == 0
                || (GetWindowLong(handle, GwlExStyle) & WsExToolWindow) != 0)
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);

            // The editor's own window is not a useful capture source: recording
            // it would either recurse or show the recorder's own UI.
            if (processId == (uint)Environment.ProcessId)
            {
                return true;
            }

            if (seen.Add(title))
            {
                titles.Add(title);
            }

            return true;
        }

        try
        {
            EnumWindows(Callback, IntPtr.Zero);
        }
        catch
        {
            return [];
        }

        return titles
            .OrderBy(title => title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out WindowRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref WindowPoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Finds a visible window by exact title and returns its client area in screen
    /// pixels. The client area excludes the OS title bar and borders, matching a
    /// normal window-capture source.
    /// </summary>
    /// <param name="title">The exact top-level window title.</param>
    /// <param name="window">Receives the matching window handle.</param>
    /// <param name="x">Receives the client-area left edge.</param>
    /// <param name="y">Receives the client-area top edge.</param>
    /// <param name="width">Receives the client-area width.</param>
    /// <param name="height">Receives the client-area height.</param>
    /// <returns><see langword="true"/> when a visible, non-minimised match was found.</returns>
    private static bool TryGetWindowClientRect(
        string title,
        out IntPtr window,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        window = IntPtr.Zero;
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        var resultWindow = IntPtr.Zero;
        var resultX = 0;
        var resultY = 0;
        var resultWidth = 0;
        var resultHeight = 0;
        var found = false;

        bool Callback(IntPtr handle, IntPtr _)
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);

            if (processId == (uint)Environment.ProcessId)
            {
                return true;
            }

            var length = GetWindowTextLength(handle);

            if (length <= 0)
            {
                return true;
            }

            var builder = new StringBuilder(length + 1);

            if (GetWindowText(handle, builder, builder.Capacity) <= 0
                || !string.Equals(builder.ToString(), title, StringComparison.Ordinal))
            {
                return true;
            }

            if (!GetClientRect(handle, out var client))
            {
                return true;
            }

            var origin = new WindowPoint { X = 0, Y = 0 };

            if (!ClientToScreen(handle, ref origin))
            {
                return true;
            }

            resultWindow = handle;
            resultX = origin.X;
            resultY = origin.Y;
            resultWidth = client.Right - client.Left;
            resultHeight = client.Bottom - client.Top;
            found = true;
            return false;
        }

        try
        {
            EnumWindows(Callback, IntPtr.Zero);
        }
        catch
        {
            return false;
        }

        window = resultWindow;
        x = resultX;
        y = resultY;
        width = resultWidth;
        height = resultHeight;
        return found && width > 0 && height > 0;
    }

    /// <summary>
    /// Starts capturing. A null <paramref name="video"/> records audio only.
    /// </summary>
    public static CaptureRecorder Start(
        CaptureDevice? video,
        CaptureDevice? audio,
        int width,
        int height,
        int fps,
        string outputPath)
    {
        if (Ffmpeg.Exe is null)
        {
            throw new InvalidOperationException("ffmpeg was not found.");
        }

        var info = new ProcessStartInfo(Ffmpeg.Exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        void Arg(string value) => info.ArgumentList.Add(value);

        Arg("-hide_banner");
        Arg("-loglevel");
        Arg("warning");
        Arg("-y");

        if (video is { IsScreen: true } && OperatingSystem.IsWindows())
        {
            // Do not pass -video_size to the desktop input: gdigrab would crop
            // that region instead of scaling it. The full desktop is captured and
            // the output filter below fits it to the canvas.
            Arg("-f");
            Arg("gdigrab");
            Arg("-framerate");
            Arg(fps.ToString(CultureInfo.InvariantCulture));
            Arg("-draw_mouse");
            Arg("1");
            Arg("-i");
            Arg("desktop");
        }
        else if (video is { IsWindow: true } && OperatingSystem.IsWindows())
        {
            if (!TryGetWindowClientRect(
                video.Name,
                out var windowHandle,
                out var left,
                out var top,
                out var windowWidth,
                out var windowHeight))
            {
                throw new InvalidOperationException(
                    "The selected window is not available. Refresh the list and try again.");
            }

            // gdigrab's title= input reads the window's GDI surface, which is blank
            // for GPU-composited apps. Capturing the desktop region occupied by the
            // window uses the composited pixels instead, so the window must be on
            // top while it is recorded.
            SetForegroundWindow(windowHandle);
            Thread.Sleep(250);
            windowWidth = Math.Max(2, windowWidth - (windowWidth % 2));
            windowHeight = Math.Max(2, windowHeight - (windowHeight % 2));
            Arg("-f");
            Arg("gdigrab");
            Arg("-framerate");
            Arg(fps.ToString(CultureInfo.InvariantCulture));
            Arg("-offset_x");
            Arg(left.ToString(CultureInfo.InvariantCulture));
            Arg("-offset_y");
            Arg(top.ToString(CultureInfo.InvariantCulture));
            Arg("-video_size");
            Arg($"{windowWidth}x{windowHeight}");
            Arg("-draw_mouse");
            Arg("0");
            Arg("-i");
            Arg("desktop");
        }
        else if (video is not null && OperatingSystem.IsWindows())
        {
            Arg("-f");
            Arg("dshow");
            Arg("-framerate");
            Arg(fps.ToString(CultureInfo.InvariantCulture));
            Arg("-video_size");
            Arg($"{width}x{height}");
            Arg("-i");
            Arg($"video={video.Name}");
        }

        if (audio is not null && OperatingSystem.IsWindows())
        {
            Arg("-f");
            Arg("dshow");
            Arg("-i");
            Arg($"audio={audio.Name}");
        }

        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (video is not null)
        {
            // Fit the source inside the project canvas without cropping, then pad
            // the remainder. A 4K desktop is scaled down; a small window is scaled
            // up. The resulting file is exactly canvas-sized, so it drops onto the
            // timeline at full frame.
            Arg("-vf");
            Arg($"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1");
            Arg("-c:v");
            Arg("libx264");
            Arg("-preset");
            Arg("veryfast");
            Arg("-crf");
            Arg("21");
            Arg("-pix_fmt");
            Arg("yuv420p");
        }
        else
        {
            Arg("-vn");
        }

        if (audio is not null)
        {
            Arg("-c:a");
            Arg("aac");
            Arg("-b:a");
            Arg("160k");
        }

        Arg("-movflags");
        Arg("+faststart");
        Arg(outputPath);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var recorder = new CaptureRecorder(process, outputPath, video is not null);

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (recorder._log)
                {
                    recorder._log.AppendLine(e.Data);
                }
            }
        };

        process.OutputDataReceived += (_, _) => { };
        process.Exited += (_, _) => recorder._exited.TrySetResult(true);
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        return recorder;
    }

    /// <summary>
    /// Asks ffmpeg to finish the file and waits for it to exit.
    /// </summary>
    public async Task<bool> StopAsync()
    {
        if (_stopped)
        {
            return File.Exists(OutputPath);
        }

        _stopped = true;

        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("q");
                await _process.StandardInput.FlushAsync();
                _process.StandardInput.Close();
            }

            var finished = await Task.WhenAny(_exited.Task, Task.Delay(TimeSpan.FromSeconds(12)));

            if (finished != _exited.Task && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
        }
        catch
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Already gone.
            }
        }

        try
        {
            return _process.ExitCode == 0 && File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 512;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
        }

        _process.Dispose();
    }
}
