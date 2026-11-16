using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieDotNet.Core;

/// <summary>What a media asset is.</summary>
public enum AssetKind
{
    Video,
    Audio,
    Image
}

/// <summary>What a timeline track carries.</summary>
public enum TrackKind
{
    Video,
    Overlay,
    Text,
    Audio
}

/// <summary>A viewport box in canvas-relative units (0..1).</summary>
public sealed class LayerBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
}

/// <summary>One imported file and the analysis derived from it.</summary>
public sealed class MediaAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public AssetKind Kind { get; set; }
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; } = 60;
    public bool HasAudio { get; set; }

    /// <summary>Absolute path of the generated poster frame.</summary>
    public string? Poster { get; set; }

    /// <summary>Absolute path of a tiled filmstrip image.</summary>
    public string? Filmstrip { get; set; }

    /// <summary>Absolute path of a rendered waveform image.</summary>
    public string? Waveform { get; set; }

    [JsonIgnore]
    public bool Analyzing { get; set; }
}

/// <summary>A staged effect on a clip.</summary>
public sealed class EffectInstance
{
    public string Id { get; set; } = "eq";
    public double Amount { get; set; } = 0.5;
}

/// <summary>A single element placed on a track.</summary>
public sealed class Clip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public string TrackId { get; set; } = "";
    public string AssetId { get; set; } = "";
    public string Name { get; set; } = "Clip";
    public TrackKind Kind { get; set; }

    /// <summary>Timeline position in seconds.</summary>
    public double Start { get; set; }

    /// <summary>Length on the timeline in seconds.</summary>
    public double Duration { get; set; } = 4;

    /// <summary>Source offset in seconds.</summary>
    public double InPoint { get; set; }

    /// <summary>Source playback speed. One is normal.</summary>
    public double Speed { get; set; } = 1;

    public double Volume { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public LayerBox Box { get; set; } = new();

    /// <summary>Rotation in degrees.</summary>
    public double Rotation { get; set; }

    // Text layer options.
    public string Text { get; set; } = "Your text";
    public double FontSize { get; set; } = 96;
    public string Color { get; set; } = "#ffffff";
    public string FontWeight { get; set; } = "700";
    public string Align { get; set; } = "center";
    public double LetterSpacing { get; set; }
    public string Shadow { get; set; } = "soft";

    // Motion.
    public string EnterAnimation { get; set; } = "fade";
    public string ExitAnimation { get; set; } = "fade";
    public double AnimationDuration { get; set; } = 0.6;

    /// <summary>Transition coming into this clip from the previous one on the same track.</summary>
    public string TransitionId { get; set; } = "none";
    public double TransitionDuration { get; set; } = 0.6;

    public List<EffectInstance> Effects { get; set; } = new();

    [JsonIgnore]
    public double End => Start + Duration;

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
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];
    public TrackKind Kind { get; set; }
    public string Name { get; set; } = "Track";
    public bool Muted { get; set; }
    public bool Hidden { get; set; }
    public bool Locked { get; set; }
    public double Height { get; set; } = 58;
    public List<Clip> Clips { get; set; } = new();

    public double End => Clips.Count == 0 ? 0 : Clips.Max(c => c.End);
}

/// <summary>The saved project: canvas settings, media and tracks.</summary>
public sealed class ProjectDocument
{
    public string Name { get; set; } = "Untitled";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 60;
    public string Background { get; set; } = "#05070c";
    public List<MediaAsset> Assets { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();

    [JsonIgnore]
    public double Duration
    {
        get
        {
            var end = Tracks.SelectMany(t => t.Clips).DefaultIfEmpty().Max(c => c?.End ?? 0);
            return Math.Max(10, Math.Ceiling(end));
        }
    }

    [JsonIgnore]
    public double Aspect => Width / (double)Math.Max(1, Height);

    public MediaAsset? FindAsset(string? id) =>
        id is null ? null : Assets.FirstOrDefault(a => a.Id == id);

    public Track? FindTrack(string? id) =>
        id is null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    public Clip? FindClip(string? id) =>
        id is null ? null : Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == id);

    public Track? TrackOf(Clip clip) => Tracks.FirstOrDefault(t => t.Id == clip.TrackId);

    public ProjectDocument Clone() =>
        JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(this, EditorStore.JsonOptions), EditorStore.JsonOptions)
        ?? new ProjectDocument();
}
