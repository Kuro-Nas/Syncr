using Syncr.Core;
using Syncr.Core.Models;
using Syncr.Core.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncr.CLI
{
    class Program
    {

        // ── ANSI escape helpers ─────────────────────────────────────────────────
        static string Fg(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";
        static string Bg(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";
        const string Reset    = "\x1b[0m";
        const string Bold     = "\x1b[1m";
        const string Dim      = "\x1b[2m";
        const string ClearScr = "\x1b[2J\x1b[H";
        const string HideCursor = "\x1b[?25l";
        const string ShowCursor = "\x1b[?25h";

        // Predefined theme colors
        static readonly string ColHeader  = Fg(30, 215, 96)  + Bold;   // green
        static readonly string ColAccent  = Fg(56, 182, 255) + Bold;   // blue
        static readonly string ColWarn    = Fg(255, 165, 0);            // orange
        static readonly string ColError   = Fg(255, 80,  80);           // red
        static readonly string ColDim     = Fg(120, 120, 140);          // gray
        static readonly string ColValue   = Fg(255, 255, 255) + Bold;   // white bold
        static readonly string ColUnit    = Fg(160, 200, 255);          // light blue
        static readonly string ColTime    = Fg(180, 180, 255);          // lavender

        // ── State ───────────────────────────────────────────────────────────────
        static readonly ConcurrentDictionary<string, MachineDataPoint> _latest = new();
        static readonly ConcurrentDictionary<string, string>           _status = new();
        static volatile int  _selectedMachineIdx = -1;  // -1 = all
        static volatile bool _plotOnly            = true;
        static volatile bool _csvLogging          = false;
        static volatile bool _running             = true;
        static string        _csvPath             = "";
        static int           _pollIntervalMs      = -1;

        // ── Update State ─────────────────────────────────────────────────────
        static readonly UpdateService _updater    = new UpdateService();
        static volatile bool          _updateReady = false;
        static string                 _updateMsg   = "";

        static async Task Main(string[] args)
        {
            // ── Parse CLI arguments ─────────────────────────────────────────────
            string? machineFilter = GetArg(args, "--machine");
            string? logPath       = GetArg(args, "--log");
            bool    liveMode      = args.Contains("--live") || !args.Contains("--no-live");
            if (GetArg(args, "--interval") is string iv && int.TryParse(iv, out int ivMs))
                _pollIntervalMs = ivMs;

            if (logPath != null)
            {
                _csvPath   = logPath;
                _csvLogging = true;
            }

            // ── Print banner ────────────────────────────────────────────────────
            PrintBanner();

            // ── Load config ─────────────────────────────────────────────────────
            var configService = new ConfigService();
            var config        = configService.LoadConfig();

            // ── Background update check ─────────────────────────────────────────
            _updater.OnLog += msg => { /* suppress in CLI — shown in header */ };
            _updater.OnUpdateAvailable += rel =>
            {
                _updateReady = true;
                _updateMsg   = $"🆕  {rel.Version} available ({rel.PublishedAt:d MMM yyyy})  —  Press [U] to install";
            };
            _ = Task.Run(async () => { await Task.Delay(3000); await _updater.CheckForUpdateAsync(); });


            if (config.Machines == null || config.Machines.Count == 0)
            {
                Console.WriteLine($"{ColError}[ERROR] No machines configured. Launch SYNCR GUI first to set up machines.{Reset}");
                return;
            }

            // Override poll interval if specified
            if (_pollIntervalMs > 0)
            {
                foreach (var m in config.Machines)
                    m.PollingIntervalMs = _pollIntervalMs;
            }

            // Filter machine if specified
            List<MachineConfig> machines = config.Machines;
            if (machineFilter != null)
            {
                machines = config.Machines
                    .Where(m => m.Name.Contains(machineFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (machines.Count == 0)
                {
                    Console.WriteLine($"{ColError}[ERROR] No machine found matching '{machineFilter}'.{Reset}");
                    return;
                }
            }

            // ── Start Modbus polling ────────────────────────────────────────────
            var modbusService = new ModbusService(config);
            modbusService.OnDataReceived += (point) =>
            {
                _latest[point.MachineName] = point;
                _status[point.MachineName] = "ONLINE";
                if (_csvLogging && !string.IsNullOrEmpty(_csvPath))
                    AppendCsv(point);
            };
            modbusService.OnConnectionError += (machineName) =>
            {
                _status[machineName] = "ERROR";
            };

            modbusService.UpdateConfig(config);

            Console.Write(HideCursor);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; _running = false; };

            // ── Keyboard handler (background thread) ────────────────────────────
            var kbTask = Task.Run(() =>
            {
                while (_running)
                {
                    if (!Console.KeyAvailable) { Thread.Sleep(50); continue; }
                    var key = Console.ReadKey(intercept: true).Key;
                    switch (key)
                    {
                        case ConsoleKey.Q: _running = false; break;
                        case ConsoleKey.A: _selectedMachineIdx = -1; break;
                        case ConsoleKey.P: _plotOnly = !_plotOnly; break;
                        case ConsoleKey.U:
                            if (_updateReady && _updater.LatestRelease != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    _updateMsg = "Downloading update...";
                                    string? zip = await _updater.DownloadUpdateAsync(
                                        _updater.LatestRelease.DownloadUrl);
                                    if (zip != null)
                                    {
                                        _updateMsg = "Applying update — SYNCR will restart...";
                                        await Task.Delay(1000);
                                        _updater.ApplyUpdateAndRestart(zip);
                                    }
                                    else
                                    {
                                        _updateMsg = "Download failed. Check connectivity.";
                                    }
                                });
                            }
                            break;

                        case ConsoleKey.L:
                            _csvLogging = !_csvLogging;
                            if (_csvLogging && string.IsNullOrEmpty(_csvPath))
                                _csvPath = $"syncr_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                            break;
                        default:
                            // Number keys 1-9 → select machine
                            if (key >= ConsoleKey.D1 && key <= ConsoleKey.D9)
                                _selectedMachineIdx = (int)(key - ConsoleKey.D1);
                            break;
                    }
                }
            });

            // ── Main display loop ───────────────────────────────────────────────
            while (_running)
            {
                if (liveMode)
                    RenderLive(machines);
                else
                    RenderOnce(machines);

                await Task.Delay(500);
            }

            // ── Cleanup ─────────────────────────────────────────────────────────
            Console.Write(ShowCursor);
            Console.Write(Reset);
            Console.WriteLine();
            Console.WriteLine($"{ColHeader}SYNCR CLI stopped.{Reset}");
            modbusService.Stop();
        }

        // ── Render: Full live dashboard (clear + redraw) ────────────────────────
        static void RenderLive(List<MachineConfig> machines)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ClearScr);

            PrintHeader(sb, machines);

            var toShow = _selectedMachineIdx >= 0 && _selectedMachineIdx < machines.Count
                ? new List<MachineConfig> { machines[_selectedMachineIdx] }
                : machines;

            foreach (var m in toShow)
            {
                PrintMachineBlock(sb, m);
            }

            PrintFooter(sb, machines);
            Console.Write(sb.ToString());
        }

        static void RenderOnce(List<MachineConfig> machines)
        {
            // Non-live mode: just print latest values once and scroll
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{ColTime}[{DateTime.Now:HH:mm:ss}]{Reset}");
            foreach (var m in machines)
                PrintMachineBlock(sb, m);
            Console.Write(sb.ToString());
        }

        // ── Banner ──────────────────────────────────────────────────────────────
        static void PrintBanner()
        {
            Console.WriteLine();
            Console.WriteLine($"{ColHeader}╔══════════════════════════════════════════════════════════╗{Reset}");
            Console.WriteLine($"{ColHeader}║  {ColAccent}SYNCR CLI v2.6  {ColDim}— Growatt PV Modbus Monitor{ColHeader}            ║{Reset}");
            Console.WriteLine($"{ColHeader}║  {ColDim}Raspberry Pi Terminal Mode | Growatt RS485 RTU{ColHeader}            ║{Reset}");
            Console.WriteLine($"{ColHeader}╚══════════════════════════════════════════════════════════╝{Reset}");
            Console.WriteLine();
        }

        // ── Header block ────────────────────────────────────────────────────────
        static void PrintHeader(System.Text.StringBuilder sb, List<MachineConfig> machines)
        {
            string time    = DateTime.Now.ToString("HH:mm:ss  dd MMM yyyy");
            string csvInfo = _csvLogging ? $"{ColHeader}[CSV:{_csvPath}]{Reset}" : $"{ColDim}[CSV: off]{Reset}";
            string filter  = _plotOnly ? $"{ColDim}[Plotted only]{Reset}" : $"{ColDim}[All tags]{Reset}";

            sb.AppendLine($"{ColHeader}╔══════════════════════════════════════════════════════════════════════════╗{Reset}");
            sb.AppendLine($"{ColHeader}║  {ColAccent}{Bold}SYNCR CLI{Reset}{ColHeader}  {ColDim}Growatt PV Monitor{ColHeader}          {ColTime}{time}{ColHeader}          ║{Reset}");
            sb.AppendLine($"{ColHeader}╚══════════════════════════════════════════════════════════════════════════╝{Reset}");

            // Machine tabs
            sb.Append($"  {ColDim}Machines: {Reset}");
            for (int i = 0; i < machines.Count; i++)
            {
                var m = machines[i];
                bool sel = _selectedMachineIdx == i;
                string st = _status.GetValueOrDefault(m.Name, "WAITING");
                string stColor = st == "ONLINE" ? ColHeader : st == "ERROR" ? ColError : ColWarn;
                string bg = sel ? Bg(40, 60, 100) : "";
                sb.Append($"{bg}[{Bold}{i + 1}{Reset}{bg}] {ColAccent}{m.Name}{Reset}{bg} {stColor}●{Reset}  ");
            }
            if (_selectedMachineIdx < 0)
                sb.Append($"{Bg(40, 60, 100)}[A] All{Reset}");
            sb.AppendLine();
            sb.AppendLine($"  {filter}  {csvInfo}");
            string keysStr = "Keys: [Q]Quit  [1-9]Machine  [A]All  [P]Toggle Plotted  [L]Toggle CSV";
            if (_updateReady) keysStr += "  [U]Update";
            sb.AppendLine($"  {ColDim}{keysStr}{Reset}");
            if (_updateReady && !string.IsNullOrEmpty(_updateMsg))
            {
                sb.AppendLine($"  {ColHeader}{_updateMsg}{Reset}");
            }
            sb.AppendLine();
        }

        // ── Machine block ────────────────────────────────────────────────────────
        static void PrintMachineBlock(System.Text.StringBuilder sb, MachineConfig m)
        {
            string st      = _status.GetValueOrDefault(m.Name, "WAITING");
            string stColor = st == "ONLINE" ? ColHeader : st == "ERROR" ? ColError : ColWarn;
            _latest.TryGetValue(m.Name, out var dp);

            sb.AppendLine($"{ColAccent}┌── {Bold}{m.Name}{Reset}{ColAccent} [Slave {m.SlaveId}] {stColor}{st}{Reset}{ColAccent} ──── {ColDim}{m.SerialPort} @ {m.BaudRate} baud{Reset}");

            if (dp == null)
            {
                sb.AppendLine($"{ColDim}│  Waiting for data...{Reset}");
                sb.AppendLine($"{ColAccent}└──────────────────────────────────────────────────{Reset}");
                sb.AppendLine();
                return;
            }

            // Filter tags
            var tagsToShow = _plotOnly
                ? m.Tags.Where(t => t.IsPlotted).ToList()
                : m.Tags;

            // Group by category (derive from tag name prefix)
            string? lastCat = null;
            int col = 0;
            const int COLS = 2;       // 2-column layout
            const int COL_W = 38;     // column width

            foreach (var tag in tagsToShow)
            {
                string cat = GetCategory(tag.Name);
                if (cat != lastCat)
                {
                    if (col > 0) { sb.AppendLine(); col = 0; }
                    sb.AppendLine($"{ColAccent}│  {ColDim}{Bold}{cat.ToUpper()}{Reset}");
                    lastCat = cat;
                }

                // Get value
                string valStr;
                string unitStr;
                if (dp.Values.TryGetValue(tag.Name, out double val))
                {
                    valStr  = FormatValue(val, tag.ScalingFactor);
                    unitStr = tag.SiUnit;
                }
                else
                {
                    valStr  = "---";
                    unitStr = tag.SiUnit;
                }

                // Parse hex color for ANSI
                string tagColor = HexToAnsi(tag.Color);

                string tagLine = $"{ColAccent}│  {tagColor}{tag.Name,-28}{Reset} {ColValue}{valStr,8}{Reset} {ColUnit}{unitStr,-6}{Reset}";

                if (col == 0)
                {
                    sb.Append(tagLine.PadRight(COL_W));
                }
                else
                {
                    sb.AppendLine(tagLine);
                }
                col = (col + 1) % COLS;
            }

            if (col != 0) sb.AppendLine();

            string ts = dp.Timestamp.ToString("HH:mm:ss.fff");
            sb.AppendLine($"{ColAccent}└──── {ColTime}Last: {ts}  {ColDim}Latency: {dp.LatencyMs:F0}ms  Tags: {m.Tags.Count}{Reset}");
            sb.AppendLine();
        }

        // ── Footer ───────────────────────────────────────────────────────────────
        static void PrintFooter(System.Text.StringBuilder sb, List<MachineConfig> machines)
        {
            int online  = _status.Values.Count(s => s == "ONLINE");
            int total   = machines.Count;
            sb.AppendLine($"{ColDim}  Status: {ColHeader}{online}/{total} online{ColDim}  |  SYNCR CLI v2.6  |  Press Q to quit{Reset}");
        }

        // ── CSV Logging ───────────────────────────────────────────────────────────
        static readonly object _csvLock = new();
        static bool _csvHeaderWritten = false;

        static void AppendCsv(MachineDataPoint dp)
        {
            lock (_csvLock)
            {
                try
                {
                    if (!_csvHeaderWritten)
                    {
                        var hdr = "Timestamp,Machine," + string.Join(",", dp.Values.Keys);
                        System.IO.File.AppendAllText(_csvPath, hdr + "\n");
                        _csvHeaderWritten = true;
                    }
                    var row = $"{dp.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{dp.MachineName},"
                            + string.Join(",", dp.Values.Values.Select(v => v.ToString("G6")));
                    System.IO.File.AppendAllText(_csvPath, row + "\n");
                }
                catch { /* ignore write errors */ }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        static string? GetArg(string[] args, string key)
        {
            int idx = Array.IndexOf(args, key);
            return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
        }

        static string FormatValue(double val, double scale)
        {
            // Already scaled by Modbus service
            if (Math.Abs(val) >= 10000) return val.ToString("F0");
            if (Math.Abs(val) >= 100)   return val.ToString("F1");
            return val.ToString("F2");
        }

        static string GetCategory(string tagName)
        {
            if (tagName.StartsWith("Grid") || tagName.StartsWith("Phase") || tagName.StartsWith("AC") || tagName.StartsWith("Reactive") || tagName.StartsWith("Output"))
                return "Grid AC";
            if (tagName.StartsWith("PV1") || tagName.StartsWith("PV2") || tagName.StartsWith("PV3") || tagName.StartsWith("PV Input"))
                return "PV DC";
            if (tagName.StartsWith("String"))
                return "PV Strings";
            if (tagName.StartsWith("Energy") || tagName.StartsWith("Epv") || tagName.StartsWith("Work"))
                return "Energy";
            if (tagName.StartsWith("Grid Fault"))
                return "Fault Records";
            if (tagName.Contains("Temp") || tagName.Contains("Bus") || tagName.Contains("Fault") || tagName.Contains("Warn") || tagName.Contains("Derat"))
                return "Health";
            if (tagName.StartsWith("On/Off") || tagName.EndsWith("Protect") || tagName.EndsWith("Rate") || tagName.Contains("Setting") || tagName.Contains("Delay") || tagName.Contains("Voltage") && tagName.Contains("Start"))
                return "Settings";
            if (tagName.StartsWith("Energy Hour") || tagName.StartsWith("Energy Day") || tagName.StartsWith("Energy Month") || tagName.StartsWith("Energy Year"))
                return "History";
            return "General";
        }

        static string HexToAnsi(string hex)
        {
            try
            {
                if (hex.StartsWith('#') && hex.Length == 7)
                {
                    int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                    return Fg(r, g, b);
                }
            }
            catch { }
            return "";
        }
    }
}
