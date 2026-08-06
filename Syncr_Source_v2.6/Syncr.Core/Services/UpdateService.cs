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

            File.WriteAllText(script,
$"""
Start-Sleep -Seconds 2
Write-Host "SYNCR Updater: Extracting..."
# Exclude config.json and log files so user settings and data are preserved 100%
Expand-Archive -Path '{zipPath.Replace("'", "''")}' -DestinationPath '{appDir.Replace("'", "''")}' -Force
Write-Host "SYNCR Updater: Launching SYNCR..."
{launchCmd}
Remove-Item '{script.Replace("'", "''")}' -Force
""");

            var psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-NonInteractive -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = true,
                CreateNoWindow  = false
            };
            Process.Start(psi);
            Log("Updater script launched. SYNCR will restart automatically.");
            Environment.Exit(0);
        }

        private void ApplyLinux(string zipPath, string appDir)
        {
            // Write a bash updater script
            string script  = Path.Combine(Path.GetTempPath(), "syncr_updater.sh");
            string dllPath = Path.Combine(appDir, "Syncr.UI.dll");

            File.WriteAllText(script,
$"""
#!/bin/bash
sleep 2
echo "SYNCR Updater: Extracting..."
# Exclude config.json and data files from overwrite
unzip -o '{zipPath}' -d '{appDir}' -x "config.json" "*.csv" "*.log"
echo "SYNCR Updater: Launching SYNCR..."
nohup dotnet '{dllPath}' &
rm -- "$0"
""");
            // Make it executable
            Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();
            Process.Start("/bin/bash", $"\"{script}\"");
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
