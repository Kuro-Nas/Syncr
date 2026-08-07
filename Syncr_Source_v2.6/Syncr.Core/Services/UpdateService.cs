using Newtonsoft.Json;
using Syncr.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Syncr.Core.Services
{
    /// <summary>
    /// Self-Update service for SYNCR — GitHub Releases based OTA updates.
    ///
    /// HOW IT WORKS:
    ///   1. On startup SYNCR calls CheckForUpdateAsync() → GitHub API returns latest release info.
    ///   2. If a newer tag_name is available, UpdateAvailable = true + LatestRelease is populated.
    ///   3. UI/CLI shows "Update available: vX.Y" banner with a download button.
    ///   4. User clicks Update → DownloadUpdateAsync() downloads the ZIP asset.
    ///   5. ApplyUpdateAndRestart() writes a small OS-specific updater script, runs it, then exits.
    ///   6. The updater script waits for SYNCR to exit, extracts ZIP, re-launches SYNCR.
    ///
    /// SETUP (one-time, for the developer):
    ///   • Set GitHubOwner and GitHubRepo below to your actual repository.
    ///   • Tag every GitHub release as "vMAJOR.MINOR.PATCH" (e.g. "v2.7.0").
    ///   • Upload the release asset named exactly as the constants below
    ///     (SyncrWindowsAsset / SyncrPiAsset).
    ///   • Bump SyncrVersion.Current before every release and rebuild.
    /// </summary>
    public class UpdateService
    {
        // ── ⚙️  CONFIGURE THESE BEFORE RELEASING ────────────────────────────────
        public const string GitHubOwner     = "Kuro-Nas";
        public const string GitHubRepo      = "Syncr";
        public const string SyncrWindowsAsset = "Syncr_Windows_x64.zip";
        public const string SyncrPiAsset      = "Syncr_Pi_arm64.zip";
        // ─────────────────────────────────────────────────────────────────────────

        private static readonly HttpClient _http = new HttpClient();

        static UpdateService()
        {
            // GitHub API requires a User-Agent header
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("SyncrApp", SyncrVersion.Current));
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        // ── State ────────────────────────────────────────────────────────────────
        public bool       UpdateAvailable  { get; private set; }
        public ReleaseInfo? LatestRelease  { get; private set; }
        public string     CheckError       { get; private set; } = string.Empty;

        // ── Events ───────────────────────────────────────────────────────────────
        public event Action<ReleaseInfo>? OnUpdateAvailable;
        public event Action<double>?      OnDownloadProgress;   // 0.0–1.0
        public event Action<string>?      OnLog;

        // ── Check for update ────────────────────────────────────────────────────
        public async Task<bool> CheckForUpdateAsync()
        {
            try
            {
                string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                Log($"Checking for updates at: {url}");

                string json = await _http.GetStringAsync(url);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                if (release == null) return false;

                string latestTag = release.TagName?.TrimStart('v') ?? "0.0.0";
                string currentTag = SyncrVersion.Current.TrimStart('v');

                Log($"Current: v{currentTag}  |  Latest: v{latestTag}");

                if (IsNewer(latestTag, currentTag))
                {
                    string assetName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? SyncrWindowsAsset
                        : SyncrPiAsset;

                    string? downloadUrl = null;
                    foreach (var asset in release.Assets ?? Array.Empty<GitHubAsset>())
                    {
                        if (asset.Name?.Equals(assetName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            downloadUrl = asset.BrowserDownloadUrl;
                            break;
                        }
                    }

                    LatestRelease = new ReleaseInfo
                    {
                        Version     = $"v{latestTag}",
                        TagName     = release.TagName ?? "",
                        Body        = release.Body ?? "",
                        PublishedAt = release.PublishedAt,
                        DownloadUrl = downloadUrl ?? "",
                        AssetName   = assetName
                    };

                    UpdateAvailable = true;
                    Log($"✅ Update available: {LatestRelease.Version}");
                    OnUpdateAvailable?.Invoke(LatestRelease);
                    return true;
                }

                Log("✅ SYNCR is up to date.");
                return false;
            }
            catch (Exception ex)
            {
                CheckError = ex.Message;
                Log($"⚠️  Update check failed: {ex.Message}");
                return false;
            }
        }

        // ── Download the update ZIP ──────────────────────────────────────────────
        public async Task<string?> DownloadUpdateAsync(string downloadUrl)
        {
            try
            {
                string tempDir  = Path.Combine(Path.GetTempPath(), "SyncrUpdate");
                Directory.CreateDirectory(tempDir);
                string zipPath  = Path.Combine(tempDir, "syncr_update.zip");

                Log($"Downloading from: {downloadUrl}");
                using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long? total = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var file   = File.Create(zipPath);

                byte[] buffer = new byte[81920];
                long   read   = 0;
                int    chunk;
                while ((chunk = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, chunk));
                    read += chunk;
                    if (total.HasValue && total.Value > 0)
                        OnDownloadProgress?.Invoke((double)read / total.Value);
                }

                Log($"Downloaded: {zipPath}  ({read / 1024 / 1024:F1} MB)");
                return zipPath;
            }
            catch (Exception ex)
            {
                Log($"❌ Download failed: {ex.Message}");
                return null;
            }
        }

        // ── Apply update and restart ─────────────────────────────────────────────
        public void ApplyUpdateAndRestart(string zipPath)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ApplyWindows(zipPath, appDir);
            else
                ApplyLinux(zipPath, appDir);
        }

        private void ApplyWindows(string zipPath, string appDir)
        {
            // Write a PowerShell updater that waits for SYNCR to exit, then
            // extracts the zip, re-launches SYNCR, and deletes itself.
            string script = Path.Combine(Path.GetTempPath(), "syncr_updater.ps1");
            string exe    = Path.Combine(appDir, "Syncr.UI.exe");
            string exeAlt = Path.Combine(appDir, "Syncr.UI.dll");

            string launchCmd = File.Exists(exe)
                ? $"Start-Process '{exe}'"
                : $"Start-Process 'dotnet' -ArgumentList '{exeAlt}'";

            string psContent = @"$ErrorActionPreference = 'SilentlyContinue'
Start-Sleep -Seconds 2

# Wait up to 10s for processes to close
$timeout = 10
while ($timeout -gt 0 -and (Get-Process -Name 'Syncr.UI', 'Syncr.CLI' -ErrorAction SilentlyContinue)) {
    Start-Sleep -Seconds 1
    $timeout--
}
Stop-Process -Name 'Syncr.UI', 'Syncr.CLI' -Force -ErrorAction SilentlyContinue

Write-Host 'SYNCR Updater: Extracting...'
Expand-Archive -Path '" + zipPath.Replace("'", "''") + @"' -DestinationPath '" + appDir.Replace("'", "''") + @"' -Force

Write-Host 'SYNCR Updater: Launching SYNCR...'
" + launchCmd + @"

Remove-Item '" + zipPath.Replace("'", "''") + @"' -Force -ErrorAction SilentlyContinue
Remove-Item '" + script.Replace("'", "''") + @"' -Force -ErrorAction SilentlyContinue
";

            File.WriteAllText(script, psContent);

            var psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-NonInteractive -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = true,
                CreateNoWindow  = true
            };
            Process.Start(psi);
            Log("Updater script launched. SYNCR will restart automatically.");
            Environment.Exit(0);
        }

        private void ApplyLinux(string zipPath, string appDir)
        {
            // Write a bash updater script that handles systemd service stop/restart,
            // unlinks busy binary files, extracts update, and re-launches app.
            string script  = Path.Combine(Path.GetTempPath(), "syncr_updater.sh");
            string exePath = Path.Combine(appDir, "Syncr.UI");
            string cliPath = Path.Combine(appDir, "Syncr.CLI");
            string dllPath = Path.Combine(appDir, "Syncr.UI.dll");

            string bashContent = @"#!/bin/bash
exec > /tmp/syncr_updater.log 2>&1
echo '=== SYNCR Updater Started ==='
echo 'Target Dir: " + appDir + @"'
echo 'Zip File:   " + zipPath + @"'

# 1. Stop systemd service if running so it does not auto-restart old binary
if command -v systemctl >/dev/null 2>&1; then
    echo 'Stopping systemd syncr.service...'
    sudo systemctl stop syncr.service 2>/dev/null || systemctl stop syncr.service 2>/dev/null || true
fi

# 2. Wait for running processes to exit & force kill if needed
sleep 2
pkill -9 -f 'Syncr.UI' 2>/dev/null || true
pkill -9 -f 'Syncr.CLI' 2>/dev/null || true

# 3. Unlink/remove existing executables to avoid 'Text file busy' during unzip
echo 'Removing old binaries...'
rm -f '" + exePath + @"' '" + cliPath + @"'

# 4. Extract update (preserving user settings and logs)
echo 'Extracting update package...'
unzip -o '" + zipPath + @"' -d '" + appDir + @"' -x 'config.json' '*.csv' '*.log'

# 5. Restore executable permissions
echo 'Setting permissions...'
chmod +x '" + exePath + @"' '" + cliPath + @"' 2>/dev/null || true
chmod +x '" + appDir + @"'/* 2>/dev/null || true

# 6. Re-launch SYNCR (via systemd if available, else standalone background process)
if command -v systemctl >/dev/null 2>&1 && systemctl is-enabled syncr.service 2>/dev/null; then
    echo 'Restarting syncr.service via systemd...'
    sudo systemctl start syncr.service 2>/dev/null || systemctl start syncr.service 2>/dev/null
else
    echo 'Starting Syncr.UI process...'
    if [ -f '" + exePath + @"' ]; then
        nohup '" + exePath + @"' >/dev/null 2>&1 &
    elif [ -f '" + dllPath + @"' ]; then
        nohup dotnet '" + dllPath + @"' >/dev/null 2>&1 &
    fi
fi

echo '=== Update Completed Successfully ==='
rm -f '" + zipPath + @"'
rm -- ""$0""
";

            File.WriteAllText(script, bashContent);

            Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);

            Log("Updater script launched. SYNCR will restart automatically.");
            Environment.Exit(0);
        }

        // ── Version comparison (semver-aware) ───────────────────────────────────
        private static bool IsNewer(string latest, string current)
        {
            return Version.TryParse(NormVer(latest), out var lv) &&
                   Version.TryParse(NormVer(current), out var cv) &&
                   lv > cv;
        }

        private static string NormVer(string v)
        {
            // Ensure at least 3 parts for Version.Parse
            var parts = v.Split('.');
            return parts.Length >= 3 ? v :
                   parts.Length == 2 ? $"{v}.0" : $"{v}.0.0";
        }

        private void Log(string msg) => OnLog?.Invoke(msg);

        // ── GitHub API response models ───────────────────────────────────────────
        private class GitHubRelease
        {
            [JsonProperty("tag_name")]    public string?       TagName     { get; set; }
            [JsonProperty("name")]        public string?       Name        { get; set; }
            [JsonProperty("body")]        public string?       Body        { get; set; }
            [JsonProperty("published_at")]public DateTime      PublishedAt { get; set; }
            [JsonProperty("assets")]      public GitHubAsset[]? Assets    { get; set; }
        }

        private class GitHubAsset
        {
            [JsonProperty("name")]                  public string? Name               { get; set; }
            [JsonProperty("browser_download_url")]  public string? BrowserDownloadUrl { get; set; }
            [JsonProperty("size")]                  public long    Size               { get; set; }
        }
    }

    // ── Public model surfaced to UI/CLI ──────────────────────────────────────────
    public class ReleaseInfo
    {
        public string   Version     { get; set; } = "";
        public string   TagName     { get; set; } = "";
        public string   Body        { get; set; } = "";
        public DateTime PublishedAt { get; set; }
        public string   DownloadUrl { get; set; } = "";
        public string   AssetName   { get; set; } = "";
    }
}
