using Donkeh.UILab;
using MovieDotNet.Core;

namespace MovieDotNet.UI;

/// <summary>
/// Builds the context menus shared by the timeline, media bin and inspector.
/// Each method selects the item under the pointer before opening its menu so the
/// follow-up commands act on what the user actually clicked.
/// </summary>
public static class ContextMenus
{
    /// <summary>Opens the menu for a timeline clip.</summary>
    /// <param name="store">The editor store.</param>
    /// <param name="clip">The clip that was right-clicked or long-pressed.</param>
    /// <param name="e">The host context-menu event.</param>
    public static void ShowClip(EditorStore store, Clip clip, ContextMenuEvent e)
    {
        store.Select(clip.Id);

        var canSplit = store.Ui.Playhead > clip.Start + 0.05
            && store.Ui.Playhead < clip.End - 0.05;
        var items = new List<EditorContextMenuItem>
        {
            Item("fa-solid fa-scissors", "Cut", () => store.CutSelected()),
            Item("fa-solid fa-copy", "Copy", () => store.CopySelected()),
            Item("fa-solid fa-paste", "Paste at playhead", () => store.PasteClip(), !store.CanPaste),
            Divider(),
            Item("fa-solid fa-clone", "Duplicate", () => store.DuplicateSelected()),
            Item("fa-solid fa-scissors", "Split at playhead", () => store.SplitAtPlayhead(selectionOnly: true), !canSplit)
        };

        if (Device.Mobile)
        {
            items.Add(Item("fa-solid fa-sliders", "Adjust clip", () =>
            {
                store.Ui.MobilePage = "inspector";
                store.Notify();
            }));
        }

        items.Add(Divider());
        items.Add(Item("fa-solid fa-trash", "Delete", () => store.DeleteSelected(), danger: true));

        Show(store, clip.Name, e, items);
    }

    /// <summary>Opens the menu for a media-bin asset.</summary>
    /// <param name="store">The editor store.</param>
    /// <param name="asset">The asset that was right-clicked or long-pressed.</param>
    /// <param name="e">The host context-menu event.</param>
    public static void ShowAsset(EditorStore store, MediaAsset asset, ContextMenuEvent e)
    {
        store.Ui.SelectedAssetId = asset.Id;
        var items = new List<EditorContextMenuItem>
        {
            Item("fa-solid fa-plus", "Add to timeline", () =>
            {
                store.AddAssetToTimeline(asset);
                store.Toast($"{asset.Name} added at playhead");
            })
        };

        if (asset.Kind == AssetKind.Video)
        {
            items.Add(Item("fa-solid fa-layer-group", "Add as picture-in-picture", () =>
            {
                store.AddAssetToTimeline(asset, asOverlay: true);
                store.Toast($"{asset.Name} added as a layer");
            }));
        }

        Show(store, asset.Name, e, items);
    }

    /// <summary>Opens the menu for a track header.</summary>
    /// <param name="store">The editor store.</param>
    /// <param name="track">The track that was right-clicked or long-pressed.</param>
    /// <param name="e">The host context-menu event.</param>
    public static void ShowTrack(EditorStore store, Track track, ContextMenuEvent e)
    {
        var items = new List<EditorContextMenuItem>
        {
            Item(
                track.Muted ? "fa-solid fa-volume-high" : "fa-solid fa-volume-xmark",
                track.Muted ? "Unmute track" : "Mute track",
                () => store.Edit(_ => track.Muted = !track.Muted, "Toggle mute")),
            Item(
                track.Hidden ? "fa-solid fa-eye" : "fa-solid fa-eye-slash",
                track.Hidden ? "Show track" : "Hide track",
                () => store.Edit(_ => track.Hidden = !track.Hidden, "Toggle visibility")),
            Item(
                track.Locked ? "fa-solid fa-lock-open" : "fa-solid fa-lock",
                track.Locked ? "Unlock track" : "Lock track",
                () => store.Edit(_ => track.Locked = !track.Locked, "Toggle lock"))
        };

        if (track.Clips.Count > 0)
        {
            items.Add(Divider());
            items.Add(Item("fa-solid fa-trash", "Clear track", () =>
            {
                store.Edit(_ => track.Clips.Clear(), "Clear track");
            }, danger: true));
        }

        Show(store, track.Name, e, items);
    }

    /// <summary>Opens the menu for an empty part of the timeline.</summary>
    /// <param name="store">The editor store.</param>
    /// <param name="e">The host context-menu event.</param>
    public static void ShowTimeline(EditorStore store, ContextMenuEvent e) =>
        Show(store, "Timeline", e,
        [
            Item("fa-solid fa-paste", "Paste at playhead", () => store.PasteClip(), !store.CanPaste),
            Item("fa-solid fa-scissors", "Split at playhead", () => store.SplitAtPlayhead()),
            Divider(),
            Item("fa-solid fa-font", "Add a title", () => store.AddTextClip("Your text")),
            Item("fa-solid fa-arrows-left-right-to-line", "Fit timeline", () =>
            {
                store.Ui.TimelineFit = true;
                store.Notify();
            })
        ]);

    /// <summary>Opens the menu for the program monitor.</summary>
    /// <param name="store">The editor store.</param>
    /// <param name="e">The host context-menu event.</param>
    public static void ShowPreview(EditorStore store, ContextMenuEvent e) =>
        Show(store, "Preview", e,
        [
            Item("fa-solid fa-paste", "Paste at playhead", () => store.PasteClip(), !store.CanPaste),
            Item("fa-solid fa-font", "Add a title", () => store.AddTextClip("Your text")),
            Divider(),
            Item(store.Ui.Playing ? "fa-solid fa-pause" : "fa-solid fa-play",
                store.Ui.Playing ? "Pause" : "Play",
                store.TogglePlay)
        ]);

    private static void Show(
        EditorStore store,
        string title,
        ContextMenuEvent e,
        List<EditorContextMenuItem> items)
    {
        var x = e.WindowX;
        var y = e.WindowY;

        // Keep the desktop popover inside the window. The phone menu is a sheet.
        // The host does not expose its viewport to app code, so the menu flips to
        // the other side of the pointer when it sits in the lower or right half.
        if (!Device.Mobile)
        {
            const float menuWidth = 236;
            var menuHeight = 52f + items.Sum(item => item.IsDivider ? 11f : 32f);

            if (x > 700)
            {
                x -= menuWidth;
            }

            if (y > 520)
            {
                y -= menuHeight;
            }

            x = Math.Max(8, x);
            y = Math.Max(8, y);
        }

        store.Ui.ContextMenu = new EditorContextMenu
        {
            Title = title,
            X = x,
            Y = y,
            Items = items
        };
        store.Notify();
    }

    private static EditorContextMenuItem Item(
        string icon,
        string label,
        Action invoke,
        bool disabled = false,
        bool danger = false) => new()
        {
            Icon = icon,
            Label = label,
            Invoke = invoke,
            Disabled = disabled,
            Danger = danger
        };

    private static EditorContextMenuItem Divider() => new() { IsDivider = true };
}
