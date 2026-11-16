using System.Globalization;
using System.Text;

namespace MovieDotNet.Core.Media;

/// <summary>Options for an export run.</summary>
public sealed class RenderSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 60;
    public int Crf { get; set; } = 20;
    public string Preset { get; set; } = "veryfast";
    public string AudioBitrate { get; set; } = "192k";
    public double? Start { get; set; }
    public double? End { get; set; }
    public string Background { get; set; } = "#05070c";
    public bool IncludeAudio { get; set; } = true;
}

/// <summary>A ready-to-run ffmpeg invocation plus metadata about the graph.</summary>
public sealed class RenderPlan
{
    public required string FilterGraph { get; init; }
    public required List<string> InputArguments { get; init; }
    public required List<string> OutputArguments { get; init; }
    public required double Duration { get; init; }
    public string? Warning { get; init; }

    public string[] BuildArguments(string outputPath)
    {
        var args = new List<string> { "-progress", "pipe:1", "-nostats" };
        args.AddRange(InputArguments);
        args.Add("-filter_complex");
        args.Add(FilterGraph);
        args.AddRange(OutputArguments);
        args.Add(outputPath);
        return [.. args];
    }

    public static string QuoteArgument(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    public string ToCommandLine(string outputPath) =>
        string.Join(" ", BuildArguments(outputPath).Select(QuoteArgument));
}

/// <summary>
/// Translates the timeline into an ffmpeg filter graph. The base video track is
/// built as a concatenated/xfade chain; overlay, text and audio tracks are
/// composited on top. The same plan is used for the full render and for single
/// frame previews so what you see and what you export stay in step.
/// </summary>
public static class RenderGraph
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string F(double value) => value.ToString("0.####", Inv);

