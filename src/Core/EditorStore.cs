using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieDotNet.Core;

/// <summary>Transient editor state that is not saved with the project.</summary>
public sealed class EditorUi
{
    /// <summary>Gets or sets the selected clip id, or <see langword="null"/>.</summary>
    public string? SelectedClipId { get; set; }

    /// <summary>Gets or sets the selected media asset id, or <see langword="null"/>.</summary>
    public string? SelectedAssetId { get; set; }

    /// <summary>Gets or sets the selected track id, or <see langword="null"/>.</summary>
    public string? SelectedTrackId { get; set; }

    /// <summary>Gets or sets the current playhead position in seconds.</summary>
    public double Playhead { get; set; }

    /// <summary>Gets or sets whether the transport is playing.</summary>
    public bool Playing { get; set; }

    /// <summary>Gets or sets the timeline zoom in pixels per second.</summary>
    public double Zoom { get; set; } = 64;

    /// <summary>Gets or sets the timeline horizontal scroll offset in pixels.</summary>
    public double ScrollX { get; set; }

    /// <summary>Gets or sets whether edits snap to clip edges and the playhead.</summary>
    public bool Snap { get; set; } = true;

    /// <summary>Gets or sets whether the timeline automatically fits the whole edit.</summary>
    public bool TimelineFit { get; set; } = true;

    /// <summary>Gets or sets whether the render dialog is open.</summary>
    public bool ShowExport { get; set; }

    /// <summary>Gets or sets whether the media browser is open.</summary>
    public bool ShowBrowser { get; set; }

    /// <summary>Gets or sets whether the project setup panel is open.</summary>
    public bool ShowSettings { get; set; }

    /// <summary>Gets or sets whether the shortcut reference is open.</summary>
    public bool ShowShortcuts { get; set; }

    /// <summary>Gets or sets the active left-rail destination.</summary>
    public string ActiveTab { get; set; } = "media";

    /// <summary>Gets or sets the active Looks sub-tab.</summary>
    public string LooksTab { get; set; } = "transitions";

    /// <summary>Gets or sets the active phone-shell page.</summary>
    public string MobilePage { get; set; } = "preview";

    /// <summary>Gets or sets the media browser mode: media or open.</summary>
    public string BrowserMode { get; set; } = "media";

    /// <summary>Gets or sets the folder currently shown by the media browser.</summary>
    public string BrowserPath { get; set; } = "";

    /// <summary>Gets or sets a counter that forces the browser to reload.</summary>
    public int BrowserRefresh { get; set; }

    /// <summary>Gets or sets the current toast message, or <see langword="null"/>.</summary>
    public string? Toast { get; set; }

    /// <summary>Gets or sets the tick at which the toast expires.</summary>
    public double ToastUntil { get; set; }

    /// <summary>Gets or sets whether a long-running operation is in progress.</summary>
    public bool Busy { get; set; }

    /// <summary>Gets or sets the label shown on the busy pill.</summary>
    public string BusyLabel { get; set; } = "";

    /// <summary>Gets or sets the busy progress in the range 0..1.</summary>
    public double BusyProgress { get; set; }

    /// <summary>Gets or sets whether the program monitor is expanded.</summary>
    public bool PreviewFullscreen { get; set; }

    /// <summary>Gets or sets the latest ffmpeg log tail shown by the render dialog.</summary>
    public string? Log { get; set; }

    // Capture settings shared by the Record tab.

    /// <summary>Gets or sets the capture source: screen, window or camera.</summary>
    public string CaptureSource { get; set; } = "screen";

    /// <summary>Gets or sets the selected camera device name.</summary>
    public string? CameraDevice { get; set; }

    /// <summary>Gets or sets the selected window title for window capture.</summary>
    public string? CaptureWindow { get; set; }

    /// <summary>Gets or sets the selected microphone device name.</summary>
    public string? MicDevice { get; set; }

    /// <summary>Gets or sets the last capture width in pixels.</summary>
    public int CaptureWidth { get; set; } = 1920;

    /// <summary>Gets or sets the last capture height in pixels.</summary>
    public int CaptureHeight { get; set; } = 1080;

    /// <summary>Gets or sets the last capture frame rate.</summary>
    public int CaptureFps { get; set; } = 30;

    /// <summary>Gets or sets whether video capture also records the microphone.</summary>
    public bool RecordWithMic { get; set; } = true;

