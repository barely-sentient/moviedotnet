using System.Globalization;

namespace MovieDotNet.Core;

/// <summary>
/// A transition between two adjacent clips. <see cref="Xfade"/> is the ffmpeg
/// <c>xfade</c> transition name; <see cref="Motion"/> drives the live preview.
/// Register new entries with <see cref="TransitionRegistry.Register"/> to extend
/// the editor without touching the UI.
/// </summary>
public sealed record TransitionDefinition(
    string Id,
    string Name,
    string Category,
    string Xfade,
    string Motion = "fade");

public static class TransitionRegistry
{
    private static readonly List<TransitionDefinition> Items =
    [
        new("none", "Cut", "Basic", "fade", "cut"),
        new("dissolve", "Cross Dissolve", "Basic", "dissolve", "fade"),
        new("fade", "Fade", "Basic", "fade", "fade"),
        new("fadeblack", "Dip to Black", "Basic", "fadeblack", "fadeblack"),
        new("fadewhite", "Dip to White", "Basic", "fadewhite", "fadewhite"),

        new("wipeleft", "Wipe Left", "Wipe", "wipeleft", "wipeleft"),
        new("wiperight", "Wipe Right", "Wipe", "wiperight", "wiperight"),
        new("wipeup", "Wipe Up", "Wipe", "wipeup", "wipeup"),
        new("wipedown", "Wipe Down", "Wipe", "wipedown", "wipedown"),
        new("wipetl", "Wipe Corner", "Wipe", "wipetl", "wipeleft"),
        new("wipetr", "Wipe Corner TR", "Wipe", "wipetr", "wiperight"),

        new("slideleft", "Slide Left", "Slide", "slideleft", "slideleft"),
        new("slideright", "Slide Right", "Slide", "slideright", "slideright"),
        new("slideup", "Slide Up", "Slide", "slideup", "slideup"),
        new("slidedown", "Slide Down", "Slide", "slidedown", "slidedown"),
        new("smoothleft", "Smooth Left", "Slide", "smoothleft", "slideleft"),
        new("smoothright", "Smooth Right", "Slide", "smoothright", "slideright"),

        new("circleopen", "Circle Open", "Shape", "circleopen", "circleopen"),
        new("circleclose", "Circle Close", "Shape", "circleclose", "circleclose"),
        new("radial", "Radial", "Shape", "radial", "circleopen"),
        new("rectcrop", "Box Crop", "Shape", "rectcrop", "circleclose"),
        new("circlecrop", "Circle Crop", "Shape", "circlecrop", "circleopen"),

        new("zoomin", "Zoom In", "Motion", "zoomin", "zoomin"),
        new("hblur", "Blur", "Motion", "hblur", "blur"),
        new("squeezeh", "Squeeze H", "Motion", "squeezeh", "squeezeh"),
        new("squeezev", "Squeeze V", "Motion", "squeezev", "squeezev"),
        new("pixelize", "Pixelize", "Motion", "pixelize", "pixelize"),

        new("diagtl", "Diagonal TL", "Stylised", "diagtl", "wipetl"),
        new("diagtr", "Diagonal TR", "Stylised", "diagtr", "wipeleft"),
        new("diagbl", "Diagonal BL", "Stylised", "diagbl", "wipedown"),
        new("diagbr", "Diagonal BR", "Stylised", "diagbr", "wiperight"),
        new("dissolve_noise", "Noise Dissolve", "Stylised", "dissolve", "blur"),
        new("glitch", "Glitch", "Stylised", "hblur", "glitch"),
        new("float", "Float", "Stylised", "fade", "float"),
        new("swap", "Swap", "Stylised", "slideup", "slideup")
    ];

    public static IReadOnlyList<TransitionDefinition> All => Items;

    public static IReadOnlyList<string> Categories => Items.Select(i => i.Category).Distinct().ToList();

    public static TransitionDefinition Get(string? id) =>
        Items.FirstOrDefault(t => t.Id == id) ?? Items[0];

    public static void Register(TransitionDefinition definition)
    {
        if (Items.Any(i => i.Id == definition.Id))
        {
            return;
        }

        Items.Add(definition);
    }

    public static bool IsTransition(string? id) => id is not null && id != "none";
}

/// <summary>A colour, blur or grading effect that can be stacked on a clip.</summary>
public sealed record EffectDefinition(
    string Id,
    string Name,
    Func<double, string> CssFilter,
    Func<double, string?> FfmpegFilter);

