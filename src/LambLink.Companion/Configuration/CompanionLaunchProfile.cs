using System.Text.Json;

namespace LambLink.Companion.Configuration;

internal sealed class CompanionLaunchProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Release { get; set; } = "";
    public string Environment { get; set; } = "production";
    public string? ApiBaseUrl { get; set; }
    public string? FrontendUrl { get; set; }
    public string? DataDirectory { get; set; }

    public bool IsStaging => Environment.Equals("staging", StringComparison.OrdinalIgnoreCase);
}

internal static class CompanionLaunchProfileLoader
{
    public const string FileName = "companion-launch-profile.json";

    public static CompanionLaunchProfile? Load(string executableDirectory, string expectedRelease)
    {
        var path = Path.Combine(executableDirectory, FileName);
        if (!File.Exists(path)) return null;

        var profile = JsonSerializer.Deserialize<CompanionLaunchProfile>(
                          File.ReadAllText(path),
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? throw new InvalidDataException($"Companion launch profile is empty: {path}");

        if (profile.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported Companion launch profile schema: {profile.SchemaVersion}");
        if (!string.Equals(profile.Release, expectedRelease, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Companion launch profile release mismatch: expected={expectedRelease}, actual={profile.Release}");

        profile.Environment = profile.Environment.Trim().ToLowerInvariant();
        if (profile.Environment is not ("production" or "staging"))
            throw new InvalidDataException($"Unsupported Companion environment: {profile.Environment}");

        if (profile.IsStaging)
        {
            ValidateHttps(profile.ApiBaseUrl, "apiBaseUrl");
            ValidateHttps(profile.FrontendUrl, "frontendUrl");
            if (string.IsNullOrWhiteSpace(profile.DataDirectory))
                throw new InvalidDataException("Staging Companion launch profile is missing dataDirectory.");
        }

        return profile;
    }

    private static void ValidateHttps(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Staging Companion launch profile requires an HTTPS {name}.");
        }
    }
}
