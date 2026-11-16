using System.Security.Cryptography;
using System.Text;

namespace MovieDotNet.Core.Media;

/// <summary>
/// Generates the visual derivatives the editor needs: poster frames, timeline
/// filmstrips and audio waveforms. Output is cached per file identity so
/// re-importing the same media is instant.
/// </summary>
public static class MediaAnalysis
{
    public static string CacheRoot { get; } = CreateCacheRoot();

    private static string CreateCacheRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        var path = Path.Combine(root, "MovieDotNet", "cache");

        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            var fallback = Path.Combine(Path.GetTempPath(), "MovieDotNet", "cache");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    public static string KeyFor(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var identity = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(identity));
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch
        {
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();
        }
    }

    private static string DirectoryFor(string path)
    {
        var dir = Path.Combine(CacheRoot, KeyFor(path));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Probes a file and fills in duration, size, frame rate and the generated
    /// poster, filmstrip and waveform paths.
    /// </summary>
    public static async Task<MediaAsset?> AnalyzeAsync(string path, CancellationToken cancellation = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var info = await Ffmpeg.ProbeAsync(path, cancellation);

        if (info is null)
        {
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var kind = extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif"
            ? AssetKind.Image
            : info.HasAudio && info.Width == 0
                ? AssetKind.Audio
                : AssetKind.Video;

        var asset = new MediaAsset
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            Kind = kind,
            Duration = kind == AssetKind.Image ? 5 : Math.Max(0.2, info.Duration),
            Width = info.Width,
            Height = info.Height,
            Fps = info.Fps > 1 ? info.Fps : 30,
            HasAudio = info.HasAudio
        };

        await BuildDerivativesAsync(asset, cancellation);
        return asset;
    }

    /// <summary>Regenerates poster, filmstrip and waveform files for an asset.</summary>
    public static async Task BuildDerivativesAsync(MediaAsset asset, CancellationToken cancellation = default)
    {
        var dir = DirectoryFor(asset.Path);
        var poster = Path.Combine(dir, "poster.jpg");
        var strip = Path.Combine(dir, "strip.jpg");
        var wave = Path.Combine(dir, "wave.png");

        if (asset.Kind == AssetKind.Image)
        {
            if (!File.Exists(poster))
            {
                await Ffmpeg.RunAsync(
                    $"-i \"{asset.Path}\" -vf \"scale=480:-2\" -frames:v 1 -q:v 3 \"{poster}\"",
                    cancellation: cancellation);
            }

            asset.Poster = File.Exists(poster) ? poster : null;
            asset.Filmstrip = asset.Poster;
            return;
        }

        var posterAt = Math.Min(2.0, Math.Max(0, asset.Duration * 0.25));
        var tasks = new List<Task>();

        if (!File.Exists(poster))
        {
            tasks.Add(Ffmpeg.RunAsync(
                $"-ss {posterAt.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} -i \"{asset.Path}\" -vf \"scale=480:-2\" -frames:v 1 -q:v 3 \"{poster}\"",
                cancellation: cancellation));
        }

        if (asset.Kind == AssetKind.Video && !File.Exists(strip))
        {
            var frames = 12;
            var fps = Math.Max(0.05, frames / Math.Max(0.5, asset.Duration));
            tasks.Add(Ffmpeg.RunAsync(
                $"-i \"{asset.Path}\" -vf \"fps={fps.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)},scale=-2:64,tile={frames}x1\" -frames:v 1 -q:v 4 \"{strip}\"",
                cancellation: cancellation));
        }

        if (asset.HasAudio && !File.Exists(wave))
        {
            tasks.Add(Ffmpeg.RunAsync(
                $"-i \"{asset.Path}\" -filter_complex \"[0:a]aformat=channel_layouts=mono,showwavespic=s=1800x160:colors=#8bc1ff\" -frames:v 1 \"{wave}\"",
                cancellation: cancellation));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        asset.Poster = File.Exists(poster) ? poster : null;
        asset.Filmstrip = File.Exists(strip) ? strip : asset.Poster;
        asset.Waveform = File.Exists(wave) ? wave : null;
    }

    /// <summary>Supported media extensions for the in-app browser.</summary>
    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".mkv", ".webm", ".avi", ".m4v", ".wmv", ".mpg", ".mpeg", ".ts"];
    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma"];
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];
    public static readonly string[] AllExtensions = [.. VideoExtensions, .. AudioExtensions, .. ImageExtensions];

    public static bool IsSupported(string path) =>
        AllExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
