using Donkeh.UILab;

namespace MovieDotNet.Core;

/// <summary>Provides the single editor store to every component in the tree.</summary>
public static class EditorContext
{
    /// <summary>Gets the context key used to provide the editor store to the tree.</summary>
    public static readonly Context<EditorStore> Store = new(EditorStore.CreateNew());
}
