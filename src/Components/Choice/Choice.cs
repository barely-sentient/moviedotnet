using Donkeh.UILab;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/Choice/Choice.css")]

namespace MovieDotNet.Components;

/// <summary>One entry of a <see cref="Choice"/>.</summary>
public sealed record ChoiceOption(string Value, string Label);

/// <summary>Props for <see cref="Choice"/>.</summary>
public sealed class ChoiceProps
{
    /// <summary>Gets the selectable options in display order.</summary>
    public IReadOnlyList<ChoiceOption> Options { get; init; } = [];

    /// <summary>Gets the currently selected option value.</summary>
    public string Value { get; init; } = "";

    /// <summary>Gets the callback invoked with the chosen option value.</summary>
    public Action<string>? OnSelect { get; init; }

    /// <summary>Use the compact chip layout instead of full-width rows.</summary>
    public bool Chips { get; init; } = true;
}

/// <summary>
/// A host-painted-free alternative to <c>select</c>: a wrapping list of buttons
/// that keeps the editor's palette instead of the platform control colours.
/// </summary>
public sealed class Choice : Component<ChoiceProps>
{
    /// <inheritdoc/>
    public override Node Render()
    {
        // The engine does not always report an intrinsic height for wrapped
        // inline content inside nested flex/grid cells, so reserve it here.
        var rows = Props.Chips
            ? (int)Math.Ceiling(Props.Options.Count / 2d)
            : Props.Options.Count;
        var height = Math.Max(32, rows * 40);

        return Div(new()
        {
            ClassName = Props.Chips ? "choice" : "choice is-list",
            Attributes = new Dictionary<string, string>
            {
                ["style"] = $"min-height:{height}px;"
            }
        },
            Each(Props.Options, option => Div(new()
            {
                ClassName = (option.Value == Props.Value ? "choice-item is-active" : "choice-item") + (Props.Chips ? "" : " is-wide"),
                Key = option.Value,
                Title = option.Label,
                OnClick = () => Props.OnSelect?.Invoke(option.Value)
            },
                Span(new() { ClassName = "choice-label" }, Text(option.Label)))));
    }
}
