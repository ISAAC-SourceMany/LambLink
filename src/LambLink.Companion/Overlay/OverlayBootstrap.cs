using System.Text;

namespace LambLink.Companion.Overlay;

/// <summary>A local OBS entry point that can run before the HTTP server exists.</summary>
public static class OverlayBootstrap
{
    public static string EnsureFile(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.GetFullPath(Path.Combine(dataDirectory, "LambLink-Overlay.html"));
        using var stream = typeof(OverlayBootstrap).Assembly.GetManifestResourceStream(
            "LambLink.OverlayBootstrap.html") ?? throw new InvalidOperationException("Overlay bootstrap resource missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();
        if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != html)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, html, new UTF8Encoding(false));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return path;
    }
}
