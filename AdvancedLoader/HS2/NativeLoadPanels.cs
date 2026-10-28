using System.Linq;
using BepInEx.Bootstrap;
using CharaCustom;
using HarmonyLib;
using KKAPI.Utilities;
using UnityEngine;

namespace AdvancedLoader
{
    /// <summary>
    /// Watches the game's "Load character" and "Clothes > Load" panels: which one is open and which card is selected in it.
    /// Also draws the "Advanced load" button while one of them is open. The HS2API sidebar ("Plugin Settings") is not
    /// used for this because other plugins can hide it.
    /// </summary>
    internal sealed class NativeLoadPanels
    {
        private const string BrowserFoldersGUID = "marco.FolderBrowser";
        private const float Gap = 6;

        private CustomCharaWindow _charaLoadWindow;
        private CustomClothesWindow _clothesLoadWindow;
        private CustomClothesScrollController _clothesScroll;

        /// <summary>Mode of the load panel that is open, or null when none is.</summary>
        public LoadMode? OpenMode { get; private set; }

        /// <summary>Card selected in the open load panel, or null.</summary>
        public string SelectedPath { get; private set; }

        /// <summary>Name shown for the selected card in the game's list (character name or outfit name).</summary>
        public string SelectedName { get; private set; }

        public void Reset()
        {
            _charaLoadWindow = null;
            _clothesLoadWindow = null;
            _clothesScroll = null;
            OpenMode = null;
            SelectedPath = null;
            SelectedName = null;
        }

        public void Update()
        {
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
                OpenMode = null;
                SelectedPath = null;
                SelectedName = null;
            }
        }

        /// <summary>
        /// Where BrowserFolders puts its folder window by default (MakerFolders.GetDefaultDisplayRect), moved below it when installed.
        /// </summary>
        public static Vector2 DefaultPosition
        {
            get
            {
                var y = Screen.height * 0.17f;
                if (Chainloader.PluginInfos.ContainsKey(BrowserFoldersGUID)) y += Screen.height * 0.4f + Gap;
                return new Vector2(Screen.width * 0.623f, y);
            }
        }

        /// <summary>Where the Advanced load window opens first: at the button, down to the bottom of the screen.</summary>
        public static Rect DefaultWindowRect(float minWidth, float maxHeight)
        {
            var position = DefaultPosition;
            var height = Mathf.Clamp(Screen.height - position.y - 10, 200, maxHeight);
            return new Rect(position.x, position.y, Mathf.Max(minWidth, Screen.width * 0.125f), height);
        }

        /// <returns>True when the "Advanced load" button was clicked.</returns>
        public bool DrawOpenButton()
        {
            if (OpenMode == null) return false;

            var position = DefaultPosition;
            var rect = new Rect(position.x, position.y, Screen.width * 0.125f, 32);

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

        /// <summary>
        /// Maker keeps inactive menus alive and hides them by CanvasGroup alpha, so activeInHierarchy alone is not enough.
        /// </summary>
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
