using Donkeh.UILab;
using Donkeh.Windowing;
using MovieDotNet.Core;
using MovieDotNet.Core.Media;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/PreviewMonitor/PreviewMonitor.css")]

namespace MovieDotNet.Components;

/// <summary>
/// The program monitor. Layers are painted with real video/audio elements and
/// CSS transforms; the transport clock and selection box live on a capture
/// surface so dragging never competes with media playback.
/// </summary>
public sealed class PreviewMonitor : Component
{
    private sealed class BoxDrag
    {
        public string? ClipId;
        public string Mode = "";
        public double StartX;
        public double StartY;
        public LayerBox Origin = new();
        public bool Moved;
    }

    private readonly Dictionary<string, VideoPlayer> _players = new();
    private readonly Dictionary<string, AudioPlayer> _audioPlayers = new();
    private readonly Dictionary<string, double> _lastSeek = new();
    private readonly HashSet<string> _liveVideos = [];
    private readonly HashSet<string> _liveAudio = [];

    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var document = store.Document;
        var playhead = store.Ui.Playhead;
        var drag = UseRef<BoxDrag?>(null);

        Effect(() =>
        {
            SyncPlayers(store);
        }, [store.Ui.Playing]);

        Effect(() =>
        {
            if (!store.Ui.Playing)
            {
                SeekPlayers(store);
            }
        }, [Math.Round(playhead * 4)]);

        Effect(() => () =>
        {
            foreach (var player in _players.Values)
            {
                try
                {
                    player.Pause();
                }
                catch
                {
                    // Player already disposed by the host.
                }
            }

            foreach (var player in _audioPlayers.Values)
            {
                try
                {
                    player.Pause();
                }
                catch
                {
                    // Player already disposed by the host.
                }
            }

            _players.Clear();
            _audioPlayers.Clear();
        }, []);

        var aspect = document.Width / (double)Math.Max(1, document.Height);
        var mobile = Device.Mobile;
        var canvasClass = store.Ui.PreviewFullscreen ? "pv-canvas is-max" : "pv-canvas";

        // Phones size the canvas in viewport units so it always fits the screen;
        // the font-size matches the canvas height so layer text keeps its scale.
        // Desktop keeps pixel dimensions because the transform box maps pointer
        // coordinates through them.
        string canvasStyle;
        double canvasWidth;
        double canvasHeight;

        if (mobile)
        {
            if (aspect >= 1)
            {
                canvasHeight = 24;
                canvasWidth = canvasHeight * aspect;
                canvasStyle = $"width:{canvasWidth:0.###}vh;height:{canvasHeight:0.###}vh;font-size:{canvasHeight:0.###}vh;";
            }
            else
            {
                canvasWidth = 86;
                canvasHeight = canvasWidth / aspect;
                canvasStyle = $"width:{canvasWidth:0.###}vw;height:{canvasHeight:0.###}vw;font-size:{canvasHeight:0.###}vw;";
            }
        }
        else
        {
            canvasHeight = store.Ui.PreviewFullscreen ? 580d : 420d;
            canvasWidth = Math.Round(canvasHeight * aspect);
            canvasStyle = $"width:{canvasWidth:0}px;height:{canvasHeight:0}px;font-size:{canvasHeight:0}px;";
        }

        canvasStyle += $"background-color:{document.Background};";

