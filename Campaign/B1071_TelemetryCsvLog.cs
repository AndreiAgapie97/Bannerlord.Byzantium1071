using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Byzantium1071.Campaign
{
    /// <summary>
    /// One CSV file per play session, one row per campaign day, written beside the session
    /// log in the module's Logs folder.
    ///
    /// WHY A SEPARATE FILE: the session log is prose meant to be read top to bottom; the
    /// daily numbers are only useful plotted against each other over dozens of in-game days.
    /// Interleaving them would mean hand-extracting rows from a log full of unrelated
    /// subsystem chatter before any trend could be seen. The digest lines in the session log
    /// and the rows here carry the same figures deliberately — the log answers "what happened
    /// just now", the CSV answers "what has been happening".
    ///
    /// Shares B1071_SessionFileLog's Logs-folder resolution, its lock-and-never-throw
    /// discipline, and its 30-file prune. Nothing here may throw into gameplay.
    /// </summary>
    internal static class B1071_TelemetryCsvLog
    {
        private static readonly object Sync = new();
        private static string? _csvPath;
        private static bool _started;
        private static bool _failed;

        internal static string? CurrentCsvPath => _csvPath;

        /// <summary>
        /// Closes the current file so the next write opens a fresh one. Called when a campaign
        /// ends, because day numbers restart and appending would produce a CSV whose day
        /// column runs backwards halfway down.
        /// </summary>
        internal static void EndSession()
        {
            lock (Sync)
            {
                _started = false;
                _failed = false;
                _csvPath = null;
            }
        }

        /// <summary>
        /// Appends one row, creating the file and writing the header on first use. A row is
        /// dropped rather than retried if the file cannot be opened; <see cref="_failed"/>
        /// makes that decision once instead of re-attempting every campaign day.
        /// </summary>
        internal static void AppendRow(string row)
        {
            if (string.IsNullOrEmpty(row)) return;

            lock (Sync)
            {
                if (_failed) return;
                if (!_started && !TryStart()) return;

                try
                {
                    File.AppendAllText(_csvPath!, row + Environment.NewLine);
                }
                catch
                {
                    // Never break gameplay if a telemetry row cannot be written.
                }
            }
        }

        private static bool TryStart()
        {
            try
            {
                string? resolvedRoot = B1071_SessionFileLog.ResolveModuleLogsRoot();
                if (string.IsNullOrWhiteSpace(resolvedRoot))
                {
                    _failed = true;
                    return false;
                }

                string root = resolvedRoot!;
                Directory.CreateDirectory(root);
                PruneOldCsvs(root, keepNewest: 30);

                string fileName = $"b1071_telemetry_{DateTime.Now:yyyyMMdd_HHmmss}_{Process.GetCurrentProcess().Id}.csv";
                _csvPath = Path.Combine(root, fileName);

                File.AppendAllText(_csvPath, B1071_TelemetryMath.CsvHeader() + Environment.NewLine);
                _started = true;
                return true;
            }
            catch
            {
                _failed = true;
                return false;
            }
        }

        private static void PruneOldCsvs(string directory, int keepNewest)
        {
            try
            {
                var oldFiles = new DirectoryInfo(directory)
                    .GetFiles("b1071_telemetry_*.csv")
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
    }
}