    public static RenderPlan Build(ProjectDocument document, RenderSettings settings, string cacheDirectory)
    {
        var fps = settings.Fps > 1 ? settings.Fps : 30;
        var width = Math.Max(16, settings.Width);
        var height = Math.Max(16, settings.Height);
        var duration = document.Tracks.SelectMany(t => t.Clips).DefaultIfEmpty()
            .Max(c => c?.End ?? 0);
        duration = Math.Max(0.2, duration);

        var inputs = new List<string>();
        var filters = new List<string>();
        var inputIndex = new Dictionary<string, int>(); // clip id -> input index
        var textFiles = new List<string>();

        int AddInput(string path, bool loop)
        {
            if (loop)
            {
                inputs.Add("-loop");
                inputs.Add("1");
                inputs.Add("-framerate");
                inputs.Add(F(fps));
            }

            inputs.Add("-i");
            inputs.Add(path);
            return inputs.Count(x => x == "-i") - 1;
        }

        var tracks = document.Tracks.Where(t => !t.Hidden).ToList();
        var visualTracks = tracks.Where(t => t.Kind != TrackKind.Audio).ToList();

        // Tracks are stored top-to-bottom. The bottom-most picture track is the
        // base layer; everything above it is composited on top, bottom-up.
        var baseTrack = visualTracks.LastOrDefault(t => t.Kind is TrackKind.Video or TrackKind.Overlay);
        var overlayTracks = visualTracks.Where(t => t != baseTrack).Reverse().ToList();
        var audioTracks = tracks.Where(t => t.Kind == TrackKind.Audio).ToList();

        // Register inputs for every visual clip that reads from a file.
        foreach (var track in visualTracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.Kind == TrackKind.Text)
                {
                    continue;
                }

                var asset = document.FindAsset(clip.AssetId);

                if (asset is null || !File.Exists(asset.Path))
                {
                    continue;
                }

                inputIndex[clip.Id] = AddInput(asset.Path, asset.Kind == AssetKind.Image);
            }
        }

        foreach (var track in audioTracks)
        {
            foreach (var clip in track.Clips)
            {
                var asset = document.FindAsset(clip.AssetId);

                if (asset is null || !File.Exists(asset.Path))
                {
                    continue;
                }

                if (!inputIndex.ContainsKey(clip.Id))
                {
                    inputIndex[clip.Id] = AddInput(asset.Path, false);
                }
            }
        }

        // ------------------------------------------------------------- base chain
        string baseLabel;
        var background = ParseColor(settings.Background);
        var baseClips = baseTrack is null
            ? []
            : baseTrack.Clips.OrderBy(c => c.Start).ToList();

        if (baseClips.Count == 0)
        {
            filters.Add($"color=c={background}:s={width}x{height}:r={F(fps)}:d={F(duration)},format=yuv420p,setsar=1[base]");
            baseLabel = "base";
        }
        else
        {
            baseLabel = BuildBaseChain(document, baseClips, filters, inputIndex, width, height, fps, background);
        }

        // ---------------------------------------------------------- overlay chain
        var accumulator = baseLabel;
        var overlayCount = 0;

        foreach (var track in overlayTracks)
        {
            foreach (var clip in track.Clips.OrderBy(c => c.Start))
            {
                var overlayLabel = $"ov{overlayCount}";

                if (clip.Kind == TrackKind.Text)
                {
                    var textFilter = BuildDrawText(clip, width, height, cacheDirectory, textFiles);

                    if (textFilter is null)
                    {
                        continue;
                    }

                    var accOut = $"mix{overlayCount}";
                    filters.Add($"[{accumulator}]{textFilter}[{accOut}]");
                    accumulator = accOut;
                    overlayCount++;
                    continue;
                }

                if (!inputIndex.TryGetValue(clip.Id, out var index))
                {
                    continue;
                }

                var sourceLabel = $"src{overlayCount}";
                var prepared = $"prep{overlayCount}";
                var boxWidth = Math.Max(8, (int)Math.Round(clip.Box.Width * width));
                var boxHeight = Math.Max(8, (int)Math.Round(clip.Box.Height * height));
                var boxX = (int)Math.Round(clip.Box.X * width);
                var boxY = (int)Math.Round(clip.Box.Y * height);

                var chain = new StringBuilder();
                chain.Append($"[{index}:v]");

                if (clip.Kind == TrackKind.Text)
                {
                    chain.Append($"trim=duration={F(clip.Duration)},");
                }
                else
                {
                    var asset = document.FindAsset(clip.AssetId);
                    var sourceDuration = asset?.Kind == AssetKind.Image ? clip.Duration : clip.Duration * Math.Max(0.05, clip.Speed);
                    var sourceStart = clip.InPoint;

                    if (asset?.Kind != AssetKind.Image)
                    {
                        chain.Append($"trim=start={F(sourceStart)}:duration={F(sourceDuration)},");
                    }
                    else
                    {
                        chain.Append($"trim=duration={F(sourceDuration)},");
                    }
                }

                chain.Append("setpts=PTS-STARTPTS,");
                chain.Append($"fps={F(fps)},");
                chain.Append($"scale={boxWidth}:{boxHeight}:force_original_aspect_ratio=decrease,");
                chain.Append("format=rgba,");
                chain.Append($"colorchannelmixer=aa={F(Math.Clamp(clip.Opacity, 0, 1))},");

                foreach (var effect in clip.Effects)
                {
                    var definition = EffectRegistry.Get(effect.Id);
                    var filter = definition?.FfmpegFilter(effect.Amount);

                    if (!string.IsNullOrWhiteSpace(filter))
                    {
                        chain.Append(filter).Append(',');
                    }
                }

                if (Math.Abs(clip.Rotation) > 0.01)
                {
                    chain.Append($"rotate={F(clip.Rotation * Math.PI / 180)}:ow=rotw(iw):oh=roth(ih):c=none,");
                }

                var animation = Math.Clamp(Math.Min(clip.AnimationDuration, clip.Duration / 2), 0.05, 5);

                if (HasFade(clip.EnterAnimation))
                {
                    chain.Append($"fade=t=in:st=0:d={F(animation)}:alpha=1,");
                }

                if (HasFade(clip.ExitAnimation))
                {
                    chain.Append($"fade=t=out:st={F(Math.Max(0, clip.Duration - animation))}:d={F(animation)}:alpha=1,");
                }

                chain.Append($"setpts=PTS+{F(clip.Start)}/TB");
                chain.Append($"[{prepared}]");
                filters.Add(chain.ToString());

                filters.Add(
                    $"[{accumulator}][{prepared}]overlay=" +
                    $"x='{boxX}+({boxWidth}-overlay_w)/2':" +
                    $"y='{boxY}+({boxHeight}-overlay_h)/2':" +
                    $"enable='between(t,{F(clip.Start)},{F(clip.End)})':" +
                    $"eof_action=pass:format=auto[{overlayLabel}]");

                accumulator = overlayLabel;
                overlayCount++;
            }
        }

        // The base chain can end before the audio (for example a music bed that
        // outlasts the picture). Pad the video with the canvas colour so the
        // exported stream covers the whole timeline instead of stopping early.
        var visualEnd = baseClips.Count == 0
            ? 0
            : baseClips.Max(c => c.End);
        var pad = baseClips.Count == 0 ? 0 : Math.Max(0, duration - visualEnd);

        filters.Add(pad > 0.02
            ? $"[{accumulator}]format=yuv420p,setsar=1,tpad=stop_mode=add:stop_duration={F(pad)}:color={background}[vout]"
            : $"[{accumulator}]format=yuv420p,setsar=1[vout]");

        // ------------------------------------------------------------ audio chain
        var audioLabels = new List<string>();
        var audioCount = 0;

        if (settings.IncludeAudio)
        {
            foreach (var track in tracks)
            {
                if (track.Muted)
                {
                    continue;
                }

                foreach (var clip in track.Clips.OrderBy(c => c.Start))
                {
                    var asset = document.FindAsset(clip.AssetId);

                    if (asset is null || !asset.HasAudio || !inputIndex.TryGetValue(clip.Id, out var index))
                    {
                        continue;
                    }

                    var label = $"a{audioCount}";
                    var chain = new StringBuilder();
                    var sourceDuration = clip.Duration * Math.Max(0.05, clip.Speed);
                    chain.Append($"[{index}:a]atrim=start={F(clip.InPoint)}:duration={F(sourceDuration)},");
                    chain.Append("asetpts=PTS-STARTPTS,");

                    if (Math.Abs(clip.Speed - 1) > 0.001 && clip.Speed is >= 0.5 and <= 2)
                    {
                        chain.Append($"atempo={F(clip.Speed)},");
                    }

                    chain.Append($"volume={F(Math.Clamp(clip.Volume, 0, 4))},");
                    chain.Append("aformat=sample_rates=48000:channel_layouts=stereo,");
                    chain.Append($"adelay={Math.Max(0, (int)Math.Round(clip.Start * 1000))}|{Math.Max(0, (int)Math.Round(clip.Start * 1000))}");
                    chain.Append($"[{label}]");
                    filters.Add(chain.ToString());
                    audioLabels.Add(label);
                    audioCount++;
                }
            }
        }

        if (audioLabels.Count == 0)
        {
            filters.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={F(duration)}[aout]");
        }
        else if (audioLabels.Count == 1)
        {
            filters.Add($"[{audioLabels[0]}]apad,atrim=duration={F(duration)}[aout]");
        }
        else
        {
            var mixInputs = string.Concat(audioLabels.Select(l => $"[{l}]"));
            filters.Add($"{mixInputs}amix=inputs={audioLabels.Count}:duration=longest:normalize=0,apad,atrim=duration={F(duration)}[aout]");
        }

        var output = new List<string>
        {
            "-map", "[vout]",
            "-map", "[aout]",
            "-c:v", "libx264",
            "-preset", settings.Preset,
            "-crf", Math.Clamp(settings.Crf, 0, 51).ToString(Inv),
            "-pix_fmt", "yuv420p",
            "-r", F(fps),
            "-c:a", "aac",
            "-b:a", settings.AudioBitrate,
            "-movflags", "+faststart",
            "-t", F(duration)
        };

        if (settings.Start is { } start && start > 0.01)
        {
            output.Add("-ss");
            output.Add(F(start));
        }

        if (settings.End is { } end && end > 0.02)
        {
            output.Add("-t");
            output.Add(F(Math.Max(0.1, end - (settings.Start ?? 0))));
        }

        return new RenderPlan
        {
            FilterGraph = string.Join(";", filters),
            InputArguments = inputs,
            OutputArguments = output,
            Duration = duration
        };
    }

    private static string BuildBaseChain(
        ProjectDocument document,
        List<Clip> clips,
        List<string> filters,
        Dictionary<string, int> inputIndex,
        int width,
        int height,
        double fps,
        string background)
    {
        var accumulator = "";
        var accumulatorDuration = 0d;
        var count = 0;

        void Fill(double gapDuration)
        {
            if (gapDuration <= 0.01)
            {
                return;
            }

            var gapLabel = $"gp{count++}";
            filters.Add($"color=c={background}:s={width}x{height}:r={F(fps)}:d={F(gapDuration)},format=yuv420p,setsar=1[{gapLabel}]");
            Append(gapLabel, gapDuration, null);
        }

        void Append(string label, double segmentDuration, Clip? current)
        {
            if (accumulator.Length == 0)
            {
                accumulator = label;
                accumulatorDuration = segmentDuration + Math.Max(0, current?.Start ?? 0);
                return;
            }

            var start = current?.Start ?? accumulatorDuration;
            var overlap = accumulatorDuration - start;
            var transition = current is not null && TransitionRegistry.IsTransition(current.TransitionId)
                ? TransitionRegistry.Get(current.TransitionId)
                : null;
            var name = $"b{count++}";

            if (transition is not null && overlap > 0.05)
            {
                var transitionDuration = Math.Clamp(overlap, 0.05, Math.Max(0.06, Math.Min(accumulatorDuration, segmentDuration) - 0.02));
                var offset = Math.Max(0, accumulatorDuration - transitionDuration);
                filters.Add(
                    $"[{accumulator}][{label}]xfade=transition={transition.Xfade}:duration={F(transitionDuration)}:offset={F(offset)}[{name}]");
                accumulatorDuration = accumulatorDuration + segmentDuration - transitionDuration;
                accumulator = name;
                return;
            }

            if (overlap > 0.01)
            {
                var trimmed = $"cut{count++}";
                filters.Add($"[{accumulator}]trim=duration={F(Math.Max(0.05, start))},setpts=PTS-STARTPTS[{trimmed}]");
                accumulator = trimmed;
                accumulatorDuration = Math.Max(0.05, start);
            }

            var concatLabel = $"b{count++}";
            filters.Add($"[{accumulator}][{label}]concat=n=2:v=1:a=0[{concatLabel}]");
            accumulator = concatLabel;
            accumulatorDuration += segmentDuration;
        }

        var cursor = 0d;

        foreach (var clip in clips)
        {
            Fill(clip.Start - cursor);

            if (inputIndex.TryGetValue(clip.Id, out var index))
            {
                var asset = document.FindAsset(clip.AssetId);
                var label = $"seg{count++}";
                var chain = new StringBuilder();
                chain.Append($"[{index}:v]");

                if (asset?.Kind == AssetKind.Image)
                {
                    chain.Append($"trim=duration={F(clip.Duration)},");
                }
                else
                {
                    chain.Append($"trim=start={F(clip.InPoint)}:duration={F(clip.Duration * Math.Max(0.05, clip.Speed))},");
                }

                chain.Append("setpts=PTS-STARTPTS,");
                chain.Append($"fps={F(fps)},");

                var fullCanvas = clip.Box.X <= 0.001 && clip.Box.Y <= 0.001
                    && clip.Box.Width >= 0.999 && clip.Box.Height >= 0.999;
                var boxWidth = Math.Max(8, (int)Math.Round(clip.Box.Width * width));
                var boxHeight = Math.Max(8, (int)Math.Round(clip.Box.Height * height));
                var boxX = (int)Math.Round(clip.Box.X * width);
                var boxY = (int)Math.Round(clip.Box.Y * height);

                if (fullCanvas)
                {
                    // Fit the source inside the canvas instead of cropping it.
                    // Sources with a different aspect ratio are letterboxed
                    // against the canvas colour so the whole frame stays visible.
                    chain.Append($"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,");
                    chain.Append($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color={background},");
                    chain.Append("setsar=1,format=yuv420p");
                }
                else
                {
                    // The base clip has been moved or resized into a layer: fit it
                    // inside its transform box, then composite that box on top of
                    // the canvas background at the requested position.
                    chain.Append($"scale={boxWidth}:{boxHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2,");
                    chain.Append("setsar=1,format=rgba");
                }

                foreach (var effect in clip.Effects)
                {
                    var definition = EffectRegistry.Get(effect.Id);
                    var filter = definition?.FfmpegFilter(effect.Amount);

                    if (!string.IsNullOrWhiteSpace(filter))
                    {
                        chain.Append(',').Append(filter);
                    }
                }

                if (fullCanvas)
                {
                    chain.Append($"[{label}]");
                    filters.Add(chain.ToString());
                }
                else
                {
                    var sourceLabel = $"{label}fit";
                    var backgroundLabel = $"{label}bg";

                    chain.Append($"[{sourceLabel}]");
                    filters.Add(chain.ToString());
                    filters.Add($"color=c={background}:s={width}x{height}:r={F(fps)}:d={F(clip.Duration)},format=yuv420p,setsar=1[{backgroundLabel}]");
                    filters.Add(
                        $"[{backgroundLabel}][{sourceLabel}]overlay=" +
                        $"x='{boxX}+({boxWidth}-overlay_w)/2':" +
                        $"y='{boxY}+({boxHeight}-overlay_h)/2':" +
                        $"eof_action=pass:shortest=0,format=yuv420p,setsar=1[{label}]");
                }

                Append(label, clip.Duration, clip);
            }
            else
            {
                // A clip whose file is gone still holds its slot on the timeline.
                var label = $"miss{count++}";
                filters.Add($"color=c={background}:s={width}x{height}:r={F(fps)}:d={F(clip.Duration)},format=yuv420p,setsar=1[{label}]");
                Append(label, clip.Duration, clip);
            }

            cursor = Math.Max(cursor, clip.End);
        }

        if (accumulator.Length == 0)
        {
            filters.Add($"color=c={background}:s={width}x{height}:r={F(fps)}:d=1,format=yuv420p,setsar=1[emptybase]");
            accumulator = "emptybase";
        }

        return accumulator;
    }

    private static string? BuildDrawText(Clip clip, int width, int height, string cacheDirectory, List<string> textFiles)
    {
        var text = string.IsNullOrWhiteSpace(clip.Text) ? "Text" : clip.Text;
        var fontFile = ResolveFont(clip.FontWeight);
        var directory = Path.Combine(cacheDirectory, "text");
        Directory.CreateDirectory(directory);
        var textPath = Path.Combine(directory, $"{clip.Id}.txt");

        try
        {
            File.WriteAllText(textPath, text);
            textFiles.Add(textPath);
        }
        catch
        {
            return null;
        }

        var boxWidth = clip.Box.Width * width;
        var boxHeight = clip.Box.Height * height;
        var boxX = clip.Box.X * width;
        var boxY = clip.Box.Y * height;
        var fontSize = (int)Math.Round(clip.FontSize * height / 1080.0 * 1.4);
        fontSize = Math.Max(8, fontSize);

        var x = clip.Align switch
        {
            "left" => F(boxX),
            "right" => $"{F(boxX + boxWidth)}-text_w",
            _ => $"{F(boxX)}+({F(boxWidth)}-text_w)/2"
        };

        var y = $"{F(boxY)}+({F(boxHeight)}-text_h)/2";
        var alpha = Motion.AlphaExpression(clip, 30);
        var color = ToFfmpegColor(clip.Color);
        var shadow = clip.Shadow == "none"
            ? ""
            : ":shadowx=3:shadowy=3:shadowcolor=black@0.55";
        var fontPath = fontFile.Length == 0 ? "" : $"fontfile='{EscapePath(fontFile)}':";

        return $"drawtext={fontPath}textfile='{EscapePath(textPath)}':" +
               $"fontsize={fontSize}:fontcolor={color}:line_spacing={Math.Max(2, fontSize / 6)}:" +
               $"x='{x}':y='{y}':alpha='{alpha}'{shadow}:" +
               $"enable='between(t,{F(clip.Start)},{F(clip.End)})'";
    }

    private static bool HasFade(string animation) =>
        animation is "fade" or "slide-up" or "slide-down" or "slide-left" or "slide-right"
            or "scale" or "zoom-in" or "zoom-out" or "bounce" or "float" or "spin" or "blur";

    private static string ResolveFont(string weight)
    {
        var bold = weight is "700" or "800" or "900" or "bold";
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            candidates.Add(Path.Combine(fonts, bold ? "segoeuib.ttf" : "segoeui.ttf"));
            candidates.Add(Path.Combine(fonts, bold ? "arialbd.ttf" : "arial.ttf"));
            candidates.Add(Path.Combine(fonts, "seguisb.ttf"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/System/Library/Fonts/Supplemental/Arial Bold.ttf");
            candidates.Add("/System/Library/Fonts/Helvetica.ttc");
        }
        else
        {
            candidates.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
            candidates.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        }

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    private static string EscapePath(string path) =>
        path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");

    private static string ParseColor(string color)
    {
        var value = color.TrimStart('#');

        if (value.Length == 6)
        {
            return $"0x{value}";
        }

        return "black";
    }

    private static string ToFfmpegColor(string color)
    {
        var value = color.TrimStart('#');

        return value.Length switch
        {
            6 => $"0x{value}",
            8 => $"0x{value[2..]}{value[..2]}",
            _ => "white"
        };
    }
}
