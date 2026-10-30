using BepInEx;
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
    [BepInProcess("StudioNEOV2")]
#elif AI
    [BepInProcess("AI-Syoujyo")]
    [BepInProcess("AI-Shoujo")]
    [BepInProcess("StudioNEOV2")]
#elif KKS
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("CharaStudio")]
#elif KK
    [BepInProcess("Koikatu")]
    [BepInProcess("Koikatsu Party")]
    [BepInProcess("CharaStudio")]
#endif
    public class AdvancedLoaderPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.aktymel.advancedloader";
        public const string PluginName = "Advanced Loader";
        public const string Version = "1.4.2";

        internal static AdvancedLoaderPlugin Instance;
        internal static new ManualLogSource Logger;

        private readonly NativeLoadPanels _panels = new NativeLoadPanels();
        private AdvancedLoadWindow _window;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            Settings.Bind(Config);
            LimitedCardData.Init();
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

        private void OnApplicationQuit()
        {
            _window.SaveTicks();
        }

        internal void SetWindowVisible(bool visible)
        {
            _window.Visible = visible;
        }

        private void Update()
        {
            if (!MakerAPI.InsideAndLoaded && !NativeLoadPanels.OutsideMakerPanelExists) return;
            var openBefore = _panels.OpenMode;
            _panels.Update();
            if (Settings.OpenAutomatically.Value && _panels.OpenMode != null && _panels.OpenMode != openBefore) SetWindowVisible(true);
            _window.Update();
            _panels.WindowShown(_window.Shown);
        }

        private void OnGUI()
        {
            if (!MakerAPI.InsideAndLoaded && !NativeLoadPanels.OutsideMakerPanelExists) return;
            _panels.DrawNotices();
            if (!_window.Visible && _panels.DrawOpenButton()) SetWindowVisible(true);
            _window.OnGUI();
        }
    }
}
