using System.Reflection;
using HarmonyLib;

namespace AdvancedLoader
{
    // MoreAccessories 2 keeps all slots in ChaFileAccessory.parts; no-op without it
    internal static class MoreAccessoriesBridge
    {
        public const string GUID = "com.joan6694.illusionplugins.moreaccessories";

        private static bool _initialized;
        private static MethodInfo _arraySync;
        private static MethodInfo _updateUI;

        public static bool Available
        {
            get
            {
                Init();
                return _arraySync != null;
            }
        }

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            if (!Dependencies.MoreAccessories.IsOk) return;

            var type = AccessTools.TypeByName("MoreAccessoriesKOI.MoreAccessories");
            if (type == null) return;
            _arraySync = AccessTools.Method(type, "ArraySync", new[] { typeof(ChaControl) });
            _updateUI = AccessTools.Method(type, "ManuallyUpdateUI");
        }

        public static void Sync(ChaControl chaCtrl)
        {
            if (!Available) return;
            _arraySync.Invoke(null, new object[] { chaCtrl });
            _updateUI?.Invoke(null, null);
        }
    }
}
