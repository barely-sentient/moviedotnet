using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieDotNet.Core;

/// <summary>Transient editor state that is not saved with the project.</summary>
public sealed class EditorUi
{
    public string? SelectedClipId { get; set; }
    public string? SelectedAssetId { get; set; }
    public string? SelectedTrackId { get; set; }
    public double Playhead { get; set; }
    public bool Playing { get; set; }
    public double Zoom { get; set; } = 64;
    public double ScrollX { get; set; }
    public bool Snap { get; set; } = true;
    public bool TimelineFit { get; set; } = true;
    public bool ShowExport { get; set; }
    public bool ShowBrowser { get; set; }
    public bool ShowSettings { get; set; }
    public bool ShowShortcuts { get; set; }
    public string ActiveTab { get; set; } = "media";
    public string MobilePage { get; set; } = "preview";
    public string BrowserMode { get; set; } = "media";
    public string BrowserPath { get; set; } = "";
    public string? Toast { get; set; }
    public double ToastUntil { get; set; }
    public bool Busy { get; set; }
    public string BusyLabel { get; set; } = "";
    public double BusyProgress { get; set; }
    public bool PreviewFullscreen { get; set; }
    public string? Log { get; set; }

    // Capture settings shared by the Record tab.
    public string? CameraDevice { get; set; }
    public string? MicDevice { get; set; }
    public int CaptureWidth { get; set; } = 1280;
    public int CaptureHeight { get; set; } = 720;
    public int CaptureFps { get; set; } = 30;
    public bool RecordWithMic { get; set; } = true;
}

