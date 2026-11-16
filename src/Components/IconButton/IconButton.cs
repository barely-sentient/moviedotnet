using Donkeh.UILab;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/IconButton/IconButton.css")]

namespace MovieDotNet.Components;

/// <summary>Props for <see cref="IconButton"/>.</summary>
public sealed class IconButtonProps : Props
{
    /// <summary>Font Awesome class list, for example <c>fa-solid fa-play</c>.</summary>
    public string Icon { get; init; } = "fa-solid fa-circle";

    public string? Label { get; init; }
    public string? Tooltip { get; init; }
    public bool Active { get; init; }
    public string Size { get; init; } = "normal";
}

/// <summary>A square icon button used by the title bar, transports and timeline.</summary>
public sealed class IconButton : Component<IconButtonProps>
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var className = Props.Size == "small" ? "btn is-icon is-small" : "btn is-icon";

        if (Props.Active)
        {
            className += " is-active";
        }

        return Button(new()
        {
            ClassName = className,
            Title = Props.Tooltip,
            Disabled = Props.Disabled,
            OnClick = Props.OnClick,
            Attributes = new Dictionary<string, string>
            {
                ["aria-label"] = Props.Tooltip ?? Props.Label ?? "button"
            }
        },
            H("i", new Props { ClassName = Props.Icon }),
            Show(Props.Label is not null, Span(new() { ClassName = "btn-label" }, Text(Props.Label ?? ""))));
    }
}