        return Div(new Props() { ClassName = "preview" },
            Div(new Props() { ClassName = "preview-head" },
                Div(new Props() { ClassName = "preview-title" },
                    H("i", new Props() { ClassName = "fa-solid fa-tv" }),
                    Span(new Props(), Text("Program"))),
                Span(new Props() { ClassName = "chip" }, Text($"{document.Width}×{document.Height}")),
                Span(new Props() { ClassName = "chip" }, Text($"{document.Fps:0.##} fps")),
                Span(new Props() { ClassName = "chip is-accent" },
                    Span(new Props() { ClassName = "dot" }),
                    Text(document.Tracks.Any(t => t.Clips.Count > 0) ? "Composition live" : "Empty timeline")),
                Div(new Props() { ClassName = "spacer" }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-camera",
                    Tooltip = "Save current frame (ffmpeg)",
                    Disabled = !Core.Media.Ffmpeg.Available,
                    OnClick = () =>
                    {
                        var path = Path.Combine(ProjectFiles.RendersDirectory,
                            $"{ProjectFiles.Sanitize(document.Name)}-frame-{(int)(playhead * 1000)}.png");
                        EditorCommands.SnapshotAsync(store, SettingsFor(store), path);
                    }
                }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = store.Ui.PreviewFullscreen ? "fa-solid fa-compress" : "fa-solid fa-expand",
                    Tooltip = "Fullscreen preview",
                    OnClick = () =>
                    {
                        store.Ui.PreviewFullscreen = !store.Ui.PreviewFullscreen;
                        store.Notify();
                    }
                })),

            Div(new Props() { ClassName = "preview-stage" },
                Div(new Props() { ClassName = canvasClass, Attributes = new Dictionary<string, string> { ["style"] = canvasStyle } },
                    Each(Layers(store, playhead), layer => layer),
                    SelectionOverlay(store, playhead),
                    mobile
                        ? null
                        : Div(new Props()
                        {
                            ClassName = "pv-capture",
                            OnPointerDown = (x, y) => BeginBoxDrag(store, drag, x, y, canvasWidth, canvasHeight),
                            OnPointerMove = (x, y) => UpdateBoxDrag(store, drag, x, y, canvasWidth, canvasHeight),
                            OnPointerUp = (x, y) => EndBoxDrag(store, drag)
                        }))),

            Div(new Props() { ClassName = "transport" },
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-backward-step",
                    Tooltip = "Go to start",
                    OnClick = () => store.SetPlayhead(0)
                }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-backward",
                    Tooltip = "Back one second",
                    OnClick = () => store.SetPlayhead(playhead - 1)
                }),
                Div(new Props()
                {
                    ClassName = "play-button",
                    Title = store.Ui.Playing ? "Pause (space)" : "Play (space)",
                    OnClick = () =>
                    {
                        if (!store.Ui.Playing && playhead >= document.Duration - 0.05)
                        {
                            store.SetPlayhead(0);
                        }

                        store.TogglePlay();
                    }
                },
                    H("i", new Props() { ClassName = store.Ui.Playing ? "fa-solid fa-pause" : "fa-solid fa-play" })),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-forward",
                    Tooltip = "Forward one second",
                    OnClick = () => store.SetPlayhead(playhead + 1)
                }),
                Div(new Props() { ClassName = "timecode" },
                    Span(new Props() { ClassName = "timecode-now" }, Text(Format.Timecode(playhead, document.Fps))),
                    Span(new Props() { ClassName = "timecode-sep" }, Text("/")),
                    Span(new Props() { ClassName = "timecode-total" }, Text(Format.Timecode(document.Duration, document.Fps)))),
                Div(new Props() { ClassName = "spacer" }),
                Button(new Props()
                {
                    ClassName = "btn is-small",
                    OnClick = () => store.AddTextClip("Your text")
                },
                    H("i", new Props() { ClassName = "fa-solid fa-font" }),
                    Text("Text")),
                Button(new Props()
                {
                    ClassName = "btn is-small",
                    OnClick = () => store.SplitAtPlayhead()
                },
                    H("i", new Props() { ClassName = "fa-solid fa-scissors" }),
                    Text("Split")),
                Div(new Props() { ClassName = "preview-zoom" },
                    H("i", new Props() { ClassName = "fa-solid fa-clapperboard" }),
                    Span(new Props() { ClassName = "value-badge" }, Text(Format.Short(document.Duration))))));
    }

    private static RenderSettings SettingsFor(EditorStore store) => new()
    {
        Width = store.Document.Width,
        Height = store.Document.Height,
        Fps = store.Document.Fps,
        Background = store.Document.Background
    };

    // ------------------------------------------------------------------ layers

    private Node[] Layers(EditorStore store, double playhead)
    {
        var document = store.Document;
        var layers = new List<Node>();
        var baseTrack = document.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);

        _liveVideos.Clear();
        _liveAudio.Clear();

        foreach (var track in Enumerable.Reverse(document.Tracks))
        {
            if (track.Hidden || track.Kind == TrackKind.Audio)
            {
                continue;
            }

            foreach (var clip in track.Clips.OrderBy(c => c.Start))
            {
                if (!IsVisible(clip, playhead))
                {
                    continue;
                }

                var style = LayerStyle(store, track, clip, playhead, baseTrack == track);

                if (clip.Kind == TrackKind.Text)
                {
                    layers.Add(Div(new Props()
                    {
                        ClassName = "pv-text",
                        Key = "text-" + clip.Id,
                        Attributes = new Dictionary<string, string>
                        {
                            ["style"] = style + $"color:{clip.Color};font-weight:{clip.FontWeight};" +
                                $"letter-spacing:{clip.LetterSpacing / 19.2:0.###}em;text-align:{clip.Align};"
                        }
                    }, Text(clip.Text)));
                    continue;
                }

                var asset = store.Document.FindAsset(clip.AssetId);

                if (asset is null)
                {
                    continue;
                }

                if (asset.Kind == AssetKind.Video)
                {
                    _liveVideos.Add(clip.Id);
                    var ready = _players.ContainsKey(clip.Id);
                    var fit = baseTrack == track ? "cover" : "contain";

                    // The poster stands in until the decoder publishes its first frame.
                    if (asset.Poster is not null)
                    {
                        layers.Add(Img(new Props
                        {
                            Key = "poster-" + clip.Id,
                            ClassName = "pv-media pv-poster",
                            Src = asset.Poster,
                            Alt = clip.Name,
                            Attributes = new Dictionary<string, string>
                            {
                                ["style"] = style + $"object-fit:{fit};"
                            }
                        }));
                    }

                    layers.Add(H("video", new VideoProps
                    {
                        Key = "video-" + clip.Id,
                        ClassName = "pv-media",
                        Src = asset.Path,
                        Muted = baseTrack != track,
                        Volume = (float)Math.Clamp(clip.Volume, 0, 1),
                        Loop = false,
                        Attributes = new Dictionary<string, string>
                        {
                            ["style"] = style + $"object-fit:{fit};opacity:{(ready ? 1 : 0)};"
                        },
                        OnReady = player =>
                        {
                            _players[clip.Id] = player;
                            ApplyPlayer(store, clip, player);
                            store.RaiseChanged();
                        },
                        OnTimeUpdate = seconds =>
                        {
                            if (baseTrack == track && store.Ui.Playing)
                            {
                                var expected = playhead - clip.Start + clip.InPoint;

                                if (Math.Abs(seconds - expected) > 0.6
                                    && _players.TryGetValue(clip.Id, out var current))
                                {
                                    current.Seek((float)Math.Max(0, expected));
                                }
                            }
                        }
                    }));

                    if (!ready)
                    {
                        layers.Add(Div(new()
                        {
                            ClassName = "pv-decoding",
                            Key = "loading-" + clip.Id,
                            Attributes = new Dictionary<string, string> { ["style"] = style }
                        },
                            H("i", new Props { ClassName = "fa-solid fa-circle-notch spin" }),
                            Span(new(), Text("Decoding"))));
                    }
                }
                else if (asset.Kind == AssetKind.Image)
                {
                    layers.Add(Img(new Props()
                    {
                        Key = "image-" + clip.Id,
                        ClassName = "pv-media",
                        Src = asset.Path,
                        Alt = clip.Name,
                        Attributes = new Dictionary<string, string>
                        {
                            ["style"] = style + $"object-fit:{(baseTrack == track ? "cover" : "contain")};"
                        }
                    }));
                }
            }
        }

        // Audio-only tracks play through hidden audio elements.
        foreach (var track in document.Tracks.Where(t => t.Kind == TrackKind.Audio && !t.Muted))
        {
            foreach (var clip in track.Clips.OrderBy(c => c.Start))
            {
                if (!IsVisible(clip, playhead))
                {
                    continue;
                }

                var asset = document.FindAsset(clip.AssetId);

                if (asset is null || !asset.HasAudio)
                {
                    continue;
                }

                _liveAudio.Add(clip.Id);
                layers.Add(H("audio", new AudioProps
                {
                    Key = "audio-" + clip.Id,
                    Src = asset.Path,
                    Volume = (float)Math.Clamp(clip.Volume, 0, 1),
                    OnReady = player =>
                    {
                        _audioPlayers[clip.Id] = player;
                        ApplyAudio(store, clip, player);
                        store.RaiseChanged();
                    }
                }));
            }
        }

        // Drop players whose element left the tree: the host disposes them, and a
        // call into a released native player is an access violation.
        foreach (var id in _players.Keys.Where(id => !_liveVideos.Contains(id)).ToList())
        {
            _players.Remove(id);
            _lastSeek.Remove(id);
        }

        foreach (var id in _audioPlayers.Keys.Where(id => !_liveAudio.Contains(id)).ToList())
        {
            _audioPlayers.Remove(id);
        }

        if (layers.Count == 0)
        {
            layers.Add(Div(new Props() { ClassName = "pv-empty" },
                H("i", new Props() { ClassName = "fa-solid fa-clapperboard" }),
                Span(new Props() { ClassName = "pv-empty-title" }, Text("Nothing at the playhead")),
                Span(new Props() { ClassName = "pv-empty-copy" },
                    Text("Drop a clip on the timeline or add a title to start."))));
        }

        return [.. layers];
    }

    private static bool IsVisible(Clip clip, double playhead) =>
        playhead >= clip.Start - 0.001 && playhead < clip.End;

    private string LayerStyle(EditorStore store, Track track, Clip clip, double playhead, bool isBase)
    {
        var motion = Motion.For(clip, playhead - clip.Start);
        var opacity = motion.Opacity;
        var translateX = motion.TranslateX;
        var translateY = motion.TranslateY;
        var scale = motion.Scale;
        var blur = motion.Blur;
        var clipPath = "";

        // Transition preview: the incoming clip reveals itself over the tail of
        // its neighbour. Values mirror the xfade names used by the exporter.
        var previous = track.Clips
            .Where(c => c.Id != clip.Id && c.End > clip.Start && c.Start < clip.Start)
            .OrderByDescending(c => c.End)
            .FirstOrDefault();

        if (previous is not null && TransitionRegistry.IsTransition(clip.TransitionId))
        {
            var overlap = Math.Max(0.001, previous.End - clip.Start);
            var progress = Math.Clamp((playhead - clip.Start) / overlap, 0, 1);

            if (progress < 1)
            {
                switch (TransitionRegistry.Get(clip.TransitionId).Motion)
                {
                    case "fade":
                        opacity *= progress;
                        break;
                    case "fadeblack":
                    case "fadewhite":
                        opacity *= progress * progress;
                        break;
                    case "wipeleft":
                        clipPath = $"inset(0 {(1 - progress) * 100:0.##}% 0 0)";
                        break;
                    case "wiperight":
                        clipPath = $"inset(0 0 0 {(1 - progress) * 100:0.##}%)";
                        break;
                    case "wipeup":
                        clipPath = $"inset(0 0 {(1 - progress) * 100:0.##}% 0)";
                        break;
                    case "wipedown":
                        clipPath = $"inset({(1 - progress) * 100:0.##}% 0 0 0)";
                        break;
                    case "slideleft":
                        translateX += (1 - progress) * 100;
                        break;
                    case "slideright":
                        translateX -= (1 - progress) * 100;
                        break;
                    case "slideup":
                        translateY += (1 - progress) * 100;
                        break;
                    case "slidedown":
                        translateY -= (1 - progress) * 100;
                        break;
                    case "zoomin":
                        scale *= 0.72 + progress * 0.28;
                        opacity *= progress;
                        break;
                    case "circleopen":
                    case "circleclose":
                        clipPath = $"inset(0 {(1 - progress) * 50:0.##}% 0 {(1 - progress) * 50:0.##}%)";
                        opacity *= progress;
                        break;
                    case "blur":
                        blur += (1 - progress) * 14;
                        opacity *= progress;
                        break;
                    case "pixelize":
                    case "squeezeh":
                        translateX += (1 - progress) * 40;
                        opacity *= progress;
                        break;
                    case "squeezev":
                        translateY += (1 - progress) * 40;
                        opacity *= progress;
                        break;
                    case "float":
                        translateY -= (1 - progress) * 12;
                        scale *= 0.94 + progress * 0.06;
                        opacity *= progress;
                        break;
                    default:
                        opacity *= progress;
                        break;
                }
            }
        }

        if (isBase && clip.Box.Width >= 0.999 && clip.Box.Height >= 0.999)
        {
            return "position:absolute;left:0;top:0;width:100%;height:100%;" +
                   $"opacity:{Motion.Fmt(opacity)};" +
                   $"transform:translate({Motion.Fmt(translateX)}%,{Motion.Fmt(translateY)}%) scale({Motion.Fmt(scale)});" +
                   (blur > 0.01 ? $"filter:blur({Motion.Fmt(blur)}px);" : "") +
                   (clipPath.Length > 0 ? $"clip-path:{clipPath};" : "");
        }

        var fontSize = clip.Kind == TrackKind.Text
            ? $"font-size:{clip.FontSize / 10.8:0.###}%;line-height:1.05;white-space:pre-wrap;"
            : "";

        var shadow = clip.Kind == TrackKind.Text && clip.Shadow != "none"
            ? "text-shadow:0 4px 18px rgba(0,0,0,0.55);"
            : "";

        return "position:absolute;" +
               $"left:{clip.Box.X * 100:0.###}%;top:{clip.Box.Y * 100:0.###}%;" +
               $"width:{clip.Box.Width * 100:0.###}%;height:{clip.Box.Height * 100:0.###}%;" +
               fontSize + shadow +
               $"opacity:{Motion.Fmt(opacity)};" +
               $"transform:translate({Motion.Fmt(translateX)}%,{Motion.Fmt(translateY)}%) scale({Motion.Fmt(scale)}) rotate({Motion.Fmt(motion.Rotate)}deg);" +
               (blur > 0.01 ? $"filter:blur({Motion.Fmt(blur)}px);" : "") +
               (clipPath.Length > 0 ? $"clip-path:{clipPath};" : "");
    }

    private static Node SelectionOverlay(EditorStore store, double playhead)
    {
        var clip = store.SelectedClip;

        if (clip is null || !IsVisible(clip, playhead) || clip.Kind == TrackKind.Audio)
        {
            return F();
        }

        string Handle(string corner, string style) =>
            $"position:absolute;width:9px;height:9px;border-radius:2px;background-color:#ffffff;border:1px solid #2f6bff;{style}";

        Node dot(string corner, string style) => Div(new Props()
        {
            ClassName = "pv-handle",
            Attributes = new Dictionary<string, string> { ["style"] = Handle(corner, style) }
        });

        return Div(new Props()
        {
            ClassName = "pv-selection",
            Attributes = new Dictionary<string, string>
            {
                ["style"] =
                    $"position:absolute;left:{clip.Box.X * 100:0.###}%;top:{clip.Box.Y * 100:0.###}%;" +
                    $"width:{clip.Box.Width * 100:0.###}%;height:{clip.Box.Height * 100:0.###}%;" +
                    "outline:1.5px solid #3f7cff;pointer-events:none;"
            }
        },
            dot("tl", "left:-5px;top:-5px;"),
            dot("tr", "right:-5px;top:-5px;"),
            dot("bl", "left:-5px;bottom:-5px;"),
            dot("br", "right:-5px;bottom:-5px;"));
    }

    // -------------------------------------------------------------------- drag

    private static void BeginBoxDrag(EditorStore store, Ref<BoxDrag?> dragRef, float x, float y, double canvasWidth, double canvasHeight)
    {
        var clip = store.SelectedClip;
        var nx = Math.Clamp(x / Math.Max(1, canvasWidth), 0, 1);
        var ny = Math.Clamp(y / Math.Max(1, canvasHeight), 0, 1);

        if (clip is not null && clip.Kind != TrackKind.Audio)
        {
            var box = clip.Box;
            var insideX = nx >= box.X && nx <= box.X + box.Width;
            var insideY = ny >= box.Y && ny <= box.Y + box.Height;

            if (insideX && insideY)
            {
                var nearLeft = Math.Abs(nx - box.X) < 0.035;
                var nearRight = Math.Abs(nx - (box.X + box.Width)) < 0.035;
                var nearTop = Math.Abs(ny - box.Y) < 0.035;
                var nearBottom = Math.Abs(ny - (box.Y + box.Height)) < 0.035;

                store.BeginEdit();
                var mode = nearBottom && nearRight ? "se"
                    : nearBottom && nearLeft ? "sw"
                    : nearTop && nearRight ? "ne"
                    : nearTop && nearLeft ? "nw"
                    : nearRight ? "e"
                    : nearLeft ? "w"
                    : nearBottom ? "s"
                    : nearTop ? "n"
                    : "move";

                dragRef.Value = new BoxDrag
                {
                    ClipId = clip.Id,
                    Mode = mode,
                    StartX = nx,
                    StartY = ny,
                    Origin = new LayerBox { X = box.X, Y = box.Y, Width = box.Width, Height = box.Height }
                };

                return;
            }
        }

        // Nothing selected under the pointer: select the topmost clip there.
        var hit = store.Document.Tracks
            .Where(t => !t.Hidden && t.Kind != TrackKind.Audio && t.Kind != TrackKind.Text)
            .SelectMany(t => t.Clips.Where(c => c.Start <= store.Ui.Playhead && c.End > store.Ui.Playhead))
            .LastOrDefault(c => nx >= c.Box.X && nx <= c.Box.X + c.Box.Width && ny >= c.Box.Y && ny <= c.Box.Y + c.Box.Height);

        if (hit is not null)
        {
            store.Select(hit.Id);
        }
    }

    private static void UpdateBoxDrag(EditorStore store, Ref<BoxDrag?> dragRef, float x, float y, double canvasWidth, double canvasHeight)
    {
        var drag = dragRef.Value;

        if (drag?.ClipId is null)
        {
            return;
        }

        var clip = store.Document.FindClip(drag.ClipId);

        if (clip is null)
        {
            return;
        }

        var nx = Math.Clamp(x / Math.Max(1, canvasWidth), -0.2, 1.2);
        var ny = Math.Clamp(y / Math.Max(1, canvasHeight), -0.2, 1.2);
        var dx = nx - drag.StartX;
        var dy = ny - drag.StartY;

        if (!drag.Moved && Math.Abs(dx) < 0.004 && Math.Abs(dy) < 0.004)
        {
            return;
        }

        drag.Moved = true;
        var origin = drag.Origin;

        switch (drag.Mode)
        {
            case "move":
                clip.Box.X = Math.Clamp(origin.X + dx, -0.2, 1.1);
                clip.Box.Y = Math.Clamp(origin.Y + dy, -0.2, 1.1);
                break;
            default:
            {
                var left = origin.X;
                var top = origin.Y;
                var right = origin.X + origin.Width;
                var bottom = origin.Y + origin.Height;

                if (drag.Mode.Contains('e'))
                {
                    right = Math.Max(left + 0.05, origin.X + origin.Width + dx);
                }

                if (drag.Mode.Contains('w'))
                {
                    left = Math.Min(right - 0.05, origin.X + dx);
                }

                if (drag.Mode.Contains('s'))
                {
                    bottom = Math.Max(top + 0.05, origin.Y + origin.Height + dy);
                }

                if (drag.Mode.Contains('n'))
                {
                    top = Math.Min(bottom - 0.05, origin.Y + dy);
                }

                clip.Box.X = Math.Clamp(left, -0.2, 1.2);
                clip.Box.Y = Math.Clamp(top, -0.2, 1.2);
                clip.Box.Width = Math.Clamp(right - left, 0.05, 1.6);
                clip.Box.Height = Math.Clamp(bottom - top, 0.05, 1.6);
                break;
            }
        }

        store.Notify();
    }

    private static void EndBoxDrag(EditorStore store, Ref<BoxDrag?> dragRef)
    {
        var drag = dragRef.Value;

        if (drag is null)
        {
            return;
        }

        if (drag.Moved)
        {
            store.CommitEdit("Transform layer");
        }
        else
        {
            store.CancelEdit();
        }

        dragRef.Value = null;
    }

    // ----------------------------------------------------------------- players

    private void SyncPlayers(EditorStore store)
    {
        foreach (var clip in store.Document.Tracks.SelectMany(t => t.Clips))
        {
            if (_players.TryGetValue(clip.Id, out var player))
            {
                ApplyPlayer(store, clip, player);
            }

            if (_audioPlayers.TryGetValue(clip.Id, out var audio))
            {
                ApplyAudio(store, clip, audio);
            }
        }
    }

    private static void ApplyAudio(EditorStore store, Clip clip, AudioPlayer player)
    {
        try
        {
            var expected = clip.InPoint + (store.Ui.Playhead - clip.Start) * clip.Speed;

            if (store.Ui.Playing)
            {
                if (Math.Abs(player.Time - expected) > 0.4)
                {
                    player.Seek((float)Math.Max(0, expected));
                }

                if (!player.IsPlaying)
                {
                    player.Play();
                }
            }
            else
            {
                if (player.IsPlaying)
                {
                    player.Pause();
                }

                player.Seek((float)Math.Max(0, expected));
            }
        }
        catch
        {
            // Player already disposed by the host.
        }
    }

    private void ApplyPlayer(EditorStore store, Clip clip, VideoPlayer player)
    {
        var expected = clip.InPoint + (store.Ui.Playhead - clip.Start) * clip.Speed;

        try
        {
            if (store.Ui.Playing)
            {
                if (!player.IsPlaying)
                {
                    player.Play();
                }

                if (Math.Abs(player.Time - expected) > 0.5)
                {
                    player.Seek((float)Math.Max(0, expected));
                }

                return;
            }

            if (player.IsPlaying)
            {
                player.Pause();
            }

            // A paused player does not produce a frame for a new position, so
            // start the pipeline, seek and pause again. Only when the target
            // really changed: doing this every frame thrashes the decoder.
            if (_lastSeek.TryGetValue(clip.Id, out var last) && Math.Abs(last - expected) < 0.02)
            {
                return;
            }

            player.Play();
            player.Seek((float)Math.Max(0, expected));
            player.Pause();
            _lastSeek[clip.Id] = expected;
        }
        catch
        {
            // The host may have pruned the player between renders.
        }
    }

    private void SeekPlayers(EditorStore store)
    {
        foreach (var (id, player) in _players)
        {
            var clip = store.Document.FindClip(id);

            if (clip is null)
            {
                continue;
            }

            try
            {
                player.Seek((float)Math.Max(0, clip.InPoint + (store.Ui.Playhead - clip.Start) * clip.Speed));
            }
            catch
            {
                // Ignore a disposed player.
            }
        }

        foreach (var (id, player) in _audioPlayers)
        {
            var clip = store.Document.FindClip(id);

            if (clip is null)
            {
                continue;
            }

            try
            {
                player.Seek((float)Math.Max(0, clip.InPoint + (store.Ui.Playhead - clip.Start) * clip.Speed));
            }
            catch
            {
                // Ignore a disposed player.
            }
        }
    }
}
