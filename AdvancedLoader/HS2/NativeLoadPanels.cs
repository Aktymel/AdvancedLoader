using System.Linq;
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

        private bool UpdateStudio()
        {
            var outfits = StudioOutfitPanel.IsShown;
            var character = outfits ? StudioOutfitPanel.Character : StudioCharaPanel.IsShown ? StudioCharaPanel.Character : null;
            if (character == null || character.charInfo == null)
            {
                Clear();
                return false;
            }
            OpenMode = outfits ? LoadMode.Coordinate : LoadMode.Character;
            Target = character.charInfo;
            InStudio = true;
            var info = outfits ? StudioOutfitPanel.Selected : StudioCharaPanel.Selected;
            SelectedPath = info?.file;
            SelectedName = info?.name;
            return true;
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
            var canvas = FreeHPanel.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            Rect? result = null;
            var characterCard = FreeHPanel.transform.parent == null ? null : FreeHPanel.transform.parent.Find("CharacterCard");
            foreach (var card in new[] { FreeHPanel.transform, characterCard })
            {
                if (card == null || !card.gameObject.activeInHierarchy) continue;
                foreach (var part in new[] { card.Find("CoodenatePanel"), card.Find("CardImageBG") })
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
            if (!MakerAPI.InsideAndLoaded)
            {
                if (!UpdateStudio()) UpdateFreeH();
                return;
            }

            Target = MakerAPI.GetCharacterControl();
            if (_charaLoadWindow == null) _charaLoadWindow = FindLoadWindow<CvsO_CharaLoad, CustomCharaWindow>("charaLoadWin");
            if (_clothesLoadWindow == null) _clothesLoadWindow = FindLoadWindow<CvsC_ClothesLoad, CustomClothesWindow>("clothesLoadWin");

            if (IsShownOnScreen(_charaLoadWindow))
            {
                OpenMode = LoadMode.Character;
                var info = _charaLoadWindow.GetSelectInfo()?.info;
                SelectedPath = info?.FullPath;
                SelectedName = info?.name;
            }
            else if (IsShownOnScreen(_clothesLoadWindow))
            {
                if (_clothesScroll == null)
                    _clothesScroll = Traverse.Create(_clothesLoadWindow).Field("cscClothes").GetValue<CustomClothesScrollController>();
                OpenMode = LoadMode.Coordinate;
                var info = _clothesScroll?.selectInfo?.info;
                SelectedPath = info?.FullPath;
                SelectedName = info?.name;
            }
            else
            {
                Clear();
            }
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
        public void WindowShown(bool shown)
        {
            var hide = shown && InStudio;
            if (hide == _wearCustomHidden || !Chainloader.PluginInfos.ContainsKey(WearCustomGUID)) return;
            _wearCustomHidden = hide;
            var type = AccessTools.TypeByName("WearCustom.StudioCharaListUtil");
            if (type == null) return;
            foreach (var window in Object.FindObjectsOfType(type))
                Traverse.Create(window).Field("showGUI").SetValue(!hide);
        }

        public bool DrawOpenButton()
        {
            // In Studio only once a card is picked, like the game's buttons
            if (OpenMode == null || (InStudio && SelectedPath == null)) return false;

            var rect = ButtonRect;

            var skin = GUI.skin;
            GUI.skin = IMGUIUtils.SolidBackgroundGuiSkin;
            var clicked = GUI.Button(rect, "Advanced load");
            GUI.skin = skin;
            IMGUIUtils.EatInputInRect(rect);
            return clicked;
        }

        private static TWindow FindLoadWindow<TCanvas, TWindow>(string fieldName) where TCanvas : Component where TWindow : Component
        {
            var canvas = Resources.FindObjectsOfTypeAll<TCanvas>().FirstOrDefault(x => x.gameObject.scene.IsValid());
            return canvas == null ? null : Traverse.Create(canvas).Field(fieldName).GetValue<TWindow>();
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