    // HTML-style drag state, used to highlight and place dropped media.

    /// <summary>Gets or sets the asset id currently being dragged.</summary>
    public string? DraggingAssetId { get; set; }

    /// <summary>Gets or sets the text preset index currently being dragged.</summary>
    public int? DraggingTextPreset { get; set; }

    /// <summary>Gets or sets the track highlighted as the current drop target.</summary>
    public string? DropTrackId { get; set; }

    /// <summary>Gets or sets the preferred drop time in seconds.</summary>
    public double DropTime { get; set; }
}

/// <summary>
/// Owns the project document, the transient UI state, undo history and all
/// timeline commands. Mutations raise <see cref="Changed"/> on the caller's
/// thread; background work must marshal through <c>Host.Post</c> before touching
/// the store.
/// </summary>
public sealed class EditorStore
{
    /// <summary>Gets the JSON contract used for projects, undo snapshots and cloning.</summary>
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

    /// <summary>Creates a store around an existing document.</summary>
    /// <param name="document">The document to edit.</param>
    /// <param name="projectPath">The source project path, or <see langword="null"/> for a new document.</param>
    public EditorStore(ProjectDocument document, string? projectPath = null)
    {
        Document = document;
        ProjectPath = projectPath;
        Ui = new EditorUi();
    }

    /// <summary>Gets the current project document.</summary>
    public ProjectDocument Document { get; private set; }

    /// <summary>Gets the transient editor UI state.</summary>
    public EditorUi Ui { get; }

    /// <summary>Gets the project file path, or <see langword="null"/> when unsaved.</summary>
    public string? ProjectPath { get; private set; }

    /// <summary>Gets whether the document changed since the last save.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Raised after a mutation or a UI-state change.</summary>
    public event Action? Changed;

    /// <summary>Creates an empty project with the default overlay, video and audio tracks.</summary>
    /// <returns>A ready-to-edit store.</returns>
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

    /// <summary>Raises <see cref="Changed"/> so the host re-renders.</summary>
    public void Notify() => Changed?.Invoke();

    /// <summary>Raises <see cref="Changed"/> for a UI-only state change.</summary>
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

    /// <summary>Gets whether an undo step is available.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether a redo step is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Restores the previous document snapshot.</summary>
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

    /// <summary>Reapplies the most recently undone snapshot.</summary>
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

    /// <summary>Writes the project to disk and clears the dirty flag.</summary>
    /// <param name="path">Destination <c>.mdproj</c> path.</param>
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

    /// <summary>Changes the path associated with the current project without saving.</summary>
    /// <param name="path">The project path to remember.</param>
    public void SetProjectPath(string path)
    {
        ProjectPath = path;
        Notify();
    }

