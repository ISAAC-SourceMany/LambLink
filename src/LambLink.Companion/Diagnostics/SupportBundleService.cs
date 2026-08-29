using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LambLink.Companion.Diagnostics;

internal sealed class SupportBundleService(string dataDirectory, string releaseVersion, string companionLogPath)
{
    private const long MaximumCopiedLogBytes = 10L * 1024 * 1024;

    public SupportBundleResult Create(SupportBundleSnapshot snapshot)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var reportId = Guid.NewGuid().ToString("N")[..8];
        var workRoot = Path.Combine(dataDirectory, "support-temp", reportId);
        var logRoot = Path.Combine(workRoot, "logs");
        Directory.CreateDirectory(logRoot);

        try
        {
            var receiptPath = Path.Combine(dataDirectory, "install-receipt.json");
            var gameRoot = TryReadReceiptValue(receiptPath, "gameRoot");
            var companionRoot = TryReadReceiptValue(receiptPath, "companionRoot");
            var sourceSummary = new List<object>();

            for (var index = 0; index <= 4; index++)
            {
                var source = index == 0 ? companionLogPath : companionLogPath + "." + index;
                if (!File.Exists(source)) continue;
                var targetName = index == 0 ? "companion-current.log" : $"companion-archive-{index}.log";
                CopyRedactedLog(source, Path.Combine(logRoot, targetName));
                sourceSummary.Add(DescribeSource("companion", source));
            }

            var installerLog = Path.Combine(dataDirectory, "installer.log");
            if (File.Exists(installerLog))
            {
                CopyRedactedLog(installerLog, Path.Combine(logRoot, "installer.log"));
                sourceSummary.Add(DescribeSource("installer", installerLog));
            }

            string? bepinExLog = null;
            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                bepinExLog = Path.Combine(gameRoot, "BepInEx", "LogOutput.log");
                if (File.Exists(bepinExLog))
                {
                    CopyRedactedLog(bepinExLog, Path.Combine(logRoot, "bepinex-logoutput.log"));
                    sourceSummary.Add(DescribeSource("bepinex", bepinExLog));
                }
            }

            var hashes = CollectRuntimeHashes(gameRoot, companionRoot);
            var report = new
            {
                schemaVersion = 1,
                reportId,
                createdAtUtc = createdAt,
                releaseVersion,
                privacy = new
                {
                    automaticUpload = false,
                    redaction = "best-effort",
                    excluded = new[]
                    {
                        "OAuth access/refresh tokens",
                        "settings.json",
                        "viewer-followers.json",
                        "viewer-appearances.json",
                        "donation outbox and Mod receipt contents",
                        "game save data"
                    }
                },
                runtime = snapshot,
                environment = new
                {
                    os = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    framework = RuntimeInformation.FrameworkDescription,
                    process64Bit = Environment.Is64BitProcess
                },
                install = new
                {
                    receiptPresent = File.Exists(receiptPath),
                    gameRootKnown = !string.IsNullOrWhiteSpace(gameRoot),
                    companionRootKnown = !string.IsNullOrWhiteSpace(companionRoot),
                    bepinExLogFound = bepinExLog is not null && File.Exists(bepinExLog)
                },
                logSources = sourceSummary,
                runtimeHashes = hashes
            };

