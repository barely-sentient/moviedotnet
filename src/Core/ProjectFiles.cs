namespace MovieDotNet.Core;

/// <summary>Well-known folders and helpers for project files and exports.</summary>
/// <remarks>
/// Android does not expose a usable Documents folder, so every location falls
/// back to the app's private files area instead of throwing during start-up.
/// </remarks>
public static class ProjectFiles
{
    /// <summary>Gets the folder where saved <c>.mdproj</c> files live.</summary>
    public static string ProjectsDirectory { get; } = Ensure(
        SafeDocuments(), "Projects");

    /// <summary>Gets the folder where renders and frame snapshots are written.</summary>
    public static string RendersDirectory { get; } = Ensure(
        SafeDocuments(), "Renders");

    /// <summary>Gets the folder where screen, window, camera and microphone captures are written.</summary>
    public static string RecordingsDirectory { get; } = Ensure(
        SafeDocuments(), "Recordings");

    private static string SafeDocuments()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (!string.IsNullOrWhiteSpace(documents))
            {
                return documents;
            }
        }
        catch
        {
            // Platform without a documents folder.
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local
            ? local
            : Path.GetTempPath();
    }

    private static string Ensure(string root, string folder)
    {
        var path = Path.Combine(root, "MovieDotNet", folder);

        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            var fallback = Path.Combine(Path.GetTempPath(), "MovieDotNet", folder);
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    /// <summary>Makes a display name safe to use as a file name.</summary>
    /// <param name="name">The project, media or output name.</param>
    /// <returns>A non-empty file-name fragment.</returns>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    /// <summary>Builds a project path for a display name.</summary>
    /// <param name="name">The project name.</param>
    /// <returns>An absolute <c>.mdproj</c> path in <see cref="ProjectsDirectory"/>.</returns>
    public static string ProjectPath(string name) =>
        Path.Combine(ProjectsDirectory, $"{Sanitize(name)}.mdproj");

    /// <summary>Builds a render path for a display name.</summary>
    /// <param name="name">The output name.</param>
    /// <param name="extension">The file extension without the leading dot.</param>
    /// <returns>An absolute output path in <see cref="RendersDirectory"/>.</returns>
    public static string RenderPath(string name, string extension = "mp4") =>
        Path.Combine(RendersDirectory, $"{Sanitize(name)}.{extension}");

    /// <summary>Lists saved projects, newest first.</summary>
    /// <returns>Absolute project paths; an empty list when the folder is unavailable.</returns>
    public static IReadOnlyList<string> ListProjects()
    {
        try
        {
            return Directory.GetFiles(ProjectsDirectory, "*.mdproj")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
