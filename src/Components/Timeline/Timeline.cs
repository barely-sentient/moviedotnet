using Donkeh.UILab;
using MovieDotNet.Core;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/Timeline/Timeline.css")]

namespace MovieDotNet.Components;

/// <summary>
/// The zoomable timeline: ruler, track headers, filmstrip clips, transitions and
/// a scrubbable playhead. All pointer work is hit-tested against a stable capture
/// surface so dragging a clip never feeds back into its own coordinates.
/// </summary>
public sealed class Timeline : Component
{
    private const double DesktopHeaderWidth = 172;
    private const double MobileHeaderWidth = 134;
    private const double RulerHeight = 32;
    private const double TrackGap = 4;

    private sealed class DragState
    {
        public string Mode = "";
        public string? ClipId;
        public string? TrackId;
        public double GrabTime;
        public double OriginalStart;
        public double OriginalEnd;
        public double OriginalIn;
        public bool Moved;
    }

    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var document = store.Document;
        var ui = store.Ui;
        var drag = UseRef<DragState?>(null);

        var rows = new List<(Track? Track, double Top, double Height)>();
        var top = RulerHeight;
        var rowScale = Device.Mobile ? 0.7 : 1.0;

        foreach (var track in document.Tracks)
        {
            var rowHeight = Math.Max(Device.Mobile ? 30 : 38, track.Height * rowScale);
            rows.Add((track, top, rowHeight));
            top += rowHeight + TrackGap;
        }

        // Fit the whole edit into the visible lane until the user zooms manually.
        if (ui.TimelineFit)
        {
            var fitWidth = Device.Mobile ? 300 : 560;
            var fit = Math.Clamp(fitWidth / Math.Max(8, document.Duration), 8, 320);
            ui.Zoom = Math.Round(fit, 1);
        }

        var headerWidth = Device.Mobile ? MobileHeaderWidth : DesktopHeaderWidth;
        var contentHeight = top + 12;
        var duration = Math.Max(document.Duration, ui.Playhead + 4);
        var contentWidth = Math.Max(720, Math.Round(duration * ui.Zoom + 70));

