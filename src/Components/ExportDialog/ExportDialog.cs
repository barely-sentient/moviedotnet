using Donkeh.UILab;
using MovieDotNet.Core;
using MovieDotNet.Core.Media;
using MovieDotNet.UI;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

[assembly: Donkeh.UILab.Stylesheet("src/Components/ExportDialog/ExportDialog.css")]

namespace MovieDotNet.Components;

/// <summary>Render settings, progress and the ffmpeg log.</summary>
public sealed class ExportDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var store = UseContext(EditorContext.Store);
        var document = store.Document;
        var ui = store.Ui;
        var cancellation = UseRef<CancellationTokenSource?>(null);

        var (preset, setPreset) = State("source");
        var (crf, setCrf) = State(20);
        var (fps, setFps) = State(document.Fps);
        var (fileName, setFileName) = State($"{ProjectFiles.Sanitize(document.Name)}");

        var (width, height, label) = preset switch
        {
            "1080p" => (1920, 1080, "1080p"),
            "720p" => (1280, 720, "720p"),
            "480p" => (854, 480, "480p"),
            "vertical" => (1080, 1920, "Vertical 1080"),
            _ => (document.Width, document.Height, $"Source {document.Width}×{document.Height}")
        };

        var settings = new RenderSettings
        {
            Width = width,
            Height = height,
            Fps = fps,
            Crf = crf,
            Preset = "veryfast",
            Background = document.Background
        };

        var outputPath = Path.Combine(ProjectFiles.RendersDirectory, $"{ProjectFiles.Sanitize(fileName)}.mp4");

        return Div(new() { ClassName = "modal-backdrop" },
            Div(new() { ClassName = "modal export-modal" },
                Div(new() { ClassName = "modal-head" },
                    Div(new() { ClassName = "export-title" },
                        H("i", new Props { ClassName = "fa-solid fa-film" }),
                        Text("Render video")),
                    Div(new() { ClassName = "spacer" }),
                    C<IconButton, IconButtonProps>(new IconButtonProps
                    {
                        Icon = "fa-solid fa-xmark",
                        Tooltip = "Close",
                        Size = "small",
                        Disabled = ui.Busy,
                        OnClick = () =>
                        {
                            ui.ShowExport = false;
                            store.Notify();
                        }
                    })),

                Div(new() { ClassName = "modal-body export-body" },
                    Div(new() { ClassName = "export-grid" },
                        Div(new() { ClassName = "field" },
                            Span(new() { ClassName = "field-label" }, Text("File name")),
                            Input(new()
                            {
                                ClassName = "input",
                                Value = fileName,
                                OnInput = value => setFileName(value)
                            })),
                        Div(new() { ClassName = "field" },
                            Span(new() { ClassName = "field-label" }, Text("Resolution")),
                            C<Choice, ChoiceProps>(new ChoiceProps
                            {
                                Value = preset,
                                Options =
                                [
                                    new("source", "Source"),
                                    new("1080p", "1080p"),
                                    new("720p", "720p"),
                                    new("480p", "480p"),
                                    new("vertical", "Vertical")
                                ],
                                OnSelect = setPreset
                            })),
                        Div(new() { ClassName = "field" },
                            Span(new() { ClassName = "field-label" }, Text("Frame rate")),
                            C<Choice, ChoiceProps>(new ChoiceProps
                            {
                                Value = fps.ToString("0"),
                                Options =
                                [
                                    new("24", "24"),
                                    new("25", "25"),
                                    new("30", "30"),
                                    new("50", "50"),
                                    new("60", "60")
                                ],
                                OnSelect = value =>
                                {
                                    if (double.TryParse(value, out var parsed))
                                    {
                                        setFps(parsed);
                                    }
                                }
                            }))),

                    Div(new() { ClassName = "export-grid" },
                        Div(new() { ClassName = "slider-row is-flat" },
                            Div(new() { ClassName = "slider-head" },
                                Span(new() { ClassName = "field-label" }, Text("Quality (CRF)")),
                                Span(new() { ClassName = "value-badge" }, Text(crf.ToString()))),
                            Input(new()
                            {
                                Type = "range",
                                Value = crf.ToString(),
                                Attributes = new Dictionary<string, string>
                                {
                                    ["min"] = "14", ["max"] = "34", ["step"] = "1"
                                },
                                OnInput = value =>
                                {
                                    if (int.TryParse(value, out var parsed))
                                    {
                                        setCrf(parsed);
                                    }
                                }
                            }),
                            Span(new() { ClassName = "export-hint" }, Text("Lower is better quality and a larger file."))),
                        Div(new() { ClassName = "export-summary" },
                            SummaryRow("Canvas", $"{width}×{height}"),
                            SummaryRow("Frame rate", $"{fps:0.##} fps"),
                            SummaryRow("Duration", Format.Time(document.Duration)),
                            SummaryRow("Clips", document.Tracks.Sum(t => t.Clips.Count).ToString()),
                            SummaryRow("Audio", document.Tracks.Where(t => t.Kind == TrackKind.Audio).Sum(t => t.Clips.Count).ToString()))),

                    Div(new() { ClassName = "export-target" },
                        H("i", new Props { ClassName = "fa-solid fa-folder-open" }),
                        Span(new(), Text(outputPath))),

                    Show(ui.Busy, Div(new() { ClassName = "progress-block" },
                        Div(new() { ClassName = "progress-head" },
                            Span(new() { ClassName = "progress-label" }, Text(ui.BusyLabel)),
                            Span(new() { ClassName = "value-badge" }, Text($"{ui.BusyProgress * 100:0}%"))),
                        Div(new() { ClassName = "progress-track" },
                            Div(new()
                            {
                                ClassName = "progress-fill",
                                Attributes = new Dictionary<string, string>
                                {
                                    ["style"] = $"width:{Math.Clamp(ui.BusyProgress, 0, 1) * 100:0.##}%;"
                                }
                            })))),

                    Show(ui.Log is not null, Div(new() { ClassName = "export-log" },
                        Pre(new() { ClassName = "export-log-text" }, Text(Tail(ui.Log ?? "", 1600)))))),

                Div(new() { ClassName = "modal-foot" },
                    Div(new() { ClassName = "export-note" },
                        H("i", new Props { ClassName = Ffmpeg.Available ? "fa-solid fa-circle-check" : "fa-solid fa-triangle-exclamation" }),
                        Span(new(), Text(Ffmpeg.Available ? "Rendering with libx264 + aac" : "ffmpeg not found"))),
                    Div(new() { ClassName = "spacer" }),
                    Show(!ui.Busy, Button(new()
                    {
                        ClassName = "btn",
                        OnClick = () =>
                        {
                            ui.ShowExport = false;
                            store.Notify();
                        }
                    }, Text("Cancel"))),
                    Show(ui.Busy, Button(new()
                    {
                        ClassName = "btn",
                        OnClick = () =>
                        {
                            cancellation.Value?.Cancel();
                        }
                    }, Text("Stop render"))),
                    Button(new()
                    {
                        ClassName = "btn is-accent",
                        Disabled = ui.Busy || !Ffmpeg.Available || document.Tracks.Sum(t => t.Clips.Count) == 0,
                        OnClick = () =>
                        {
                            cancellation.Value = new CancellationTokenSource();
                            EditorCommands.RenderAsync(store, settings, outputPath, cancellation.Value.Token);
                        }
                    },
                        H("i", new Props { ClassName = "fa-solid fa-clapperboard" }),
                        Text("Start render")))));
    }

    private static Node SummaryRow(string label, string value) =>
        Div(new() { ClassName = "summary-row" },
            Span(new() { ClassName = "summary-label" }, Text(label)),
            Span(new() { ClassName = "summary-value" }, Text(value)));

    private static string Tail(string text, int length) =>
        text.Length <= length ? text : text[^length..];
}
