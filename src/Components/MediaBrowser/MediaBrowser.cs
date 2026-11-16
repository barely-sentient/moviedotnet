using Donkeh.UILab;
using MovieDotNet.Core;
using MovieDotNet.Core.Media;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/MediaBrowser/MediaBrowser.css")]

namespace MovieDotNet.Components;

/// <summary>
/// An in-app file browser. Desktop hosts expose no native picker, so imports and
/// project opening both go through this listing.
/// </summary>
public sealed class MediaBrowser : Component
{
    private sealed record Entry(string Path, string Name, bool IsFolder, long Size, string Kind);

    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var ui = store.Ui;
        var openMode = ui.BrowserMode == "open";
        var (selected, setSelected) = State<HashSet<string>>(
            () => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        void SelectPath(string selectedPath)
        {
            if (string.IsNullOrEmpty(selectedPath))
            {
                setSelected(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                return;
            }

            if (openMode)
            {
                setSelected(new HashSet<string>([selectedPath], StringComparer.OrdinalIgnoreCase));
                return;
            }

            var next = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

            if (!next.Add(selectedPath))
            {
                next.Remove(selectedPath);
            }

            setSelected(next);
        }

        if (string.IsNullOrWhiteSpace(ui.BrowserPath) || !Directory.Exists(ui.BrowserPath))
        {
            ui.BrowserPath = openMode
                ? ProjectFiles.ProjectsDirectory
                : KnownFolder(Environment.SpecialFolder.MyVideos);
        }

        var path = ui.BrowserPath;
        var entries = Memo(() => List(path, openMode), path, openMode, ui.BrowserRefresh);

        return Div(new() { ClassName = "modal-backdrop" },
            Div(new() { ClassName = "modal browser-modal" },
                Div(new() { ClassName = "modal-head" },
                    Div(new() { ClassName = "browser-title" },
                        H("i", new Props { ClassName = openMode ? "fa-solid fa-folder-open" : "fa-solid fa-photo-film" }),
                        Text(openMode ? "Open project" : "Import media")),
                    Div(new() { ClassName = "spacer" }),
                    C<IconButton, IconButtonProps>(new IconButtonProps
                    {
                        Icon = "fa-solid fa-xmark",
                        Tooltip = "Close",
                        Size = "small",
                        OnClick = () =>
                        {
                            ui.ShowBrowser = false;
                            store.Notify();
                        }
                    })),

                Div(new() { ClassName = "browser-quick" },
                    Each(QuickLocations(openMode), location => Div(new()
                    {
                        ClassName = "quick-pill",
                        OnClick = () =>
                        {
                            ui.BrowserPath = location.Path;
                            setSelected(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                            store.Notify();
                        }
                    },
                        H("i", new Props { ClassName = location.Icon }),
                        Span(new(), Text(location.Label))))),

                Div(new() { ClassName = "browser-path" },
                    C<IconButton, IconButtonProps>(new IconButtonProps
                    {
                        Icon = "fa-solid fa-arrow-up",
                        Tooltip = "Parent folder",
                        Size = "small",
                        OnClick = () =>
                        {
                            var parent = Path.GetDirectoryName(path);

                            if (parent is not null)
                            {
                                ui.BrowserPath = parent;
                                store.Notify();
                            }
                        }
                    }),
                    Div(new() { ClassName = "path-text" }, Text(path)),
                    C<IconButton, IconButtonProps>(new IconButtonProps
                    {
                        Icon = "fa-solid fa-rotate",
                        Tooltip = "Refresh",
                        Size = "small",
                        OnClick = () =>
                        {
                            ui.BrowserRefresh++;
                            store.Notify();
                        }
                    })),

                Div(new() { ClassName = "browser-list scroll" },
                    Show(entries.Count == 0,
                        Div(new() { ClassName = "empty-panel is-compact" },
                            Span(new() { ClassName = "empty-copy" },
                                Text(openMode ? "No saved projects here." : "No media in this folder.")))),
                    Each(entries, entry => Row(store, ui, entry, selected, SelectPath))),

                Div(new() { ClassName = "modal-foot" },
                    Span(new() { ClassName = "browser-selection" },
                        Text(SelectionLabel(selected, openMode))),
                    Div(new() { ClassName = "spacer" }),
                    Button(new()
                    {
                        ClassName = "btn",
                        OnClick = () =>
                        {
                            ui.ShowBrowser = false;
                            store.Notify();
                        }
                    }, Text("Cancel")),
                    Button(new()
                    {
                        ClassName = "btn is-accent",
                        Disabled = selected.Count == 0,
                        OnClick = () =>
                        {
                            if (selected.Count == 0)
                            {
                                return;
                            }

                            ui.ShowBrowser = false;

                            if (openMode)
                            {
                                EditorCommands.OpenProject(store, selected.First());
                            }
                            else
                            {
                                EditorCommands.ImportAsync(store, selected.ToList());
                            }

                            store.Notify();
                        }
                    },
                        H("i", new Props { ClassName = openMode ? "fa-solid fa-folder-open" : "fa-solid fa-plus" }),
                        Text(openMode ? "Open project" : "Import")))));
    }

    private static string SelectionLabel(HashSet<string> selected, bool openMode)
    {
        if (selected.Count == 0)
        {
            return "Nothing selected";
        }

        if (openMode)
        {
            return Path.GetFileName(selected.First());
        }

        return selected.Count == 1
            ? Path.GetFileName(selected.First())
            : $"{selected.Count} files selected";
    }

    private static Node Row(
        EditorStore store,
        EditorUi ui,
        Entry entry,
        HashSet<string> selected,
        Action<string> setSelected)
    {
        if (entry.IsFolder)
        {
            return Div(new()
            {
                ClassName = "browser-row is-folder",
                OnClick = () =>
                {
                    ui.BrowserPath = entry.Path;
                    setSelected(string.Empty);
                    store.Notify();
                }
            },
                Div(new() { ClassName = "browser-row-icon" },
                    H("i", new Props { ClassName = "fa-solid fa-folder" })),
                Span(new() { ClassName = "browser-row-name" }, Text(entry.Name)),
                Span(new() { ClassName = "browser-row-meta" }, Text("folder")),
                H("i", new Props { ClassName = "fa-solid fa-chevron-right browser-chevron" }));
        }

        var isSelected = selected.Contains(entry.Path);

        return Div(new()
        {
            ClassName = isSelected ? "browser-row is-file is-selected" : "browser-row is-file",
            OnClick = () => setSelected(entry.Path)
        },
            Div(new() { ClassName = "browser-row-icon" },
                H("i", new Props { ClassName = IconFor(entry.Kind) })),
            Span(new() { ClassName = "browser-row-name" }, Text(entry.Name)),
            Span(new() { ClassName = "browser-row-meta" },
                Text(entry.Size > 0 ? FormatBytes(entry.Size) : entry.Kind)),
            Show(isSelected, H("i", new Props { ClassName = "fa-solid fa-circle-check browser-check" })));
    }

    private static string IconFor(string kind) => kind switch
    {
        "video" => "fa-solid fa-file-video",
        "audio" => "fa-solid fa-file-audio",
        "image" => "fa-solid fa-file-image",
        "project" => "fa-solid fa-diagram-project",
        _ => "fa-solid fa-file"
    };

    private static List<Entry> List(string path, bool openMode)
    {
        var entries = new List<Entry>();

        try
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                var info = new DirectoryInfo(directory);

                if (info.Attributes.HasFlag(FileAttributes.Hidden))
                {
                    continue;
                }

                entries.Add(new Entry(directory, info.Name, true, 0, "folder"));
            }

            var patterns = openMode ? ["*.mdproj"] : MediaAnalysis.AllExtensions.Select(e => "*" + e).ToArray();

            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.GetFiles(path, pattern))
                {
                    var info = new FileInfo(file);

                    if (info.Attributes.HasFlag(FileAttributes.Hidden))
                    {
                        continue;
                    }

                    var kind = openMode
                        ? "project"
                        : MediaAnalysis.VideoExtensions.Contains(info.Extension.ToLowerInvariant())
                            ? "video"
                            : MediaAnalysis.AudioExtensions.Contains(info.Extension.ToLowerInvariant())
                                ? "audio"
                                : "image";

                    entries.Add(new Entry(file, info.Name, false, info.Length, kind));
                }
            }
        }
        catch
        {
            // Unreadable folder: show what we have.
        }

        return entries
            .OrderByDescending(e => e.IsFolder)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<(string Label, string Icon, string Path)> QuickLocations(bool openMode)
    {
        yield return ("Projects", "fa-solid fa-diagram-project", ProjectFiles.ProjectsDirectory);
        yield return ("Renders", "fa-solid fa-film", ProjectFiles.RendersDirectory);
        yield return ("Videos", "fa-solid fa-video", KnownFolder(Environment.SpecialFolder.MyVideos));
        yield return ("Music", "fa-solid fa-music", KnownFolder(Environment.SpecialFolder.MyMusic));
        yield return ("Pictures", "fa-solid fa-image", KnownFolder(Environment.SpecialFolder.MyPictures));
        yield return ("Downloads", "fa-solid fa-download", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        yield return ("Desktop", "fa-solid fa-desktop", KnownFolder(Environment.SpecialFolder.DesktopDirectory));
    }

    private static string KnownFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return Directory.Exists(path) ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        > 1_073_741_824 => $"{bytes / 1_073_741_824d:0.0} GB",
        > 1_048_576 => $"{bytes / 1_048_576d:0.0} MB",
        > 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };
}
