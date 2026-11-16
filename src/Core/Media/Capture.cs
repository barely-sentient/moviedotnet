using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MovieDotNet.Core.Media;

/// <summary>A DirectShow/AVFoundation capture device.</summary>
public sealed record CaptureDevice(string Name, bool IsVideo);

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

    public string OutputPath { get; }
    public bool HasVideo { get; }
    public DateTime StartedAt { get; }
    public TimeSpan Elapsed => DateTime.Now - StartedAt;
    public bool IsRecording => !_process.HasExited;
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

        try
        {
            using var process = new Process { StartInfo = info };
            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);

            var devices = new List<CaptureDevice>();

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

            return devices
                .DistinctBy(d => (d.Name, d.IsVideo))
                .ToList();
        }
        catch
        {
            return [];
        }
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

        if (video is not null && OperatingSystem.IsWindows())
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
