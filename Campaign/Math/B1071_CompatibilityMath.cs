using System;

namespace Byzantium1071.Campaign
{
    internal static class B1071_CompatibilityMath
    {
        internal static bool IsNativeAssembly(string? assemblyName)
        {
            if (assemblyName == null || assemblyName.Length == 0) return false;

            return assemblyName.StartsWith("Tale" + "Worlds", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("SandBox", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("StoryMode", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("CustomBattle", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("NavalDLC", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("BirthAndDeath", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Multiplayer", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.StartsWith("Native", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsFrameworkId(string? harmonyId)
        {
            if (harmonyId == null || harmonyId.Length == 0) return true;

            string lowercaseId = harmonyId.ToLowerInvariant();
            return lowercaseId.Contains("taleworlds")     || lowercaseId.Contains("butterlib")        ||
                   lowercaseId.Contains("butlib")         || lowercaseId.Contains(".mcm")             ||
                   lowercaseId.Contains("modlib")         || lowercaseId.Contains("uiextender")       ||
                   lowercaseId.Contains("mboptionscreen") || lowercaseId.Contains("betterexception")  ||
                   lowercaseId.Contains("debugmode")      || lowercaseId.Contains("nativemodule")     ||
                   lowercaseId.Contains("unpatch")        || lowercaseId.Contains("blse")             ||
                   lowercaseId.Contains("launcherex")     ||
                   lowercaseId == "0harmony"              || lowercaseId.StartsWith("0harmony.");
        }
    }
}
