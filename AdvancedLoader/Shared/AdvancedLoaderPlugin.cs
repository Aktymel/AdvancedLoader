using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Maker;
using KKAPI.Maker.UI.Sidebar;
using UniRx;

namespace AdvancedLoader
{
    /// <summary>
    /// Adds an "Advanced load" window to the maker's "Load character" and "Clothes > Load" pages that loads chosen parts
    /// of the card selected there: face, body, hair, parameters, clothes, single accessory slots and Material Editor groups.
    /// </summary>
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInDependency(MoreAccessoriesBridge.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MaterialEditorPresence.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(AbmxPresence.GUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("KCOX", BepInDependency.DependencyFlags.SoftDependency)]
#if HS2
    [BepInProcess("HoneySelect2")]
#elif KKS
    [BepInProcess("KoikatsuSunshine")]
#endif
    public class AdvancedLoaderPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.aktymel.advancedloader";
        public const string PluginName = "Advanced Loader";
        public const string Version = "1.1.0";

        internal static AdvancedLoaderPlugin Instance;
        internal static new ManualLogSource Logger;

        private ConfigEntry<KeyboardShortcut> _openKey;
        private readonly NativeLoadPanels _panels = new NativeLoadPanels();
        private AdvancedLoadWindow _window;
        private SidebarToggle _sidebarToggle;

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            _openKey = Config.Bind("General", "Open Advanced load", KeyboardShortcut.Empty, "Show or hide the Advanced load window in chara maker.");
            _window = new AdvancedLoadWindow(this, _panels);

            Harmony.CreateAndPatchAll(typeof(Hooks), GUID);
            MakerAPI.RegisterCustomSubCategories += (sender, e) =>
            {
                _sidebarToggle = e.AddSidebarControl(new SidebarToggle("Advanced load", false, this));
                _sidebarToggle.ValueChanged.Subscribe(visible => _window.Visible = visible);
            };
            MakerAPI.MakerExiting += (sender, e) =>
            {
                _sidebarToggle = null;
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
            if (_sidebarToggle != null) _sidebarToggle.Value = visible;
        }

        private void Update()
        {
            if (!MakerAPI.InsideAndLoaded) return;
            _panels.Update();
            if (_openKey.Value.IsDown()) SetWindowVisible(!_window.Visible);
            _window.Update();
        }

        private void OnGUI()
        {
            if (!MakerAPI.InsideAndLoaded) return;
            if (!_window.Visible && _panels.DrawOpenButton()) SetWindowVisible(true);
            _window.OnGUI();
        }
    }
}
