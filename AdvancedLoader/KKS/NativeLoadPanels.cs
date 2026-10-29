using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using ChaCustom;
using HarmonyLib;
using KKAPI.Maker;
using KKAPI.Utilities;
using UnityEngine;

namespace AdvancedLoader
{
    internal sealed class NativeLoadPanels
    {
        private const string BrowserFoldersGUID = "marco.FolderBrowser";
        private const float Gap = 6;

        private CustomCharaFile _charaFile;
        private CustomCoordinateFile _coordinateFile;

        // Outfit list of the H scene and the scene it belongs to, set by Hooks when it starts
        public static clothesFileControl FreeHPanel;
        public static string FreeHSceneName;
        public static bool FreeHPanelExists => FreeHPanel != null;

        // Same check as BrowserFolders: hidden while settings or another screen is on top
#if KK
        public static string AddSceneName => Singleton<Manager.Scene>.Instance?.AddSceneName;
        private static bool SceneCovered => Singleton<Manager.Scene>.Instance == null || Singleton<Manager.Scene>.Instance.IsOverlap || Singleton<Manager.Scene>.Instance.IsNowLoadingFade;
#else
        public static string AddSceneName => Manager.Scene.AddSceneName;
        private static bool SceneCovered => Manager.Scene.IsOverlap || Manager.Scene.IsNowLoadingFade;
#endif

        public LoadMode? OpenMode { get; private set; }

        public ChaControl Target { get; private set; }

        public bool InFreeH => !MakerAPI.InsideAndLoaded && OpenMode != null;

        public string SelectedPath { get; private set; }

        public string SelectedName { get; private set; }

        public void Reset()
        {
            _charaFile = null;
            _coordinateFile = null;
            Clear();
        }

        private void Clear()
        {
            OpenMode = null;
            Target = null;
            SelectedPath = null;
            SelectedName = null;
        }

        public void Update()
        {
            if (!MakerAPI.InsideAndLoaded)
            {
                UpdateFreeH();
                return;
            }

            Target = MakerAPI.GetCharacterControl();
            if (_charaFile == null) _charaFile = FindInScene<CustomCharaFile>();
            if (_coordinateFile == null) _coordinateFile = FindInScene<CustomCoordinateFile>();

            if (IsLoadShown(_charaFile, CustomFileWindow.FileWindowType.CharaLoad, out var charaList))
            {
                OpenMode = LoadMode.Character;
                SetSelection(charaList);
            }
            else if (IsLoadShown(_coordinateFile, CustomFileWindow.FileWindowType.CoordinateLoad, out var coordinateList))
            {
                OpenMode = LoadMode.Coordinate;
                SetSelection(coordinateList);
            }
            else
            {
                Clear();
            }
        }

        private void UpdateFreeH()
        {
            var panel = FreeHPanel;
            if (panel == null || !panel.gameObject.activeInHierarchy || panel.chaCtrl == null || SceneCovered || AddSceneName != FreeHSceneName)
            {
                Clear();
                return;
            }
            OpenMode = LoadMode.Coordinate;
            Target = panel.chaCtrl;
            SetSelection(panel.listCtrl);
        }

        private void SetSelection(CustomFileListCtrl list)
        {
#if KK
            // Koikatsu Party's list differs, so read by name
            var item = list == null ? null : Traverse.Create(list).Method("GetSelectTopItem").GetValue();
            var info = item == null ? null : ReadMember(item, "info");
            SelectedPath = info == null ? null : ReadMember(info, "FullPath") as string;
            SelectedName = info == null ? null : ReadMember(info, "name") as string;
#else
            var info = list?.GetSelectTopItem()?.info;
            SelectedPath = info?.FullPath;
            SelectedName = info?.name;
#endif
        }

#if KK
        private static object ReadMember(object obj, string name)
        {
            var traverse = Traverse.Create(obj);
            return traverse.Field(name).FieldExists() ? traverse.Field(name).GetValue() : traverse.Property(name).GetValue();
        }
#endif

