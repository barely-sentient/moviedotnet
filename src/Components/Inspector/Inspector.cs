using Donkeh.UILab;
using MovieDotNet.Core;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/Inspector/Inspector.css")]

namespace MovieDotNet.Components;

/// <summary>The right dock: everything about the selected clip or the project.</summary>
public sealed class Inspector : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var clip = store.SelectedClip;

        return Div(new() { ClassName = "inspector" },
            Div(new() { ClassName = "inspector-head" },
                Div(new() { ClassName = "inspector-title" },
                    H("i", new Props { ClassName = clip is null ? "fa-solid fa-diagram-project" : IconFor(clip) }),
                    Span(new(), Text(clip is null ? "Inspector" : ClipKindName(clip)))),
                Div(new() { ClassName = "spacer" }),
                Show(clip is not null, C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-copy",
                    Tooltip = "Duplicate clip",
                    Size = "small",
                    OnClick = store.DuplicateSelected
                })),
                Show(clip is not null, C<IconButton, IconButtonProps>(new IconButtonProps
                {
                    Icon = "fa-solid fa-trash",
                    Tooltip = "Delete clip",
                    Size = "small",
                    OnClick = store.DeleteSelected
                }))),
            Div(new() { ClassName = "inspector-body scroll" },
                clip is null ? Empty(store) : ClipInspector(store, clip)));
    }

    private static string ClipKindName(Clip clip) => clip.Kind switch
    {
        TrackKind.Text => "Text layer",
        TrackKind.Audio => "Audio clip",
        TrackKind.Overlay => "Overlay",
        _ => "Video clip"
    };

    private static string IconFor(Clip clip) => clip.Kind switch
    {
        TrackKind.Text => "fa-solid fa-font",
        TrackKind.Audio => "fa-solid fa-wave-square",
        TrackKind.Overlay => "fa-solid fa-layer-group",
        _ => "fa-solid fa-film"
    };

    private static Node Empty(EditorStore store)
    {
        var document = store.Document;
        var clips = document.Tracks.Sum(t => t.Clips.Count);
        var videoSeconds = document.Tracks.Where(t => t.Kind != TrackKind.Audio).Sum(t => t.Clips.Sum(c => c.Duration));

        return Div(new() { ClassName = "inspector-empty" },
            Div(new() { ClassName = "inspector-empty-head" },
                Div(new() { ClassName = "inspector-empty-icon" },
                    H("i", new Props { ClassName = "fa-solid fa-sliders" })),
                Div(new() { ClassName = "inspector-empty-text" },
                    Span(new() { ClassName = "empty-title" }, Text("Select a clip")),
                    Span(new() { ClassName = "empty-copy" },
                        Text("Click any clip in the timeline to edit its transform, motion, transition and effects.")))),
            Div(new() { ClassName = "stat-grid" },
                Stat("Clips", clips.ToString()),
                Stat("Tracks", document.Tracks.Count.ToString()),
                Stat("Video", Format.Short(videoSeconds)),
                Stat("Duration", Format.Time(document.Duration))),
            Div(new() { ClassName = "panel-note" },
                H("i", new Props { ClassName = "fa-solid fa-circle-info" }),
                Span(new(), Text("Transitions require overlapping clips. Choosing one makes the overlap for you."))));
    }

    private static Node Stat(string label, string value) =>
        Div(new() { ClassName = "stat" },
            Span(new() { ClassName = "stat-value" }, Text(value)),
            Span(new() { ClassName = "stat-label" }, Text(label)));

    private static Node ClipInspector(EditorStore store, Clip clip)
    {
        var asset = store.Document.FindAsset(clip.AssetId);

        Node Slider(string label, double value, double min, double max, double step, string suffix, Action<double> apply) =>
            Div(new() { ClassName = "slider-row" },
                Div(new() { ClassName = "slider-head" },
                    Span(new() { ClassName = "field-label" }, Text(label)),
                    Span(new() { ClassName = "value-badge" }, Text($"{value:0.##}{suffix}"))),
                Input(new()
                {
                    Type = "range",
                    Value = value.ToString("0.####"),
                    Attributes = new Dictionary<string, string>
                    {
                        ["min"] = min.ToString("0.####"),
                        ["max"] = max.ToString("0.####"),
                        ["step"] = step.ToString("0.####")
                    },
                    OnInput = text =>
                    {
                        if (double.TryParse(text, out var parsed))
                        {
                            apply(parsed);
                        }
                    }
                }));

        return F(
            Div(new() { ClassName = "clip-hero" },
                Div(new() { ClassName = "clip-hero-thumb" },
                    Show(asset?.Poster is not null,
                        Img(new() { ClassName = "clip-hero-image", Src = asset?.Poster, Alt = clip.Name })),
                    Show(asset?.Poster is null,
                        H("i", new Props { ClassName = IconFor(clip) }))),
                Div(new() { ClassName = "clip-hero-info" },
                    Span(new() { ClassName = "clip-hero-name" }, Text(clip.Name)),
                    Span(new() { ClassName = "clip-hero-meta" },
                        Text(asset is null
                            ? $"{ClipKindName(clip)} · {Format.Short(clip.Duration)}"
                            : $"{asset.Width}×{asset.Height} · {Format.Short(clip.Duration)}")))),

            Show(clip.Kind == TrackKind.Text, TextSection(store, clip)),

            SectionTitle("Timing"),
            Div(new() { ClassName = "field-grid" },
                NumberField("Start", clip.Start, value => store.EditCoalesced(_ => clip.Start = Math.Max(0, value), "clip-start")),
                NumberField("Length", clip.Duration, value => store.EditCoalesced(_ => clip.Duration = Math.Max(0.15, value), "clip-length")),
                NumberField("In point", clip.InPoint, value => store.EditCoalesced(_ => clip.InPoint = Math.Max(0, value), "clip-in"),
                    wide: clip.Kind == TrackKind.Text),
                Show(clip.Kind != TrackKind.Text,
                    NumberField("Speed", clip.Speed, value => store.EditCoalesced(_ => clip.Speed = Math.Clamp(value, 0.1, 4), "clip-speed")))),

            SectionTitle("Transform"),
            Slider("Position X", clip.Box.X * 100, -20, 110, 1, "%", value => Mutate(store, clip, c => c.Box.X = value / 100, "t-x")),
            Slider("Position Y", clip.Box.Y * 100, -20, 110, 1, "%", value => Mutate(store, clip, c => c.Box.Y = value / 100, "t-y")),
            Slider("Width", clip.Box.Width * 100, 5, 150, 1, "%", value => Mutate(store, clip, c => c.Box.Width = value / 100, "t-w")),
            Slider("Height", clip.Box.Height * 100, 5, 150, 1, "%", value => Mutate(store, clip, c => c.Box.Height = value / 100, "t-h")),
            Slider("Rotation", clip.Rotation, -180, 180, 1, "°", value => Mutate(store, clip, c => c.Rotation = value, "t-r")),

            SectionTitle("Appearance"),
            Slider("Opacity", clip.Opacity * 100, 0, 100, 1, "%", value => Mutate(store, clip, c => c.Opacity = value / 100, "opacity")),
            Show(clip.Kind != TrackKind.Text && clip.Kind != TrackKind.Audio,
                Slider("Audio level", clip.Volume * 100, 0, 200, 1, "%", value => Mutate(store, clip, c => c.Volume = value / 100, "volume"))),

            Show(clip.Kind != TrackKind.Text && clip.Kind != TrackKind.Audio, MotionSection(store, clip)),            Show(clip.Kind != TrackKind.Audio, TransitionSection(store, clip)),
            Show(clip.Effects.Count > 0, EffectSection(store, clip)),
            Div(new() { ClassName = "inspector-actions" },
                Button(new()
                {
                    ClassName = "btn is-small",
                    OnClick = () => store.SplitAtPlayhead()
                },
                    H("i", new Props { ClassName = "fa-solid fa-scissors" }),
                    Text("Split")),
                Button(new()
                {
                    ClassName = "btn is-small",
                    OnClick = store.DuplicateSelected
                },
                    H("i", new Props { ClassName = "fa-solid fa-copy" }),
                    Text("Duplicate")),
                Button(new()
                {
                    ClassName = "btn is-small is-danger",
                    OnClick = store.DeleteSelected
                },
                    H("i", new Props { ClassName = "fa-solid fa-trash" }),
                    Text("Delete"))));
    }

    private static Node SectionTitle(string text) =>
        Div(new() { ClassName = "inspector-section" }, Text(text));

    private static Node NumberField(string label, double value, Action<double> apply, bool wide = false) =>
        Div(new() { ClassName = wide ? "field is-wide" : "field" },
            Span(new() { ClassName = "field-label" }, Text(label)),
            Input(new()
            {
                ClassName = "input",
                Value = value.ToString("0.###"),
                OnChange = text =>
                {
                    if (double.TryParse(text, out var parsed))
                    {
                        apply(parsed);
                    }
                }
            }));

    private static void Mutate(EditorStore store, Clip clip, Action<Clip> change, string label) =>
        store.EditCoalesced(_ => change(clip), label);

    private static Node TextSection(EditorStore store, Clip clip)
    {
        return F(
            SectionTitle("Text"),
            Div(new() { ClassName = "field-stack" },
                Textarea(new()
                {
                    ClassName = "textarea",
                    Value = clip.Text,
                    OnInput = value => store.EditCoalesced(_ => clip.Text = value, "text-body")
                })),
            Div(new() { ClassName = "field-grid" },
                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Size")),
                    Input(new()
                    {
                        ClassName = "input",
                        Value = clip.FontSize.ToString("0"),
                        OnChange = value =>
                        {
                            if (double.TryParse(value, out var size))
                            {
                                store.EditCoalesced(_ => clip.FontSize = Math.Clamp(size, 6, 420), "text-size");
                            }
                        }
                    })),
                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Weight")),
                    C<Choice, ChoiceProps>(new ChoiceProps
                    {
                        Value = clip.FontWeight,
                        Options =
                        [
                            new("400", "Regular"),
                            new("600", "Semi"),
                            new("700", "Bold"),
                            new("900", "Black")
                        ],
                        OnSelect = value => store.EditCoalesced(_ => clip.FontWeight = value, "text-weight")
                    }))),

            Div(new() { ClassName = "field" },
                Span(new() { ClassName = "field-label" }, Text("Colour")),
                Div(new() { ClassName = "color-row" },
                    Each(new[] { "#ffffff", "#0b0d12", "#4f7dff", "#a06bff", "#2fd39b", "#ffae4d", "#ff5f9e" }, color =>
                        Div(new()
                        {
                            ClassName = clip.Color == color ? "swatch is-active" : "swatch",
                            OnClick = () => store.Edit(_ => clip.Color = color, "text-colour"),
                            Attributes = new Dictionary<string, string> { ["style"] = $"background-color:{color}" }
                        })))),

            Div(new() { ClassName = "segmented" },
                Each(new[] { ("left", "fa-solid fa-align-left"), ("center", "fa-solid fa-align-center"), ("right", "fa-solid fa-align-right") },
                    option => Div(new()
                    {
                        ClassName = clip.Align == option.Item1 ? "segment is-active" : "segment",
                        OnClick = () => store.Edit(_ => clip.Align = option.Item1, "text-align")
                    },
                        H("i", new Props { ClassName = option.Item2 })))),

            Div(new() { ClassName = "slider-row" },
                Div(new() { ClassName = "slider-head" },
                    Span(new() { ClassName = "field-label" }, Text("Letter spacing")),
                    Span(new() { ClassName = "value-badge" }, Text($"{clip.LetterSpacing:0.##}"))),
                Input(new()
                {
                    Type = "range",
                    Value = clip.LetterSpacing.ToString("0.##"),
                    Attributes = new Dictionary<string, string>
                    {
                        ["min"] = "-20", ["max"] = "30", ["step"] = "0.5"
                    },
                    OnInput = value =>
                    {
                        if (double.TryParse(value, out var spacing))
                        {
                            store.EditCoalesced(_ => clip.LetterSpacing = spacing, "text-spacing");
                        }
                    }
                })));
    }

    private static Node MotionSection(EditorStore store, Clip clip) =>
        F(
            SectionTitle("Motion"),
            Div(new() { ClassName = "field-stack" },
                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Enter")),
                    C<Choice, ChoiceProps>(new ChoiceProps
                    {
                        Value = clip.EnterAnimation,
                        Options = [.. AnimationRegistry.All.Select(preset => new ChoiceOption(preset.Id, preset.Name))],
                        OnSelect = value => store.Edit(_ => clip.EnterAnimation = value, "motion-in")
                    })),
                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Exit")),
                    C<Choice, ChoiceProps>(new ChoiceProps
                    {
                        Value = clip.ExitAnimation,
                        Options = [.. AnimationRegistry.All.Select(preset => new ChoiceOption(preset.Id, preset.Name))],
                        OnSelect = value => store.Edit(_ => clip.ExitAnimation = value, "motion-out")
                    }))),
            Div(new() { ClassName = "slider-row" },
                Div(new() { ClassName = "slider-head" },
                    Span(new() { ClassName = "field-label" }, Text("Duration")),
                    Span(new() { ClassName = "value-badge" }, Text($"{clip.AnimationDuration:0.00}s"))),
                Input(new()
                {
                    Type = "range",
                    Value = clip.AnimationDuration.ToString("0.##"),
                    Attributes = new Dictionary<string, string>
                    {
                        ["min"] = "0.1", ["max"] = "3", ["step"] = "0.05"
                    },
                    OnInput = value =>
                    {
                        if (double.TryParse(value, out var duration))
                        {
                            store.EditCoalesced(_ => clip.AnimationDuration = duration, "motion-duration");
                        }
                    }
                })));

    private static Node TransitionSection(EditorStore store, Clip clip)
    {
        var definition = TransitionRegistry.Get(clip.TransitionId);

        return F(
            SectionTitle("Transition in"),
            Div(new() { ClassName = "field-stack" },
                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Style")),
                    C<Choice, ChoiceProps>(new ChoiceProps
                    {
                        Value = clip.TransitionId,
                        Options = [.. TransitionRegistry.All.Select(t => new ChoiceOption(t.Id, t.Name))],
                        OnSelect = value => EditorCommands.ApplyTransition(store, value, clip.TransitionDuration)
                    })),
                Div(new() { ClassName = "field" },
                    Span(new() { ClassName = "field-label" }, Text("Length")),
                    Input(new()
                    {
                        ClassName = "input",
                        Value = clip.TransitionDuration.ToString("0.##"),
                        OnChange = value =>
                        {
                            if (double.TryParse(value, out var duration))
                            {
                                store.Edit(_ => clip.TransitionDuration = Math.Clamp(duration, 0.1, 5), "transition-length");
                            }
                        }
                    }))),
            Div(new() { ClassName = "settings-note is-dim" },
                Span(new(), Text($"{definition.Category} · ffmpeg xfade={definition.Xfade}"))));
    }

    private static Node EffectSection(EditorStore store, Clip clip) =>
        F(
            SectionTitle("Effects"),
            Div(new() { ClassName = "effect-stack" },
                Each(clip.Effects, effect =>
                {
                    var definition = EffectRegistry.Get(effect.Id);

                    return Div(new() { ClassName = "effect-stack-row" },
                        Span(new() { ClassName = "effect-name" }, Text(definition?.Name ?? effect.Id)),
                        Input(new()
                        {
                            Type = "range",
                            Value = effect.Amount.ToString("0.##"),
                            Attributes = new Dictionary<string, string>
                            {
                                ["min"] = "0", ["max"] = "1", ["step"] = "0.01"
                            },
                            OnInput = value =>
                            {
                                if (double.TryParse(value, out var amount))
                                {
                                    store.EditCoalesced(_ =>
                                    {
                                        var target = clip.Effects.FirstOrDefault(e => e.Id == effect.Id);

                                        if (target is not null)
                                        {
                                            target.Amount = amount;
                                        }
                                    }, "effect-" + effect.Id);
                                }
                            }
                        }),
                        Div(new()
                        {
                            ClassName = "effect-remove",
                            OnClick = () => store.Edit(_ => clip.Effects.RemoveAll(e => e.Id == effect.Id), "remove effect")
                        },
                            H("i", new Props { ClassName = "fa-solid fa-xmark" })));
                })));
}
