namespace MovieDotNet.UI;

/// <summary>Time and number formatting shared by the editor chrome.</summary>
public static class Format
{
    /// <summary>Formats seconds as <c>m:ss.ff</c>.</summary>
    public static string Time(double seconds)
    {
        seconds = Math.Max(0, seconds);
        var minutes = (int)(seconds / 60);
        var rest = seconds - minutes * 60;
        return $"{minutes}:{rest:00.00}";
    }

    /// <summary>Formats seconds as a timecode with frames.</summary>
    public static string Timecode(double seconds, double fps)
    {
        seconds = Math.Max(0, seconds);
        var rate = Math.Max(1, (int)Math.Round(fps));
        var totalFrames = (int)Math.Round(seconds * rate);
        var frames = totalFrames % rate;
        var totalSeconds = totalFrames / rate;
        var span = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}:{frames:00}";
    }

    public static string Short(double seconds) =>
        seconds < 1 ? $"{seconds * 1000:0}ms" : $"{seconds:0.##}s";

    public static string Percent(double value) => $"{value * 100:0}%";
}
