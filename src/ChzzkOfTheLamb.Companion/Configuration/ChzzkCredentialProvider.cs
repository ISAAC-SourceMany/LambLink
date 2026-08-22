using System.Diagnostics;
using System.Text.Json;

namespace ChzzkOfTheLamb.Companion.Configuration;

public sealed record ChzzkCredentials(string ClientId, string ClientSecret, string ProviderName);

public static class ChzzkCredentialProvider
{
    public static ChzzkCredentials? Load()
    {
        var provider = (Environment.GetEnvironmentVariable("COTL_COMPANION_CONFIG_PROVIDER") ?? "environment")
            .Trim().ToLowerInvariant();

        return provider switch
        {
            "environment" or "env" or "local" => FromEnvironment(),
            "aws" or "aws-cli" => FromAwsCli(),
            _ => throw new InvalidOperationException($"Unsupported COTL_COMPANION_CONFIG_PROVIDER: {provider}")
        };
    }

    private static ChzzkCredentials? FromEnvironment()
    {
        var id = Environment.GetEnvironmentVariable("CHZZK_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("CHZZK_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return null;
        return new ChzzkCredentials(id.Trim(), secret.Trim(), "environment");
    }

    // Local-development provider. Uses the caller's AWS CLI + SSO profile.
    // Production distributions must NOT ship the CHZZK client secret to the Companion.
    private static ChzzkCredentials FromAwsCli()
    {
        var profile = Environment.GetEnvironmentVariable("AWS_PROFILE") ?? "cotl-dev";
        var region = Environment.GetEnvironmentVariable("AWS_REGION")
                     ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                     ?? "ap-northeast-2";
        var idParameter = Environment.GetEnvironmentVariable("COTL_COMPANION_CLIENT_ID_PARAMETER")
                          ?? "/cotl/prod/chzzk/companion/client-id";
        var secretId = Environment.GetEnvironmentVariable("COTL_COMPANION_SECRET_ID")
                       ?? "/cotl/prod/chzzk/companion";

        var clientId = RunAws($"ssm get-parameter --name {Quote(idParameter)} --region {Quote(region)} --profile {Quote(profile)} --query Parameter.Value --output text").Trim();
        var secretRaw = RunAws($"secretsmanager get-secret-value --secret-id {Quote(secretId)} --region {Quote(region)} --profile {Quote(profile)} --query SecretString --output text").Trim();
        var clientSecret = ParseClientSecret(secretRaw);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("AWS configuration returned an empty CHZZK credential.");

        return new ChzzkCredentials(clientId, clientSecret, $"aws-cli(profile={profile}, region={region})");
    }

    private static string ParseClientSecret(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return trimmed;
        using var doc = JsonDocument.Parse(trimmed);
        if (doc.RootElement.TryGetProperty("clientSecret", out var value)) return value.GetString() ?? string.Empty;
        if (doc.RootElement.TryGetProperty("client_secret", out value)) return value.GetString() ?? string.Empty;
        throw new InvalidOperationException("Secrets Manager JSON must contain 'clientSecret'.");
    }

    private static string RunAws(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "aws",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start AWS CLI.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var hint = stderr.Contains("expired", StringComparison.OrdinalIgnoreCase)
                ? " AWS SSO may be expired; run 'aws sso login --profile cotl-dev'."
                : string.Empty;
            throw new InvalidOperationException($"AWS CLI configuration lookup failed: {stderr.Trim()}{hint}");
        }
        return stdout;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
