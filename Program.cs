using Donkeh.Hosting;
using Donkeh.UILab;
using MovieDotNet.Components;
using MovieDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Styles/theme.css")]

namespace MovieDotNet;

[AppRoot(Width = 1600, Height = 980, Title = "MovieDotNet — Video Editor")]
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

            Store.Toast(FfmpegAvailableMessage());
        }, [booted]);

        GC.KeepAlive(revision);
        GC.KeepAlive(booted);

        var mobile = Device.Mobile;

        return Provider(EditorContext.Store, Store,
            Div(new() { ClassName = mobile ? "app is-mobile" : "app" },
                C<TitleBar>(),
                mobile ? MobileShell(Store) : DesktopShell(),
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
                MobileTab(store, "preview", "fa-solid fa-tv", "Edit"),
                MobileTab(store, "media", "fa-solid fa-photo-film", "Media"),
                MobileTab(store, "inspector", "fa-solid fa-sliders", "Look")));
    }

    private static Node MobileTab(EditorStore store, string page, string icon, string label) =>
        Div(new()
        {
            ClassName = store.Ui.MobilePage == page ? "mobile-tab is-active" : "mobile-tab",
            OnClick = () =>
            {
                store.Ui.MobilePage = page;
                store.Notify();
            }
        },
            H("i", new Props { ClassName = icon }),
            Span(new() { ClassName = "mobile-tab-label" }, Text(label)));

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
