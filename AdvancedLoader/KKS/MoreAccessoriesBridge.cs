using System.Reflection;
using HarmonyLib;

namespace AdvancedLoader
{
    /// <summary>
    /// MoreAccessories 2 (Koikatsu Sunshine) keeps every slot in ChaFileAccessory.parts itself, so slots 21+ are read and
    /// written like the first 20. After the array changes size the plugin has to resize its own per-slot data and UI.
    /// Every member is a no-op when MoreAccessories is not installed.
    /// </summary>
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

        /// <summary>
        /// Let MoreAccessories catch up with a changed slot count (accessory objects, visibility flags, maker slots).
        /// </summary>
        public static void Sync(ChaControl chaCtrl)
        {
            if (!Available) return;
            _arraySync.Invoke(null, new object[] { chaCtrl });
            _updateUI?.Invoke(null, null);
        }
    }
}
