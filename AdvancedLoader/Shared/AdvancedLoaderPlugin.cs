using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Maker;

namespace AdvancedLoader
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInDependency(MoreAccessoriesBridge.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MaterialEditorPresence.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AbmxPresence.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("KCOX", BepInDependency.DependencyFlags.SoftDependency)]
#if HS2
    [BepInProcess("HoneySelect2")]
#elif AI
    [BepInProcess("AI-Syoujyo")]
    [BepInProcess("AI-Shoujo")]
#elif KKS
    [BepInProcess("KoikatsuSunshine")]
#elif KK
    [BepInProcess("Koikatu")]
    [BepInProcess("Koikatsu Party")]
#endif
    public class AdvancedLoaderPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.aktymel.advancedloader";
        public const string PluginName = "Advanced Loader";
        public const string Version = "1.3.0";

        internal static AdvancedLoaderPlugin Instance;
        internal static new ManualLogSource Logger;

        private ConfigEntry<KeyboardShortcut> _openKey;
        private readonly NativeLoadPanels _panels = new NativeLoadPanels();
        private AdvancedLoadWindow _window;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            _openKey = Config.Bind("General", "Open Advanced load", KeyboardShortcut.Empty, "Show or hide the Advanced load window in chara maker.");
            _window = new AdvancedLoadWindow(this, _panels);

            var harmony = Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
            if (MaterialEditorPresence.Available) MaterialEditorBridge.Patch(harmony);
            MakerAPI.MakerExiting += (sender, e) =>
            {
                _window.Reset();
                _panels.Reset();
            };
        }

        private void Start()
        {
            // All plugins are loaded by now, so their versions can be checked
            Dependencies.LogProblems();
        }

        internal void SetWindowVisible(bool visible)
        {
            _window.Visible = visible;
        }

        private void Update()
        {
            if (!MakerAPI.InsideAndLoaded && !NativeLoadPanels.FreeHPanelExists) return;
            _panels.Update();
            if (_openKey.Value.IsDown()) SetWindowVisible(!_window.Visible);
            _window.Update();
        }

        private void OnGUI()
        {
            if (!MakerAPI.InsideAndLoaded && !NativeLoadPanels.FreeHPanelExists) return;
            _panels.DrawNotices();
            if (!_window.Visible && _panels.DrawOpenButton()) SetWindowVisible(true);
            _window.OnGUI();
        }
    }
}
