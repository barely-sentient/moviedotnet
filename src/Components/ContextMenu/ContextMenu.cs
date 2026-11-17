using Donkeh.UILab;
using MovieDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/ContextMenu/ContextMenu.css")]

namespace MovieDotNet.Components;

/// <summary>
/// The app-wide context menu. It anchors to the pointer on desktop and becomes a
/// bottom sheet on phones, matching the long-press interaction.
/// </summary>
public sealed class ContextMenu : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var menu = store.Ui.ContextMenu;

        if (menu is null)
        {
            return F();
        }

        void Close()
        {
            store.Ui.ContextMenu = null;
            store.Notify();
        }

        var mobile = Device.Mobile;
        var className = mobile ? "context-menu is-sheet" : "context-menu";

        return Div(new() { ClassName = "context-menu-layer" },
            Div(new() { ClassName = "context-menu-backdrop", OnClick = Close }),
            Div(new()
            {
                ClassName = className,
                Attributes = mobile
                    ? null
                    : new Dictionary<string, string>
                    {
                        ["style"] = $"left:{menu.X:0.##}px;top:{menu.Y:0.##}px;"
                    }
            },
                Build(menu, mobile, Close)));
    }

    private static Node[] Build(EditorContextMenu menu, bool mobile, Action close)
    {
        Node Row(EditorContextMenuItem item)
        {
            if (item.IsDivider)
            {
                return Div(new() { ClassName = "context-menu-divider" });
            }

            var disabled = item.Disabled || item.Invoke is null;
            var rowClass = "context-menu-item";

            if (disabled)
            {
                rowClass += " is-disabled";
            }

            if (item.Danger)
            {
                rowClass += " is-danger";
            }

            return Div(new()
            {
                ClassName = rowClass,
                OnClick = disabled
                    ? null
                    : () =>
                    {
                        close();
                        item.Invoke?.Invoke();
                    }
            },
                H("i", new Props { ClassName = item.Icon }),
                Span(new() { ClassName = "context-menu-label" }, Text(item.Label)));
        }

        var children = new List<Node>();

        if (mobile)
        {
            children.Add(Div(new() { ClassName = "context-menu-grab" }));
        }

        children.Add(Div(new() { ClassName = "context-menu-head" },
            Span(new() { ClassName = "context-menu-title" }, Text(menu.Title)),
            Show(mobile, Div(new()
            {
                ClassName = "context-menu-close",
                Title = "Close menu",
                OnClick = close
            },
                H("i", new Props { ClassName = "fa-solid fa-xmark" })))));

        children.AddRange(menu.Items.Select(Row));
        return children.ToArray();
    }
}
