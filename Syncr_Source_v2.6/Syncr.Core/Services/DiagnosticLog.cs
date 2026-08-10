using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Syncr.Core.Services
{
    /// <summary>
    /// Persistent diagnostic log for SYNCR.
    /// Captures startup, Modbus connection, OTA update, and UI action events
    /// to a plain text file that can be opened and copy-pasted for debugging.
    ///
    /// Log file location:
    ///   Windows : %AppData%\SYNCR\syncr_diagnostic.txt
    ///   Linux   : ~/.config/SYNCR/syncr_diagnostic.txt
    /// </summary>
    public static class DiagnosticLog
    {
        private static readonly string _logPath;
        private static readonly object _writeLock = new object();
        private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB cap before rotation

        static DiagnosticLog()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SYNCR");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "syncr_diagnostic.txt");

            WriteSessionHeader();
        }

        public static string LogPath => _logPath;

        // Category constants
        public const string CAT_STARTUP = "STARTUP";
        public const string CAT_MODBUS  = "MODBUS ";
        public const string CAT_UPDATE  = "UPDATE ";
        public const string CAT_UI      = "UI     ";
        public const string CAT_NETWORK = "NETWORK";
        public const string CAT_CONFIG  = "CONFIG ";
        public const string CAT_ERROR   = "ERROR  ";

        public static void Write(string category, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}";
            Console.WriteLine(line);
            try
            {
                lock (_writeLock)
                {
                    RotateIfNeeded();
                    File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Startup(string message)  => Write(CAT_STARTUP, message);
        public static void Modbus(string message)   => Write(CAT_MODBUS,  message);
        public static void Update(string message)   => Write(CAT_UPDATE,  message);
        public static void Ui(string message)       => Write(CAT_UI,      message);
        public static void Network(string message)  => Write(CAT_NETWORK, message);
        public static void Config(string message)   => Write(CAT_CONFIG,  message);
        public static void Error(string category, string message) => Write(CAT_ERROR, $"[{category}] {message}");

        private static void WriteSessionHeader()
        {
            string sep = new string('=', 72);
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux";
            string header =
                Environment.NewLine + sep + Environment.NewLine +
                $"  SYNCR DIAGNOSTIC LOG — Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                $"  Platform : {platform} | OS: {RuntimeInformation.OSDescription}" + Environment.NewLine +
                $"  Runtime  : {RuntimeInformation.FrameworkDescription}" + Environment.NewLine +
                sep + Environment.NewLine;

            lock (_writeLock)
            {
                try { File.AppendAllText(_logPath, header, Encoding.UTF8); } catch { }
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                var info = new FileInfo(_logPath);
                if (info.Length < MaxFileSizeBytes) return;

                string archive = _logPath.Replace(".txt", $"_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.Move(_logPath, archive);
            }
            catch { }
        }
    }
}
