using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using CharaCustom;
using HarmonyLib;
using AIChara;
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

        private CustomCharaWindow _charaLoadWindow;
        private CustomClothesWindow _clothesLoadWindow;
        private static CvsO_CharaLoad _charaLoad;
        private static CvsC_ClothesLoad _clothesLoad;
        private CustomClothesScrollController _clothesScroll;

        public LoadMode? OpenMode { get; private set; }

        public string SelectedPath { get; private set; }

        public string SelectedName { get; private set; }

        // Outfit list of the H scene, set by Hooks when it starts
        public static HSceneSpriteCoordinatesCard FreeHPanel;
        public static bool OutsideMakerPanelExists => FreeHPanel != null || StudioOutfitPanel.Exists || StudioCharaPanel.Exists;

        public ChaControl Target { get; private set; }

        public bool InStudio { get; private set; }

        public bool InFreeH => !MakerAPI.InsideAndLoaded && OpenMode != null && !InStudio;

        public string Location => InStudio ? (OpenMode == LoadMode.Character ? "studio chara" : "studio") : InFreeH ? "h" : "maker";

        public void Reset()
        {
            _charaLoadWindow = null;
            _clothesLoadWindow = null;
            _clothesScroll = null;
            SetDock(null, null, null, null);
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

        // The game's Load and Change, and buttons other plugins add next to them
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
                if (button != null && button.gameObject.activeInHierarchy) area = Union(area, ScreenRect((RectTransform)button.transform));
            if (area == null)
            {
                DockRect = null;
                return;
            }
            var a = area.Value;
            var width = Mathf.Max(a.width, Screen.height * 0.5f);
            // The character list lines up with its card picture; otherwise right of BrowserFolders' folder window, which sits under the list
            var x = card != null && card.gameObject.activeInHierarchy ? ScreenRect((RectTransform)card.transform).xMin + 5
                : Chainloader.PluginInfos.ContainsKey(BrowserFoldersGUID) ? Mathf.Max(a.xMin, StudioOutfitPanel.FolderWindow.xMax + Gap) : a.xMin;
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
            }
            var siblings = _studioButtons.FirstOrDefault(x => x != null)?.transform.parent;
            if (siblings != null)
                foreach (Transform child in siblings)
                    sb.Append("\n  sibling ").Append(child.name).Append(child.gameObject.activeSelf ? " " : " (inactive) ").Append(child is RectTransform crt ? ScreenRect(crt).ToString() : "");
            Diag.Log(sb.ToString());
        }

        // The game's button our button and window go under: the outfit list's load or the character list's change
        private Rect? StudioButtonRect => OpenMode == LoadMode.Character ? StudioCharaPanel.ChangeButtonRect : StudioOutfitPanel.LoadButtonRect;

        private void UpdateFreeH()
        {
            var panel = FreeHPanel;
            if (!IsShownOnScreen(panel))
            {
                Clear();
                return;
            }
            var traverse = Traverse.Create(panel);
            var manager = traverse.Field("hSceneManager");
            var index = manager.Field("numFemaleClothCustom").GetValue<int>();
            var characters = traverse.Field(index < 2 ? "femailes" : "mailes").GetValue<ChaControl[]>();
            var slot = index < 2 ? index : index - 2;

            OpenMode = LoadMode.Coordinate;
            Target = characters != null && slot < characters.Length ? characters[slot] : null;
            var path = traverse.Field("filename").GetValue<string>();
            SelectedPath = string.IsNullOrEmpty(path) ? null : path;
            SelectedName = SelectedPath == null ? null : traverse.Field("SelectedLabel").GetValue<Text>()?.text;
            if (Target == null) Clear();
        }

        // Visible part of the H outfit panel in GUI coordinates, with HCharaSwitcher's list under it
        private static Rect? FreeHPanelRect()
        {
            if (MakerAPI.InsideAndLoaded || FreeHPanel == null) return null;
            var canvas = UiCache.CanvasOf(FreeHPanel);
            if (canvas == null) return null;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            Rect? result = null;
            var characterCard = FreeHPanel.transform.parent == null ? null : UiCache.Child(FreeHPanel.transform.parent, "CharacterCard");
            foreach (var card in new[] { FreeHPanel.transform, characterCard })
            {
                if (card == null || !card.gameObject.activeInHierarchy) continue;
                foreach (var part in new[] { UiCache.Child(card, "CoodenatePanel"), UiCache.Child(card, "CardImageBG") })
                    result = Union(result, ScreenRect(part as RectTransform, camera));
            }
            return result ?? ScreenRect(FreeHPanel.transform as RectTransform, camera);
        }

        private static Rect? ScreenRect(RectTransform rectTransform, Camera camera)
        {
            if (rectTransform == null || !rectTransform.gameObject.activeInHierarchy) return null;
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
        }

        private static Rect? Union(Rect? a, Rect? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return Rect.MinMaxRect(Mathf.Min(a.Value.xMin, b.Value.xMin), Mathf.Min(a.Value.yMin, b.Value.yMin),
                Mathf.Max(a.Value.xMax, b.Value.xMax), Mathf.Max(a.Value.yMax, b.Value.yMax));
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
            if (_charaLoadWindow == null && _charaLoad != null) _charaLoadWindow = Traverse.Create(_charaLoad).Field("charaLoadWin").GetValue<CustomCharaWindow>();
            if (_clothesLoadWindow == null && _clothesLoad != null) _clothesLoadWindow = Traverse.Create(_clothesLoad).Field("clothesLoadWin").GetValue<CustomClothesWindow>();

            LogMakerState();
            if (IsMakerShown(_charaLoadWindow, _charaLoadGroups))
            {
                OpenMode = LoadMode.Character;
                var info = _charaLoadWindow.GetSelectInfo()?.info;
                SelectedPath = info?.FullPath;
                SelectedName = info?.name;
                UpdateDock(_charaLoadWindow, LoadParts);
            }
            else if (IsMakerShown(_clothesLoadWindow, _clothesLoadGroups))
            {
                if (_clothesScroll == null)
                    _clothesScroll = Traverse.Create(_clothesLoadWindow).Field("cscClothes").GetValue<CustomClothesScrollController>();
                OpenMode = LoadMode.Coordinate;
                var info = _clothesScroll?.selectInfo?.info;
                SelectedPath = info?.FullPath;
                SelectedName = info?.name;
                UpdateDock(_clothesLoadWindow, LoadParts);
            }
            else
            {
                Clear();
                UpdateDock(null, null);
            }
        }

        private sealed class DockParts
        {
            public Toggle[] Toggles;
            public Button[] Buttons;
            public Component[] Keep;
        }

        // Private in the game, public only in the reference assemblies
        private static DockParts LoadParts(Component window)
        {
            var traverse = Traverse.Create(window);
            return new DockParts
            {
                Toggles = traverse.Field("tglLoadOption").GetValue<Toggle[]>(),
                Buttons = traverse.Field("button").GetValue<Button[]>(),
                Keep = new Component[] { traverse.Field("btnShowWinSort").GetValue<Button>(), traverse.Field("tglSortOrder").GetValue<Toggle>() }
            };
        }

        private Component _dockSource;
        private readonly List<Transform> _dockHideTargets = new List<Transform>();
        private readonly List<RectTransform> _dockParts = new List<RectTransform>();
        private RectTransform _dockWindow;
        private RectTransform _dockList;
        private readonly List<CanvasGroup> _dockHidden = new List<CanvasGroup>();

        // The game's load options in the maker, which Advanced load covers; screen pixels, top left origin
        public Rect? DockRect { get; private set; }

        private void UpdateDock(Component window, System.Func<Component, DockParts> parts)
        {
            if (!Settings.DockInMaker.Value || OpenMode == null) window = null;
            if (window != _dockSource)
            {
                var found = window == null ? null : parts(window);
                SetDock(window, found?.Toggles, found?.Buttons, found?.Keep);
                LogDock();
            }
            DockRect = _dockSource == null ? (Rect?)null : DockArea();
        }

        private void SetDock(Component window, Toggle[] toggles, Button[] buttons, Component[] keep)
        {
            SetNativeHidden(false);
            _dockSource = window;
            _dockHideTargets.Clear();
            _dockParts.Clear();
            _dockWindow = null;
            _dockList = null;
            if (window == null) return;
            foreach (var toggle in toggles ?? new Toggle[0])
                if (toggle != null) _dockParts.Add((RectTransform)toggle.transform);
            foreach (var button in buttons ?? new Button[0])
                if (button != null && button.gameObject.activeInHierarchy) _dockParts.Add((RectTransform)button.transform);
            if (_dockParts.Count == 0)
            {
                _dockSource = null;
                return;
            }
            _dockWindow = (RectTransform)window.transform;

            var top = _dockParts.Where(x => x.gameObject.activeInHierarchy).Select(x => ScreenRect(x).yMin).DefaultIfEmpty(0).Min();
            foreach (var scroll in window.GetComponentsInChildren<ScrollRect>(true))
            {
                var rt = (RectTransform)scroll.transform;
                var rect = ScreenRect(rt);
                if (rect.yMax <= top + 1 && (_dockList == null || rect.height > ScreenRect(_dockList).height)) _dockList = rt;
            }

            // Each toggle's or button's own container is hidden, with what other plugins added to it (KKAPI's load toggles)
            var kept = keep.Where(x => x != null).Select(x => x.transform).ToList();
            if (_dockList != null) kept.Add(_dockList);
            foreach (var part in _dockParts)
            {
                var parent = part.parent;
                var target = parent != null && parent != _dockWindow && !kept.Any(x => x.IsChildOf(parent)) ? parent : part;
                if (!_dockHideTargets.Contains(target)) _dockHideTargets.Add(target);
            }
            foreach (var target in _dockHideTargets)
            {
                if (_dockParts.Contains(target)) continue;
                foreach (Transform child in target)
                    if (child is RectTransform rt && !_dockParts.Contains(rt)) _dockParts.Add(rt);
            }
        }

        // From the card list's bottom to the load window's bottom
        private Rect? DockArea()
        {
            Rect? area = null;
            foreach (var part in _dockParts)
                if (part != null && part.gameObject.activeInHierarchy) area = Union(area, ScreenRect(part));
            if (area == null) return null;
            var a = area.Value;
            var list = _dockList != null && _dockList.gameObject.activeInHierarchy ? ScreenRect(_dockList) : (Rect?)null;
            var top = list != null ? Mathf.Min(a.yMin, list.Value.yMax + 2) : a.yMin;
            var right = list != null ? Mathf.Max(a.xMax, list.Value.xMax) : a.xMax;
            var bottom = a.yMax;
            if (_dockWindow != null)
            {
                var window = ScreenRect(_dockWindow);
                right = Mathf.Min(right, window.xMax - 6);
                bottom = Mathf.Max(bottom, window.yMax - 6);
            }
            // The game's window is narrow, so the panel also takes the free space left of it
            var left = Mathf.Max(0, Mathf.Min(a.xMin, right - Screen.height * 0.52f));
            bottom = Mathf.Max(bottom, top + Screen.height * 0.2f);
            return Rect.MinMaxRect(left, top, right, Mathf.Min(bottom, Screen.height - 4));
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
            else if (_dockSource != null)
            {
                foreach (var target in _dockHideTargets)
                    if (target != null) _dockHidden.Add(CanvasGroupOf(target.gameObject));
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
        private void LogDock()
        {
            if (_dockSource == null)
            {
                Diag.Log("Dock: none");
                return;
            }
            var sb = new StringBuilder("Dock ").Append(OpenMode).Append(" screen ").Append(Screen.width).Append('x').Append(Screen.height);
            sb.Append("\n  window ").Append(_dockWindow.name).Append(' ').Append(ScreenRect(_dockWindow));
            foreach (Transform child in _dockWindow)
                sb.Append("\n    ").Append(child.name).Append(child.gameObject.activeSelf ? " " : " (inactive) ").Append(child is RectTransform crt ? ScreenRect(crt).ToString() : "");
            sb.Append("\n  hidden ").Append(string.Join(", ", _dockHideTargets.Select(x => x.name).ToArray()));
            foreach (var part in _dockParts)
                sb.Append("\n    ").Append(part.name).Append(" < ").Append(part.parent == null ? "" : part.parent.name).Append(part.gameObject.activeInHierarchy ? " " : " (inactive) ").Append(ScreenRect(part));
            foreach (var scroll in _dockWindow.GetComponentsInChildren<ScrollRect>(true))
                sb.Append("\n  scroll ").Append(scroll.name).Append(' ').Append(ScreenRect((RectTransform)scroll.transform));
            sb.Append("\n  list ").Append(_dockList == null ? "(none)" : _dockList.name);
            sb.Append("\n  dock ").Append(DockArea());
            Diag.Log(sb.ToString());
        }

        private string _makerState;

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogMakerState()
        {
            var state = $"page {(_charaLoad != null ? "found" : "missing")}, window {Describe(_charaLoadWindow, _charaLoadGroups)}; " +
                        $"outfit page {(_clothesLoad != null ? "found" : "missing")}, window {Describe(_clothesLoadWindow, _clothesLoadGroups)}";
            if (state == _makerState) return;
            _makerState = state;
            Diag.Log("Maker load " + state);
        }

        private static string Describe(Component window, CanvasGroup[] pageGroups)
        {
            if (window == null) return "missing";
            if (!window.gameObject.activeInHierarchy) return "inactive";
            var hidden = UiCache.GroupsAbove(window).Where(x => x.alpha <= 0.01f).Select(x => x.name).ToArray();
            var blocked = pageGroups == null ? new string[0] : pageGroups.Where(x => x != null && !x.interactable).Select(x => x.name).ToArray();
            return (hidden.Length == 0 ? "visible" : "hidden by " + string.Join("/", hidden)) +
                   (blocked.Length == 0 ? "" : ", not interactable: " + string.Join("/", blocked));
        }

        private static CanvasGroup[] _charaLoadGroups;
        private static CanvasGroup[] _clothesLoadGroups;
        private string _shownState;

        // Same check as BrowserFolders: the load page's groups turn non-interactable while something covers the maker
        private bool IsMakerShown(Component window, CanvasGroup[] pageGroups)
        {
            if (!IsShownOnScreen(window)) return false;
            var interactable = pageGroups == null || pageGroups.All(x => x == null || x.interactable);
            var state = window.name + (interactable ? " shown" : " covered");
            if (state != _shownState)
            {
                _shownState = state;
                Diag.Log("Maker load window " + state);
            }
            return interactable;
        }

        public static Vector2 DefaultPosition
        {
            get
            {
                var y = Screen.height * 0.17f;
                if (Chainloader.PluginInfos.ContainsKey(BrowserFoldersGUID)) y += Screen.height * 0.4f + Gap;
                return new Vector2(Screen.width * 0.623f, y);
            }
        }

        private const string WearCustomGUID = "GarryWu.HS2WearCustom";
        // Height of Studio Wear Custom's window under the game's button: ClothOnly / AccOnly, or the character Replace toggles
        private float WearCustomHeight => !Chainloader.PluginInfos.ContainsKey(WearCustomGUID) ? 0 : OpenMode == LoadMode.Character ? 5 + 180 : 5 + 100;

        // In H the button sits under the outfit panel and the window opens right of it; in Studio under the list's button
        private Rect ButtonRect
        {
            get
            {
                if (InStudio)
                {
                    var game = StudioButtonRect;
                    if (game == null)
                    {
                        var folders = StudioOutfitPanel.FolderWindow;
                        return new Rect(folders.x, folders.yMax + Gap, folders.width, 32);
                    }
                    return new Rect(game.Value.x, game.Value.yMax + WearCustomHeight + Gap, game.Value.width, 32);
                }
                var panel = FreeHPanelRect();
                if (panel == null)
                {
                    var position = DefaultPosition;
                    return new Rect(position.x, position.y, Screen.width * 0.125f, 32);
                }
                var p = panel.Value;
                var y = p.yMax + Gap + 32 <= Screen.height ? p.yMax + Gap : p.y - Gap - 32;
                return new Rect(p.x, y, Mathf.Min(p.width, Screen.width * 0.125f), 32);
            }
        }

        public Rect DefaultWindowRect(float minWidth, float maxHeight)
        {
            if (InStudio)
            {
                // Under the game's button, over Wear Custom's window (hidden while ours is open)
                var game = StudioButtonRect ?? ButtonRect;
                var top = game.yMax + Gap;
                return new Rect(game.x, top, Mathf.Max(minWidth, game.width), Mathf.Clamp(Screen.height - top - 10, 200, maxHeight));
            }
            var panel = FreeHPanelRect();
            if (panel != null)
            {
                var p = panel.Value;
                var w = Mathf.Max(minWidth, Screen.width * 0.125f);
                var h = Mathf.Clamp(Screen.height - p.y - 10, 200, maxHeight);
                return new Rect(Mathf.Min(p.xMax + Gap, Screen.width - w - 10), Mathf.Max(10, p.y), w, h);
            }

            var position = DefaultPosition;
            var height = Mathf.Clamp(Screen.height - position.y - 10, 200, maxHeight);
            return new Rect(position.x, position.y, Mathf.Max(minWidth, Screen.width * 0.125f), height);
        }

        public void DrawNotices()
        {
        }

        private bool _wearCustomHidden;

        // Studio Wear Custom pops its windows up over ours when a card is clicked, so they are hidden while ours is open
        private string _state;

#if AI
        private static bool SceneCovered => Singleton<Manager.Scene>.Instance == null || Singleton<Manager.Scene>.Instance.IsOverlap;
#else
        private static bool SceneCovered => Manager.Scene.IsOverlap;
#endif

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogState(bool shown)
        {
            var state = $"{OpenMode} shown {shown} dock {(DockRect.HasValue ? DockRect.Value.ToString() : "none")} config manager {ConfigManagerBridge.WindowShown}" +
                        (InStudio ? " | studio" : MakerAPI.InsideAndLoaded ? $" | maker: overlap {SceneCovered}, load window {(_charaLoadWindow != null ? _charaLoadWindow.gameObject.activeInHierarchy.ToString() : "missing")}" : "");
            if (state == _state) return;
            _state = state;
            Diag.Log("State " + state);
        }

        public void WindowShown(bool shown)
        {
            LogState(shown);
            SetNativeHidden(shown && DockRect != null);
            var hide = shown && InStudio;
            if (hide == _wearCustomHidden || !Chainloader.PluginInfos.ContainsKey(WearCustomGUID)) return;
            _wearCustomHidden = hide;
            var type = AccessTools.TypeByName("WearCustom.StudioCharaListUtil");
            if (type == null) return;
            foreach (var window in Object.FindObjectsOfType(type))
                Traverse.Create(window).Field("showGUI").SetValue(!hide);
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

        public bool DockButtonsBelow => true;

        public bool CanOpen => OpenMode != null && !(InStudio && SelectedPath == null);

        public bool DrawOpenButton()
        {
            // In Studio only once a card is picked, like the game's buttons
            if (!CanOpen) return false;

            var rect = ButtonRect;

            GuiTheme.Begin();
            var clicked = GUI.Button(rect, "Advanced load");
            GuiTheme.End();
            IMGUIUtils.EatInputInRect(rect);
            return clicked;
        }

        // The maker's load windows, taken when the game creates them
        public static void Patch(Harmony harmony)
        {
            PatchStart(harmony, typeof(CvsO_CharaLoad), nameof(CharaLoadStarted));
            PatchStart(harmony, typeof(CvsC_ClothesLoad), nameof(ClothesLoadStarted));
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

        private static void CharaLoadStarted(CvsO_CharaLoad __instance)
        {
            _charaLoad = __instance;
            _charaLoadGroups = __instance.GetComponentsInParent<CanvasGroup>(true);
        }

        private static void ClothesLoadStarted(CvsC_ClothesLoad __instance)
        {
            _clothesLoad = __instance;
            _clothesLoadGroups = __instance.GetComponentsInParent<CanvasGroup>(true);
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
