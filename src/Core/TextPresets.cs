namespace MovieDotNet.Core;

/// <summary>Media types used by HTML-style drag and drop inside the editor.</summary>
public static class DragFormats
{
    /// <summary>Payload is a <see cref="MediaAsset"/> id.</summary>
    public const string Asset = "application/x-moviedotnet-asset";

    /// <summary>Payload is an index into <see cref="TextPreset.All"/>.</summary>
    public const string TextPreset = "application/x-moviedotnet-text-preset";
}

/// <summary>
/// A ready-made title style. Presets are shared by the Text rail and the
/// timeline drop target, so clicking and dragging produce the same clip.
/// </summary>
public sealed record TextPreset(string Title, string Sample, double Size, string Weight)
{
    /// <summary>Every built-in title preset, in rail order.</summary>
    public static IReadOnlyList<TextPreset> All { get; } =
    [
        new("Display", "BIG\nIDEA", 150, "900"),
        new("Title", "Your story starts here", 84, "700"),
        new("Subtitle", "A short supporting line", 46, "500"),
        new("Lower third", "Name / Role", 40, "600"),
        new("Caption", "Captions for social", 34, "600"),
        new("Quote", "“Make it real.”", 64, "700")
    ];

    /// <summary>
    /// Adds a text clip with this preset's size, weight and frame at a time.
    /// </summary>
    /// <param name="store">The editor store to mutate.</param>
    /// <param name="at">Start time in seconds, or the playhead when omitted.</param>
    /// <param name="target">The text track a drop landed on, when compatible.</param>
    /// <returns>The created clip.</returns>
    public Clip AddTo(EditorStore store, double? at = null, Track? target = null)
    {
        var clip = store.AddTextClip(Sample, at, target);
        store.UpdateClipLive(clip.Id, c =>
        {
            c.FontSize = Size;
            c.FontWeight = Weight;
            c.Box = Title == "Lower third"
                ? new LayerBox { X = 0.07, Y = 0.68, Width = 0.5, Height = 0.16 }
                : new LayerBox { X = 0.08, Y = 0.34, Width = 0.84, Height = 0.3 };
        });

        return clip;
    }
}
