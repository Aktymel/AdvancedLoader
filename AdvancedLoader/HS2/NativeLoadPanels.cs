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
        public static bool FreeHPanelExists => FreeHPanel != null;

        public ChaControl Target { get; private set; }

        public bool InFreeH => !MakerAPI.InsideAndLoaded && OpenMode != null;

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
            SelectedPath = null;
            SelectedName = null;
        }

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
                UpdateFreeH();
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

        // In H the button sits under the outfit panel and the window opens right of it
        private static Rect ButtonRect
        {
            get
            {
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

        public static Rect DefaultWindowRect(float minWidth, float maxHeight)
        {
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