    /// <summary>Loads a project from disk.</summary>
    /// <param name="path">Source <c>.mdproj</c> path.</param>
    /// <returns>A store around the deserialized document.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="JsonException">The file does not contain a valid project.</exception>
    public static EditorStore Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ProjectDocument>(json, JsonOptions) ?? new ProjectDocument();
        return new EditorStore(document, path);
    }

    // --------------------------------------------------------------- playhead

    /// <summary>Moves the playhead, clamped to the project duration.</summary>
    /// <param name="seconds">Target time in seconds.</param>
    /// <param name="throttled">
    /// When <see langword="true"/>, notifications are limited to roughly 36 per
    /// second during playback or scrubbing.
    /// </param>
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

    /// <summary>Toggles transport playback.</summary>
    public void TogglePlay()
    {
        Ui.Playing = !Ui.Playing;
        Notify();
    }

    /// <summary>Sets the timeline zoom, clamped to a usable range.</summary>
    /// <param name="zoom">Pixels per second.</param>
    public void SetZoom(double zoom)
    {
        Ui.Zoom = Math.Clamp(zoom, 6, 640);
        Notify();
    }

    /// <summary>Selects a clip and its owning track.</summary>
    /// <param name="clipId">The clip id, or <see langword="null"/> to clear the selection.</param>
    public void Select(string? clipId)
    {
        Ui.SelectedClipId = clipId;
        Ui.SelectedTrackId = clipId is null ? Ui.SelectedTrackId : Document.FindClip(clipId)?.TrackId;
        Notify();
    }

    /// <summary>Gets the selected clip, or <see langword="null"/>.</summary>
    public Clip? SelectedClip => Document.FindClip(Ui.SelectedClipId);

    // --------------------------------------------------------------- document

    /// <summary>Finds a track of a kind, creating one when necessary.</summary>
    /// <param name="kind">The track kind to find or create.</param>
    /// <returns>The existing or newly inserted track.</returns>
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

    /// <summary>Adds a media asset to the bin, optionally placing it on the timeline.</summary>
    /// <param name="asset">The analysed asset to add.</param>
    /// <param name="placeOnTimeline">When <see langword="true"/>, also creates a clip at the playhead.</param>
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

    /// <summary>Places an asset at a time, optionally on a specific compatible track.</summary>
    /// <param name="asset">The asset to place.</param>
    /// <param name="at">Start time in seconds, or the playhead when omitted.</param>
    /// <param name="asOverlay">Place a video as a picture-in-picture overlay.</param>
    /// <param name="target">
    /// The track a drop landed on. Used when its kind matches the asset; otherwise
    /// the first track of the required kind is chosen.
    /// </param>
    public Clip AddAssetToTimeline(MediaAsset asset, double? at = null, bool asOverlay = false, Track? target = null)
    {
        var kind = asset.Kind switch
        {
            AssetKind.Audio => TrackKind.Audio,
            AssetKind.Image => TrackKind.Overlay,
            _ => asOverlay ? TrackKind.Overlay : TrackKind.Video
        };

        var track = target is not null && target.Kind == kind
            ? target
            : Document.Tracks.FirstOrDefault(t => t.Kind == kind) ?? EnsureTrack(kind);
        var duration = asset.Kind == AssetKind.Image ? 5 : Math.Max(0.2, asset.Duration);
        var start = at ?? Ui.Playhead;

        // Do not silently stack on top of an existing clip.
        while (track.Clips.Any(c => c.Start < start + duration - 0.05 && c.End > start + 0.05))
        {
            start = track.Clips.Where(c => c.End > start).Select(c => c.End).DefaultIfEmpty(start).Max() + 0.1;
        }

        var box = new LayerBox();

        // Put a video layer straight into a picture-in-picture corner so it can
        // sit over a screen recording without covering it.
        if (kind == TrackKind.Overlay && asset.Kind == AssetKind.Video)
        {
            const double width = 0.32;
            var canvasAspect = Document.Width / (double)Math.Max(1, Document.Height);
            var sourceAspect = asset.Width > 0 && asset.Height > 0
                ? asset.Width / (double)asset.Height
                : canvasAspect;
            var height = Math.Clamp(width * canvasAspect / Math.Max(0.1, sourceAspect), 0.08, 0.9);

            box = new LayerBox
            {
                X = Math.Max(0, 1 - width - 0.04),
                Y = Math.Max(0, 1 - height - 0.04),
                Width = width,
                Height = height
            };
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
            Box = box
        };

        Edit(_ => track.Clips.Add(clip), "Add clip");
        Ui.SelectedClipId = clip.Id;
        Ui.SelectedTrackId = track.Id;
        Notify();
        return clip;
    }

    /// <summary>Creates a title clip, moving it past an existing title when needed.</summary>
    /// <param name="text">The title text.</param>
    /// <param name="at">Start time in seconds, or the playhead when omitted.</param>
    /// <param name="target">The text track a drop landed on, when compatible.</param>
    /// <returns>The created clip.</returns>
    public Clip AddTextClip(string text, double? at = null, Track? target = null)
    {
        var track = target is { Kind: TrackKind.Text }
            ? target
            : Document.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Text) ?? EnsureTrack(TrackKind.Text);
        var start = at ?? Ui.Playhead;
        const double duration = 4;

        // Keep dropped or clicked titles from stacking on an existing one.
        while (track.Clips.Any(c => c.Start < start + duration - 0.05 && c.End > start + 0.05))
        {
            start = track.Clips.Where(c => c.End > start).Select(c => c.End).DefaultIfEmpty(start).Max() + 0.1;
        }

        var clip = new Clip
        {
            TrackId = track.Id,
            Kind = TrackKind.Text,
            Name = text.Length > 24 ? text[..24] : text,
            Text = text,
            Start = start,
            Duration = duration,
            Box = new LayerBox { X = 0.08, Y = 0.36, Width = 0.84, Height = 0.28 },
            TransitionId = "none"
        };

        Edit(_ => track.Clips.Add(clip), "Add text");
        Ui.SelectedClipId = clip.Id;
        Ui.SelectedTrackId = track.Id;
        Notify();
        return clip;
    }

    /// <summary>Deletes the selected clip, if any.</summary>
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

    /// <summary>Duplicates the selected clip directly after itself.</summary>
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

    /// <summary>Moves a clip in time and optionally to another compatible track.</summary>
    /// <param name="clip">The clip to move.</param>
    /// <param name="start">The new start time in seconds.</param>
    /// <param name="track">The destination track, when it has the same kind as the clip.</param>
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

    /// <summary>Trims a clip, keeping the source in-point consistent with the new start.</summary>
    /// <param name="clip">The clip to trim.</param>
    /// <param name="start">The new start time in seconds.</param>
    /// <param name="duration">The new duration in seconds.</param>
    public void TrimClip(Clip clip, double start, double duration)
    {
        var delta = start - clip.Start;
        clip.Start = Math.Max(0, start);
        clip.Duration = Math.Max(0.15, duration);
        clip.InPoint = Math.Max(0, clip.InPoint + delta * clip.Speed);
    }

    /// <summary>Changes a clip's incoming transition and optional duration.</summary>
    /// <param name="clipId">The target clip id.</param>
    /// <param name="transitionId">The transition registry id.</param>
    /// <param name="duration">The new duration in seconds, or <see langword="null"/> to keep it.</param>
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

    /// <summary>Applies a clip mutation as one undo step.</summary>
    /// <param name="clipId">The target clip id.</param>
    /// <param name="change">The mutation to apply.</param>
    /// <param name="label">The undo label.</param>
    public void UpdateClip(string clipId, Action<Clip> change, string label = "Update clip")
    {
        var clip = Document.FindClip(clipId);

        if (clip is null)
        {
            return;
        }

        Edit(_ => change(clip), label);
    }

    /// <summary>Applies a clip mutation immediately without adding an undo step.</summary>
    /// <param name="clipId">The target clip id.</param>
    /// <param name="change">The mutation to apply.</param>
    public void UpdateClipLive(string clipId, Action<Clip> change)
    {
        var clip = Document.FindClip(clipId);
        change(clip!);
        Dirty = true;
        Notify();
    }

    /// <summary>Shows a transient message.</summary>
    /// <param name="message">The message text.</param>
    /// <param name="seconds">How long the message remains visible.</param>
    public void Toast(string message, double seconds = 3.2)
    {
        Ui.Toast = message;
        Ui.ToastUntil = Environment.TickCount64 + (long)(seconds * 1000);
        Notify();
    }

    /// <summary>Shows or hides the busy pill.</summary>
    /// <param name="busy">Whether an operation is running.</param>
    /// <param name="label">The operation label.</param>
    /// <param name="progress">Progress in the range 0..1.</param>
    public void SetBusy(bool busy, string label = "", double progress = 0)
    {
        Ui.Busy = busy;
        Ui.BusyLabel = label;
        Ui.BusyProgress = progress;
        Notify();
    }

    /// <summary>Updates the busy progress and optional label.</summary>
    /// <param name="progress">Progress in the range 0..1.</param>
    /// <param name="label">A new label, or <see langword="null"/> to keep the current one.</param>
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

    /// <summary>Finds the media asset behind a clip.</summary>
    /// <param name="clip">The clip to inspect, or <see langword="null"/>.</param>
    /// <returns>The matching asset, or <see langword="null"/> for text clips and missing media.</returns>
    public MediaAsset? AssetOf(Clip? clip) => clip is null ? null : Document.FindAsset(clip.AssetId);

    /// <summary>Enumerates visible clips that cover a timeline time.</summary>
    /// <param name="time">The timeline time in seconds.</param>
    /// <returns>Track/clip pairs in document order.</returns>
    public IEnumerable<(Track Track, Clip Clip)> ClipsAt(double time) =>
        Document.Tracks
            .Where(t => !t.Hidden)
            .SelectMany(t => t.Clips.Where(c => c.Start <= time && c.End > time).Select(c => (t, c)));

    /// <summary>Snaps a time to the nearest clip edge or the playhead when within a pixel threshold.</summary>
    /// <param name="time">The proposed time in seconds.</param>
    /// <param name="ignoreClipId">A clip to exclude, usually the clip being dragged.</param>
    /// <returns>The snapped time, or the original time when nothing is close enough.</returns>
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