/// <summary>
/// Owns the project document, the transient UI state, undo history and all
/// timeline commands. Mutations raise <see cref="Changed"/> on the caller's
/// thread; background work must marshal through <c>Host.Post</c> before touching
/// the store.
/// </summary>
public sealed class EditorStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string? _pendingSnapshot;
    private double _lastPlayheadNotify;
    private string? _lastLabel;
    private long _lastEditTicks;

    public EditorStore(ProjectDocument document, string? projectPath = null)
    {
        Document = document;
        ProjectPath = projectPath;
        Ui = new EditorUi();
    }

    public ProjectDocument Document { get; private set; }
    public EditorUi Ui { get; }
    public string? ProjectPath { get; private set; }
    public bool Dirty { get; private set; }
    public event Action? Changed;

    public static EditorStore CreateNew() =>
        new(new ProjectDocument
        {
            Name = "Untitled project",
            Tracks =
            {
                new Track { Kind = TrackKind.Overlay, Name = "Overlay", Height = 54 },
                new Track { Kind = TrackKind.Video, Name = "Video 1", Height = 66 },
                new Track { Kind = TrackKind.Audio, Name = "Audio 1", Height = 48 }
            }
        });

    public void Notify() => Changed?.Invoke();
    public void RaiseChanged() => Changed?.Invoke();

    // ---------------------------------------------------------------- history

    /// <summary>Captures the document before a continuous edit such as a drag.</summary>
    public void BeginEdit()
    {
        _pendingSnapshot ??= Serialize();
    }

    /// <summary>Commits a continuous edit, recording one undo step.</summary>
    public void CommitEdit(string? label = null)
    {
        if (_pendingSnapshot is null)
        {
            return;
        }

        _undo.Add(_pendingSnapshot);
        _pendingSnapshot = null;
        _redo.Clear();
        TrimHistory();
        Dirty = true;
        Notify();
    }

    /// <summary>Discards a gesture that changed nothing, such as a click on a clip.</summary>
    public void CancelEdit()
    {
        _pendingSnapshot = null;
    }

    /// <summary>Applies an atomic change and records one undo step.</summary>
    public void Edit(Action<ProjectDocument> change, string? label = null)
    {
        var snapshot = Serialize();
        change(Document);
        _undo.Add(snapshot);
        _redo.Clear();
        TrimHistory();
        Dirty = true;
        _lastLabel = label;
        _lastEditTicks = Environment.TickCount64;
        Notify();
    }

    /// <summary>
    /// Records one undo step for a burst of edits that share a label, such as a
    /// slider drag, instead of one step per input event.
    /// </summary>
    public void EditCoalesced(Action<ProjectDocument> change, string label, int windowMs = 700)
    {
        var now = Environment.TickCount64;

        if (label == _lastLabel && now - _lastEditTicks < windowMs && _undo.Count > 0)
        {
            change(Document);
            Dirty = true;
            _lastEditTicks = now;
            Notify();
            return;
        }

        Edit(change, label);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(Serialize());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        Dirty = true;
        Notify();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(Serialize());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Dirty = true;
        Notify();
    }

    private void Restore(string json)
    {
        Document = JsonSerializer.Deserialize<ProjectDocument>(json, JsonOptions) ?? new ProjectDocument();
        Ui.SelectedClipId = null;
        DockSelection();
    }

    private string Serialize() => JsonSerializer.Serialize(Document, JsonOptions);

    private void TrimHistory()
    {
        while (_undo.Count > 80)
        {
            _undo.RemoveAt(0);
        }
    }

    private void DockSelection()
    {
        if (Ui.SelectedClipId is not null && Document.FindClip(Ui.SelectedClipId) is null)
        {
            Ui.SelectedClipId = null;
        }
    }

    // ------------------------------------------------------------ persistence

    public void Save(string path)
    {
        File.WriteAllText(path, Serialize());
        ProjectPath = path;
        Dirty = false;
        Notify();
    }

    /// <summary>Swaps in a new document, keeping the previous one on the undo stack.</summary>
    public void ReplaceDocument(ProjectDocument document, string? label = null)
    {
        var snapshot = Serialize();
        Document = document;
        _undo.Add(snapshot);
        _redo.Clear();
        TrimHistory();
        Dirty = true;
        DockSelection();
        Notify();
    }

    public void SetProjectPath(string path)
    {
        ProjectPath = path;
        Notify();
    }

    public static EditorStore Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, JsonOptions) ?? new ProjectDocument();
        return new EditorStore(document, path);
    }

    // --------------------------------------------------------------- playhead

    public void SetPlayhead(double seconds, bool throttled = false)
    {
        seconds = Math.Clamp(seconds, 0, Math.Max(0.1, Document.Duration));
        Ui.Playhead = seconds;

        if (throttled)
        {
            var now = Environment.TickCount64;

            if (now - _lastPlayheadNotify < 28)
            {
                return;
            }

            _lastPlayheadNotify = now;
        }

        Notify();
    }

    public void TogglePlay()
    {
        Ui.Playing = !Ui.Playing;
        Notify();
    }

    public void SetZoom(double zoom)
    {
        Ui.Zoom = Math.Clamp(zoom, 6, 640);
        Notify();
    }

    public void Select(string? clipId)
    {
        Ui.SelectedClipId = clipId;
        Ui.SelectedTrackId = clipId is null ? Ui.SelectedTrackId : Document.FindClip(clipId)?.TrackId;
        Ui.ActiveTab = clipId is null ? Ui.ActiveTab : "clip";
        Notify();
    }

    public Clip? SelectedClip => Document.FindClip(Ui.SelectedClipId);

    // --------------------------------------------------------------- document

    public Track EnsureTrack(TrackKind kind)
    {
        var existing = Document.Tracks.FirstOrDefault(t => t.Kind == kind);

        if (existing is not null)
        {
            return existing;
        }

        var track = new Track
        {
            Kind = kind,
            Name = kind switch
            {
                TrackKind.Video => "Video 1",
                TrackKind.Overlay => "Overlay 1",
                TrackKind.Text => "Text 1",
                _ => "Audio 1"
            },
            Height = kind == TrackKind.Audio ? 48 : 58
        };

        Document.Tracks.Insert(kind == TrackKind.Audio ? Document.Tracks.Count : 0, track);
        return track;
    }

    public void AddAsset(MediaAsset asset, bool placeOnTimeline = true)
    {
        Document.Assets.Add(asset);
        Ui.SelectedAssetId = asset.Id;

        if (placeOnTimeline)
        {
            AddAssetToTimeline(asset);
        }
        else
        {
            Edit(_ => { }, "Import media");
        }
    }

    /// <summary>Places an asset at the playhead using the next free slot on its track.</summary>
    public Clip AddAssetToTimeline(MediaAsset asset, double? at = null)
    {
        var kind = asset.Kind switch
        {
            AssetKind.Audio => TrackKind.Audio,
            AssetKind.Image => TrackKind.Overlay,
            _ => TrackKind.Video
        };

        var track = Document.Tracks.FirstOrDefault(t => t.Kind == kind) ?? EnsureTrack(kind);
        var duration = asset.Kind == AssetKind.Image ? 5 : Math.Max(0.2, asset.Duration);
        var start = at ?? Ui.Playhead;

        // Do not silently stack on top of an existing clip.
        while (track.Clips.Any(c => c.Start < start + duration - 0.05 && c.End > start + 0.05))
        {
            start = track.Clips.Where(c => c.End > start).Select(c => c.End).DefaultIfEmpty(start).Max() + 0.1;
        }

        var clip = new Clip
        {
            TrackId = track.Id,
            AssetId = asset.Id,
            Name = asset.Name,
            Kind = kind,
            Start = start,
            Duration = duration,
            InPoint = 0,
            Text = asset.Name,
            Box = new LayerBox()
        };

        Edit(_ => track.Clips.Add(clip), "Add clip");
        Ui.SelectedClipId = clip.Id;
        Ui.SelectedTrackId = track.Id;
        Notify();
        return clip;
    }

    public Clip AddTextClip(string text, double? at = null)
    {
        var track = Document.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Text) ?? EnsureTrack(TrackKind.Text);
        var start = at ?? Ui.Playhead;
        var clip = new Clip
        {
            TrackId = track.Id,
            Kind = TrackKind.Text,
            Name = text.Length > 24 ? text[..24] : text,
            Text = text,
            Start = start,
            Duration = 4,
            Box = new LayerBox { X = 0.08, Y = 0.36, Width = 0.84, Height = 0.28 },
            TransitionId = "none"
        };

        Edit(_ => track.Clips.Add(clip), "Add text");
        Ui.SelectedClipId = clip.Id;
        Ui.SelectedTrackId = track.Id;
        Notify();
        return clip;
    }

    public void DeleteSelected()
    {
        var clip = SelectedClip;

        if (clip is null)
        {
            return;
        }

        var track = Document.TrackOf(clip);
        Edit(_ => track?.Clips.RemoveAll(c => c.Id == clip.Id), "Delete clip");
        Ui.SelectedClipId = null;
    }

    public void DuplicateSelected()
    {
        var clip = SelectedClip;
        var track = clip is null ? null : Document.TrackOf(clip);

        if (clip is null || track is null)
        {
            return;
        }

        var copy = clip.Clone();
        copy.Start = clip.End;
        copy.Id = Guid.NewGuid().ToString("n")[..8];
        Edit(_ => track.Clips.Add(copy), "Duplicate clip");
        Ui.SelectedClipId = copy.Id;
    }

    /// <summary>Splits every unlocked clip that spans the playhead, or just the selection.</summary>
    public void SplitAtPlayhead(bool selectionOnly = false)
    {
        var time = Ui.Playhead;
        var splitCount = 0;
        var snapshot = Serialize();

        foreach (var track in Document.Tracks)
        {
            if (track.Locked)
            {
                continue;
            }

            foreach (var clip in track.Clips.ToList())
            {
                if (selectionOnly && clip.Id != Ui.SelectedClipId)
                {
                    continue;
                }

                if (time <= clip.Start + 0.05 || time >= clip.End - 0.05)
                {
                    continue;
                }

                var cut = time - clip.Start;
                var right = clip.Clone();
                right.Id = Guid.NewGuid().ToString("n")[..8];
                right.Start = time;
                right.Duration = clip.Duration - cut;
                right.InPoint = clip.InPoint + cut * clip.Speed;
                right.TransitionId = "none";

                clip.Duration = cut;
                track.Clips.Add(right);
                splitCount++;
            }
        }

        if (splitCount == 0)
        {
            Toast("Nothing to split at the playhead");
            return;
        }

        _undo.Add(snapshot);
        _redo.Clear();
        Dirty = true;
        Notify();
    }

    public void MoveClip(Clip clip, double start, Track? track = null)
    {
        clip.Start = Math.Max(0, start);

        if (track is not null && track.Id != clip.TrackId && track.Kind == clip.Kind)
        {
            Document.TrackOf(clip)?.Clips.Remove(clip);
            clip.TrackId = track.Id;
            track.Clips.Add(clip);
        }
    }

    public void TrimClip(Clip clip, double start, double duration)
    {
        var delta = start - clip.Start;
        clip.Start = Math.Max(0, start);
        clip.Duration = Math.Max(0.15, duration);
        clip.InPoint = Math.Max(0, clip.InPoint + delta * clip.Speed);
    }

    public void SetTransition(string clipId, string transitionId, double? duration = null)
    {
        var clip = Document.FindClip(clipId);

        if (clip is null)
        {
            return;
        }

        Edit(_ =>
        {
            clip.TransitionId = transitionId;

            if (duration is not null)
            {
                clip.TransitionDuration = Math.Clamp(duration.Value, 0.1, 5);
            }
        }, "Change transition");
    }

    public void UpdateClip(string clipId, Action<Clip> change, string label = "Update clip")
    {
        var clip = Document.FindClip(clipId);

        if (clip is null)
        {
            return;
        }

        Edit(_ => change(clip), label);
    }

    public void UpdateClipLive(string clipId, Action<Clip> change)
    {
        var clip = Document.FindClip(clipId);
        change(clip!);
        Dirty = true;
        Notify();
    }

    public void Toast(string message, double seconds = 3.2)
    {
        Ui.Toast = message;
        Ui.ToastUntil = Environment.TickCount64 + (long)(seconds * 1000);
        Notify();
    }

    public void SetBusy(bool busy, string label = "", double progress = 0)
    {
        Ui.Busy = busy;
        Ui.BusyLabel = label;
        Ui.BusyProgress = progress;
        Notify();
    }

    public void SetProgress(double progress, string? label = null)
    {
        Ui.BusyProgress = progress;

        if (label is not null)
        {
            Ui.BusyLabel = label;
        }

        Notify();
    }

    // ------------------------------------------------------------------ query

    public MediaAsset? AssetOf(Clip? clip) => clip is null ? null : Document.FindAsset(clip.AssetId);

    public IEnumerable<(Track Track, Clip Clip)> ClipsAt(double time) =>
        Document.Tracks
            .Where(t => !t.Hidden)
            .SelectMany(t => t.Clips.Where(c => c.Start <= time && c.End > time).Select(c => (t, c)));

    public double SnapTime(double time, string? ignoreClipId = null)
    {
        if (!Ui.Snap)
        {
            return Math.Max(0, time);
        }

        var threshold = 8 / Math.Max(6, Ui.Zoom);
        var candidates = new List<double> { 0, Ui.Playhead };

        foreach (var clip in Document.Tracks.SelectMany(t => t.Clips).Where(c => c.Id != ignoreClipId))
        {
            candidates.Add(clip.Start);
            candidates.Add(clip.End);
        }

        var best = candidates.Select(c => (Value: c, Distance: Math.Abs(c - time))).OrderBy(c => c.Distance).First();

        return best.Distance <= threshold ? best.Value : Math.Max(0, time);
    }
}