public static class EffectRegistry
{
    private static readonly List<EffectDefinition> Items =
    [
        new("brightness", "Brightness",
            amount => $"brightness({Fmt(0.35 + amount)})",
            amount => $"eq=brightness={Fmt((amount - 0.5) * 0.7)}"),
        new("contrast", "Contrast",
            amount => $"contrast({Fmt(0.4 + amount * 1.8)})",
            amount => $"eq=contrast={Fmt(0.4 + amount * 1.8)}"),
        new("saturation", "Saturation",
            amount => $"saturate({Fmt(amount * 2.2)})",
            amount => $"eq=saturation={Fmt(amount * 2.2)}"),
        new("blur", "Blur",
            amount => $"blur({Fmt(amount * 14)}px)",
            amount => $"gblur=sigma={Fmt(amount * 14)}"),
        new("grayscale", "Monochrome",
            amount => $"grayscale({Fmt(amount)})",
            amount => $"hue=s={Fmt(1 - amount)}"),
        new("sepia", "Sepia",
            amount => $"sepia({Fmt(amount)})",
            amount => amount > 0.05
                ? "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131"
                : null),
        new("vignette", "Vignette",
            amount => $"brightness({Fmt(1 - amount * 0.05)})",
            amount => $"vignette=angle={Fmt(Math.PI / (4 + amount * 25))}")
    ];

    public static IReadOnlyList<EffectDefinition> All => Items;

    public static EffectDefinition? Get(string? id) => Items.FirstOrDefault(e => e.Id == id);

    public static void Register(EffectDefinition definition)
    {
        if (Items.Any(i => i.Id == definition.Id))
        {
            return;
        }

        Items.Add(definition);
    }

