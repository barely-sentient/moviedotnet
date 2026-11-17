using Donkeh.Hosting;
using Donkeh.UILab;
using Donkeh.Windowing;
using MovieDotNet.Components;
using MovieDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Styles/theme.css")]

namespace MovieDotNet;

/// <summary>
/// The MovieDotNet application root. Owns the editor store, the frame clock,
/// the desktop or phone shell, and the global overlay layers.
/// </summary>
[AppRoot(Width = 1600, Height = 980, Title = "MovieDotNet — Video Editor", Frameless = true)]
public sealed class App : Component
{
    private static readonly EditorStore Store = EditorStore.CreateNew();
    private static long _lastCaptureTick;
    private static bool _wasRecording;

    /// <inheritdoc/>
    public override Node Render()
    {
        // Any store mutation re-renders the tree; the revision only needs to exist.
        var (revision, setRevision, updateRevision) = StateWithUpdate(0);
        var (booted, setBooted) = State(false);

        Effect(() =>
        {
            void Handler() => updateRevision(value => value + 1);
            Store.Changed += Handler;
            return () => Store.Changed -= Handler;
        }, []);

        Effect(() =>
        {
            void Tick(double delta)
            {
                var now = Environment.TickCount64;

                if (Store.Ui.Toast is not null && now > Store.Ui.ToastUntil)
                {
                    Store.Ui.Toast = null;
                    Store.RaiseChanged();
                }

                if (Store.Ui.Playing)
                {
                    var next = Store.Ui.Playhead + delta;
                    var end = Store.Document.Tracks.SelectMany(t => t.Clips).DefaultIfEmpty().Max(c => c?.End ?? 0);

                    if (next >= end)
                    {
                        next = end;
                        Store.Ui.Playing = false;
                    }

                    Store.Ui.Playhead = Math.Max(0, next);
                    Store.SetPlayhead(Store.Ui.Playhead, throttled: true);
                }

                var recording = UI.EditorCommands.AnyRecording;

                if (recording && now - _lastCaptureTick >= 1000)
                {
                    _lastCaptureTick = now;
                    Store.Notify();
                }
                else if (_wasRecording && !recording)
                {
                    Store.Notify();
                }

                _wasRecording = recording;
            }

            Host.FrameTick += Tick;
            return () => Host.FrameTick -= Tick;
        }, []);

        Effect(() =>
        {
            if (booted)
            {
                return;
            }

            setBooted(true);

            if (Environment.GetEnvironmentVariable("MOVIEDOTNET_MOBILE_PAGE") is { Length: > 0 } mobilePage)
            {
                Store.Ui.MobilePage = mobilePage;
            }

            if (Environment.GetEnvironmentVariable("MOVIEDOTNET_MOBILE_TAB") is { Length: > 0 } mobileTab)
            {
                Store.Ui.ActiveTab = mobileTab;
            }

            // Development helpers: open a project or generate the demo at start-up.
            var projectPath = Environment.GetEnvironmentVariable("MOVIEDOTNET_PROJECT");

            if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
            {
                UI.EditorCommands.OpenProject(Store, projectPath);

                // Diagnostic hook: render an opened project through the normal
                // export path (used for headless layout/composition checks).
                var projectRender = Environment.GetEnvironmentVariable("MOVIEDOTNET_RENDER");

                if (!string.IsNullOrWhiteSpace(projectRender))
                {
                    var document = Store.Document;
                    UI.EditorCommands.RenderAsync(Store, new MovieDotNet.Core.Media.RenderSettings
                    {
                        Width = document.Width,
                        Height = document.Height,
                        Fps = document.Fps,
                        Crf = 24,
                        Background = document.Background
                    }, projectRender);
                }

                return;
            }

            if (Environment.GetEnvironmentVariable("MOVIEDOTNET_IMPORT_FILE") is { Length: > 0 } importPath)
            {
                UI.EditorCommands.ImportAsync(Store, importPath.Split(
                    Path.PathSeparator,
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                return;
            }

            if (Environment.GetEnvironmentVariable("MOVIEDOTNET_DEMO") is "1" or "true"
                && Store.Document.Assets.Count == 0)
            {
                UI.EditorCommands.LoadDemoAsync(Store);
                return;
            }

            if (!UI.EditorCommands.SuppressToasts)
            {
                Store.Toast(Device.Mobile
                    ? "Tip: open Media to add clips, titles and looks."
                    : FfmpegAvailableMessage());
            }
        }, [booted]);

        GC.KeepAlive(revision);
        GC.KeepAlive(booted);

        var mobile = Device.Mobile;

        var appClass = mobile
            ? "app is-mobile"
            : WindowChrome.IsMaximized ? "app is-maximized" : "app";

        return Provider(EditorContext.Store, Store,
            Div(new() { ClassName = appClass },
                C<Window>(),
                mobile ? MobileShell(Store) : DesktopShell(),
                Show(mobile && Store.Ui.MobileSheet.Length > 0, MobileMenu(Store)),
                Show(Store.Ui.ContextMenu is not null, C<ContextMenu>()),
                Show(Store.Ui.ShowExport, C<ExportDialog>()),
                Show(Store.Ui.ShowBrowser, C<MediaBrowser>()),
                C<ToastHost>()));
    }

    private static Node DesktopShell() =>
        Div(new() { ClassName = "app-main" },
            Div(new() { ClassName = "app-left" },
                Rail(),
                C<MediaLibrary>()),
            Div(new() { ClassName = "app-center" },
                C<PreviewMonitor>(),
                C<Timeline>()),
            Div(new() { ClassName = "app-right" },
                C<Inspector>()));

    /// <summary>
    /// One panel at a time on phones, with a bottom navigation bar. Desktop keeps
    /// the three-dock shell; both share the same components and store.
    /// </summary>
    private static Node MobileShell(EditorStore store)
    {
        var page = store.Ui.MobilePage;

        return F(
            Div(new() { ClassName = "mobile-main" },
                page == "media" ? C<MediaLibrary>() :
                page == "inspector" ? C<Inspector>() :
                Div(new() { ClassName = "mobile-preview-page" },
                    C<PreviewMonitor>(),
                    C<Timeline>())),
            Div(new() { ClassName = "mobile-nav" },
                MobileTab(store, "preview", "fa-solid fa-tv", "Edit", "Edit the timeline"),
                MobileTab(store, "media", "fa-solid fa-photo-film", "Media", "Add media, text, audio and looks"),
                MobileTab(store, "inspector", "fa-solid fa-sliders", "Adjust", "Adjust the selected clip")));
    }

    private static Node MobileTab(EditorStore store, string page, string icon, string label, string tooltip) =>
        Div(new()
        {
            ClassName = store.Ui.MobilePage == page ? "mobile-tab is-active" : "mobile-tab",
            Title = tooltip,
            Attributes = new Dictionary<string, string>
            {
                ["role"] = "tab",
                ["aria-label"] = label,
                ["aria-selected"] = store.Ui.MobilePage == page ? "true" : "false"
            },
            OnClick = () =>
            {
                store.Ui.MobilePage = page;
                store.Notify();
            }
        },
            H("i", new Props { ClassName = icon }),
            Show(store.Ui.SelectedClipId is not null && page == "inspector",
                Span(new() { ClassName = "mobile-tab-dot" })),
            Span(new() { ClassName = "mobile-tab-label" }, Text(label)));

    /// <summary>
    /// Phone sheets for project actions and track creation. They live at the app
    /// root so every row is a large, reliable touch target.
    /// </summary>
    private static Node MobileMenu(EditorStore store)
    {
        var sheet = store.Ui.MobileSheet;
        var addingTrack = sheet == "track";

        void Close()
        {
            store.Ui.MobileSheet = "";
            store.Notify();
        }

        Node Item(string icon, string label, string hint, Action? action) =>
            Div(new()
            {
                ClassName = action is null ? "mobile-sheet-item is-disabled" : "mobile-sheet-item",
                OnClick = action is null
                    ? null
                    : () =>
                    {
                        Close();
                        action();
                    }
            },
                Div(new() { ClassName = "mobile-sheet-icon" },
                    H("i", new Props { ClassName = icon })),
                Div(new() { ClassName = "mobile-sheet-text" },
                    Span(new() { ClassName = "mobile-sheet-label" }, Text(label)),
                    Span(new() { ClassName = "mobile-sheet-hint" }, Text(hint))));

        Node TrackItem(string icon, string label, string hint, TrackKind kind, double height, bool atTop) =>
            Item(icon, label, hint, () =>
            {
                var count = store.Document.Tracks.Count(t => t.Kind == kind) + 1;
                store.AddTrack(kind, $"{label.Replace(" track", "")} {count}", height, atTop);
            });

        var items = new List<Node>();

        if (addingTrack)
        {
            items.Add(TrackItem("fa-solid fa-film", "Video track", "Add above the other lanes", TrackKind.Video, 62, true));
            items.Add(TrackItem("fa-solid fa-layer-group", "Overlay track", "For picture-in-picture layers", TrackKind.Overlay, 52, true));
            items.Add(TrackItem("fa-solid fa-font", "Text track", "For titles and captions", TrackKind.Text, 46, true));
            items.Add(TrackItem("fa-solid fa-music", "Audio track", "Add below the other lanes", TrackKind.Audio, 46, false));
        }
        else
        {
            items.Add(Item("fa-solid fa-floppy-disk", "Save project", "Save to the project folder",
                () => UI.EditorCommands.SaveProject(store)));
            items.Add(Item("fa-solid fa-folder-open", "Open project", "Browse projects on this device", () =>
            {
                store.Ui.BrowserMode = "open";
                store.Ui.ShowBrowser = true;
                store.Notify();
            }));
            items.Add(Div(new() { ClassName = "mobile-sheet-divider" }));
            items.Add(Item("fa-solid fa-rotate-left", "Undo", store.CanUndo ? "Step back one edit" : "Nothing to undo",
                store.CanUndo ? store.Undo : null));
            items.Add(Item("fa-solid fa-rotate-right", "Redo", store.CanRedo ? "Step forward again" : "Nothing to redo",
                store.CanRedo ? store.Redo : null));
            items.Add(Div(new() { ClassName = "mobile-sheet-divider" }));
            items.Add(Item("fa-solid fa-gear", "Project setup", "Canvas, frame rate, storage and media", () =>
            {
                store.Ui.MobilePage = "media";
                store.Ui.ActiveTab = "settings";
                store.Notify();
            }));
        }

        var children = new List<Node>
        {
            Div(new() { ClassName = "mobile-sheet-grab" }),
            Div(new() { ClassName = "mobile-sheet-head" },
                Div(new() { ClassName = "mobile-sheet-head-text" },
                    Span(new() { ClassName = "mobile-sheet-title" },
                        Text(addingTrack ? "Add track" : "Project")),
                    Span(new() { ClassName = "mobile-sheet-sub" },
                        Text(addingTrack ? "Choose the kind of lane to add" : store.Document.Name))),
                Div(new() { ClassName = "spacer" }),
                Button(new()
                {
                    ClassName = "btn is-icon mobile-sheet-close",
                    Title = "Close menu",
                    OnClick = Close
                },
                    H("i", new Props { ClassName = "fa-solid fa-xmark" })))
        };

        children.AddRange(items);

        return Div(new() { ClassName = "mobile-sheet-layer" },
            Div(new() { ClassName = "mobile-sheet-backdrop", OnClick = Close }),
            Div(new() { ClassName = "mobile-sheet" }, children.ToArray()));
    }

    private static string FfmpegAvailableMessage()
    {
        if (!MovieDotNet.Core.Media.Ffmpeg.Available)
        {
            return "ffmpeg was not found — install it to import, thumbnail and render.";
        }

        if (!MovieDotNet.Core.Media.Ffmpeg.ProbeAvailable)
        {
            return "ffprobe was not found — media analysis is limited.";
        }

        return "Ready. Import media or load the demo project.";
    }

    private static Node Rail()
    {
        // Five destinations instead of eight. Transitions, effects and motion
        // live together under Looks, and project setup stays in the title bar.
        var tabs = new (string Id, string Icon, string Label)[]
        {
            ("media", "fa-solid fa-photo-film", "Media"),
            ("text", "fa-solid fa-font", "Text"),
            ("audio", "fa-solid fa-music", "Audio"),
            ("record", "fa-solid fa-video", "Record"),
            ("looks", "fa-solid fa-wand-magic-sparkles", "Looks")
        };

        var current = Store.Ui.ActiveTab;

        return Div(new() { ClassName = "rail" },
            Each(tabs, tab =>
            {
                var active = current == tab.Id
                    || (tab.Id == "looks" && current is "transitions" or "effects" or "animations");

                return Div(new()
                {
                    ClassName = active ? "rail-item is-active" : "rail-item",
                    Title = tab.Label,
                    OnClick = () =>
                    {
                        Store.Ui.ActiveTab = tab.Id;
                        Store.RaiseChanged();
                    }
                },
                    H("i", new Props { ClassName = tab.Icon }),
                    Span(new() { ClassName = "rail-label" }, Text(tab.Label)));
            }));
    }
}
