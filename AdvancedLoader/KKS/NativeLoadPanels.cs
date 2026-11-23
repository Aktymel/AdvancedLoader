using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using ChaCustom;
using HarmonyLib;
using KKAPI.Maker;
using KKAPI.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedLoader
{
    internal sealed class NativeLoadPanels
    {
        private const string BrowserFoldersGUID = "marco.FolderBrowser";
        private const float Gap = 6;

        private CustomCharaFile _charaFile;
        private CustomCoordinateFile _coordinateFile;
        private static CustomCharaFile _startedCharaFile;
        private static CustomCoordinateFile _startedCoordinateFile;

        // Outfit list of the H scene and the scene it belongs to, set by Hooks when it starts
        public static clothesFileControl FreeHPanel;
        public static string FreeHSceneName;
        public static bool OutsideMakerPanelExists => FreeHPanel != null || StudioOutfitPanel.Exists || StudioCharaPanel.Exists;

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

        public bool InStudio { get; private set; }

        public bool InFreeH => !MakerAPI.InsideAndLoaded && OpenMode != null && !InStudio;

        public string Location => InStudio ? (OpenMode == LoadMode.Character ? "studio chara" : "studio") : InFreeH ? "h" : "maker";

        public string SelectedPath { get; private set; }

        public string SelectedName { get; private set; }

        public void Reset()
        {
            _charaFile = null;
            _coordinateFile = null;
            _pageParts.Clear();
            SetDock(null, null, null);
            Clear();
        }

        private void Clear()
        {
            OpenMode = null;
            Target = null;
            InStudio = false;
            SelectedPath = null;
            SelectedName = null;
        }

        public void Update()
        {
            RunRequestedAdd();
            if (!MakerAPI.InsideAndLoaded)
            {
                if (!UpdateStudio())
                {
                    UpdateFreeH();
                    UpdateStudioDock(null, null);
                }
                return;
            }

            Target = MakerAPI.GetCharacterControl();
            if (_charaFile == null) _charaFile = _startedCharaFile;
            if (_coordinateFile == null) _coordinateFile = _startedCoordinateFile;

            CustomFileWindow window;
            if (IsLoadShown(_charaFile, CustomFileWindow.FileWindowType.CharaLoad, out var charaList, out window))
            {
                OpenMode = LoadMode.Character;
                SetSelection(charaList);
            }
            else if (IsLoadShown(_coordinateFile, CustomFileWindow.FileWindowType.CoordinateLoad, out var coordinateList, out window))
            {
                OpenMode = LoadMode.Coordinate;
                SetSelection(coordinateList);
            }
            else
            {
                Clear();
            }
            UpdateDock(window);
        }

        private static readonly FieldInfo CharaLoadField = AccessTools.Field(typeof(CustomFileWindow), "objCharaLoad");
        private static readonly FieldInfo CoordinateLoadField = AccessTools.Field(typeof(CustomFileWindow), "objCoordinateLoad");

        private GameObject _dockOptions;
        private Button _dockButton;
        private readonly List<RectTransform> _dockParts = new List<RectTransform>();
        private RectTransform _dockWindow;
        private RectTransform _dockList;
        private readonly List<CanvasGroup> _dockHidden = new List<CanvasGroup>();

        // The game's load options in the maker, which Advanced load covers; screen pixels, top left origin
        public Rect? DockRect { get; private set; }

        private void UpdateDock(CustomFileWindow window)
        {
            GameObject options = null;
            Button load = null;
            if (Settings.DockInMaker.Value && window != null && OpenMode != null)
            {
                var character = OpenMode == LoadMode.Character;
                options = (character ? CharaLoadField : CoordinateLoadField)?.GetValue(window) as GameObject;
                load = character ? window.btnChaLoadLoad : window.btnCoordeLoadLoad;
            }
            if (options != _dockOptions)
            {
                SetDock(options, load, window);
                LogDock(window);
            }
            DockRect = options == null ? (Rect?)null : DockArea();
        }

        private void SetDock(GameObject options, Button load, CustomFileWindow window)
        {
            SetNativeHidden(false);
            _dockOptions = options;
            _dockButton = load;
            _dockParts.Clear();
            _dockWindow = null;
            _dockList = null;
            if (options == null) return;
            foreach (Transform child in options.transform)
                if (child is RectTransform rt) _dockParts.Add(rt);
            if (load != null && !load.transform.IsChildOf(options.transform)) _dockParts.Add((RectTransform)load.transform);
            if (window == null) return;
            _dockWindow = (RectTransform)window.transform;
            // The card list: the tallest scroll view above the options
            var top = ScreenRect((RectTransform)options.transform).yMin;
            foreach (var scroll in window.GetComponentsInChildren<ScrollRect>(true))
            {
                var rt = (RectTransform)scroll.transform;
                var rect = ScreenRect(rt);
                if (rect.yMax <= top + 1 && (_dockList == null || rect.height > ScreenRect(_dockList).height)) _dockList = rt;
            }
        }

        // From the card list's bottom to the load window's bottom, as wide as the options
        private Rect? DockArea()
        {
            Rect? area = null;
            var strip = ScreenRect((RectTransform)_dockOptions.transform);
            foreach (var part in _dockParts)
            {
                if (part == null || !part.gameObject.activeInHierarchy) continue;
                var rect = ScreenRect(part);
                // Pop-ups like CoordinateTooglePanel open above the strip
                if (rect.yMin < strip.yMin - 4) continue;
                area = area == null ? rect : Rect.MinMaxRect(Mathf.Min(area.Value.xMin, rect.xMin), Mathf.Min(area.Value.yMin, rect.yMin),
                    Mathf.Max(area.Value.xMax, rect.xMax), Mathf.Max(area.Value.yMax, rect.yMax));
            }
            if (area == null) return null;
            var a = area.Value;
            var options = ScreenRect((RectTransform)_dockOptions.transform);
            var top = _dockList != null && _dockList.gameObject.activeInHierarchy ? Mathf.Min(a.yMin, ScreenRect(_dockList).yMax + 2) : a.yMin;
            var bottom = _dockWindow != null ? Mathf.Max(a.yMax, ScreenRect(_dockWindow).yMax - 6) : a.yMax;
            return Rect.MinMaxRect(Mathf.Min(a.xMin, options.xMin), top, Mathf.Max(a.xMax, options.xMax), bottom);
        }

        private void SetNativeHidden(bool hide)
        {
            if (hide == _dockHidden.Count > 0) return;
            foreach (var group in _dockHidden)
                if (group != null) SetHidden(group, false);
            _dockHidden.Clear();
            if (!hide) return;
            if (InStudio)
            {
                foreach (var button in _studioButtons)
                    if (button != null) _dockHidden.Add(CanvasGroupOf(button.gameObject));
            }
            else if (_dockOptions != null)
            {
                _dockHidden.Add(CanvasGroupOf(_dockOptions));
                if (_dockButton != null && !_dockButton.transform.IsChildOf(_dockOptions.transform)) _dockHidden.Add(CanvasGroupOf(_dockButton.gameObject));
            }
            foreach (var group in _dockHidden) SetHidden(group, true);
        }

        private static CanvasGroup CanvasGroupOf(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }

        private static void SetHidden(CanvasGroup group, bool hidden)
        {
            group.alpha = hidden ? 0 : 1;
            group.interactable = !hidden;
            group.blocksRaycasts = !hidden;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static Rect ScreenRect(RectTransform rt)
        {
            var canvas = UiCache.CanvasOf(rt);
            if (canvas != null) canvas = canvas.rootCanvas;
            var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var height = canvas == null ? Screen.height : canvas.pixelRect.height;
            rt.GetWorldCorners(Corners);
            var min = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
            return new Rect(min.x, height - max.y, max.x - min.x, max.y - min.y);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogDock(CustomFileWindow window)
        {
            if (_dockOptions == null)
            {
                Diag.Log("Dock: none");
                return;
            }
            var sb = new StringBuilder("Dock ").Append(OpenMode).Append(" screen ").Append(Screen.width).Append('x').Append(Screen.height);
            sb.Append("\n  window ").Append(ScreenRect((RectTransform)window.transform));
            sb.Append("\n  options ").Append(_dockOptions.name).Append(' ').Append(ScreenRect((RectTransform)_dockOptions.transform));
            foreach (Transform child in _dockOptions.transform)
                sb.Append("\n    ").Append(child.name).Append(child.gameObject.activeSelf ? " " : " (inactive) ").Append(child is RectTransform rt ? ScreenRect(rt).ToString() : "");
            if (_dockButton != null) sb.Append("\n  load button ").Append(_dockButton.transform.IsChildOf(_dockOptions.transform) ? "(inside) " : "").Append(ScreenRect((RectTransform)_dockButton.transform));
            foreach (var scroll in window.GetComponentsInChildren<ScrollRect>(true))
                sb.Append("\n  scroll ").Append(scroll.name).Append(' ').Append(ScreenRect((RectTransform)scroll.transform));
            sb.Append("\n  dock ").Append(DockArea());
            Diag.Log(sb.ToString());
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

        // The character list also shows with nothing selected: Advanced load then only adds a new character
        private bool UpdateStudio()
        {
            var outfits = StudioOutfitPanel.IsShown;
            var characters = !outfits && StudioCharaPanel.IsShown;
            var character = outfits ? StudioOutfitPanel.Character : characters ? StudioCharaPanel.Character : null;
            if ((character == null || character.charInfo == null) && !characters)
            {
                Clear();
                return false;
            }
            OpenMode = outfits ? LoadMode.Coordinate : LoadMode.Character;
            Target = character?.charInfo;
            InStudio = true;
            var info = outfits ? StudioOutfitPanel.Selected : StudioCharaPanel.Selected;
            SelectedPath = info?.file;
            SelectedName = info?.name;
            UpdateStudioDock(outfits ? OutfitListButtons(StudioOutfitPanel.LoadButton) : CharaListButtons(StudioCharaPanel.LoadButton), outfits ? null : StudioCharaPanel.CardImage);
            return true;
        }

        public bool StudioAdds => InStudio && OpenMode == LoadMode.Character && Target == null;

        public byte DefaultSex => (byte)(InStudio ? StudioCharaPanel.Sex : MakerAPI.GetMakerSex());

        private bool _addRequested;

        public void AddStudioCharacter() => _addRequested = true;

        private void RunRequestedAdd()
        {
            if (!_addRequested) return;
            _addRequested = false;
            var button = StudioCharaPanel.LoadButton;
            if (button == null) return;
            try
            {
                button.onClick.Invoke();
            }
            catch (System.Exception e)
            {
                AdvancedLoaderPlugin.Logger.LogWarning("Adding the character failed: " + e);
            }
        }

        private readonly List<Button> _studioButtons = new List<Button>();
        private Button _buttonsNextTo;
        private Button[] _outfitListButtons = { null };

        private Button[] OutfitListButtons(Button load)
        {
            _outfitListButtons[0] = load;
            return _outfitListButtons;
        }

        private bool SameButtons(Button[] buttons)
        {
            if (buttons == null || buttons.Length != _studioButtons.Count) return false;
            for (var i = 0; i < buttons.Length; i++)
                if (buttons[i] != _studioButtons[i]) return false;
            return true;
        }
        private Button[] _charaListButtons = new Button[0];

        // The game's Load and Change, and buttons other plugins add next to them (e.g. Keep Coordinate Change)
        private Button[] CharaListButtons(Button load)
        {
            if (load == _buttonsNextTo) return _charaListButtons;
            _buttonsNextTo = load;
            _charaListButtons = load == null || load.transform.parent == null ? new Button[0]
                : load.transform.parent.Cast<Transform>().Where(x => x.gameObject.activeSelf).Select(x => x.GetComponent<Button>()).Where(x => x != null).ToArray();
            return _charaListButtons;
        }

        // Under the list, over the game's buttons and the free space below them
        private void UpdateStudioDock(Button[] buttons, Component card)
        {
            if (!Settings.DockInStudio.Value) buttons = null;
            var same = SameButtons(buttons);
            if (!same && (buttons != null || _studioButtons.Count > 0))
            {
                SetNativeHidden(false);
                _studioButtons.Clear();
                if (buttons != null) _studioButtons.AddRange(buttons);
                LogStudioDock();
            }
            if (!InStudio)
            {
                DockRect = null;
                return;
            }
            Rect? area = null;
            foreach (var button in _studioButtons)
            {
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                var rect = ScreenRect((RectTransform)button.transform);
                area = area == null ? rect : Rect.MinMaxRect(Mathf.Min(area.Value.xMin, rect.xMin), Mathf.Min(area.Value.yMin, rect.yMin),
                    Mathf.Max(area.Value.xMax, rect.xMax), Mathf.Max(area.Value.yMax, rect.yMax));
            }
            if (area == null)
            {
                DockRect = null;
                return;
            }
            var a = area.Value;
            var width = Mathf.Max(a.width, Screen.height * 0.5f);
            // The character list lines up with its card picture; otherwise right of BrowserFolders' folder window, which sits under the list
            var x = card != null && card.gameObject.activeInHierarchy ? ScreenRect((RectTransform)card.transform).xMin + 5
                : HasFolderWindow ? Mathf.Max(a.xMin, StudioOutfitPanel.FolderWindow.xMax + Gap) : a.xMin;
            var left = Mathf.Max(4, Mathf.Min(x, Screen.width - width - 4));
            var height = Mathf.Clamp(Screen.height - a.yMin - 8, 160, Screen.height * 0.32f);
            DockRect = new Rect(left, a.yMin, width, height);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogStudioDock()
        {
            var sb = new StringBuilder("Studio dock screen ").Append(Screen.width).Append('x').Append(Screen.height);
            foreach (var button in _studioButtons)
            {
                if (button == null) continue;
                sb.Append("\n  ").Append(button.name).Append(button.gameObject.activeInHierarchy ? " " : " (inactive) ").Append(ScreenRect((RectTransform)button.transform));
                for (var parent = button.transform.parent; parent != null; parent = parent.parent)
                    if (parent is RectTransform rt) sb.Append("\n    < ").Append(parent.name).Append(' ').Append(ScreenRect(rt));
                foreach (var group in button.GetComponentsInParent<CanvasGroup>(true))
                    sb.Append("\n    group ").Append(group.name).Append(" alpha ").Append(group.alpha).Append(group.ignoreParentGroups ? " ignores parents" : "");
                foreach (var canvas in button.GetComponentsInChildren<Canvas>(true))
                    sb.Append("\n    own canvas ").Append(canvas.name);
            }
            var siblings = _studioButtons.FirstOrDefault(x => x != null)?.transform.parent;
            if (siblings != null)
                foreach (Transform child in siblings)
                    sb.Append("\n  sibling ").Append(child.name).Append(child.gameObject.activeSelf ? " " : " (inactive) ").Append(child is RectTransform crt ? ScreenRect(crt).ToString() : "");
            Diag.Log(sb.ToString());
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

        private readonly Dictionary<Component, KeyValuePair<CustomFileWindow, CustomFileListCtrl>> _pageParts = new Dictionary<Component, KeyValuePair<CustomFileWindow, CustomFileListCtrl>>();

        private bool IsLoadShown(Component page, CustomFileWindow.FileWindowType loadType, out CustomFileListCtrl list, out CustomFileWindow window)
        {
            list = null;
            window = null;
            if (page == null) return false;
            if (!_pageParts.TryGetValue(page, out var parts))
            {
                var traverse = Traverse.Create(page);
                parts = new KeyValuePair<CustomFileWindow, CustomFileListCtrl>(traverse.Field("fileWindow").GetValue<CustomFileWindow>(), traverse.Field("listCtrl").GetValue<CustomFileListCtrl>());
                if (parts.Key != null) _pageParts[page] = parts;
            }
            window = parts.Key;
            if (window == null || window.fwType != loadType || !IsShownOnScreen(window))
            {
                window = null;
                return false;
            }
            list = parts.Value;
            return true;
        }

        private const float ButtonHeight = 32;

        // Where BrowserFolders puts its folder window in the maker (MakerFolders.GetDefaultDisplayRect): bottom left
        private static float FolderWindowX => Screen.width * 0.004f;
        private static float FolderWindowY => Screen.height * 0.57f;
        private static float FolderWindowWidth => Screen.width * 0.125f;
        private static bool HasFolderWindow => Chainloader.PluginInfos.ContainsKey(BrowserFoldersGUID);

        private Rect ButtonRect
        {
            get
            {
                if (InStudio)
                {
                    var folders = StudioOutfitPanel.FolderWindow;
                    return new Rect(folders.x, folders.yMax + Gap, folders.width, ButtonHeight);
                }
                return HasFolderWindow
                    ? new Rect(FolderWindowX, FolderWindowY - Gap - ButtonHeight, FolderWindowWidth, ButtonHeight)
                    : new Rect(FolderWindowX, FolderWindowY, FolderWindowWidth, ButtonHeight);
            }
        }

        public Rect DefaultWindowRect(float minWidth, float maxHeight)
        {
            if (InStudio)
            {
                var folders = StudioOutfitPanel.FolderWindow;
                var w = Mathf.Max(minWidth, folders.width);
                return new Rect(folders.xMax + Gap, folders.y, w, Mathf.Clamp(Screen.height - folders.y - 10, 200, maxHeight));
            }

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
        private static GUIStyle _noticeSource;

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
            GuiTheme.Begin();
            if (_noticeSource != GuiTheme.Warning)
            {
                _noticeSource = GuiTheme.Warning;
                _noticeStyle = new GUIStyle(GuiTheme.Warning) { alignment = TextAnchor.MiddleCenter };
            }
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, "Quick load is enabled! Turn off Outfit(HScene) in QuickLoadOption (F1).", _noticeStyle);
            GuiTheme.End();
        }

        public void WindowShown(bool shown)
        {
            LogState(shown);
            SetNativeHidden(shown && DockRect != null);
        }

        private string _state;

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogState(bool shown)
        {
            var state = $"{OpenMode} shown {shown} dock {(DockRect.HasValue ? DockRect.Value.ToString() : "none")} config manager {ConfigManagerBridge.WindowShown}" +
                        (MakerAPI.InsideAndLoaded
                            ? $" | maker: interface visible {MakerAPI.IsInterfaceVisible()}"
                            : InStudio ? " | studio" : InFreeH ? $" | h: overlap/loading {SceneCovered}, scene '{AddSceneName}' (h '{FreeHSceneName}')" : "");
            if (state == _state) return;
            _state = state;
            Diag.Log("State " + state);
        }

        public bool DockButtonsBelow => InStudio;

        public bool CanOpen => OpenMode != null && !(InStudio && SelectedPath == null);

        public bool DrawOpenButton()
        {
            // In Studio only once an outfit is picked, like the game's load button
            if (!CanOpen) return false;

            var rect = ButtonRect;

            GuiTheme.Begin();
            var clicked = GUI.Button(rect, "Advanced load");
            GuiTheme.End();
            IMGUIUtils.EatInputInRect(rect);
            return clicked;
        }

        // The maker's load pages, taken when the game creates them
        public static void Patch(Harmony harmony)
        {
            PatchStart(harmony, typeof(CustomCharaFile), nameof(CharaFileStarted));
            PatchStart(harmony, typeof(CustomCoordinateFile), nameof(CoordinateFileStarted));
        }

        private static void PatchStart(Harmony harmony, System.Type type, string postfix)
        {
            var start = AccessTools.Method(type, "Start");
            if (start == null)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"{type.Name}.Start was not found, the Advanced load button won't show for it");
                return;
            }
            harmony.Patch(start, postfix: new HarmonyMethod(typeof(NativeLoadPanels), postfix));
        }

        private static void CharaFileStarted(CustomCharaFile __instance)
        {
            _startedCharaFile = __instance;
        }

        private static void CoordinateFileStarted(CustomCoordinateFile __instance)
        {
            _startedCoordinateFile = __instance;
        }

        // Maker hides inactive menus by CanvasGroup alpha
        private static bool IsShownOnScreen(Component component)
        {
            if (component == null || !component.gameObject.activeInHierarchy) return false;
            foreach (var group in UiCache.GroupsAbove(component))
            {
                if (group.alpha <= 0.01f) return false;
                if (group.ignoreParentGroups) break;
            }
            return true;
        }
    }
}
