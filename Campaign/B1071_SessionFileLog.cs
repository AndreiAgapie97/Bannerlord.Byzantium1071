using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Byzantium1071.Campaign
{
    internal static class B1071_SessionFileLog
    {
        private static readonly object Sync = new();
        private static string? _sessionLogPath;
        private static bool _started;

        internal static string? CurrentLogPath => _sessionLogPath;

        internal static void BeginSession()
        {
            lock (Sync)
            {
                if (_started) return;

                try
                {
                    string? resolvedRoot = ResolveModuleLogsRoot();
                    if (string.IsNullOrWhiteSpace(resolvedRoot))
                        return;
                    string root = resolvedRoot!;

                    Directory.CreateDirectory(root);
                    PruneOldLogs(root, keepNewest: 30);

                    string fileName = $"b1071_session_{DateTime.Now:yyyyMMdd_HHmmss}_{Process.GetCurrentProcess().Id}.log";
                    _sessionLogPath = Path.Combine(root, fileName);

                    File.AppendAllText(
                        _sessionLogPath,
                        $"[Byzantium1071][SessionFile] Start {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}");

                    _started = true;
                }
                catch
                {
                    // Never break gameplay if file logging cannot start.
                }
            }
        }

        internal static void EndSession()
        {
            lock (Sync)
            {
                if (!_started || string.IsNullOrWhiteSpace(_sessionLogPath))
                {
                    _started = false;
                    _sessionLogPath = null;
                    return;
                }

                try
                {
                    File.AppendAllText(
                        _sessionLogPath,
                        $"[Byzantium1071][SessionFile] End {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}");
                }
                catch
                {
                    // Never break gameplay if file logging cannot flush end marker.
                }
                finally
                {
                    _started = false;
                    _sessionLogPath = null;
                }
            }
        }

        internal static void WriteTagged(string subsystem, string message)
        {
            lock (Sync)
            {
                if (!_started)
                    BeginSession();

                if (!_started || string.IsNullOrWhiteSpace(_sessionLogPath))
                    return;

                try
                {
                    string stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    string line = $"[{stamp}] [Byzantium1071][{subsystem}] {message}{Environment.NewLine}";
                    File.AppendAllText(_sessionLogPath, line);
                }
                catch
                {
                    // Never break gameplay if file logging write fails.
                }
            }
        }

        private static void PruneOldLogs(string directory, int keepNewest)
        {
            try
            {
                var oldFiles = new DirectoryInfo(directory)
                    .GetFiles("b1071_session_*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(Math.Max(keepNewest, 1));

                foreach (var file in oldFiles)
                {
                    try { file.Delete(); }
                    catch { }
                }
            }
            catch
            {
                // Non-fatal cleanup best effort.
            }
        }

        private static string? ResolveModuleLogsRoot()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            var directory = new DirectoryInfo(Path.GetDirectoryName(dllPath) ?? string.Empty);

            // Expected runtime path:
            // .../Modules/Byzantium1071/bin/Win64_Shipping_Client/Byzantium1071.dll
            // We walk up until we find the "Modules" parent and use the module folder.
            while (directory != null)
            {
                if (directory.Parent != null &&
                    directory.Parent.Name.Equals("Modules", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(directory.FullName, "Logs");
                }

                directory = directory.Parent;
            }

            // Strict mode: do not write anywhere outside module root.
            return null;
        }
    }
}