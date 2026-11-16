using System.Globalization;

namespace MovieDotNet.Core.Media;

/// <summary>Runs an export and reports progress against the plan duration.</summary>
public static class ExportService
{
    /// <summary>Renders a project through ffmpeg and reports progress.</summary>
    /// <param name="document">The project to render.</param>
    /// <param name="settings">Resolution, frame rate, quality and range.</param>
    /// <param name="outputPath">Destination media path.</param>
    /// <param name="cacheDirectory">Folder used for intermediate clips.</param>
    /// <param name="progress">Receives progress in the range 0..1.</param>
    /// <param name="log">Receives diagnostic output lines.</param>
    /// <param name="cancellation">Cancels the render and stops ffmpeg.</param>
    /// <returns>A success flag and the ffmpeg output tail.</returns>
    public static async Task<(bool Ok, string Output)> RenderAsync(
        ProjectDocument document,
        RenderSettings settings,
        string outputPath,
        string cacheDirectory,
        Action<double>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellation = default)
    {
        var plan = RenderGraph.Build(document, settings, cacheDirectory);
        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var duration = Math.Max(0.1, plan.Duration - (settings.Start ?? 0));
        var lastReport = -1d;

        var arguments = string.Join(" ", plan.BuildArguments(outputPath).Select(RenderPlan.QuoteArgument));

        var result = await Ffmpeg.RunAsync(
            arguments,
            line =>
            {
                if (line.Contains("Error", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke(line);
                }
            },
            seconds =>
            {
                if (progress is null)
                {
                    return;
                }

                var value = Math.Clamp(seconds / duration, 0, 1);

                if (value - lastReport >= 0.01)
                {
                    lastReport = value;
                    progress(value);
                }
            },
            cancellation);

        if (!result.Ok)
        {
            return (false, result.StandardError);
        }

        progress?.Invoke(1);
        return (true, outputPath);
    }

    /// <summary>Renders a single frame of the composition to an image file.</summary>
    public static async Task<(bool Ok, string Output)> SnapshotAsync(
        ProjectDocument document,
        RenderSettings settings,
        double time,
        string outputPath,
        string cacheDirectory,
        CancellationToken cancellation = default)
    {
        var plan = RenderGraph.Build(document, settings, cacheDirectory);
        var arguments = new List<string>(plan.BuildArguments(outputPath));
        arguments.RemoveAt(arguments.Count - 1); // drop the output path
        arguments.AddRange(
        [
            "-frames:v", "1",
            "-update", "1",
            "-an",
            "-ss", time.ToString("0.###", CultureInfo.InvariantCulture),
            outputPath
        ]);

        var result = await Ffmpeg.RunAsync(
            string.Join(" ", arguments.Select(RenderPlan.QuoteArgument)),
            cancellation: cancellation);

        return result.Ok
            ? (true, outputPath)
            : (false, result.StandardError);
    }
}
