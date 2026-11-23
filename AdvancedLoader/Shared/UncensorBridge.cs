using System;
using System.Reflection;
using HarmonyLib;
using KKAPI;
using KKAPI.Maker;

namespace AdvancedLoader
{
    // Uncensor Selector reads the card in the maker only when Body is loaded
    internal static class UncensorBridge
    {
        private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");
        public static bool Loading;
        private static bool _inReload;

        public static void Patch(Harmony harmony)
        {
            var type = AccessTools.TypeByName("KK_Plugins.UncensorSelector+UncensorSelectorController");
            var reload = type == null ? null : AccessTools.Method(type, "OnReload", new[] { typeof(GameMode), typeof(bool) });
            if (reload == null) return;
            harmony.Patch(reload, new HarmonyMethod(typeof(UncensorBridge), nameof(ReloadPrefix)), finalizer: new HarmonyMethod(typeof(UncensorBridge), nameof(ReloadFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(MakerAPI), nameof(MakerAPI.GetCharacterLoadFlags)), postfix: new HarmonyMethod(typeof(UncensorBridge), nameof(FlagsPostfix)));
        }

        private static void ReloadPrefix()
        {
            _inReload = Loading;
            if (Loading) Diag.Log("Uncensor Selector reads the card");
        }

        private static Exception ReloadFinalizer(Exception __exception)
        {
            _inReload = false;
            return __exception;
        }

        private static void FlagsPostfix(ref CharacterLoadFlags __result)
        {
            if (!_inReload || __result == null || __result.Body) return;
            __result = (CharacterLoadFlags)CloneMethod.Invoke(__result, null);
            __result.Body = true;
        }
    }
}
