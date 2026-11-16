using Donkeh.UILab;
using Donkeh.Windowing;
using MovieDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/Window/Window.css")]

namespace MovieDotNet.Components;

/// <summary>
/// The frameless app window chrome. The native frame is hidden by
/// <c>AppRoot(Frameless = true)</c>; this component supplies the title bar and
/// the minimise, maximise/restore and close controls.
/// </summary>
public sealed class Window : Component
{
    /// <inheritdoc/>
    public override Node Render() =>
        Div(new() { ClassName = "app-window" },
            C<TitleBar>());
}

/// <summary>Window buttons for the custom title bar of a frameless window.</summary>
public sealed class WindowControls : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);

        Node Control(string icon, string tooltip, Action action, bool close = false) =>
            Button(new()
            {
                ClassName = close ? "window-control is-close" : "window-control",
                Title = tooltip,
                OnClick = action
            },
                H("i", new Props { ClassName = icon }));

        return Div(new() { ClassName = "window-controls" },
            Control("fa-solid fa-window-minimize", "Minimize", WindowChrome.Minimize),
            Control(
                WindowChrome.IsMaximized ? "fa-regular fa-clone" : "fa-regular fa-square",
                WindowChrome.IsMaximized ? "Restore" : "Maximize",
                () =>
                {
                    WindowChrome.ToggleMaximize();
                    store.Notify();
                }),
            Control("fa-solid fa-xmark", "Close", WindowChrome.Close, close: true));
    }
}