            WriteUtf8(Path.Combine(workRoot, "support-report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            WriteUtf8(Path.Combine(workRoot, "README.txt"),
                "LambLink 지원 로그 묶음\r\n" +
                $"Report ID: {reportId}\r\n" +
                $"Created UTC: {createdAt:O}\r\n\r\n" +
                "이 파일은 로컬에서만 생성되며 자동 업로드되지 않습니다.\r\n" +
                "닉네임, 채널 ID, 메시지, 사용자 경로 및 토큰은 가능한 범위에서 제거됩니다.\r\n" +
                "전달하기 전에 압축 내부를 직접 확인하세요. 게임 세이브와 시청자 매핑 파일은 포함되지 않습니다.\r\n");

            var destinationDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                destinationDirectory = Path.Combine(dataDirectory, "SupportBundles");
            Directory.CreateDirectory(destinationDirectory);
            var zipPath = Path.Combine(destinationDirectory,
                $"LambLink-Support-{createdAt:yyyyMMdd-HHmmss}Z-{reportId}.zip");
            ZipFile.CreateFromDirectory(workRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return new SupportBundleResult(reportId, zipPath, new FileInfo(zipPath).Length);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workRoot)) Directory.Delete(workRoot, recursive: true);
            }
            catch
            {
                // The completed support ZIP is still valid. A transient antivirus lock on the
                // redacted temporary directory must not turn successful bundle creation into failure.
            }
        }
    }

    private static object DescribeSource(string kind, string path)
    {
        var info = new FileInfo(path);
        return new { kind, bytes = info.Length, lastWriteUtc = info.LastWriteTimeUtc };
    }

    private static string? TryReadReceiptValue(string receiptPath, string propertyName)
    {
        try
        {
            if (!File.Exists(receiptPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(receiptPath));
            return doc.RootElement.TryGetProperty(propertyName, out var node) ? node.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<object> CollectRuntimeHashes(string? gameRoot, string? companionRoot)
    {
        var candidates = new List<(string Label, string Path)>();
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            var plugins = Path.Combine(gameRoot, "BepInEx", "plugins");
            candidates.Add(("LambLink.Mod.dll", Path.Combine(plugins, "LambLink", "LambLink.Mod.dll")));
            candidates.Add(("LambLink.Protocol.dll", Path.Combine(plugins, "LambLink", "LambLink.Protocol.dll")));
            candidates.Add(("Legacy Mod.dll", Path.Combine(plugins, "ChzzkOfTheLamb", "ChzzkOfTheLamb.Mod.dll")));
            candidates.Add(("Legacy Protocol.dll", Path.Combine(plugins, "ChzzkOfTheLamb", "ChzzkOfTheLamb.Protocol.dll")));
            candidates.Add(("COTL_API.dll", Path.Combine(plugins, "COTL_API", "COTL_API.dll")));
            candidates.Add(("COTL_KoreanFontFix.dll", Path.Combine(plugins, "COTL_KoreanFontFix", "COTL_KoreanFontFix.dll")));
        }
        if (!string.IsNullOrWhiteSpace(companionRoot))
        {
            candidates.Add(("LambLink.Companion.exe", Path.Combine(companionRoot, "LambLink.Companion.exe")));
            candidates.Add(("Legacy Companion.exe", Path.Combine(companionRoot, "ChzzkOfTheLamb.Companion.exe")));
        }

        var results = new List<object>();
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path)) continue;
            try
            {
                using var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                results.Add(new
                {
                    file = candidate.Label,
                    bytes = stream.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
                });
            }
            catch (Exception ex)
            {
                results.Add(new { file = candidate.Label, error = ex.GetType().Name });
            }
        }
        return results;
    }

    private static void CopyRedactedLog(string sourcePath, string destinationPath)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > MaximumCopiedLogBytes)
            input.Seek(-MaximumCopiedLogBytes, SeekOrigin.End);
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (input.Position > 0) reader.ReadLine();
        using var writer = new StreamWriter(destinationPath, append: false, encoding: new UTF8Encoding(false));
        while (reader.ReadLine() is { } line)
            writer.WriteLine(DiagnosticPrivacy.Redact(line));
    }

    private static void WriteUtf8(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(false));
}

internal sealed record SupportBundleSnapshot(
    DateTimeOffset CapturedAtUtc,
    bool GameSocketConnected,
    bool GameReady,
    string GameSyncPhase,
    string SaveState,
    bool ChzzkLiveMode,
    bool CloudConnected,
    int CatalogCount,
    string CatalogSaveState,
    bool RaffleOpen,
    int RaffleParticipants,
    int PendingDonations);

internal sealed record SupportBundleResult(string ReportId, string ZipPath, long Bytes);