        return Div(new() { ClassName = "timeline" },
            Toolbar(store),
            Div(new() { ClassName = "timeline-body" },
                Div(new() { ClassName = "track-heads", Attributes = new Dictionary<string, string>
                {
                    ["style"] = $"width:{headerWidth:0}px;min-width:{headerWidth:0}px;"
                } },
                    Div(new() { ClassName = "heads-spacer", Attributes = new Dictionary<string, string>
                    {
                        ["style"] = $"height:{RulerHeight}px;min-height:{RulerHeight}px;"
                    } },
                        Span(new() { ClassName = "heads-hint" }, Text("Tracks"))),
                    Each(rows, row => TrackHeader(store, row.Track, row.Height))),
                Div(new() { ClassName = "timeline-scroll scroll-x" },
                    Div(new()
                    {
                        ClassName = "timeline-content",
                        Attributes = new Dictionary<string, string>
                        {
                            ["style"] = $"width:{contentWidth}px;height:{contentHeight}px;"
                        }
                    },
                        Ruler(store, contentWidth),
                        Each(rows, row => Lane(store, row.Track!, row.Top, row.Height)),
                        Each(rows.SelectMany(row => row.Track!.Clips.Select(clip => (Track: row.Track!, Clip: clip, row.Top, row.Height))),
                            item => ClipNode(store, item.Track, item.Clip, item.Top, item.Height)),
                        Div(new()
                        {
                            ClassName = "playhead",
                            Attributes = new Dictionary<string, string>
                            {
                                ["style"] = $"left:{ui.Playhead * ui.Zoom:0.###}px;height:{contentHeight - 8}px;"
                            }
                        }, Div(new() { ClassName = "playhead-cap" })),
                        Show(ui.DropTrackId is not null,
                            Div(new()
                            {
                                ClassName = "drop-marker",
                                Key = "drop-marker",
                                Attributes = new Dictionary<string, string>
                                {
                                    ["style"] = $"left:{ui.DropTime * ui.Zoom:0.###}px;height:{contentHeight - 8}px;"
                                }
                            })),
                        Div(new()
                        {
                            ClassName = "timeline-capture",
                            Key = "timeline-capture",
                            Attributes = new Dictionary<string, string>
                            {
                                ["style"] = $"width:{contentWidth}px;height:{contentHeight}px;"
                            },
                            OnPointerDown = (x, y) => Begin(store, drag, rows, x, y),
                            OnPointerMove = (x, y) => Move(store, drag, rows, x, y),
                            OnPointerUp = (x, y) => End(store, drag),
                            OnDragEnter = evt => UpdateDrop(store, rows, evt),
                            OnDragOver = evt => UpdateDrop(store, rows, evt),
                            OnDragLeave = evt => ClearDrop(store),
                            OnDrop = evt => DropOnTimeline(store, rows, evt)
                        })))));
    }

    // ---------------------------------------------------------------- toolbar

    private Node Toolbar(EditorStore store)
    {
        var document = store.Document;
        var ui = store.Ui;
        var (tracksOpen, setTracksOpen) = State(false);

        Node Tool(string icon, string tooltip, Action action, bool disabled = false, bool active = false) =>
            C<IconButton, IconButtonProps>(new IconButtonProps
            {
                Icon = icon,
                Tooltip = tooltip,
                Size = "small",
                Disabled = disabled,
                Active = active,
                OnClick = action
            });

        void AddTrack(TrackKind kind, string name, double height, bool atTop)
        {
            store.Edit(_ =>
            {
                var track = new Track { Kind = kind, Name = name, Height = height };

                if (atTop)
                {
                    document.Tracks.Insert(0, track);
                }
                else
                {
                    document.Tracks.Add(track);
                }
            }, "Add track");

            setTracksOpen(false);
        }

        Node TrackOption(string icon, string label, Action action) =>
            Div(new()
            {
                ClassName = "track-add-item",
                OnClick = action
            },
                H("i", new Props { ClassName = icon }),
                Span(new(), Text(label)));

        return Div(new() { ClassName = "timeline-toolbar" },
            Tool("fa-solid fa-scissors", "Split at playhead", () => store.SplitAtPlayhead()),
            Tool("fa-solid fa-copy", "Duplicate clip", store.DuplicateSelected, store.SelectedClip is null),
            Tool("fa-solid fa-trash", "Delete clip", store.DeleteSelected, store.SelectedClip is null),
            Div(new() { ClassName = "divider" }),
            Tool("fa-solid fa-magnet", "Snap to clips and playhead", () =>
            {
                ui.Snap = !ui.Snap;
                store.Notify();
            }, active: ui.Snap),
            Div(new() { ClassName = "spacer" }),
            Div(new() { ClassName = "track-add" },
                Button(new()
                {
                    ClassName = "btn is-small",
                    OnClick = () => setTracksOpen(!tracksOpen)
                },
                    H("i", new Props { ClassName = "fa-solid fa-plus" }),
                    Text("Track")),
                Show(tracksOpen,
                    Div(new() { ClassName = "track-add-menu" },
                        TrackOption("fa-solid fa-film", "Video track",
                            () => AddTrack(TrackKind.Video,
                                $"Video {document.Tracks.Count(t => t.Kind == TrackKind.Video) + 1}", 62, true)),
                        TrackOption("fa-solid fa-layer-group", "Overlay track",
                            () => AddTrack(TrackKind.Overlay,
                                $"Overlay {document.Tracks.Count(t => t.Kind == TrackKind.Overlay) + 1}", 52, true)),
                        TrackOption("fa-solid fa-font", "Text track",
                            () => AddTrack(TrackKind.Text,
                                $"Text {document.Tracks.Count(t => t.Kind == TrackKind.Text) + 1}", 46, true)),
                        TrackOption("fa-solid fa-music", "Audio track",
                            () => AddTrack(TrackKind.Audio,
                                $"Audio {document.Tracks.Count(t => t.Kind == TrackKind.Audio) + 1}", 46, false))))),
            Div(new() { ClassName = "zoom" },
                H("i", new Props { ClassName = "fa-solid fa-magnifying-glass-minus" }),
                Input(new()
                {
                    Type = "range",
                    Value = ui.Zoom.ToString("0"),
                    Attributes = new Dictionary<string, string>
                    {
                        ["min"] = "8", ["max"] = "320", ["step"] = "1"
                    },
                    OnInput = value =>
                    {
                        if (double.TryParse(value, out var zoom))
                        {
                            store.Ui.TimelineFit = false;
                            store.SetZoom(zoom);
                        }
                    }
                }),
                H("i", new Props { ClassName = "fa-solid fa-magnifying-glass-plus" }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-arrows-left-right-to-line",
                    Tooltip = "Fit the whole edit",
                    Size = "small",
                    OnClick = () =>
                    {
                        store.Ui.TimelineFit = true;
                        store.Notify();
                    }
                }),
                Span(new() { ClassName = "zoom-value" }, Text($"{ui.Zoom:0}px/s"))));
    }

    // ------------------------------------------------------------------ ruler

    private static Node Ruler(EditorStore store, double contentWidth)
    {
        var ui = store.Ui;
        var step = TickStep(ui.Zoom);
        var duration = contentWidth / Math.Max(1, ui.Zoom);
        var ticks = new List<Node>();
        var minor = step / 5;

        for (var time = 0d; time <= duration && ticks.Count < 600; time += minor)
        {
            var isMajor = Math.Abs(time / step - Math.Round(time / step)) < 0.0001;
            var x = time * ui.Zoom;
            ticks.Add(Div(new()
            {
                ClassName = isMajor ? "tick is-major" : "tick",
                Attributes = new Dictionary<string, string>
                {
                    ["style"] = $"left:{x:0.###}px;"
                }
            },
                Show(isMajor, Span(new() { ClassName = "tick-label" }, Text(RulerLabel(time, step))))));
        }

        return Div(new() { ClassName = "ruler" },
            Div(new() { ClassName = "ruler-track", Attributes = new Dictionary<string, string>
            {
                ["style"] = $"width:{contentWidth:0}px;"
            } }, ticks.ToArray()));
    }

    private static string RulerLabel(double time, double step)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, time));

        if (step < 1)
        {
            var frames = (int)Math.Round((time - Math.Floor(time)) * 100);
            return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{frames:00}";
        }

        if (step >= 60)
        {
            return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        }

        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private static double TickStep(double zoom)
    {
        double[] steps = [0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];

        foreach (var step in steps)
        {
            if (step * zoom >= 84)
            {
                return step;
            }
        }

        return 600;
    }

    // ------------------------------------------------------------------ tracks

    private static Node TrackHeader(EditorStore store, Track track, double height)
    {
        var color = ColorFor(track.Kind);

        return Div(new()
        {
            ClassName = "track-head",
            Key = "head-" + track.Id,
            Attributes = new Dictionary<string, string>
            {
                ["style"] = $"height:{height}px;min-height:{height}px;"
            }
        },
            Div(new() { ClassName = "track-accent", Attributes = new Dictionary<string, string>
            {
                ["style"] = $"background-color:{color};"
            } }),
            Div(new() { ClassName = "track-head-info" },
                Span(new() { ClassName = "track-name" }, Text(track.Name))),
            Div(new() { ClassName = "track-tools" },
                ToolButton(track.Hidden ? "fa-solid fa-eye-slash" : "fa-solid fa-eye", "Show/hide", () =>
                {
                    store.Edit(_ => track.Hidden = !track.Hidden, "Toggle visibility");
                }, track.Hidden),
                ToolButton(track.Muted ? "fa-solid fa-volume-xmark" : "fa-solid fa-volume-high", "Mute", () =>
                {
                    store.Edit(_ => track.Muted = !track.Muted, "Toggle mute");
                }, track.Muted),
                ToolButton(track.Locked ? "fa-solid fa-lock" : "fa-solid fa-lock-open", "Lock", () =>
                {
                    store.Edit(_ => track.Locked = !track.Locked, "Toggle lock");
                }, track.Locked)));
    }

    private static Node ToolButton(string icon, string tooltip, Action action, bool active) =>
        Div(new()
        {
            ClassName = active ? "track-tool is-on" : "track-tool",
            Title = tooltip,
            OnClick = action
        },
            H("i", new Props { ClassName = icon }));

    private static Node Lane(EditorStore store, Track track, double top, double height)
    {
        var className = track.Locked ? "lane is-locked" : "lane";

        if (store.Ui.DropTrackId == track.Id)
        {
            className += " is-drop";
        }

        return Div(new()
        {
            ClassName = className,
            Key = "lane-" + track.Id,
            Attributes = new Dictionary<string, string>
            {
                ["style"] = $"top:{top}px;height:{height}px;" +
                    (track.Hidden ? "opacity:0.45;" : "")
            }
        });
    }

    private static string ColorFor(TrackKind kind) => kind switch
    {
        TrackKind.Video => "#93a6ff",
        TrackKind.Overlay => "#c3a6ff",
        TrackKind.Text => "#8edcc0",
        _ => "#ecc39a"
    };

    // ------------------------------------------------------------ drag & drop

    /// <summary>Finds the track row under a content-space Y coordinate.</summary>
    private static Track? TrackAt(List<(Track? Track, double Top, double Height)> rows, double y)
    {
        foreach (var row in rows)
        {
            if (row.Track is not null && y >= row.Top && y < row.Top + row.Height)
            {
                return row.Track;
            }
        }

        return rows
            .Where(row => row.Track is not null)
            .OrderBy(row => Math.Abs(row.Top + row.Height * 0.5 - y))
            .Select(row => row.Track)
            .FirstOrDefault();
    }

    private static void UpdateDrop(
        EditorStore store,
        List<(Track? Track, double Top, double Height)> rows,
        DragEvent drag)
    {
        var track = TrackAt(rows, drag.LocalY);

        if (track is null || track.Locked)
        {
            ClearDrop(store);
            return;
        }

        var time = Math.Round(Math.Max(0, drag.LocalX / Math.Max(1, store.Ui.Zoom)) * 10) / 10;

        if (store.Ui.DropTrackId != track.Id || Math.Abs(store.Ui.DropTime - time) > 0.04)
        {
            store.Ui.DropTrackId = track.Id;
            store.Ui.DropTime = time;
            store.Notify();
        }

        drag.PreventDefault();
    }

    private static void ClearDrop(EditorStore store)
    {
        if (store.Ui.DropTrackId is null && store.Ui.DropTime == 0)
        {
            return;
        }

        store.Ui.DropTrackId = null;
        store.Ui.DropTime = 0;
        store.Notify();
    }

    private static void DropOnTimeline(
        EditorStore store,
        List<(Track? Track, double Top, double Height)> rows,
        DragEvent drag)
    {
        var track = TrackAt(rows, drag.LocalY);
        var time = Math.Round(Math.Max(0, drag.LocalX / Math.Max(1, store.Ui.Zoom)) * 10) / 10;

        if (track is null)
        {
            store.Toast("Drop it on a track.");
            ClearDrop(store);
            return;
        }

        if (track.Locked)
        {
            store.Toast("That track is locked.");
            ClearDrop(store);
            return;
        }

        var assetId = drag.Data.GetData(DragFormats.Asset);
        var presetIndex = drag.Data.GetData(DragFormats.TextPreset);

        if (assetId is not null)
        {
            var asset = store.Document.FindAsset(assetId);

            if (asset is null)
            {
                ClearDrop(store);
                return;
            }

            var compatible = asset.Kind switch
            {
                AssetKind.Audio => track.Kind == TrackKind.Audio,
                AssetKind.Image => track.Kind is TrackKind.Video or TrackKind.Overlay,
                _ => track.Kind is TrackKind.Video or TrackKind.Overlay
            };

            if (!compatible)
            {
                store.Toast(asset.Kind == AssetKind.Audio
                    ? "Drop audio on an audio track."
                    : "Drop video and images on a video or overlay track.");
            }
            else
            {
                var clip = store.AddAssetToTimeline(
                    asset,
                    at: time,
                    asOverlay: track.Kind == TrackKind.Overlay,
                    target: track);
                store.Toast($"{asset.Name} added at {Format.Timecode(clip.Start, store.Document.Fps)}");
            }
        }
        else if (presetIndex is not null
            && int.TryParse(presetIndex, out var index)
            && index >= 0
            && index < TextPreset.All.Count)
        {
            var clip = TextPreset.All[index].AddTo(
                store,
                time,
                track.Kind == TrackKind.Text ? track : null);
            store.Toast($"Title added at {Format.Timecode(clip.Start, store.Document.Fps)}");
        }
        else
        {
            store.Toast("That item cannot be dropped on the timeline.");
        }

        ClearDrop(store);
    }

    // ------------------------------------------------------------------- clips

    private static Node ClipNode(EditorStore store, Track track, Clip clip, double top, double rowHeight)
    {
        var ui = store.Ui;
        var asset = store.Document.FindAsset(clip.AssetId);
        var selected = ui.SelectedClipId == clip.Id;
        var width = Math.Max(14, clip.Duration * ui.Zoom);
        var height = Math.Max(26, rowHeight) - 6;
        var className = "tl-clip kind-" + clip.Kind.ToString().ToLowerInvariant();

        if (selected)
        {
            className += " is-selected";
        }

        if (track.Locked)
        {
            className += " is-locked";
        }

        if (clip.Kind == TrackKind.Audio)
        {
            className += " is-audio";
        }

        var image = clip.Kind == TrackKind.Audio ? asset?.Waveform : asset?.Filmstrip;

        return Div(new()
        {
            ClassName = className,
            Key = clip.Id,
            Attributes = new Dictionary<string, string>
            {
                ["style"] =
                    $"left:{clip.Start * ui.Zoom:0.###}px;top:{top + 4}px;" +
                    $"width:{width:0.###}px;height:{height:0.###}px;"
            }
        },
            Show(image is not null, Img(new()
            {
                ClassName = "tl-clip-media",
                Src = image,
                Alt = clip.Name
            })),
            Show(image is null && clip.Kind == TrackKind.Text, Div(new() { ClassName = "tl-clip-glyph" },
                H("i", new Props { ClassName = "fa-solid fa-font" }))),
            Div(new() { ClassName = "tl-clip-label" },
                H("i", new Props
                {
                    ClassName = clip.Kind switch
                    {
                        TrackKind.Text => "fa-solid fa-font",
                        TrackKind.Audio => "fa-solid fa-wave-square",
                        TrackKind.Overlay => "fa-solid fa-layer-group",
                        _ => "fa-solid fa-film"
                    }
                }),
                Span(new() { ClassName = "tl-clip-name" }, Text(clip.Name)),
                Show(width > 120, Span(new() { ClassName = "tl-clip-time" }, Text(Format.Short(clip.Duration))))),
            Show(TransitionRegistry.IsTransition(clip.TransitionId),
                Div(new() { ClassName = "tl-transition", Title = TransitionRegistry.Get(clip.TransitionId).Name },
                    H("i", new Props { ClassName = "fa-solid fa-wand-magic-sparkles" }),
                    Span(new(), Text(TransitionRegistry.Get(clip.TransitionId).Name)))),
            Show(selected, Div(new() { ClassName = "tl-trim is-start" })),
            Show(selected, Div(new() { ClassName = "tl-trim is-end" })));
    }

    // -------------------------------------------------------------------- drag

    private static void Begin(EditorStore store, Ref<DragState?> dragRef, List<(Track? Track, double Top, double Height)> rows, float x, float y)
    {
        var ui = store.Ui;
        var time = Math.Max(0, x / Math.Max(1, ui.Zoom));

        if (y < RulerHeight)
        {
            dragRef.Value = new DragState { Mode = "scrub", GrabTime = time };
            store.SetPlayhead(store.SnapTime(time));
            return;
        }

        var row = rows.FirstOrDefault(r => y >= r.Top && y <= r.Top + r.Height);

        if (row.Track is null)
        {
            dragRef.Value = new DragState { Mode = "scrub", GrabTime = time };
            store.SetPlayhead(store.SnapTime(time));
            return;
        }

        if (row.Track.Locked)
        {
            dragRef.Value = new DragState { Mode = "scrub", GrabTime = time };
            store.SetPlayhead(store.SnapTime(time));
            return;
        }

        var clip = row.Track.Clips.FirstOrDefault(c => time >= c.Start && time <= c.End);

        if (clip is null)
        {
            dragRef.Value = new DragState { Mode = "scrub", GrabTime = time };
            store.SetPlayhead(store.SnapTime(time));
            return;
        }

        store.Select(clip.Id);
        store.BeginEdit();

        var leftEdge = Math.Abs(x - clip.Start * ui.Zoom);
        var rightEdge = Math.Abs(x - clip.End * ui.Zoom);
        var handle = Math.Min(9, Math.Max(3, ui.Zoom * 0.08));
        var mode = leftEdge <= handle && clip.Start > 0.001
            ? "trim-start"
            : rightEdge <= handle
                ? "trim-end"
                : "move";

        dragRef.Value = new DragState
        {
            Mode = mode,
            ClipId = clip.Id,
            TrackId = row.Track.Id,
            GrabTime = time,
            OriginalStart = clip.Start,
            OriginalEnd = clip.End,
            OriginalIn = clip.InPoint
        };
    }

    private static void Move(EditorStore store, Ref<DragState?> dragRef, List<(Track? Track, double Top, double Height)> rows, float x, float y)
    {
        var drag = dragRef.Value;

        if (drag is null)
        {
            return;
        }

        var ui = store.Ui;
        var time = Math.Max(0, x / Math.Max(1, ui.Zoom));

        if (drag.Mode == "scrub")
        {
            store.SetPlayhead(store.SnapTime(time));
            return;
        }

        var clip = store.Document.FindClip(drag.ClipId);

        if (clip is null)
        {
            return;
        }

        var delta = time - drag.GrabTime;

        if (!drag.Moved && Math.Abs(delta) < 2 / Math.Max(1, ui.Zoom))
        {
            return;
        }

        drag.Moved = true;

        switch (drag.Mode)
        {
            case "move":
            {
                var start = store.SnapTime(drag.OriginalStart + delta, clip.Id);
                clip.Start = Math.Max(0, start);

                var row = rows.FirstOrDefault(r => y >= r.Top && y <= r.Top + r.Height);
                var target = row.Track;
                var visualClip = clip.Kind is TrackKind.Video or TrackKind.Overlay;
                var visualTrack = target is not null
                    && target.Kind is TrackKind.Video or TrackKind.Overlay;

                if (target is not null
                    && target.Id != clip.TrackId
                    && !target.Locked
                    && (target.Kind == clip.Kind || (visualClip && visualTrack)))
                {
                    store.Document.TrackOf(clip)?.Clips.Remove(clip);
                    clip.TrackId = target.Id;
                    clip.Kind = target.Kind;
                    target.Clips.Add(clip);
                }

                break;
            }
            case "trim-start":
            {
                var start = Math.Max(0, Math.Min(drag.OriginalEnd - 0.15, store.SnapTime(drag.OriginalStart + delta, clip.Id)));
                var shift = start - drag.OriginalStart;
                clip.Start = start;
                clip.Duration = drag.OriginalEnd - start;
                clip.InPoint = Math.Max(0, drag.OriginalIn + shift * clip.Speed);
                break;
            }
            case "trim-end":
            {
                var end = Math.Max(clip.Start + 0.15, store.SnapTime(drag.OriginalEnd + delta, clip.Id));
                clip.Duration = end - clip.Start;
                break;
            }
        }

        store.RaiseChanged();
    }

    private static void End(EditorStore store, Ref<DragState?> dragRef)
    {
        var drag = dragRef.Value;

        if (drag is null)
        {
            return;
        }

        if (drag.Moved && drag.Mode != "scrub")
        {
            store.CommitEdit("Edit clip");
        }
        else
        {
            store.CancelEdit();
        }

        dragRef.Value = null;
        store.RaiseChanged();
    }
}
