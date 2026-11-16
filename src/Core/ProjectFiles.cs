namespace MovieDotNet.Core;

/// <summary>Well-known folders and helpers for project files and exports.</summary>
/// <remarks>
/// Android does not expose a usable Documents folder, so every location falls
/// back to the app's private files area instead of throwing during start-up.
/// </remarks>
public static class ProjectFiles
{
    public static string ProjectsDirectory { get; } = Ensure(
        SafeDocuments(), "Projects");

    public static string RendersDirectory { get; } = Ensure(
        SafeDocuments(), "Renders");

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

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    public static string ProjectPath(string name) =>
        Path.Combine(ProjectsDirectory, $"{Sanitize(name)}.mdproj");

    public static string RenderPath(string name, string extension = "mp4") =>
        Path.Combine(RendersDirectory, $"{Sanitize(name)}.{extension}");

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
