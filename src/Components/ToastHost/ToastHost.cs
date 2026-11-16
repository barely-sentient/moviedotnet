using Donkeh.UILab;
using MovieDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/ToastHost/ToastHost.css")]

namespace MovieDotNet.Components;

/// <summary>Transient status messages and the render progress pill.</summary>
public sealed class ToastHost : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var ui = store.Ui;

        if (ui.Busy)
        {
            return Div(new() { ClassName = "toast-layer" },
                Div(new() { ClassName = "busy-pill" },
                    H("i", new Props { ClassName = "fa-solid fa-circle-notch spin" }),
                    Span(new() { ClassName = "busy-label" }, Text(ui.BusyLabel)),
                    Div(new() { ClassName = "busy-track" },
                        Div(new()
                        {
                            ClassName = "busy-fill",
                            Attributes = new Dictionary<string, string>
                            {
                                ["style"] = $"width:{Math.Clamp(ui.BusyProgress, 0, 1) * 100:0.##}%;"
                            }
                        })),
                    Span(new() { ClassName = "busy-percent" }, Text($"{ui.BusyProgress * 100:0}%"))));
        }

        if (ui.Toast is null)
        {
            return Div(new() { ClassName = "toast-layer" });
        }

        return Div(new() { ClassName = "toast-layer" },
            Div(new() { ClassName = "toast anim-toast-in" },
                H("i", new Props { ClassName = "fa-solid fa-circle-check" }),
                Span(new(), Text(ui.Toast))));
    }
}
