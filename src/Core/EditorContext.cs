using Donkeh.UILab;

namespace MovieDotNet.Core;

/// <summary>Provides the single editor store to every component in the tree.</summary>
public static class EditorContext
{
    public static readonly Context<EditorStore> Store = new(EditorStore.CreateNew());
}
