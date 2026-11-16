using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MovieDotNet.Core.Media;

public sealed record FfprobeStream(
    int Index,
    string CodecType,
    string CodecName,
    int Width,
    int Height,
    double Fps,
    double Duration,
    int Channels);

public sealed record MediaInfo(
    double Duration,
    int Width,
    int Height,
    double Fps,
    bool HasAudio,
    string VideoCodec,
    string AudioCodec);

/// <summary>
/// Locates and drives the ffmpeg/ffprobe binaries. Both are looked up next to the
/// application, in a tools folder, in the winget package cache and finally on
/// PATH, so a normal install is picked up without configuration.
/// </summary>
public static class Ffmpeg
{
    private static readonly Lazy<string?> ResolvedFfmpeg = new(() => Resolve("ffmpeg.exe"));
    private static readonly Lazy<string?> ResolvedFfprobe = new(() => Resolve("ffprobe.exe"));

    public static string? Exe => ResolvedFfmpeg.Value;
    public static string? ProbeExe => ResolvedFfprobe.Value;
    public static bool Available => Exe is not null;
    public static bool ProbeAvailable => ProbeExe is not null;

    public static string Status => Exe is null
        ? "ffmpeg not found"
        : $"ffmpeg: {Exe}";

    /// <summary>Every candidate location this machine offers, for the settings panel.</summary>
    public static IEnumerable<string> Candidates()
    {
        foreach (var path in ToolDirectories())
        {
            var exe = Path.Combine(path, "ffmpeg.exe");

            if (File.Exists(exe))
            {
                yield return exe;
            }
        }
    }

    private static IEnumerable<string> ToolDirectories()
    {
        var env = Environment.GetEnvironmentVariable("MOVIEDOTNET_FFMPEG");

        if (!string.IsNullOrWhiteSpace(env))
        {
            var dir = Path.GetDirectoryName(env) ?? env;
            yield return dir;
        }

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "tools");
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg", "bin");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "tools");
        yield return @"C:\ffmpeg\bin";
        yield return @"C:\Program Files\ffmpeg\bin";

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var winget = Path.Combine(local, "Microsoft", "WinGet", "Packages");

        if (Directory.Exists(winget))
        {
            foreach (var package in SafeDirectories(winget, "Gyan.FFmpeg*").Concat(SafeDirectories(winget, "*FFmpeg*")))
            {
                foreach (var bin in SafeDirectories(package, "bin"))
                {
                    yield return bin;
                }

                foreach (var nested in Directory.EnumerateDirectories(package, "*", SearchOption.AllDirectories).Take(12))
                {
                    var bin = Path.Combine(nested, "bin");

                    if (Directory.Exists(bin))
                    {
                        yield return bin;
                    }
                }
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                yield return dir.Trim();
            }
        }
    }

    private static IEnumerable<string> SafeDirectories(string root, string pattern)
    {
        try
        {
            return Directory.GetDirectories(root, pattern);
        }
        catch
        {
            return [];
        }
    }

    private static string? Resolve(string fileName)
    {
        foreach (var dir in ToolDirectories())
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    public sealed record RunResult(int ExitCode, string StandardError, string StandardOutput)
    {
        public bool Ok => ExitCode == 0;
    }

    /// <summary>Runs ffmpeg with the given argument list and captures both pipes.</summary>
    public static async Task<RunResult> RunAsync(
        string arguments,
        Action<string>? onErrorLine = null,
        Action<double>? onProgress = null,
        CancellationToken cancellation = default)
    {
        if (Exe is null)
        {
            return new RunResult(-1, "ffmpeg was not found.", "");
        }

        var info = new ProcessStartInfo(Exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        info.ArgumentList.Add("-hide_banner");
        info.ArgumentList.Add("-nostdin");
        info.ArgumentList.Add("-y");

        foreach (var argument in FfmpegArgs.Split(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var error = new StringBuilder();
        var output = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            error.AppendLine(e.Data);
            onErrorLine?.Invoke(e.Data);
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            output.AppendLine(e.Data);
            ParseProgress(e.Data, onProgress);
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already gone.
            }

            throw;
        }

        return new RunResult(process.ExitCode, error.ToString(), output.ToString());
    }

    private static void ParseProgress(string line, Action<double>? onProgress)
    {
        if (onProgress is null || !line.StartsWith("out_time_us=", StringComparison.Ordinal))
        {
            return;
        }

        if (double.TryParse(line[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out var microseconds))
        {
            onProgress(microseconds / 1_000_000d);
        }
    }

    /// <summary>Reads stream metadata through ffprobe.</summary>
    public static async Task<MediaInfo?> ProbeAsync(string path, CancellationToken cancellation = default)
    {
        var probe = ProbeExe;

        if (probe is null || !File.Exists(path))
        {
            return null;
        }

        var info = new ProcessStartInfo(probe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.ArgumentList.Add("-v");
        info.ArgumentList.Add("error");
        info.ArgumentList.Add("-print_format");
        info.ArgumentList.Add("json");
        info.ArgumentList.Add("-show_format");
        info.ArgumentList.Add("-show_streams");
        info.ArgumentList.Add(path);

        using var process = new Process { StartInfo = info };

        try
        {
            process.Start();
            var json = await process.StandardOutput.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return ParseProbe(json);
        }
        catch
        {
            return null;
        }
    }

    private static MediaInfo? ParseProbe(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var streams = root.TryGetProperty("streams", out var streamsElement)
                ? streamsElement.EnumerateArray().ToList()
                : [];

            var video = streams.FirstOrDefault(s => Str(s, "codec_type") == "video");
            var audio = streams.FirstOrDefault(s => Str(s, "codec_type") == "audio");
            var duration = 0d;

            if (root.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var durationElement)
                && double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
            }

            if (duration <= 0 && video.ValueKind != JsonValueKind.Undefined
                && video.TryGetProperty("duration", out var streamDuration)
                && double.TryParse(streamDuration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                duration = parsed;
            }

            var fps = 0d;

            if (video.ValueKind != JsonValueKind.Undefined
                && video.TryGetProperty("avg_frame_rate", out var rate)
                && rate.GetString() is { } rateText
                && rateText.Contains('/'))
            {
                var parts = rateText.Split('/');

                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
                    && den > 0)
                {
                    fps = num / den;
                }
            }

            return new MediaInfo(
                duration,
                video.ValueKind != JsonValueKind.Undefined ? Int(video, "width") : 0,
                video.ValueKind != JsonValueKind.Undefined ? Int(video, "height") : 0,
                fps,
                audio.ValueKind != JsonValueKind.Undefined,
                video.ValueKind != JsonValueKind.Undefined ? Str(video, "codec_name") : "",
                audio.ValueKind != JsonValueKind.Undefined ? Str(audio, "codec_name") : "");
        }
        catch
        {
            return null;
        }
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;

    /// <summary>Quotes a value for ffmpeg's filtergraph dialect.</summary>
    public static string EscapeFilterValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace(":", "\\:").Replace("%", "\\%");
}

/// <summary>Splits a command line into ffmpeg arguments, honouring double quotes.</summary>
public static class FfmpegArgs
{
    public static List<string> Split(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var character in arguments)
        {
            switch (character)
            {
                case '"':
                    quoted = !quoted;
                    break;
                case ' ' when !quoted:
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