    internal static string Fmt(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Named enter/exit animations shared by the preview and the exporter.</summary>
public sealed record AnimationPreset(string Id, string Name, string Category)
{
    public bool IsNone => Id is "none" or "";
}

public static class AnimationRegistry
{
    private static readonly List<AnimationPreset> Items =
    [
        new("none", "None", "Basic"),
        new("fade", "Fade", "Basic"),
        new("slide-up", "Slide Up", "Slide"),
        new("slide-down", "Slide Down", "Slide"),
        new("slide-left", "Slide Left", "Slide"),
        new("slide-right", "Slide Right", "Slide"),
        new("scale", "Scale", "Motion"),
        new("zoom-in", "Zoom In", "Motion"),
        new("zoom-out", "Zoom Out", "Motion"),
        new("bounce", "Bounce", "Motion"),
        new("float", "Float", "Motion"),
        new("spin", "Spin", "Motion"),
        new("blur", "Blur", "Stylised"),
        new("wipe", "Wipe", "Stylised")
    ];

    public static IReadOnlyList<AnimationPreset> All => Items;

    public static AnimationPreset Get(string? id) =>
        Items.FirstOrDefault(a => a.Id == id) ?? Items[0];
}

/// <summary>The transform an animation applies at a given moment.</summary>
public readonly record struct MotionState(
    double Opacity,
    double TranslateX,
    double TranslateY,
    double Scale,
    double Blur,
    double Rotate,
    double ClipPercent)
{
    public static readonly MotionState Identity = new(1, 0, 0, 1, 0, 0, 100);

    public string Css => string.Concat(
        "opacity:", Motion.Fmt(Opacity), ";",
        "transform:translate(", Motion.Fmt(TranslateX), "%,", Motion.Fmt(TranslateY), "%) scale(",
        Motion.Fmt(Scale), ") rotate(", Motion.Fmt(Rotate), "deg);",
        Blur > 0.01 ? $"filter:blur({Motion.Fmt(Blur)}px);" : "filter:none;",
        ClipPercent < 99.99 ? $"clip-path:inset(0 {Motion.Fmt(100 - ClipPercent)}% 0 0);" : "");
}

public static class Motion
{
    internal static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    public static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    /// <summary>Combines the enter animation, exit animation and effects for preview.</summary>
    public static MotionState For(Clip clip, double clipTime)
    {
        var duration = Math.Max(0.05, clip.Duration);
        var anim = Math.Max(0.05, clip.AnimationDuration);
        var opacity = clip.Opacity;
        double x = 0, y = 0, scale = 1, blur = 0, rotate = 0, clipPercent = 100;

        var enterProgress = Math.Clamp(clipTime / anim, 0, 1);
        var exitProgress = Math.Clamp((duration - clipTime) / anim, 0, 1);

        Apply(clip.EnterAnimation, EaseOut(enterProgress), ref opacity, ref x, ref y, ref scale, ref blur, ref rotate, ref clipPercent);
        Apply(clip.ExitAnimation, EaseOut(exitProgress), ref opacity, ref x, ref y, ref scale, ref blur, ref rotate, ref clipPercent);

        return new MotionState(opacity, x, y, scale, blur, rotate, clipPercent);
    }

    private static void Apply(
        string preset,
        double eased,
        ref double opacity,
        ref double x,
        ref double y,
        ref double scale,
        ref double blur,
        ref double rotate,
        ref double clipPercent)
    {
        if (eased >= 0.9999)
        {
            return;
        }

        var inverse = 1 - eased;

        switch (preset)
        {
            case "fade":
                opacity *= eased;
                break;
            case "slide-up":
                y += inverse * 26;
                opacity *= eased;
                break;
            case "slide-down":
                y -= inverse * 26;
                opacity *= eased;
                break;
            case "slide-left":
                x += inverse * 30;
                opacity *= eased;
                break;
            case "slide-right":
                x -= inverse * 30;
                opacity *= eased;
                break;
            case "scale":
                scale *= 0.82 + eased * 0.18;
                opacity *= eased;
                break;
            case "zoom-in":
                scale *= 1 + inverse * 0.3;
                opacity *= eased;
                break;
            case "zoom-out":
                scale *= 1 - inverse * 0.22;
                opacity *= eased;
                break;
            case "bounce":
                opacity *= Math.Min(1, eased * 1.6);
                scale *= 1 + Math.Sin(eased * Math.PI) * 0.09 - inverse * 0.12;
                break;
            case "float":
                opacity *= eased;
                y += Math.Sin(eased * Math.PI) * 4 - inverse * 6;
                break;
            case "spin":
                opacity *= eased;
                rotate += inverse * 22;
                scale *= 0.9 + eased * 0.1;
                break;
            case "blur":
                opacity *= eased;
                blur += inverse * 16;
                break;
            case "wipe":
                clipPercent = Math.Min(clipPercent, eased * 100);
                break;
        }
    }

    /// <summary>
    /// Builds the overlay-position expression for a clip's motion so the exported
    /// file matches the preview for the fades and slides.
    /// </summary>
    public static string OverlayExpression(Clip clip, string axis, int canvasWidth, int canvasHeight)
    {
        var basePosition = axis == "x"
            ? (int)Math.Round(clip.Box.X * canvasWidth)
            : (int)Math.Round(clip.Box.Y * canvasHeight);
        var start = clip.Start;
        var end = clip.End;
        var anim = Math.Max(0.05, clip.AnimationDuration);
        var offset = axis == "x"
            ? (int)Math.Round(0.3 * canvasWidth)
            : (int)Math.Round(0.26 * canvasHeight);

        int Direction(string id) => id switch
        {
            "slide-up" or "slide-down" or "slide-left" or "slide-right" => 1,
            _ => 0
        };

        var enter = Direction(clip.EnterAnimation);
        var exit = Direction(clip.ExitAnimation);

        if (enter == 0 && exit == 0)
        {
            return basePosition.ToString(CultureInfo.InvariantCulture);
        }

        var enterOffset = clip.EnterAnimation switch
        {
            "slide-up" => offset,
            "slide-down" => -offset,
            "slide-left" => offset,
            "slide-right" => -offset,
            _ => 0
        };

        var exitOffset = clip.ExitAnimation switch
        {
            "slide-up" => -offset,
            "slide-down" => offset,
            "slide-left" => -offset,
            "slide-right" => offset,
            _ => 0
        };

        var value = $"({basePosition})";

        if (enterOffset != 0)
        {
            value = $"if(lt(t,{Float(start + anim)}),({basePosition})+({enterOffset})*pow(1-clip((t-{Float(start)})/{Float(anim)},0,1),3),{value})";
        }

        if (exitOffset != 0)
        {
            value = $"if(gt(t,{Float(end - anim)}),({basePosition})+({exitOffset})*pow(clip((t-{Float(end - anim)})/{Float(anim)},0,1),3),{value})";
        }

        return value;
    }

    public static string AlphaExpression(Clip clip, double fps)
    {
        var start = clip.Start;
        var end = clip.End;
        var anim = Math.Max(0.05, clip.AnimationDuration);
        var opacity = Math.Clamp(clip.Opacity, 0, 1);
        var expression = $"{Float(opacity)}";

        if (clip.EnterAnimation is "fade" or "slide-up" or "slide-down" or "slide-left" or "slide-right"
            or "scale" or "zoom-in" or "zoom-out" or "bounce" or "float" or "spin" or "blur")
        {
            expression = $"if(lt(t,{Float(start + anim)}),({Float(opacity)})*clip((t-{Float(start)})/{Float(anim)},0,1),{expression})";
        }

        if (clip.ExitAnimation is "fade" or "slide-up" or "slide-down" or "slide-left" or "slide-right"
            or "scale" or "zoom-in" or "zoom-out" or "bounce" or "float" or "spin" or "blur")
        {
            expression = $"if(gt(t,{Float(end - anim)}),({Float(opacity)})*clip(({Float(end)}-t)/{Float(anim)},0,1),{expression})";
        }

        return expression;
    }

    public static string Float(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
