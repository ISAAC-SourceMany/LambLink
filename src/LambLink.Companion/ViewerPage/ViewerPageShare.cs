using System.Diagnostics;
using System.Text;

namespace LambLink.Companion.ViewerPage;

/// <summary>
/// Owns the streamer-facing viewer-page URL presentation and Windows sharing helpers.
/// The URL becomes authoritative only after CHZZK authentication resolves the streamer's
/// channel ID, so this cannot be created by the installer ahead of the first login.
/// </summary>
public sealed class ViewerPageShare
{
    private const string DefaultDesktopShortcutName = "CHZZK 시청자 외형 설정 페이지.url";
    private readonly string _dataDirectory;
    private readonly string _desktopShortcutName;

    public ViewerPageShare(string dataDirectory, string? desktopShortcutName = null)
    {
        _dataDirectory = dataDirectory;
        _desktopShortcutName = string.IsNullOrWhiteSpace(desktopShortcutName)
            ? DefaultDesktopShortcutName
            : desktopShortcutName.Trim();
    }

    public string? Url { get; private set; }
    public string? UrlTextPath { get; private set; }
    public string? LocalShortcutPath { get; private set; }
    public string? DesktopShortcutPath { get; private set; }
    public IReadOnlyList<string> LastWarnings { get; private set; } = Array.Empty<string>();

    public void Configure(string frontendUrl, string streamerChannelId)
    {
        var warnings = new List<string>();
        Url = BuildUrl(frontendUrl, streamerChannelId);
        UrlTextPath = Path.Combine(_dataDirectory, "viewer-page-url.txt");
        LocalShortcutPath = Path.Combine(_dataDirectory, "viewer-page.url");
        DesktopShortcutPath = null;

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            WriteAllTextAtomic(UrlTextPath, Url + Environment.NewLine);
        }
        catch (Exception ex)
        {
            warnings.Add($"주소 파일 저장 실패: {ex.Message}");
        }

        try
        {
            WriteInternetShortcut(LocalShortcutPath, Url);
        }
        catch (Exception ex)
        {
            warnings.Add($"로컬 바로가기 저장 실패: {ex.Message}");
        }

        if (OperatingSystem.IsWindows())
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                warnings.Add("바탕화면 위치를 확인하지 못해 바로가기를 만들지 않았습니다.");
            }
            else
            {
                try
                {
                    var desktopShortcut = Path.Combine(desktop, _desktopShortcutName);
                    WriteInternetShortcut(desktopShortcut, Url);
                    DesktopShortcutPath = desktopShortcut;
                }
                catch (Exception ex)
                {
                    warnings.Add($"바탕화면 바로가기 저장 실패: {ex.Message}");
                }
            }
        }

        LastWarnings = warnings;
    }

    public void PrintBanner(TextWriter output)
    {
        output.WriteLine("============================================================");
        output.WriteLine("[VIEWER PAGE] 시청자 외형 설정 페이지");
        if (string.IsNullOrWhiteSpace(Url))
        {
            output.WriteLine("CHZZK 로그인과 클라우드 연결이 완료되면 주소가 표시됩니다.");
        }
        else
        {
            output.WriteLine(Url);
            output.WriteLine("시청자에게 위 주소를 공유하세요.");
            output.WriteLine("viewer copy : 주소 복사 | viewer open : 브라우저로 열기");
            if (!string.IsNullOrWhiteSpace(DesktopShortcutPath))
                output.WriteLine($"바탕화면 바로가기: {DesktopShortcutPath}");
        }
        output.WriteLine("============================================================");
    }

    public bool TryCopyToClipboard(out string message)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            message = "시청자 페이지 주소가 아직 준비되지 않았습니다.";
            return false;
        }
        if (!OperatingSystem.IsWindows())
        {
            message = "주소 복사는 Windows 배포판에서만 지원됩니다.";
            return false;
        }

        try
        {
            var clipPath = Path.Combine(Environment.SystemDirectory, "clip.exe");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = clipPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                message = "Windows 클립보드 도구를 시작하지 못했습니다.";
                return false;
            }

            process.StandardInput.Write(Url);
            process.StandardInput.Close();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(true); } catch { }
                message = "클립보드 복사 시간이 초과되었습니다.";
                return false;
            }
            if (process.ExitCode != 0)
            {
                message = $"클립보드 복사에 실패했습니다. exitCode={process.ExitCode}";
                return false;
            }

            message = "시청자 페이지 주소를 클립보드에 복사했습니다.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"클립보드 복사에 실패했습니다: {ex.Message}";
            return false;
        }
    }

    public bool TryOpen(out string message)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            message = "시청자 페이지 주소가 아직 준비되지 않았습니다.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
            message = "기본 브라우저에서 시청자 페이지를 열었습니다.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"브라우저를 열지 못했습니다: {ex.Message}";
            return false;
        }
    }

    internal static string BuildUrl(string frontendUrl, string streamerChannelId)
    {
        if (!Uri.TryCreate(frontendUrl?.Trim(), UriKind.Absolute, out var frontend)
            || (frontend.Scheme != Uri.UriSchemeHttps && frontend.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Viewer frontend URL must be an absolute HTTP(S) URL.", nameof(frontendUrl));
        if (string.IsNullOrWhiteSpace(streamerChannelId))
            throw new ArgumentException("Streamer channel ID is required.", nameof(streamerChannelId));

        var root = frontend.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return $"{root}/?streamer={Uri.EscapeDataString(streamerChannelId.Trim())}";
    }

    private static void WriteInternetShortcut(string path, string url)
    {
        var iconPath = Environment.ProcessPath;
        var content = new StringBuilder()
            .AppendLine("[InternetShortcut]")
            .Append("URL=").AppendLine(url);
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            content.Append("IconFile=").AppendLine(iconPath)
                .AppendLine("IconIndex=0");
        }
        WriteAllTextAtomic(path, content.ToString());
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