        private static bool IsLoadShown(Component page, CustomFileWindow.FileWindowType loadType, out CustomFileListCtrl list)
        {
            list = null;
            if (page == null) return false;
            var traverse = Traverse.Create(page);
            var window = traverse.Field("fileWindow").GetValue<CustomFileWindow>();
            if (window == null || window.fwType != loadType || !IsShownOnScreen(window)) return false;
            list = traverse.Field("listCtrl").GetValue<CustomFileListCtrl>();
            return true;
        }

        private const float ButtonHeight = 32;

        // Where BrowserFolders puts its folder window in the maker (MakerFolders.GetDefaultDisplayRect): bottom left
        private static float FolderWindowX => Screen.width * 0.004f;
        private static float FolderWindowY => Screen.height * 0.57f;
        private static float FolderWindowWidth => Screen.width * 0.125f;
        private static bool HasFolderWindow => Chainloader.PluginInfos.ContainsKey(BrowserFoldersGUID);

        private static Rect ButtonRect => HasFolderWindow
            ? new Rect(FolderWindowX, FolderWindowY - Gap - ButtonHeight, FolderWindowWidth, ButtonHeight)
            : new Rect(FolderWindowX, FolderWindowY, FolderWindowWidth, ButtonHeight);

        public static Rect DefaultWindowRect(float minWidth, float maxHeight)
        {
            var button = ButtonRect;
            var width = Mathf.Max(minWidth, FolderWindowWidth);
            if (!HasFolderWindow)
                return new Rect(button.x, button.y, width, Mathf.Clamp(Screen.height - button.y - 10, 200, maxHeight));

            var bottom = button.yMax;
            var height = Mathf.Clamp(bottom - 10, 200, maxHeight);
            return new Rect(button.x, Mathf.Max(10, bottom - height), width, height);
        }

        private const string QuickLoadGUID = "QuickLoad";
        private static ConfigEntryBase _quickLoadOutfit;
        private static bool _quickLoadChecked;
        private static GUIStyle _noticeStyle;

        // QuickLoadOption loads an outfit in H as soon as it is clicked
        private static bool QuickLoadLoadsOnClick
        {
            get
            {
                if (!_quickLoadChecked)
                {
                    _quickLoadChecked = true;
                    if (Chainloader.PluginInfos.TryGetValue(QuickLoadGUID, out var info) && info.Instance != null)
                    {
                        var definition = new ConfigDefinition("Hide confirmation dialog", "Outfit(HScene)");
                        if (info.Instance.Config.ContainsKey(definition)) _quickLoadOutfit = info.Instance.Config[definition];
                    }
                }
                return _quickLoadOutfit != null && _quickLoadOutfit.BoxedValue is bool on && on;
            }
        }

        public void DrawNotices()
        {
            if (!InFreeH || !QuickLoadLoadsOnClick) return;

            var button = ButtonRect;
            var rect = new Rect(button.x, button.y - Gap - 46, button.width, 46);
            var skin = GUI.skin;
            GUI.skin = IMGUIUtils.SolidBackgroundGuiSkin;
            if (_noticeStyle == null)
            {
                _noticeStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true, alignment = TextAnchor.MiddleCenter };
                _noticeStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
            }
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, "Quick load is enabled! Turn off Outfit(HScene) in QuickLoadOption (F1).", _noticeStyle);
            GUI.skin = skin;
        }

        public bool DrawOpenButton()
        {
            if (OpenMode == null) return false;

            var rect = ButtonRect;

            var skin = GUI.skin;
            GUI.skin = IMGUIUtils.SolidBackgroundGuiSkin;
            var clicked = GUI.Button(rect, "Advanced load");
            GUI.skin = skin;
            IMGUIUtils.EatInputInRect(rect);
            return clicked;
        }

        private static T FindInScene<T>() where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(x => x.gameObject.scene.IsValid());
        }

        // Maker hides inactive menus by CanvasGroup alpha
        private static bool IsShownOnScreen(Component component)
        {
            if (component == null || !component.gameObject.activeInHierarchy) return false;
            foreach (var group in component.GetComponentsInParent<CanvasGroup>())
            {
                if (group.alpha <= 0.01f) return false;
                if (group.ignoreParentGroups) break;
            }
            return true;
        }
    }
}
