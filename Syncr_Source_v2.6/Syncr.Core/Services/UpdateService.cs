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
    /// Self-Update service for SYNCR using GitHub Releases.
    /// </summary>
    public class UpdateService
    {
        public const string GitHubOwner = "Kuro-Nas";
        public const string GitHubRepo = "Syncr";
        public const string SyncrWindowsAsset = "Syncr_Windows_x64.zip";
        public const string SyncrPiAsset = "Syncr_Pi_arm64.zip";
        public const string GitHubToken = "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";

        private static readonly HttpClient _http;

        static UpdateService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("SyncrApp", SyncrVersion.Current));
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        private static async Task<string> GetJsonWithAuthFallbackAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(GitHubToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);

            var response = await _http.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                using var anonRequest = new HttpRequestMessage(HttpMethod.Get, url);
                response = await _http.SendAsync(anonRequest);
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public bool UpdateAvailable { get; private set; }
        public ReleaseInfo? LatestRelease { get; private set; }
        public string CheckError { get; private set; } = string.Empty;

        public event Action<ReleaseInfo>? OnUpdateAvailable;
        public event Action<double>? OnDownloadProgress;
        public event Action<string>? OnLog;

        public async Task<bool> CheckForUpdateAsync()
        {
            try
            {
                string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                Log($"Checking for updates at: {url}");

                string json = await GetJsonWithAuthFallbackAsync(url);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
                if (release == null) return false;

                string latestTag = release.TagName?.TrimStart('v') ?? "0.0.0";
                string currentTag = SyncrVersion.Current.TrimStart('v');

                Log($"Current: v{currentTag} | Latest: v{latestTag}");

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
                            downloadUrl = !string.IsNullOrEmpty(asset.Url) ? asset.Url : asset.BrowserDownloadUrl;
                            break;
                        }
                    }

                    LatestRelease = new ReleaseInfo
                    {
                        Version = $"v{latestTag}",
                        TagName = release.TagName ?? "",
                        Body = release.Body ?? "",
                        PublishedAt = release.PublishedAt,
                        DownloadUrl = downloadUrl ?? "",
                        AssetName = assetName
                    };

                    UpdateAvailable = true;
                    Log($"Update available: {LatestRelease.Version}");
                    OnUpdateAvailable?.Invoke(LatestRelease);
                    return true;
                }

                Log("SYNCR is up to date.");
                return false;
            }
            catch (Exception ex)
            {
                CheckError = ex.Message;
                Log($"Update check failed: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> DownloadUpdateAsync(string downloadUrl)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "SyncrUpdate");
                Directory.CreateDirectory(tempDir);
                string zipPath = Path.Combine(tempDir, "syncr_update.zip");

                Log($"Downloading from: {downloadUrl}");

                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                if (!string.IsNullOrWhiteSpace(GitHubToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                if (downloadUrl.Contains("api.github.com"))
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Log("Auth rejected, retrying download without token...");
                    using var anonRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    if (downloadUrl.Contains("api.github.com"))
                        anonRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                    response = await _http.SendAsync(anonRequest, HttpCompletionOption.ResponseHeadersRead);
                }

                response.EnsureSuccessStatusCode();

                long? total = response.Content.Headers.ContentLength;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var file = File.Create(zipPath);

                byte[] buffer = new byte[81920];
                long read = 0;
                int chunk;
                while ((chunk = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, chunk));
                    read += chunk;
                    if (total.HasValue && total.Value > 0)
                        OnDownloadProgress?.Invoke((double)read / total.Value);
                }

                Log($"Downloaded: {zipPath} ({read / 1024 / 1024:F1} MB)");
                return zipPath;
            }
            catch (Exception ex)
            {
                Log($"Download failed: {ex.Message}");
                return null;
            }
        }

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
            string script = Path.Combine(Path.GetTempPath(), "syncr_updater.ps1");
            string exe = Path.Combine(appDir, "Syncr.UI.exe");
            string exeAlt = Path.Combine(appDir, "Syncr.UI.dll");

            string launchCmd = File.Exists(exe)
                ? $"Start-Process '{exe}'"
                : $"Start-Process 'dotnet' -ArgumentList '{exeAlt}'";

            string psContent = @"$ErrorActionPreference = 'SilentlyContinue'
Start-Sleep -Seconds 2

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
                FileName = "powershell.exe",
                Arguments = $"-NonInteractive -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Log("Updater script launched. SYNCR will restart automatically.");
            Environment.Exit(0);
        }

        private void ApplyLinux(string zipPath, string appDir)
        {
            string script = Path.Combine(Path.GetTempPath(), "syncr_updater.sh");
            string exePath = Path.Combine(appDir, "Syncr.UI");
            string cliPath = Path.Combine(appDir, "Syncr.CLI");
            string dllPath = Path.Combine(appDir, "Syncr.UI.dll");

            string bashContent = @"#!/bin/bash
exec > /tmp/syncr_updater.log 2>&1
echo '=== SYNCR Updater Started ==='
echo 'Target Dir: " + appDir + @"'
echo 'Zip File:   " + zipPath + @"'

# Wait 3 seconds for main application process to exit cleanly
sleep 3

echo 'Extracting update package...'
unzip -o '" + zipPath + @"' -d '" + appDir + @"' -x 'config.json' '*.csv' '*.log'

echo 'Setting executable permissions...'
chmod +x '" + exePath + @"' '" + cliPath + @"' 2>/dev/null || true
chmod +x '" + appDir + @"'/* 2>/dev/null || true

if command -v systemctl >/dev/null 2>&1; then
    echo 'Restarting syncr.service via systemd...'
    sudo systemctl restart syncr.service 2>/dev/null || systemctl restart syncr.service 2>/dev/null || true
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
";

            File.WriteAllText(script, bashContent);

            Process.Start("chmod", $"+x \"{script}\"")?.WaitForExit();

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"nohup '{script}' >/dev/null 2>&1 &\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);

            Log("Updater script launched. SYNCR will restart automatically.");
            Environment.Exit(0);
        }

        private static bool IsNewer(string latest, string current)
        {
            return Version.TryParse(NormVer(latest), out var lv) &&
                   Version.TryParse(NormVer(current), out var cv) &&
                   lv > cv;
        }

        private static string NormVer(string v)
        {
            var parts = v.Split('.');
            return parts.Length >= 3 ? v :
                   parts.Length == 2 ? $"{v}.0" : $"{v}.0.0";
        }

        private void Log(string msg)
        {
            DiagnosticLog.Update(msg);
            OnLog?.Invoke(msg);
        }

        private class GitHubRelease
        {
            [JsonProperty("tag_name")] public string? TagName { get; set; }
            [JsonProperty("name")] public string? Name { get; set; }
            [JsonProperty("body")] public string? Body { get; set; }
            [JsonProperty("published_at")] public DateTime PublishedAt { get; set; }
            [JsonProperty("assets")] public GitHubAsset[]? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonProperty("name")] public string? Name { get; set; }
            [JsonProperty("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
            [JsonProperty("url")] public string? Url { get; set; }
            [JsonProperty("size")] public long Size { get; set; }
        }
    }

    public class ReleaseInfo
    {
        public string Version { get; set; } = "";
        public string TagName { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime PublishedAt { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string AssetName { get; set; } = "";
    }
}
