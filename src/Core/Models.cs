using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieDotNet.Core;

/// <summary>Classifies a media asset by the kind of content it carries.</summary>
public enum AssetKind
{
    /// <summary>A video file, optionally with an audio stream.</summary>
    Video,

    /// <summary>An audio-only file or stream.</summary>
    Audio,

    /// <summary>A still image used as a clip or overlay.</summary>
    Image
}

/// <summary>Classifies a timeline track by the content it composites.</summary>
public enum TrackKind
{
    /// <summary>The base picture track.</summary>
    Video,

    /// <summary>A track composited above the base picture, often used for picture-in-picture.</summary>
    Overlay,

    /// <summary>A title or caption track.</summary>
    Text,

    /// <summary>A music, voice-over or sound-effect track.</summary>
    Audio
}

/// <summary>A layer's viewport box in canvas-relative units, where 0..1 spans the frame.</summary>
public sealed class LayerBox
{
    /// <summary>Gets or sets the left edge as a fraction of the canvas width.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the top edge as a fraction of the canvas height.</summary>
    public double Y { get; set; }

    /// <summary>Gets or sets the width as a fraction of the canvas width.</summary>
    public double Width { get; set; } = 1;

    /// <summary>Gets or sets the height as a fraction of the canvas height.</summary>
    public double Height { get; set; } = 1;
}

/// <summary>One imported file and the analysis derived from it.</summary>
public sealed class MediaAsset
{
    /// <summary>Gets or sets the stable short identifier used by clips and projects.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Gets or sets the display name, usually the file name without its extension.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the absolute source path.</summary>
    public string Path { get; set; } = "";

    /// <summary>Gets or sets the kind of media this asset carries.</summary>
    public AssetKind Kind { get; set; }

    /// <summary>Gets or sets the source duration in seconds.</summary>
    public double Duration { get; set; }

    /// <summary>Gets or sets the source video width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the source video height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets the source frame rate.</summary>
    public double Fps { get; set; } = 60;

    /// <summary>Gets or sets whether the source has an audio stream.</summary>
    public bool HasAudio { get; set; }

    /// <summary>Gets or sets the absolute path of the generated poster frame.</summary>
    public string? Poster { get; set; }

    /// <summary>Gets or sets the absolute path of a tiled filmstrip image.</summary>
    public string? Filmstrip { get; set; }

    /// <summary>Gets or sets the absolute path of a rendered waveform image.</summary>
    public string? Waveform { get; set; }

    /// <summary>Gets or sets whether analysis is currently running; this is not persisted.</summary>
    [JsonIgnore]
    public bool Analyzing { get; set; }
}

/// <summary>A staged colour, blur or grading effect on a clip.</summary>
public sealed class EffectInstance
{
    /// <summary>Gets or sets the effect registry id.</summary>
    public string Id { get; set; } = "eq";

    /// <summary>Gets or sets the effect strength in the range 0..1.</summary>
    public double Amount { get; set; } = 0.5;
}

/// <summary>A single element placed on a timeline track.</summary>
public sealed class Clip
{
    /// <summary>Gets or sets the stable short identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Gets or sets the owning track id.</summary>
    public string TrackId { get; set; } = "";

    /// <summary>Gets or sets the media asset id, or an empty string for text clips.</summary>
    public string AssetId { get; set; } = "";

    /// <summary>Gets or sets the display name shown on the clip and in the inspector.</summary>
    public string Name { get; set; } = "Clip";

    /// <summary>Gets or sets the kind of track content this clip represents.</summary>
    public TrackKind Kind { get; set; }

    /// <summary>Gets or sets the timeline position in seconds.</summary>
    public double Start { get; set; }

    /// <summary>Gets or sets the length on the timeline in seconds.</summary>
    public double Duration { get; set; } = 4;

    /// <summary>Gets or sets the source offset in seconds.</summary>
    public double InPoint { get; set; }

    /// <summary>Gets or sets the source playback speed; one is normal speed.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Gets or sets the audio level multiplier.</summary>
    public double Volume { get; set; } = 1;

    /// <summary>Gets or sets the layer opacity in the range 0..1.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Gets or sets the layer frame as canvas-relative coordinates.</summary>
    public LayerBox Box { get; set; } = new();

    /// <summary>Gets or sets the layer rotation in degrees.</summary>
    public double Rotation { get; set; }

    /// <summary>Gets or sets the text content for a title clip.</summary>
    public string Text { get; set; } = "Your text";

    /// <summary>Gets or sets the title font size in canvas units.</summary>
    public double FontSize { get; set; } = 96;

    /// <summary>Gets or sets the title colour as a CSS colour string.</summary>
    public string Color { get; set; } = "#ffffff";

    /// <summary>Gets or sets the title font weight.</summary>
    public string FontWeight { get; set; } = "700";

    /// <summary>Gets or sets the title horizontal alignment: left, center or right.</summary>
    public string Align { get; set; } = "center";

    /// <summary>Gets or sets the title letter spacing in canvas units.</summary>
    public double LetterSpacing { get; set; }

    /// <summary>Gets or sets the title shadow preset.</summary>
    public string Shadow { get; set; } = "soft";

    /// <summary>Gets or sets the enter animation preset id.</summary>
    public string EnterAnimation { get; set; } = "fade";

    /// <summary>Gets or sets the exit animation preset id.</summary>
    public string ExitAnimation { get; set; } = "fade";

    /// <summary>Gets or sets the enter/exit animation duration in seconds.</summary>
    public double AnimationDuration { get; set; } = 0.6;

