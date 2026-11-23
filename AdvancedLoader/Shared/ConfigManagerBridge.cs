using System;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace AdvancedLoader
{
    // For an "s" button next to a window's X: GUILayout.Button("s", GUILayout.Width(26))
    internal static class ConfigManagerBridge
    {
        private const string GUID = "com.bepis.bepinex.configurationmanager";

        public static bool Available => Chainloader.PluginInfos.TryGetValue(GUID, out var info) && info.Instance != null;

        private static Func<bool> _getDisplayingWindow;
        public static bool WindowShown => _getDisplayingWindow != null && _getDisplayingWindow();

        public static void BindVisibility()
        {
            if (!Chainloader.PluginInfos.TryGetValue(GUID, out var info) || info.Instance == null) return;
            var getter = AccessTools.PropertyGetter(info.Instance.GetType(), "DisplayingWindow");
            if (getter != null)
                _getDisplayingWindow = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, getter);
        }

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
