using BepInEx.Bootstrap;
using HarmonyLib;

namespace AdvancedLoader
{
    internal static class ConfigManagerBridge
    {
        private const string GUID = "com.bepis.bepinex.configurationmanager";

        public static bool Available => Chainloader.PluginInfos.TryGetValue(GUID, out var info) && info.Instance != null;

        // Opens the F1 window with the search box filled in
        public static void Open(string search)
        {
            if (!Chainloader.PluginInfos.TryGetValue(GUID, out var info) || info.Instance == null) return;
            var manager = Traverse.Create(info.Instance);
            manager.Property("DisplayingWindow").SetValue(true);
            manager.Property("SearchString").SetValue(search);
        }
    }
}
