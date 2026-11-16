using Donkeh.UILab;
using MovieDotNet.Core;
using MovieDotNet.Core.Media;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/MediaLibrary/MediaLibrary.css")]

namespace MovieDotNet.Components;

/// <summary>
/// The left dock. One panel per rail tab: media bin, text presets, audio,
/// transitions, effects, motion presets and project setup.
/// </summary>
public sealed class MediaLibrary : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var active = store.Ui.ActiveTab;
        var showLooks = active is "looks" or "transitions" or "effects" or "animations";
        var looks = active is "transitions" or "effects" or "animations" ? active : store.Ui.LooksTab;

        Effect(() => EditorCommands.EnsureCaptureDevices(store), []);

        return Div(new() { ClassName = "library" },
            Div(new() { ClassName = "library-head" },
                Span(new() { ClassName = "library-title" }, Text(TitleFor(showLooks ? looks : active))),
                Div(new() { ClassName = "spacer" }),
                Show(active == "media", Button(new()
                {
                    ClassName = "btn is-small",
                    OnClick = OpenMediaBrowser(store)
                },
                    H("i", new Props { ClassName = "fa-solid fa-plus" }),
                    Text("Import")))),
            Div(new() { ClassName = "library-body scroll" },
                Show(showLooks, LooksTabs(store, looks)),
                Show(active == "media", MediaTab(store)),
                Show(active == "text", TextTab(store)),
                Show(active == "audio", AudioTab(store)),
                Show(showLooks && looks == "transitions", TransitionsTab(store)),
                Show(showLooks && looks == "effects", EffectsTab(store)),
                Show(showLooks && looks == "animations", AnimationsTab(store)),
                Show(active == "record", RecordTab(store)),
                Show(active == "settings", SettingsTab(store))));
    }

    private static Node LooksTabs(EditorStore store, string current)
    {
        Node Tab(string id, string label) => Div(new()
        {
            ClassName = current == id ? "looks-tab is-active" : "looks-tab",
            OnClick = () =>
            {
                store.Ui.ActiveTab = "looks";
                store.Ui.LooksTab = id;
                store.Notify();
            }
        },
            Text(label));

        return Div(new() { ClassName = "looks-tabs" },
            Tab("transitions", "Transitions"),
            Tab("effects", "Filters"),
            Tab("animations", "Motion"));
    }

    private static string TitleFor(string tab) => tab switch
    {
        "text" => "Text & titles",
        "audio" => "Audio",
        "transitions" => "Transitions",
        "effects" => "Effects",
        "animations" => "Motion",
        "looks" => "Looks",
        "record" => "Record",
        "settings" => "Project setup",
        _ => "Media bin"
    };

    private static Action OpenMediaBrowser(EditorStore store) => () =>
    {
        store.Ui.BrowserMode = "media";
        store.Ui.ShowBrowser = true;
        store.Notify();
    };

    // ------------------------------------------------------------------ media

    private static Node MediaTab(EditorStore store)
    {
        var videos = store.Document.Assets.Where(a => a.Kind == AssetKind.Video).ToList();
        var images = store.Document.Assets.Where(a => a.Kind == AssetKind.Image).ToList();

        if (store.Document.Assets.Count == 0)
        {
            return Div(new() { ClassName = "empty-panel" },
                Div(new() { ClassName = "empty-icon" },
                    H("i", new Props { ClassName = "fa-solid fa-cloud-arrow-up" })),
                Span(new() { ClassName = "empty-title" }, Text("Your bin is empty")),
                Span(new() { ClassName = "empty-copy" },
                    Text("Import video, audio or images.")),
                Button(new() { ClassName = "btn is-accent", OnClick = OpenMediaBrowser(store) },
                    H("i", new Props { ClassName = "fa-solid fa-folder-open" }),
                    Text("Import media")),
                Button(new()
                {
                    ClassName = "btn",
                    OnClick = () => EditorCommands.LoadDemoAsync(store)
                },
                    H("i", new Props { ClassName = "fa-solid fa-wand-magic-sparkles" }),
                    Text("Load demo project")));
        }

        return F(
            Section("Video", "fa-solid fa-film", videos.Count),
            Div(new() { ClassName = "asset-grid" }, Each(videos, asset => AssetCard(store, asset))),
            Section("Images", "fa-solid fa-image", images.Count),
            Div(new() { ClassName = "asset-grid" }, Each(images, asset => AssetCard(store, asset))),
            Div(new() { ClassName = "library-actions" },
                Button(new() { ClassName = "btn is-small", OnClick = OpenMediaBrowser(store) },
                    H("i", new Props { ClassName = "fa-solid fa-plus" }),
                    Text("Import")),
                Button(new()
                {
                    ClassName = "btn is-small",
                    OnClick = () => EditorCommands.LoadDemoAsync(store)
                },
                    H("i", new Props { ClassName = "fa-solid fa-wand-magic-sparkles" }),
                    Text("Demo"))));
    }

    private static Node AssetCard(EditorStore store, MediaAsset asset)
    {
        var selected = store.Ui.SelectedAssetId == asset.Id;
        var dragging = store.Ui.DraggingAssetId == asset.Id;
        var className = "asset-card";

        if (selected)
        {
            className += " is-selected";
        }

        if (dragging)
        {
            className += " is-dragging";
        }

        var meta = asset.Kind switch
        {
            AssetKind.Image => $"{asset.Width}×{asset.Height}",
            AssetKind.Audio => Format.Time(asset.Duration),
            _ => $"{asset.Width}×{asset.Height} · {Format.Time(asset.Duration)}"
        };

        return Div(new()
        {
            ClassName = className,
            Key = asset.Id,
            Title = $"{asset.Name}\n{asset.Path}\nDrag to the timeline to place it",
            Draggable = true,
            OnDragStart = evt =>
            {
                evt.Data.SetData(DragFormats.Asset, asset.Id);
                evt.Data.EffectAllowed = "copy";
                store.Ui.DraggingAssetId = asset.Id;
                store.Ui.DraggingTextPreset = null;
                store.Notify();
            },
            OnDragEnd = evt =>
            {
                store.Ui.DraggingAssetId = null;
                store.Ui.DropTrackId = null;
                store.Ui.DropTime = 0;
                store.Notify();
            },
            OnClick = () =>
            {
                store.Ui.SelectedAssetId = asset.Id;
                store.Ui.ActiveTab = "media";
                store.Notify();
            }
        },
            Div(new() { ClassName = "asset-thumb" },
                Show(asset.Poster is not null,
                    Img(new()
                    {
                        ClassName = "asset-poster",
                        Src = asset.Poster,
                        Alt = asset.Name
                    })),
                Show(asset.Poster is null,
                    Div(new() { ClassName = "asset-placeholder" },
                        H("i", new Props
                        {
                            ClassName = asset.Kind == AssetKind.Audio
                                ? "fa-solid fa-wave-square"
                                : "fa-solid fa-film"
                        }))),
                Div(new() { ClassName = "asset-badge" },
                    H("i", new Props
                    {
                        ClassName = asset.Kind == AssetKind.Audio
                            ? "fa-solid fa-music"
                            : asset.Kind == AssetKind.Image
                                ? "fa-solid fa-image"
                                : "fa-solid fa-video"
                    })),
                Div(new()
                {
                    ClassName = "asset-add",
                    Title = "Add to timeline",
                    OnClick = () =>
                    {
                        store.AddAssetToTimeline(asset);
                        store.Toast($"{asset.Name} added at playhead");
                    }
                },
                    H("i", new Props { ClassName = "fa-solid fa-plus" })),
                Show(asset.Kind == AssetKind.Video,
                    Div(new()
                    {
                        ClassName = "asset-pip",
                        Title = "Add as a picture-in-picture layer",
                        OnClick = () =>
                        {
                            store.AddAssetToTimeline(asset, asOverlay: true);
                            store.Toast($"{asset.Name} added as a layer");
                        }
                    },
                        H("i", new Props { ClassName = "fa-solid fa-layer-group" })))),
            Div(new() { ClassName = "asset-info" },
                Span(new() { ClassName = "asset-name" }, Text(asset.Name)),
                Span(new() { ClassName = "asset-meta" }, Text(meta))));
    }

    private static Node Section(string title, string icon, int count) =>
        Div(new() { ClassName = "library-section" },
            H("i", new Props { ClassName = icon }),
            Span(new(), Text(title)),
            Span(new() { ClassName = "library-count" }, Text(count.ToString())));

    // ------------------------------------------------------------------- text

    private static Node TextTab(EditorStore store)
    {
        return F(
            Div(new() { ClassName = "preset-grid" },
                Each(
                    TextPreset.All.Select((preset, index) => (Preset: preset, Index: index)),
                    item =>
                    {
                        var dragging = store.Ui.DraggingTextPreset == item.Index;

                        return Div(new()
                        {
                            ClassName = dragging ? "preset-card is-dragging" : "preset-card",
                            Draggable = true,
                            Title = $"Drag {item.Preset.Title} to the timeline",
                            OnDragStart = evt =>
                            {
                                evt.Data.SetData(DragFormats.TextPreset, item.Index.ToString());
                                evt.Data.EffectAllowed = "copy";
                                store.Ui.DraggingTextPreset = item.Index;
                                store.Ui.DraggingAssetId = null;
                                store.Notify();
                            },
                            OnDragEnd = evt =>
                            {
                                store.Ui.DraggingTextPreset = null;
                                store.Ui.DropTrackId = null;
                                store.Ui.DropTime = 0;
                                store.Notify();
                            },
                            OnClick = () => item.Preset.AddTo(store)
                        },
                            Span(new() { ClassName = "preset-sample", Attributes = new Dictionary<string, string>
                            {
                                ["style"] = $"font-weight:{item.Preset.Weight};font-size:{item.Preset.Size / 6:0}px"
                            } }, Text(item.Preset.Title)),
                            Span(new() { ClassName = "preset-label" }, Text(item.Preset.Title)));
                    })),
            Div(new() { ClassName = "library-actions" },
                Button(new()
                {
                    ClassName = "btn is-accent",
                    OnClick = () => store.AddTextClip("Your text")
                },
                    H("i", new Props { ClassName = "fa-solid fa-font" }),
                    Text("Add text layer"))));
    }

    // ------------------------------------------------------------------ audio

    private static Node AudioTab(EditorStore store)
    {
        var audio = store.Document.Assets.Where(a => a.Kind == AssetKind.Audio).ToList();
        var withSound = store.Document.Assets.Where(a => a.Kind == AssetKind.Video && a.HasAudio).ToList();
        var rows = audio.Concat(withSound).ToList();

        return F(
            Section("Music & audio", "fa-solid fa-music", rows.Count),
            Show(rows.Count == 0,
                Div(new() { ClassName = "empty-panel is-compact" },
                    Span(new() { ClassName = "empty-copy" }, Text("Import an audio file to add a soundtrack.")),
                    Button(new() { ClassName = "btn is-small", OnClick = OpenMediaBrowser(store) },
                        H("i", new Props { ClassName = "fa-solid fa-plus" }),
                        Text("Import audio")))),
            Div(new() { ClassName = "audio-list" },
                Each(rows, asset => Div(new()
                {
                    ClassName = store.Ui.DraggingAssetId == asset.Id ? "audio-row is-dragging" : "audio-row",
                    Draggable = true,
                    OnDragStart = evt =>
                    {
                        evt.Data.SetData(DragFormats.Asset, asset.Id);
                        evt.Data.EffectAllowed = "copy";
                        store.Ui.DraggingAssetId = asset.Id;
                        store.Ui.DraggingTextPreset = null;
                        store.Notify();
                    },
                    OnDragEnd = evt =>
                    {
                        store.Ui.DraggingAssetId = null;
                        store.Ui.DropTrackId = null;
                        store.Ui.DropTime = 0;
                        store.Notify();
                    },
                    OnClick = () =>
                    {
                        store.AddAssetToTimeline(asset);
                        store.Toast($"{asset.Name} added");
                    }
                },
                    Div(new() { ClassName = "audio-icon" },
                        H("i", new Props { ClassName = "fa-solid fa-music" })),
                    Div(new() { ClassName = "audio-info" },
                        Span(new() { ClassName = "audio-name" }, Text(asset.Name)),
                        Span(new() { ClassName = "audio-meta" }, Text($"{Format.Time(asset.Duration)} · {asset.Kind}"))),
                    H("i", new Props { ClassName = "fa-solid fa-plus audio-add" })))));
    }

    // ------------------------------------------------------------ transitions

    private static Node TransitionsTab(EditorStore store)
    {
        var clip = store.SelectedClip;
        var blocks = new List<Node>
        {
            Div(new() { ClassName = "panel-note" },
                H("i", new Props { ClassName = "fa-solid fa-circle-info" }),
                Span(new(), Text(clip is null
                    ? "Select a clip, then choose a transition."
                    : $"Transition into “{clip.Name}”")))
        };

        // Built as a flat list: nested fragments inside Each do not advance the
        // outer block flow, which made section headers overlap their grids.
        foreach (var category in TransitionRegistry.Categories)
        {
            var cards = new List<Node>();

            foreach (var transition in TransitionRegistry.All.Where(t => t.Category == category))
            {
                cards.Add(Div(new()
                {
                    ClassName = clip is not null && clip.TransitionId == transition.Id
                        ? "transition-card is-active"
                        : "transition-card",
                    Key = transition.Id,
                    Title = $"{transition.Name} · xfade {transition.Xfade}",
                    OnClick = () => EditorCommands.ApplyTransition(store, transition.Id, clip?.TransitionDuration ?? 0.6)
                },
                    Span(new() { ClassName = "transition-name" }, Text(transition.Name))));
            }

            blocks.Add(Section(category, "fa-solid fa-layer-group", cards.Count));
            blocks.Add(Div(new() { ClassName = "transition-grid" }, [.. cards]));
        }

        if (clip is not null)
        {
            blocks.Add(Div(new() { ClassName = "transition-duration" },
                Div(new() { ClassName = "field-label" }, Text("Transition length")),
                Input(new()
                {
                    Type = "range",
                    Value = clip.TransitionDuration.ToString("0.##"),
                    Attributes = new Dictionary<string, string>
                    {
                        ["min"] = "0.1", ["max"] = "3", ["step"] = "0.05"
                    },
                    OnInput = value =>
                    {
                        if (double.TryParse(value, out var duration))
                        {
                            store.UpdateClipLive(clip.Id, c => c.TransitionDuration = duration);
                        }
                    }
                }),
                Span(new() { ClassName = "value-badge" }, Text($"{clip.TransitionDuration:0.00}s"))));
        }

        return F([.. blocks]);
    }

    private static string IconFor(string id) => id switch
    {
        "fadeblack" or "fadewhite" => "fa-solid fa-circle-half-stroke",
        "wipeleft" or "wiperight" or "wipeup" or "wipedown" or "wipetl" or "wipetr" => "fa-solid fa-arrows-left-right",
        "slideleft" or "slideright" or "slideup" or "slidedown" or "smoothleft" or "smoothright" => "fa-solid fa-arrow-right-arrow-left",
        "circleopen" or "circleclose" or "radial" or "circlecrop" => "fa-solid fa-circle-notch",
        "zoomin" or "squeezeh" or "squeezev" => "fa-solid fa-magnifying-glass-plus",
        "hblur" or "dissolve_noise" or "glitch" => "fa-solid fa-bolt",
        "pixelize" => "fa-solid fa-table-cells",
        "diagtl" or "diagtr" or "diagbl" or "diagbr" => "fa-solid fa-diamond",
        "rectcrop" => "fa-solid fa-crop-simple",
        "float" => "fa-solid fa-feather",
        "swap" => "fa-solid fa-shuffle",
        "none" => "fa-solid fa-scissors",
        _ => "fa-solid fa-wand-magic-sparkles"
    };

    // ---------------------------------------------------------------- effects

    private static Node EffectsTab(EditorStore store)
    {
        var clip = store.SelectedClip;

        return F(
            Div(new() { ClassName = "panel-note" },
                H("i", new Props { ClassName = "fa-solid fa-sliders" }),
                Span(new(), Text(clip is null
                    ? "Select a clip to grade it."
                    : $"Effects on “{clip.Name}”"))),
            Div(new() { ClassName = "effect-list" },
                Each(EffectRegistry.All, effect =>
                {
                    var existing = clip?.Effects.FirstOrDefault(e => e.Id == effect.Id);

                    return Div(new() { ClassName = existing is not null ? "effect-row is-on" : "effect-row" },
                        Div(new() { ClassName = "effect-head" },
                            Span(new() { ClassName = "effect-name" }, Text(effect.Name)),
                            Button(new()
                            {
                                ClassName = "btn is-small",
                                OnClick = () =>
                                {
                                    if (clip is null)
                                    {
                                        store.Toast("Select a clip first.");
                                        return;
                                    }

                                    var current = clip.Effects.FirstOrDefault(e => e.Id == effect.Id);
                                    store.UpdateClip(clip.Id, c =>
                                    {
                                        if (current is null)
                                        {
                                            c.Effects.Add(new EffectInstance { Id = effect.Id, Amount = 0.6 });
                                        }
                                        else
                                        {
                                            c.Effects.RemoveAll(e => e.Id == effect.Id);
                                        }
                                    }, "Toggle effect");
                                }
                            },
                                H("i", new Props
                                {
                                    ClassName = existing is not null ? "fa-solid fa-check" : "fa-solid fa-plus"
                                }))),
                        Show(existing is not null, Input(new()
                        {
                            Type = "range",
                            Value = (existing?.Amount ?? 0.6).ToString("0.##"),
                            Attributes = new Dictionary<string, string>
                            {
                                ["min"] = "0", ["max"] = "1", ["step"] = "0.01"
                            },
                            OnInput = value =>
                            {
                                if (clip is not null && double.TryParse(value, out var amount))
                                {
                                    store.UpdateClipLive(clip.Id, c =>
                                    {
                                        var target = c.Effects.FirstOrDefault(e => e.Id == effect.Id);

                                        if (target is not null)
                                        {
                                            target.Amount = amount;
                                        }
                                    });
                                }
                            }
                        })));
                })));
    }

    // -------------------------------------------------------------- animations

    private static Node AnimationsTab(EditorStore store)
    {
        var clip = store.SelectedClip;

        if (clip is null)
        {
            return Div(new() { ClassName = "empty-panel is-compact" },
                Span(new() { ClassName = "empty-copy" }, Text("Select a clip to animate its entrance and exit.")));
        }

        var blocks = new List<Node>();

        foreach (var field in new[] { "Enter", "Exit" })
        {
            var cards = new List<Node>();
            var current = field == "Enter" ? clip.EnterAnimation : clip.ExitAnimation;

            foreach (var preset in AnimationRegistry.All)
            {
                cards.Add(Div(new()
                {
                    ClassName = current == preset.Id ? "animation-card is-active" : "animation-card",
                    Key = preset.Id,
                    OnClick = () => store.UpdateClip(clip.Id, c =>
                    {
                        if (field == "Enter")
                        {
                            c.EnterAnimation = preset.Id;
                        }
                        else
                        {
                            c.ExitAnimation = preset.Id;
                        }
                    }, "Change animation")
                },
                    Span(new() { ClassName = "animation-name" }, Text(preset.Name))));
            }

            blocks.Add(Section(field, "fa-solid fa-bolt", cards.Count));
            blocks.Add(Div(new() { ClassName = "animation-grid" }, [.. cards]));
        }

        blocks.Add(Div(new() { ClassName = "slider-row" },
            Div(new() { ClassName = "slider-head" },
                Div(new() { ClassName = "field-label" }, Text("Duration")),
                Span(new() { ClassName = "value-badge" }, Text($"{clip.AnimationDuration:0.00}s"))),
            Input(new()
            {
                Type = "range",
                Value = clip.AnimationDuration.ToString("0.##"),
                Attributes = new Dictionary<string, string>
                {
                    ["min"] = "0.1", ["max"] = "3", ["step"] = "0.05"
                },
                OnInput = value =>
                {
                    if (double.TryParse(value, out var duration))
                    {
                        store.UpdateClipLive(clip.Id, c => c.AnimationDuration = duration);
                    }
                }
            })));

        return F([.. blocks]);
    }

    // ---------------------------------------------------------------- settings

    private static Node SettingsTab(EditorStore store)
    {
        var document = store.Document;
        var presets = new (string Name, int Width, int Height, double Fps)[]
        {
            ("YouTube 1080p", 1920, 1080, 30),
            ("Cinema 4K", 3840, 2160, 24),
            ("Vertical 1080", 1080, 1920, 30),
            ("Square 1080", 1080, 1080, 30),
            ("Reels 720p", 720, 1280, 30),
            ("GIF 480", 480, 480, 15)
        };

        return F(
            Div(new() { ClassName = "settings-block" },
                Div(new() { ClassName = "field-label" }, Text("Canvas preset")),
                Div(new() { ClassName = "preset-grid is-tight" },
                    Each(presets, preset => Div(new()
                    {
                        ClassName = document.Width == preset.Width && document.Height == preset.Height
                            ? "size-card is-active"
                            : "size-card",
                        OnClick = () => store.Edit(_ =>
                        {
                            document.Width = preset.Width;
                            document.Height = preset.Height;
                            document.Fps = preset.Fps;
                        }, "Change canvas")
                    },
                        Span(new() { ClassName = "size-name" }, Text(preset.Name)),
                        Span(new() { ClassName = "size-meta" }, Text($"{preset.Width}×{preset.Height}")))))),
            Div(new() { ClassName = "settings-block" },
                Div(new() { ClassName = "field-label" }, Text("Frame rate")),
                Div(new() { ClassName = "segmented" },
                    Each(new[] { 24d, 25d, 30d, 50d, 60d }, fps => Div(new()
                    {
                        ClassName = Math.Abs(document.Fps - fps) < 0.01 ? "segment is-active" : "segment",
                        OnClick = () => store.Edit(_ => document.Fps = fps, "Change frame rate")
                    }, Text($"{fps:0}")))),
            Div(new() { ClassName = "settings-block" },
                Div(new() { ClassName = "field-label" }, Text("Canvas colour")),
                Div(new() { ClassName = "color-row" },
                    Each(new[] { "#05070c", "#000000", "#101826", "#1b1035", "#f4f6fb" }, color =>
                        Div(new()
                        {
                            ClassName = "swatch",
                            OnClick = () => store.Edit(_ => document.Background = color, "Change background"),
                            Attributes = new Dictionary<string, string>
                            {
                                ["style"] = $"background-color:{color}"
                            }
                        }))),
                Input(new()
                {
                    ClassName = "input",
                    Value = document.Background,
                    OnInput = value =>
                    {
                        document.Background = value;
                        store.Notify();
                    }
                })),
            Div(new() { ClassName = "settings-block" },
                Div(new() { ClassName = "field-label" }, Text("Media pipeline")),
                Div(new() { ClassName = "settings-note" },
                    Span(new() { ClassName = Ffmpeg.Available ? "dot" : "dot is-bad" }),
                    Span(new(), Text(Ffmpeg.Status))),
                Show(Ffmpeg.Available, Div(new() { ClassName = "settings-note" },
                    H("i", new Props { ClassName = "fa-solid fa-folder" }),
                    Span(new(), Text(MediaAnalysis.CacheRoot)))),
                Div(new() { ClassName = "settings-row" },
                    Button(new()
                    {
                        ClassName = "btn is-small",
                        OnClick = () => EditorCommands.SaveProject(store)
                    },
                        H("i", new Props { ClassName = "fa-solid fa-floppy-disk" }),
                        Text("Save")),
                    Button(new()
                    {
                        ClassName = "btn is-small",
                        OnClick = () =>
                        {
                            store.Ui.BrowserMode = "open";
                            store.Ui.ShowBrowser = true;
                            store.Notify();
                        }
                    },
                        H("i", new Props { ClassName = "fa-solid fa-folder-open" }),
                        Text("Open"))),
                Div(new() { ClassName = "settings-note is-dim" },
                    Span(new(), Text(ProjectFiles.ProjectsDirectory))))));
    }

    // ---------------------------------------------------------------- record

    private static Node RecordTab(EditorStore store)
    {
        var ui = store.Ui;
        var source = ui.CaptureSource;
        var cameras = EditorCommands.VideoDevices;
        var windows = EditorCommands.WindowDevices;
        var microphones = EditorCommands.AudioDevices;
        var recording = EditorCommands.CameraRecording;
        var hasScreen = EditorCommands.ScreenDevice is not null;
        var hasWindow = windows.Count > 0;
        var hasCamera = cameras.Count > 0;

        var selectedCamera = cameras.FirstOrDefault(d => d.Name == ui.CameraDevice) ?? cameras.FirstOrDefault();
        var selectedWindow = windows.FirstOrDefault(d => d.Name == ui.CaptureWindow) ?? windows.FirstOrDefault();
        var micChoice = ui.RecordWithMic
            ? ui.MicDevice ?? microphones.FirstOrDefault()?.Name ?? "none"
            : "none";
        var width = store.Document.Width;
        var height = store.Document.Height;
        var fps = Math.Clamp((int)Math.Round(store.Document.Fps), 24, 60);

        Node SourceTab(string id, string icon, string label)
        {
            var active = source == id;

            return Div(new()
            {
                ClassName = (active ? "segment is-active" : "segment")
                    + (recording ? " is-disabled" : ""),
                Title = label,
                OnClick = () =>
                {
                    if (EditorCommands.CameraRecording)
                    {
                        return;
                    }

                    ui.CaptureSource = id;
                    store.Notify();
                }
            },
                H("i", new Props { ClassName = icon }),
                Span(new() { ClassName = "segment-label" }, Text(label)));
        }

        Node Missing(string copy) =>
            Div(new() { ClassName = "empty-panel is-compact" },
                Span(new() { ClassName = "empty-copy" }, Text(copy)),
                Button(new()
                {
                    ClassName = "btn is-small",
                    OnClick = () => EditorCommands.RefreshCaptureDevices(store)
                },
                    H("i", new Props { ClassName = "fa-solid fa-rotate" }),
                    Text("Refresh")));

        Node Picker()
        {
            if (source == "window")
            {
                return hasWindow
                    ? Div(new() { ClassName = "field" },
                        Span(new() { ClassName = "field-label" }, Text("Window")),
                        Div(new() { ClassName = "device-list scroll" },
                            DeviceChoice(
                                windows.Select(d => (d.Name, d.Name)).ToList(),
                                selectedWindow?.Name,
                                value =>
                                {
                                    ui.CaptureWindow = value;
                                    store.Notify();
                                })))
                    : Missing("No windows found. Open a window, then refresh.");
            }

            if (source == "camera")
            {
                return hasCamera
                    ? Div(new() { ClassName = "field" },
                        Span(new() { ClassName = "field-label" }, Text("Camera")),
                        DeviceChoice(
                            cameras.Select(d => (d.Name, d.Name)).ToList(),
                            selectedCamera?.Name,
                            value =>
                            {
                                ui.CameraDevice = value;
                                store.Notify();
                            }))
                    : Missing("No camera found. Connect one, then refresh.");
            }

            return Div(new() { ClassName = "capture-source-card" },
                H("i", new Props { ClassName = "fa-solid fa-display" }),
                Div(new() { ClassName = "capture-source-text" },
                    Span(new() { ClassName = "capture-source-name" }, Text("Entire desktop")),
                    Span(new() { ClassName = "capture-source-meta" },
                        Text("Full screen, fitted to the project canvas"))));
        }

        var (previewIcon, previewCopy) = source switch
        {
            "window" => ("fa-solid fa-window-maximize",
                selectedWindow?.Name ?? "Choose a window, then press record"),
            "camera" => ("fa-solid fa-video",
                selectedCamera?.Name ?? "Choose a camera, then press record"),
            _ => ("fa-solid fa-display", "Captures the entire desktop")
        };

        var recordLabel = source switch
        {
            "window" => "Start window recording",
            "camera" => "Start camera recording",
            _ => "Start screen recording"
        };

        var microphoneOptions = new List<(string Value, string Label)>
        {
            ("none", "No microphone")
        };
        microphoneOptions.AddRange(microphones.Select(d => (d.Name, d.Name)));

        return F(
            Div(new() { ClassName = "panel-note" },
                H("i", new Props { ClassName = "fa-solid fa-circle-info" }),
                Span(new(), Text(Ffmpeg.Available
                    ? $"Record your screen, a window or a camera. Captures fit the {width}×{height} canvas and land in the bin."
                    : "Recording needs FFmpeg on a desktop host. Playback and editing still work."))),

            Div(new() { ClassName = "record-block" },
                Div(new() { ClassName = "field-label" }, Text("Capture source")),
                Div(new() { ClassName = "segmented capture-segments" },
                    SourceTab("screen", "fa-solid fa-display", "Screen"),
                    SourceTab("window", "fa-solid fa-window-maximize", "Window"),
                    SourceTab("camera", "fa-solid fa-video", "Camera")),

                Div(new() { ClassName = "record-preview" },
                    Div(new() { ClassName = "record-preview-placeholder" },
                        H("i", new Props { ClassName = previewIcon }),
                        Span(new(), Text(previewCopy))),
                    Div(new() { ClassName = "record-preview-badge" },
                        Span(new() { ClassName = recording ? "dot is-bad" : "dot" }),
                        Text(recording ? "Recording" : "Ready"))),

                Picker(),

                Show(source == "window",
                    Span(new() { ClassName = "capture-hint" },
                        Text("The selected window comes forward while recording."))),

                Div(new() { ClassName = "capture-meta" },
                    Span(new() { ClassName = "value-badge" }, Text($"{width}×{height}")),
                    Span(new() { ClassName = "value-badge" }, Text($"{fps} fps")),
                    Span(new() { ClassName = "capture-meta-note" }, Text("Fitted to the project canvas"))),

                RecordRow(
                    recording,
                    EditorCommands.CameraElapsed,
                    recordLabel,
                    () =>
                    {
                        if (EditorCommands.CameraRecording)
                        {
                            EditorCommands.StopCameraAsync(store);
                        }
                        else
                        {
                            EditorCommands.StartCamera(store);
                        }
                    },
                    true),

                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Microphone")),
                    DeviceChoice(
                        microphoneOptions,
                        micChoice,
                        value =>
                        {
                            ui.RecordWithMic = value != "none";
                            ui.MicDevice = value == "none" ? null : value;
                            store.Notify();
                        })),

                Show(!hasScreen && source == "screen",
                    Missing("Screen capture is not available on this host.")),

                Div(new() { ClassName = "record-actions" },
                    Button(new()
                    {
                        ClassName = "btn is-small is-ghost",
                        OnClick = () =>
                        {
                            if (EditorCommands.MicrophoneRecording)
                            {
                                EditorCommands.StopMicrophoneAsync(store);
                            }
                            else
                            {
                                EditorCommands.StartMicrophone(store);
                            }
                        }
                    },
                        H("i", new Props
                        {
                            ClassName = EditorCommands.MicrophoneRecording
                                ? "fa-solid fa-stop"
                                : "fa-solid fa-microphone"
                        }),
                        Text(EditorCommands.MicrophoneRecording
                            ? $"Stop audio ({EditorCommands.MicrophoneElapsed.TotalMinutes:00}:{EditorCommands.MicrophoneElapsed.Seconds:00})"
                            : "Record audio only")),
                    Div(new() { ClassName = "spacer" }),
                    Button(new()
                    {
                        ClassName = "btn is-small",
                        OnClick = () => EditorCommands.RefreshCaptureDevices(store)
                    },
                        H("i", new Props { ClassName = "fa-solid fa-rotate" }),
                        Text("Refresh")))));
    }

    private static Node RecordRow(
        bool recording,
        TimeSpan elapsed,
        string idleLabel,
        Action toggle,
        bool withVideo)
    {
        var clock = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        return Div(new() { ClassName = recording ? "record-row is-live" : "record-row" },
            Div(new()
            {
                ClassName = recording ? "record-button is-live" : "record-button",
                Title = recording ? "Stop recording" : idleLabel,
                OnClick = toggle
            },
                H("i", new Props
                {
                    ClassName = recording ? "fa-solid fa-stop" : "fa-solid fa-circle"
                })),
            Div(new() { ClassName = "record-meta" },
                Span(new() { ClassName = "record-state" },
                    Text(recording ? "Recording" : "Ready")),
                Span(new() { ClassName = "record-time" }, Text(clock))),
            Span(new() { ClassName = "chip" },
                H("i", new Props { ClassName = withVideo ? "fa-solid fa-film" : "fa-solid fa-music" }),
                Text(withVideo ? "H.264" : "AAC")));
    }

    /// <summary>A full-width choice list, used for capture devices.</summary>
    private static Node DeviceChoice(
        IReadOnlyList<(string Value, string Label)> options,
        string? selected,
        Action<string> onSelect)
    {
        return C<Choice, ChoiceProps>(new ChoiceProps
        {
            Value = selected ?? "",
            Chips = false,
            Options = options.Select(option => new ChoiceOption(option.Value, option.Label)).ToList(),
            OnSelect = onSelect
        });
    }
}
