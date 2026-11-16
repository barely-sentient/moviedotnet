namespace MovieDotNet.Core.Media;

/// <summary>
/// Builds a self-contained demo project by rendering a handful of test clips
/// with ffmpeg. It gives the editor something real to scrub, split and export
/// without asking for media first.
/// </summary>
public static class DemoProject
{
    /// <summary>Gets the folder where generated demo clips and music are cached.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MovieDotNet",
        "demo");

    private static readonly (string Name, double Duration, string Tint, string Label)[] Scenes =
    [
        ("Mountain drift", 6.5, "hue=h=200:s=1.2", "01 / MOUNTAIN"),
        ("Neon city", 5.5, "hue=h=300:s=1.5", "02 / NEON"),
        ("Ocean run", 7.0, "hue=h=120:s=1.3", "03 / OCEAN")
    ];

    /// <summary>Builds the demo project, generating any missing media with ffmpeg.</summary>
    /// <param name="log">Optional progress callback for the busy pill.</param>
    /// <param name="cancellation">Cancels generation and analysis.</param>
    /// <returns>A project containing three scenes, a music bed and titles.</returns>
    public static async Task<ProjectDocument> BuildAsync(Action<string>? log = null, CancellationToken cancellation = default)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var document = new ProjectDocument
        {
            Name = "Demo reel",
            Width = 1920,
            Height = 1080,
            Fps = 60,
            Background = "#05070c",
            Tracks =
            {
                new Track { Kind = TrackKind.Text, Name = "Titles", Height = 54 },
                new Track { Kind = TrackKind.Overlay, Name = "Overlay", Height = 54 },
                new Track { Kind = TrackKind.Video, Name = "Video 1", Height = 70 },
                new Track { Kind = TrackKind.Audio, Name = "Music", Height = 48 }
            }
        };

        var videoTrack = document.Tracks.First(t => t.Kind == TrackKind.Video);
        var textTrack = document.Tracks.First(t => t.Kind == TrackKind.Text);
        var audioTrack = document.Tracks.First(t => t.Kind == TrackKind.Audio);

        var start = 0d;

        for (var i = 0; i < Scenes.Length; i++)
        {
            var scene = Scenes[i];
            var path = Path.Combine(Directory, $"scene{i + 1}_60.mp4");
            log?.Invoke($"Generating demo clip {i + 1}/{Scenes.Length}");

            if (!File.Exists(path))
            {
                var font = FindDemoFont();
                var source = $"testsrc2=s=1280x720:r=60:d={scene.Duration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
                var drawText = font is null
                    ? ""
                    : $"drawtext=fontfile='{font.Replace("\\", "/").Replace(":", "\\:")}':text='{scene.Label}':fontsize=64:fontcolor=white@0.85:x=(w-text_w)/2:y=h-140:box=1:boxcolor=black@0.35:boxborderw=24,";
                var argument =
                    $"-f lavfi -i \"{source}\" " +
                    $"-f lavfi -i \"sine=frequency={180 + i * 90}:duration={scene.Duration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\" " +
                    $"-vf \"{scene.Tint},{drawText}format=yuv420p\" " +
                    $"-c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 128k -shortest \"{path}\"";

                var result = await Ffmpeg.RunAsync(argument, cancellation: cancellation);

                if (!result.Ok)
                {
                    log?.Invoke(result.StandardError.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "demo render failed");
                }
            }

            if (!File.Exists(path))
            {
                continue;
            }

            var asset = await MediaAnalysis.AnalyzeAsync(path, cancellation);

            if (asset is null)
            {
                continue;
            }

            document.Assets.Add(asset);

            var clip = new Clip
            {
                TrackId = videoTrack.Id,
                AssetId = asset.Id,
                Name = scene.Name,
                Kind = TrackKind.Video,
                Start = start,
                Duration = scene.Duration,
                InPoint = 0,
                Volume = 0.55,
                TransitionId = i == 0 ? "none" : i == 1 ? "dissolve" : "slideleft",
                TransitionDuration = 0.75,
                EnterAnimation = i == 0 ? "none" : "fade",
                ExitAnimation = "fade",
                AnimationDuration = 0.5
            };

            if (i > 0)
            {
                // Overlap the previous clip so the transition has room.
                clip.Start = start - 0.75;
            }

            videoTrack.Clips.Add(clip);
            start = clip.Start + clip.Duration;
        }

        // Music bed.
        var music = Path.Combine(Directory, "music.m4a");

        if (!File.Exists(music))
        {
            log?.Invoke("Generating demo music");

            var melody = "aevalsrc=0.12*sin(2*PI*220*t)+0.08*sin(2*PI*329.63*t)+0.06*sin(2*PI*440*t):s=48000:d=22";
            await Ffmpeg.RunAsync(
                $"-f lavfi -i \"{melody}\" -af \"volume=0.7,afade=t=in:d=1.5,afade=t=out:st=19.5:d=2\" -c:a aac -b:a 160k \"{music}\"",
                cancellation: cancellation);
        }

        if (File.Exists(music))
        {
            var audioAsset = await MediaAnalysis.AnalyzeAsync(music, cancellation);

            if (audioAsset is not null)
            {
                document.Assets.Add(audioAsset);
                audioTrack.Clips.Add(new Clip
                {
                    TrackId = audioTrack.Id,
                    AssetId = audioAsset.Id,
                    Name = "Demo score",
                    Kind = TrackKind.Audio,
                    Start = 0,
                    Duration = Math.Min(22, audioAsset.Duration),
                    Volume = 0.4
                });
            }
        }

        // Titles.
        textTrack.Clips.Add(new Clip
        {
            TrackId = textTrack.Id,
            Kind = TrackKind.Text,
            Name = "TIME TO BUILD",
            Text = "TIME TO\nBUILD.",
            Start = 0.3,
            Duration = 4.2,
            Box = new LayerBox { X = 0.06, Y = 0.24, Width = 0.88, Height = 0.46 },
            FontSize = 150,
            FontWeight = "900",
            LetterSpacing = -2,
            EnterAnimation = "slide-up",
            ExitAnimation = "slide-up",
            AnimationDuration = 0.8
        });

        textTrack.Clips.Add(new Clip
        {
            TrackId = textTrack.Id,
            Kind = TrackKind.Text,
            Name = "Subtitle",
            Text = "A tiny editor built with Donkeh + FFmpeg",
            Start = 5.2,
            Duration = 5.4,
            Box = new LayerBox { X = 0.1, Y = 0.72, Width = 0.8, Height = 0.12 },
            FontSize = 42,
            FontWeight = "600",
            EnterAnimation = "fade",
            ExitAnimation = "fade",
            AnimationDuration = 0.5
        });

        return document;
    }

    private static string? FindDemoFont()
    {
        if (OperatingSystem.IsWindows())
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

            foreach (var name in new[] { "arialbd.ttf", "segoeuib.ttf", "arial.ttf", "segoeui.ttf" })
            {
                var path = Path.Combine(fonts, name);

                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var path in new[] { "/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc" })
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        foreach (var path in new[]
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
        })
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
