namespace MovieDotNet.Core;

/// <summary>An open context menu and its location in window CSS pixels.</summary>
public sealed class EditorContextMenu
{
    /// <summary>Gets or sets the heading shown above the items.</summary>
    public string Title { get; init; } = "";

    /// <summary>Gets or sets the pointer X in window CSS pixels.</summary>
    public float X { get; init; }

    /// <summary>Gets or sets the pointer Y in window CSS pixels.</summary>
    public float Y { get; init; }

    /// <summary>Gets the items, in display order.</summary>
    public List<EditorContextMenuItem> Items { get; init; } = [];
}

/// <summary>One command row or divider in a context menu.</summary>
public sealed class EditorContextMenuItem
{
    /// <summary>Gets or sets the visible label.</summary>
    public string Label { get; init; } = "";

    /// <summary>Gets or sets the Font Awesome class list.</summary>
    public string Icon { get; init; } = "";

    /// <summary>Gets or sets the command, or <see langword="null"/> for a disabled row.</summary>
    public Action? Invoke { get; init; }

    /// <summary>Gets or sets whether the command is unavailable.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets or sets whether the row is rendered as a destructive action.</summary>
    public bool Danger { get; init; }

    /// <summary>Gets or sets whether the row is a horizontal divider.</summary>
    public bool IsDivider { get; init; }
}