    /// <summary>Gets or sets the transition coming into this clip from the previous one on the same track.</summary>
    public string TransitionId { get; set; } = "none";

    /// <summary>Gets or sets the transition length in seconds.</summary>
    public double TransitionDuration { get; set; } = 0.6;

    /// <summary>Gets or sets the staged effects applied to this clip.</summary>
    public List<EffectInstance> Effects { get; set; } = new();

    /// <summary>Gets the exclusive end time on the timeline.</summary>
    [JsonIgnore]
    public double End => Start + Duration;

    /// <summary>Creates a deep copy with a new identity.</summary>
    /// <returns>A clip that shares no mutable state with this instance.</returns>
    public Clip Clone() => new()
    {
        Id = Guid.NewGuid().ToString("n")[..8],
        TrackId = TrackId,
        AssetId = AssetId,
        Name = Name,
        Kind = Kind,
        Start = Start,
        Duration = Duration,
        InPoint = InPoint,
        Speed = Speed,
        Volume = Volume,
        Opacity = Opacity,
        Box = new LayerBox { X = Box.X, Y = Box.Y, Width = Box.Width, Height = Box.Height },
        Rotation = Rotation,
        Text = Text,
        FontSize = FontSize,
        Color = Color,
        FontWeight = FontWeight,
        Align = Align,
        LetterSpacing = LetterSpacing,
        Shadow = Shadow,
        EnterAnimation = EnterAnimation,
        ExitAnimation = ExitAnimation,
        AnimationDuration = AnimationDuration,
        TransitionId = TransitionId,
        TransitionDuration = TransitionDuration,
        Effects = Effects.Select(e => new EffectInstance { Id = e.Id, Amount = e.Amount }).ToList()
    };
}

/// <summary>A horizontal lane of clips.</summary>
public sealed class Track
{
    /// <summary>Gets or sets the stable short identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Gets or sets the kind of content the track carries.</summary>
    public TrackKind Kind { get; set; }

    /// <summary>Gets or sets the display name shown in the track header.</summary>
    public string Name { get; set; } = "Track";

    /// <summary>Gets or sets whether the track's audio is muted.</summary>
    public bool Muted { get; set; }

    /// <summary>Gets or sets whether the track's picture is hidden.</summary>
    public bool Hidden { get; set; }

    /// <summary>Gets or sets whether editing operations are blocked on this track.</summary>
    public bool Locked { get; set; }

    /// <summary>Gets or sets the preferred row height in CSS pixels.</summary>
    public double Height { get; set; } = 58;

    /// <summary>Gets or sets the clips on this track.</summary>
    public List<Clip> Clips { get; set; } = new();

    /// <summary>Gets the latest clip end time, or zero when the track is empty.</summary>
    public double End => Clips.Count == 0 ? 0 : Clips.Max(c => c.End);
}

/// <summary>The saved project: canvas settings, media and tracks.</summary>
public sealed class ProjectDocument
{
    /// <summary>Gets or sets the project name shown in the title bar.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>Gets or sets the canvas width in pixels.</summary>
    public int Width { get; set; } = 1920;

    /// <summary>Gets or sets the canvas height in pixels.</summary>
    public int Height { get; set; } = 1080;

    /// <summary>Gets or sets the project frame rate.</summary>
    public double Fps { get; set; } = 60;

    /// <summary>Gets or sets the canvas background as a CSS colour.</summary>
    public string Background { get; set; } = "#05070c";

    /// <summary>Gets or sets the media assets available to the project.</summary>
    public List<MediaAsset> Assets { get; set; } = new();

    /// <summary>Gets or sets the tracks in top-to-bottom compositing order.</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>Gets the timeline length rounded up to a whole second, with a ten-second floor.</summary>
    [JsonIgnore]
    public double Duration
    {
        get
        {
            var end = Tracks.SelectMany(t => t.Clips).DefaultIfEmpty().Max(c => c?.End ?? 0);
            return Math.Max(10, Math.Ceiling(end));
        }
    }

    /// <summary>Gets the canvas aspect ratio.</summary>
    [JsonIgnore]
    public double Aspect => Width / (double)Math.Max(1, Height);

    /// <summary>Finds an asset by id.</summary>
    /// <param name="id">The asset id, or <see langword="null"/>.</param>
    /// <returns>The matching asset, or <see langword="null"/>.</returns>
    public MediaAsset? FindAsset(string? id) =>
        id is null ? null : Assets.FirstOrDefault(a => a.Id == id);

    /// <summary>Finds a track by id.</summary>
    /// <param name="id">The track id, or <see langword="null"/>.</param>
    /// <returns>The matching track, or <see langword="null"/>.</returns>
    public Track? FindTrack(string? id) =>
        id is null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>Finds a clip by id across every track.</summary>
    /// <param name="id">The clip id, or <see langword="null"/>.</param>
    /// <returns>The matching clip, or <see langword="null"/>.</returns>
    public Clip? FindClip(string? id) =>
        id is null ? null : Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == id);

    /// <summary>Finds the track that owns a clip.</summary>
    /// <param name="clip">The clip to locate.</param>
    /// <returns>The owning track, or <see langword="null"/> when the clip is detached.</returns>
    public Track? TrackOf(Clip clip) => Tracks.FirstOrDefault(t => t.Id == clip.TrackId);

    /// <summary>Creates a deep copy through the project JSON contract.</summary>
    /// <returns>A document that shares no mutable state with this instance.</returns>
    public ProjectDocument Clone() =>
        JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(this, EditorStore.JsonOptions), EditorStore.JsonOptions)
        ?? new ProjectDocument();
}
