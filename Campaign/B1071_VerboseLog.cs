using Byzantium1071.Campaign.Settings;
using TaleWorlds.Library;

namespace Byzantium1071.Campaign
{
    /// <summary>
    /// Central verbose-logging gate. Every system calls <see cref="Log"/> instead of
    /// repeating the settings check. Output goes to Bannerlord's rgl_log file via
    /// <see cref="Debug.Print"/>, never to in-game UI.
    /// </summary>
    internal static class B1071_VerboseLog
    {
        /// <summary>
        /// Returns true when the master verbose toggle is ON.
        /// </summary>
        internal static bool Enabled =>
            (B1071_McmSettings.Instance ?? B1071_McmSettings.Defaults).EnableVerboseModLog;

        /// <summary>
        /// Writes a tagged message to rgl_log when verbose logging is enabled.
        /// Caller supplies the subsystem tag and message body.
        /// Example: <c>VLog.Log("Manpower", "Regen +42 for town_ES1");</c>
        /// Output:  <c>[Byzantium1071][Manpower] Regen +42 for town_ES1</c>
        /// </summary>
        internal static void Log(string subsystem, string message)
        {
            if (!Enabled) return;
            Debug.Print($"[Byzantium1071][{subsystem}] {message}");
            B1071_SessionFileLog.WriteTagged(subsystem, message);
        }
    }
}
