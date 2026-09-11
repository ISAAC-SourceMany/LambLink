using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace LambLink.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class InstallerForm : Form
{
    private const string ReleaseVersion = "1.0.3";
    private const string DefaultManifestUrl = "https://d1gvw9ccym1qvn.cloudfront.net/releases/installer-manifest-1.0.3.json";
    private readonly TextBox _gamePath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button _browse = new() { Text = "찾아보기", AutoSize = true };
    private readonly Button _install = new() { Text = "설치", AutoSize = true };
    private readonly Button _launch = new() { Text = "Companion 실행", AutoSize = true, Enabled = false };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "설치 준비 중..." };
    private readonly Label _environmentBanner = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        Text = "환경: 설치 매니페스트 확인 전",
        Padding = new Padding(8),
        TextAlign = ContentAlignment.MiddleCenter
    };
    private readonly TextBox _logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly CancellationTokenSource _cts = new();
    private string? _companionExe;

    public InstallerForm()
    {
        Text = "LambLink Setup 1.0.3";
        Width = 720;
        Height = 500;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 8, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label { Text = "LambLink", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true };
        var subtitle = new Label { Text = "Cult of the Lamb 치지직 연동 · 필수 구성요소 자동 설치", AutoSize = true };
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(_gamePath, 0, 0);
        pathRow.Controls.Add(_browse, 1, 0);
        var components = new Label { Text = "설치 구성: BepInEx 5.4.21 · COTL_API 0.3.4 · Korean Font Fix 4.2.1 · LambLink Mod · Companion", AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_launch);
        buttons.Controls.Add(_install);

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(_environmentBanner);
        root.Controls.Add(pathRow);
        root.Controls.Add(components);
        root.Controls.Add(_progress);
        root.Controls.Add(_logBox);
        root.Controls.Add(buttons);
        Controls.Add(root);

        _browse.Click += (_, _) => BrowseGame();
        _install.Click += async (_, _) => await InstallAsync();
        _launch.Click += (_, _) => LaunchCompanion();
        Shown += (_, _) => DetectGame();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void DetectGame()
    {
        var detected = SteamLocator.FindCultOfTheLamb();
        if (detected != null)
        {
            _gamePath.Text = detected;
            Log($"[DETECT] Cult of the Lamb: {detected}");
            _status.Text = "게임 설치 위치를 찾았습니다.";
        }
        else
        {
            Log("[DETECT] Steam library에서 Cult of the Lamb을 찾지 못했습니다.");
            _status.Text = "게임 설치 위치를 찾지 못했습니다. 찾아보기를 눌러주세요.";
        }
    }

    private void BrowseGame()
    {
        using var dialog = new FolderBrowserDialog { Description = "Cult of the Lamb 설치 폴더를 선택하세요." };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (!SteamLocator.IsGameFolder(dialog.SelectedPath))
            {
                MessageBox.Show(this, "Cult Of The Lamb.exe를 찾지 못했습니다.", "잘못된 폴더", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _gamePath.Text = dialog.SelectedPath;
            Log($"[DETECT] 수동 선택: {dialog.SelectedPath}");
        }
    }

    private async Task InstallAsync()
    {
        if (!SteamLocator.IsGameFolder(_gamePath.Text))
        {
            MessageBox.Show(this, "Cult of the Lamb 설치 폴더를 먼저 선택해주세요.", "설치 위치 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _install.Enabled = false;
        _browse.Enabled = false;
        _launch.Enabled = false;
        _progress.Value = 0;

        var workDir = Path.Combine(Path.GetTempPath(), "LambLink-Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            Log($"[START] version={ReleaseVersion}, game={_gamePath.Text}");
            _status.Text = "설치 정보를 확인하는 중...";
            var manifestUrl = Environment.GetEnvironmentVariable("COTL_INSTALLER_MANIFEST_URL") ?? DefaultManifestUrl;
            var manifest = await DownloadManifestAsync(manifestUrl, _cts.Token);
            ApplyEnvironmentPresentation(manifest);
            Log($"[MANIFEST] release={manifest.Release}, environment={manifest.Environment}, api={manifest.ApiBaseUrl}, frontend={manifest.FrontendUrl}, components={manifest.Components.Count}, source={manifestUrl}");

            var gameRoot = _gamePath.Text;
            var companionProgramName = manifest.IsStaging ? "LambLink-Staging" : "LambLink";
            var legacyCompanionProgramName = manifest.IsStaging ? "ChzzkOfTheLamb-Staging" : "ChzzkOfTheLamb";
            var companionRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", companionProgramName);
            var legacyCompanionRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", legacyCompanionProgramName);
            Directory.CreateDirectory(companionRoot);

            int index = 0;
            foreach (var component in manifest.Components)
            {
                _cts.Token.ThrowIfCancellationRequested();
                index++;
                int baseProgress = (int)((index - 1) * 100.0 / manifest.Components.Count);
                int endProgress = (int)(index * 100.0 / manifest.Components.Count);
                SetProgress(baseProgress);
                _status.Text = $"{component.DisplayName} 다운로드 중...";
                Log($"[DOWNLOAD] {component.Id} {component.Version} <- {component.Url}");

                var archive = Path.Combine(workDir, component.Id + ".zip");
                await DownloadFileAsync(component.Url, archive, _cts.Token);
                var actualHash = Sha256(archive);
                if (!actualHash.Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"{component.DisplayName} SHA-256 불일치. expected={component.Sha256}, actual={actualHash}");
                }
                Log($"[VERIFY] {component.Id} SHA-256 OK ({actualHash})");

                _status.Text = $"{component.DisplayName} 압축 해제 중... 창을 닫지 마세요.";
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 25;
                var extracted = Path.Combine(workDir, component.Id);
                var extractTimer = Stopwatch.StartNew();
                Log($"[EXTRACT] {component.Id} started");

                // ZIP extraction and the recursive file copy are intentionally kept off the WinForms
                // UI thread. Self-contained Companion contains many runtime files and antivirus/Defender
                // can make this take minutes; the installer must remain responsive while that happens.
                await Task.Run(() =>
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    ZipFile.ExtractToDirectory(archive, extracted, true);
                }, _cts.Token);
                extractTimer.Stop();
                Log($"[EXTRACT] {component.Id} complete elapsed={extractTimer.Elapsed.TotalSeconds:0.0}s");

                _status.Text = $"{component.DisplayName} 설치 중...";
                var installTimer = Stopwatch.StartNew();
                Log($"[INSTALL] {component.Id} started mode={component.InstallMode}");
                await Task.Run(() =>
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    InstallComponent(component, extracted, gameRoot, companionRoot, legacyCompanionRoot);
                }, _cts.Token);
                installTimer.Stop();
                Log($"[INSTALL] {component.Id} complete mode={component.InstallMode}, elapsed={installTimer.Elapsed.TotalSeconds:0.0}s");
                _progress.Style = ProgressBarStyle.Blocks;
                SetProgress(endProgress);
            }

            _companionExe = Path.Combine(companionRoot, "LambLink.Companion.exe");
            WriteCompanionLaunchProfile(manifest, companionRoot);
            CreateShortcuts(_companionExe, manifest.IsStaging);
            WriteInstallReceipt(manifest, gameRoot, companionRoot);
            _status.Text = manifest.IsStaging ? "스테이징 테스트 설치가 완료되었습니다." : "정식 운영 설치가 완료되었습니다.";
            SetProgress(100);
            _launch.Enabled = File.Exists(_companionExe);
            Log($"[COMPLETE] installation successful environment={manifest.Environment}, companionRoot={companionRoot}");
            var completionMessage = manifest.IsStaging
                ? "[STAGING TEST] 설치가 완료되었습니다.\n\n'LambLink Companion (STAGING TEST)' 바로가기로 실행하세요.\n운영 데이터와 시청자 웹은 사용하지 않습니다."
                : "[PRODUCTION] 정식 운영 설치가 완료되었습니다.\nCompanion을 실행한 뒤 CHZZK 로그인을 진행하세요.";
            MessageBox.Show(this, completionMessage, manifest.IsStaging ? "스테이징 테스트 설치 완료" : "설치 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            Log("[CANCEL] installation cancelled");
            _status.Text = "설치가 취소되었습니다.";
        }
        catch (Exception ex)
        {
            Log($"[ERROR] {ex}");
            _status.Text = "설치 중 오류가 발생했습니다.";
            MessageBox.Show(this, ex.Message + "\n\n자세한 로그: " + InstallerLog.FilePath, "설치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Style = ProgressBarStyle.Blocks;
            try { Directory.Delete(workDir, true); } catch { }
            _install.Enabled = true;
            _browse.Enabled = true;
        }
    }

    private static async Task<InstallerManifest> DownloadManifestAsync(string url, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LambLink-Installer/1.0.3");
        var json = await http.GetStringAsync(url, token);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new InvalidDataException("installer manifest를 읽을 수 없습니다.");
        if (!string.Equals(manifest.Release, ReleaseVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"이 설치기는 {ReleaseVersion} 전용입니다. manifest release={manifest.Release}");
        manifest.Environment = string.IsNullOrWhiteSpace(manifest.Environment)
            ? "production"
            : manifest.Environment.Trim().ToLowerInvariant();
        if (manifest.Environment is not ("production" or "staging"))
            throw new InvalidDataException($"지원하지 않는 설치 환경입니다: {manifest.Environment}");
        if (manifest.IsStaging)
        {
            ValidateHttpsManifestValue(manifest.ApiBaseUrl, "apiBaseUrl");
            ValidateHttpsManifestValue(manifest.FrontendUrl, "frontendUrl");
        }
        if (manifest.Components.Count == 0) throw new InvalidDataException("installer manifest에 구성요소가 없습니다.");
        foreach (var c in manifest.Components)
        {
            if (string.IsNullOrWhiteSpace(c.Url) || string.IsNullOrWhiteSpace(c.Sha256) || c.Sha256.Length != 64)
                throw new InvalidDataException($"manifest 구성요소 {c.Id}의 URL/SHA-256이 올바르지 않습니다.");
        }
        return manifest;
    }

    private static void ValidateHttpsManifestValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"스테이징 매니페스트의 {name}은 HTTPS 주소여야 합니다.");
        }
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LambLink-Installer/1.0.3");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(path);
        await input.CopyToAsync(output, token);
    }

    private void EnsureCompanionStopped(string companionRoot, string legacyCompanionRoot)
    {
        var targetExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(companionRoot, "LambLink.Companion.exe")),
            Path.GetFullPath(Path.Combine(legacyCompanionRoot, "ChzzkOfTheLamb.Companion.exe"))
        };
        var currentPid = Environment.ProcessId;
        var matches = new List<Process>();
        var otherEnvironmentCount = 0;
        var companionProcesses = Process.GetProcessesByName("LambLink.Companion")
            .Concat(Process.GetProcessesByName("ChzzkOfTheLamb.Companion"));
        foreach (var process in companionProcesses.Where(p => p.Id != currentPid))
        {
            try
            {
                var processPath = process.MainModule?.FileName;
                if (processPath is not null && targetExecutables.Contains(Path.GetFullPath(processPath)))
                    matches.Add(process);
                else
                {
                    otherEnvironmentCount++;
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }
        if (otherEnvironmentCount > 0)
            Log($"[PROCESS] left {otherEnvironmentCount} Companion process(es) from the other environment running");

        if (matches.Count == 0)
        {
            Log("[PROCESS] no running Companion detected");
            return;
        }

        Log($"[PROCESS] running Companion detected count={matches.Count}; stopping before update");
        foreach (var process in matches)
        {
            try
            {
                string? processPath = null;
                try { processPath = process.MainModule?.FileName; } catch { }
                Log($"[PROCESS] stopping pid={process.Id}, path={processPath ?? "unknown"}");

                bool exited = false;
                try
                {
                    if (process.CloseMainWindow())
                        exited = process.WaitForExit(3000);
                }
                catch { }

                if (!exited && !process.HasExited)
                {
                    process.Kill(true);
                    exited = process.WaitForExit(5000);
                    Log($"[PROCESS] force-stopped pid={process.Id}, exited={exited}");
                }
                else
                {
                    Log($"[PROCESS] stopped pid={process.Id}");
                }
            }
            catch (Exception ex)
            {
                Log($"[PROCESS][WARN] failed to stop Companion pid={process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        // Give Windows/AV scanners a brief moment to release loaded assemblies.
        Thread.Sleep(500);

        foreach (var targetExe in targetExecutables.Where(File.Exists))
        {
            try
            {
                using var probe = new FileStream(targetExe, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Log("[PROCESS] Companion files unlocked");
            }
            catch (IOException ex)
            {
                throw new IOException("실행 중인 LambLink Companion을 종료하지 못했습니다. 작업 관리자에서 Companion을 종료한 뒤 다시 설치해주세요.", ex);
            }
        }
    }

    private void InstallComponent(
        InstallerComponent c,
        string extracted,
        string gameRoot,
        string companionRoot,
        string legacyCompanionRoot)
    {
        switch (c.InstallMode.ToLowerInvariant())
        {
            case "game-root":
                CopyDirectory(extracted, gameRoot);
                if (c.Id.Equals("lamblink-mod", StringComparison.OrdinalIgnoreCase))
                {
                    VerifyCopiedDirectory(extracted, gameRoot);
                    RemoveLegacyModDirectory(gameRoot);
                }
                break;
            case "game-root-autostrip":
                CopyDirectory(StripSingleWrapperDirectory(extracted), gameRoot);
                break;
            case "cotl-api":
                InstallCotlApi(extracted, gameRoot);
                break;
            case "companion":
                EnsureCompanionStopped(companionRoot, legacyCompanionRoot);
                if (Directory.Exists(companionRoot))
                {
                    foreach (var f in Directory.EnumerateFiles(companionRoot))
                    {
                        try { File.Delete(f); } catch { }
                    }
                    foreach (var d in Directory.EnumerateDirectories(companionRoot))
                    {
                        try { Directory.Delete(d, true); } catch { }
                    }
                }
                CopyDirectory(StripSingleWrapperDirectory(extracted), companionRoot);
                RemoveLegacyProgramDirectory(legacyCompanionRoot, companionRoot);
                break;
            default:
                throw new InvalidDataException($"지원하지 않는 installMode: {c.InstallMode}");
        }
    }

    private static void InstallCotlApi(string extracted, string gameRoot)
    {
        var bepin = Directory.EnumerateDirectories(extracted, "BepInEx", SearchOption.AllDirectories).FirstOrDefault();
        if (bepin != null)
        {
            CopyDirectory(bepin, Path.Combine(gameRoot, "BepInEx"));
            return;
        }

        var apiDll = Directory.EnumerateFiles(extracted, "COTL_API.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (apiDll == null) throw new InvalidDataException("COTL_API 패키지에서 COTL_API.dll을 찾지 못했습니다.");
        var sourceDir = Path.GetDirectoryName(apiDll)!;
        var targetDir = Path.Combine(gameRoot, "BepInEx", "plugins", "COTL_API");
        Directory.CreateDirectory(targetDir);
        CopyDirectory(sourceDir, targetDir);
    }

    private static string StripSingleWrapperDirectory(string root)
    {
        var files = Directory.GetFiles(root);
        var dirs = Directory.GetDirectories(root);
        return files.Length == 0 && dirs.Length == 1 ? dirs[0] : root;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void VerifyCopiedDirectory(string source, string destination)
    {
        var sourceRoot = Path.GetFullPath(source);
        var destinationRoot = Path.GetFullPath(destination);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            if (!destinationFile.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(destinationFile)
                || !Sha256(sourceFile).Equals(Sha256(destinationFile), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"LambLink Mod 설치 파일 검증에 실패했습니다: {relativePath}");
            }
        }
    }

    private static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private void CreateShortcuts(string exe, bool staging)
    {
        if (!File.Exists(exe)) return;
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LambLink");
            RemoveLegacyShortcuts(desktop);
            Directory.CreateDirectory(startMenu);
            var shortcutName = staging ? "LambLink Companion (STAGING TEST).url" : "LambLink Companion.url";
            CreateUrlShortcut(Path.Combine(desktop, shortcutName), exe);
            CreateUrlShortcut(Path.Combine(startMenu, shortcutName), exe);
            Log($"[SHORTCUT] desktop/start-menu shortcuts created environment={(staging ? "staging" : "production")}, name={shortcutName}");
        }
        catch (Exception ex) { Log("[SHORTCUT][WARN] " + ex.Message); }
    }

    private void RemoveLegacyShortcuts(string desktop)
    {
        var legacyStartMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "ChzzkOfTheLamb");
        foreach (var shortcutName in new[]
                 {
                     "ChzzkOfTheLamb Companion.url",
                     "ChzzkOfTheLamb Companion (STAGING TEST).url"
                 })
        {
            var desktopShortcut = Path.Combine(desktop, shortcutName);
            var startMenuShortcut = Path.Combine(legacyStartMenu, shortcutName);
            if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);
            if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut);
        }

        if (Directory.Exists(legacyStartMenu)
            && !Directory.EnumerateFileSystemEntries(legacyStartMenu).Any())
        {
            Directory.Delete(legacyStartMenu);
        }
    }

    private static void CreateUrlShortcut(string path, string exe)
    {
        File.WriteAllText(path, "[InternetShortcut]\r\nURL=file:///" + exe.Replace('\\', '/') + "\r\nIconFile=" + exe + "\r\nIconIndex=0\r\n");
    }

    private static void WriteCompanionLaunchProfile(InstallerManifest manifest, string companionRoot)
    {
        var dataDirectoryName = manifest.IsStaging ? "LambLink-Staging" : "LambLink";
        var launchProfile = new
        {
            schemaVersion = 1,
            release = manifest.Release,
            environment = manifest.Environment,
            apiBaseUrl = manifest.ApiBaseUrl,
            frontendUrl = manifest.FrontendUrl,
            dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), dataDirectoryName)
        };
        var profilePath = Path.Combine(companionRoot, "companion-launch-profile.json");
        File.WriteAllText(profilePath, JsonSerializer.Serialize(launchProfile, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteInstallReceipt(InstallerManifest manifest, string gameRoot, string companionRoot)
    {
        var stateDirectoryName = manifest.IsStaging ? "LambLink-Staging" : "LambLink";
        var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), stateDirectoryName);
        Directory.CreateDirectory(stateDir);
        var receipt = new { installedAtUtc = DateTime.UtcNow, manifest.Release, manifest.Environment, gameRoot, companionRoot, manifest.Components };
        File.WriteAllText(Path.Combine(stateDir, "install-receipt.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LaunchCompanion()
    {
        if (_companionExe == null || !File.Exists(_companionExe)) return;
        Process.Start(new ProcessStartInfo(_companionExe) { UseShellExecute = true });
    }

    private void ApplyEnvironmentPresentation(InstallerManifest manifest)
    {
        if (manifest.IsStaging)
        {
            Text = $"LambLink Setup {ReleaseVersion} — STAGING TEST";
            _environmentBanner.Text = "STAGING TEST · 운영과 분리된 테스트 환경";
            _environmentBanner.BackColor = Color.DarkOrange;
            _environmentBanner.ForeColor = Color.Black;
            _launch.Text = "STAGING Companion 실행";
        }
        else
        {
            Text = $"LambLink Setup {ReleaseVersion} — PRODUCTION";
            _environmentBanner.Text = "PRODUCTION · 정식 운영 환경";
            _environmentBanner.BackColor = Color.DarkGreen;
            _environmentBanner.ForeColor = Color.White;
            _launch.Text = "운영 Companion 실행";
        }
    }

    private void RemoveLegacyModDirectory(string gameRoot)
    {
        var pluginRoot = Path.GetFullPath(Path.Combine(gameRoot, "BepInEx", "plugins"));
        var legacyDirectory = Path.GetFullPath(Path.Combine(pluginRoot, "ChzzkOfTheLamb"));
        if (!legacyDirectory.StartsWith(pluginRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("구 플러그인 경로가 게임의 BepInEx plugins 폴더 밖을 가리킵니다.");
        if (!Directory.Exists(legacyDirectory))
            return;

        Directory.Delete(legacyDirectory, recursive: true);
        Log($"[BRAND-MIGRATION] removed legacy plugin directory: {legacyDirectory}");
    }

    private void RemoveLegacyProgramDirectory(string legacyRoot, string currentRoot)
    {
        var legacyFullPath = Path.GetFullPath(legacyRoot);
        var currentFullPath = Path.GetFullPath(currentRoot);
        if (string.Equals(legacyFullPath, currentFullPath, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(legacyFullPath))
        {
            return;
        }

        try
        {
            Directory.Delete(legacyFullPath, recursive: true);
            Log($"[BRAND-MIGRATION] removed legacy Companion program directory: {legacyFullPath}");
        }
        catch (Exception ex)
        {
            Log($"[BRAND-MIGRATION][WARN] failed to remove legacy Companion program directory: {ex.Message}");
        }
    }

    private void SetProgress(int value) => _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value));

    private void Log(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {text}";
        InstallerLog.Write(line);
        if (_logBox.IsHandleCreated) _logBox.AppendText(line + Environment.NewLine);
    }
}

internal static class SteamLocator
{
    public static string? FindCultOfTheLamb()
    {
        foreach (var steamRoot in EnumerateSteamRoots())
        {
            foreach (var library in EnumerateLibraries(steamRoot))
            {
                var candidate = Path.Combine(library, "steamapps", "common", "Cult of the Lamb");
                if (IsGameFolder(candidate)) return candidate;
            }
        }
        return null;
    }

    public static bool IsGameFolder(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Cult Of The Lamb.exe"));

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam") ?? baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path)) yield return path;
        }
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(defaultPath) && seen.Add(defaultPath)) yield return defaultPath;
    }

    private static IEnumerable<string> EnumerateLibraries(string steamRoot)
    {
        yield return steamRoot;
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (var line in File.ReadLines(vdf))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var idx = Array.FindIndex(parts, p => p.Equals("path", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < parts.Length)
            {
                var path = parts[idx + 1].Replace("\\\\", "\\");
                if (Directory.Exists(path)) yield return path;
            }
        }
    }
}

internal static class InstallerLog
{
    private static readonly object Gate = new();
    public static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LambLink", "installer.log");
    public static void Write(string line)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
    }
}

internal sealed class InstallerManifest
{
    public string Release { get; set; } = "";
    public string Environment { get; set; } = "production";
    public string? ApiBaseUrl { get; set; }
    public string? FrontendUrl { get; set; }
    public List<InstallerComponent> Components { get; set; } = new();
    public bool IsStaging => Environment.Equals("staging", StringComparison.OrdinalIgnoreCase);
}

internal sealed class InstallerComponent
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string InstallMode { get; set; } = "";
    public string Source { get; set; } = "";
}
