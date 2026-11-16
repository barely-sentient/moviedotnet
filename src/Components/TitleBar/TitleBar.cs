using Donkeh.Hosting;
using Donkeh.UILab;
using MovieDotNet.Core;
using MovieDotNet.Core.Media;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/TitleBar/TitleBar.css")]

namespace MovieDotNet.Components;

/// <summary>The top strip: identity, project settings, history and the render CTA.</summary>
public sealed class TitleBar : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var document = store.Document;

        if (Device.Mobile)
        {
            return MobileBar(store);
        }

        return Div(new() { ClassName = "app-titlebar" },
            Div(new() { ClassName = "brand" },
                Div(new() { ClassName = "brand-mark" },
                    H("i", new Props { ClassName = "fa-solid fa-clapperboard" })),
                Div(new() { ClassName = "brand-text" },
                    Span(new() { ClassName = "brand-name" }, Text("MovieDotNet")),
                    Span(new() { ClassName = "brand-sub" }, Text("Timeline Studio")))),

            Div(new() { ClassName = "divider" }),

            Div(new() { ClassName = "project-block" },
                Input(new()
                {
                    ClassName = "project-name",
                    Value = document.Name,
                    Title = "Project name",
                    OnInput = value =>
                    {
                        store.Document.Name = value;
                        store.Notify();
                    }
                }),
                Span(new() { ClassName = "project-meta" },
                    Text($"{document.Width}×{document.Height}"),
                    Span(new() { ClassName = "project-dot" }, Text("·")),
                    Text($"{document.Fps:0.##} fps"),
                    Span(new() { ClassName = "project-dot" }, Text("·")),
                    Text(FormatDuration(document.Duration)))),

            Div(new() { ClassName = "spacer" }),

            Div(new() { ClassName = "title-actions" },
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-rotate-left",
                    Tooltip = "Undo",
                    Disabled = !store.CanUndo,
                    OnClick = store.Undo
                }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-rotate-right",
                    Tooltip = "Redo",
                    Disabled = !store.CanRedo,
                    OnClick = store.Redo
                }),
                Div(new() { ClassName = "divider" }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-folder-open",
                    Tooltip = "Open project",
                    OnClick = () =>
                    {
                        store.Ui.BrowserMode = "open";
                        store.Ui.ShowBrowser = true;
                        store.Notify();
                    }
                }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-floppy-disk",
                    Tooltip = "Save project",
                    OnClick = () => EditorCommands.SaveProject(store)
                }),
                Div(new() { ClassName = "divider" }),
                Div(new() { ClassName = "ffmpeg-chip", Title = Ffmpeg.Status },
                    Span(new() { ClassName = Ffmpeg.Available ? "dot" : "dot is-bad" }),
                    Span(new(), Text(Ffmpeg.Available ? "ffmpeg ready" : "ffmpeg missing"))),
                Div(new() { ClassName = "divider" }),
                C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-gear",
                    Tooltip = "Project settings",
                    Active = store.Ui.SelectedClipId is null && store.Ui.ActiveTab == "settings",
                    OnClick = () =>
                    {
                        store.Ui.ActiveTab = store.Ui.ActiveTab == "settings" ? "media" : "settings";
                        store.Notify();
                    }
                }),
                Button(new()
                {
                    ClassName = "btn is-accent render-button",
                    OnClick = () =>
                    {
                        store.Ui.ShowExport = true;
                        store.Notify();
                    }
                },
                    H("i", new Props { ClassName = "fa-solid fa-film" }),
                    Text("Render"))));
    }

    private static Node MobileBar(EditorStore store) =>
        Div(new() { ClassName = "app-titlebar is-mobile" },
            Div(new() { ClassName = "brand-mark" },
                H("i", new Props { ClassName = "fa-solid fa-clapperboard" })),
            Input(new()
            {
                ClassName = "project-name mobile-name",
                Value = store.Document.Name,
                Title = "Project name",
                OnInput = value =>
                {
                    store.Document.Name = value;
                    store.Notify();
                }
            }),
            Div(new() { ClassName = "spacer" }),
            C<IconButton, IconButtonProps>(new IconButtonProps
            {
                Icon = "fa-solid fa-floppy-disk",
                Tooltip = "Save project",
                OnClick = () => EditorCommands.SaveProject(store)
            }),
            Button(new()
            {
                ClassName = "btn is-accent render-button mobile-render",
                OnClick = () =>
                {
                    store.Ui.ShowExport = true;
                    store.Notify();
                }
            },
                H("i", new Props { ClassName = "fa-solid fa-film" }),
                Text("Render")));

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    }
}
