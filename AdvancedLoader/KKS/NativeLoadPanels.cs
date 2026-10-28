using System.Linq;
using BepInEx.Bootstrap;
using ChaCustom;
using HarmonyLib;
using KKAPI.Utilities;
using UnityEngine;

namespace AdvancedLoader
{
    /// <summary>
    /// Watches the game's "Load character" and "Clothes > Load" pages: which one is open and which card is selected in it.
    /// Also draws the "Advanced load" button while one of them is open. The HS2API sidebar ("Plugin Settings") is not
    /// used for this because other plugins can hide it.
    /// In Koikatsu Sunshine save and load share one file window per page, told apart by its window type.
    /// </summary>
    internal sealed class NativeLoadPanels
    {
        private const string BrowserFoldersGUID = "marco.FolderBrowser";
        private const float Gap = 6;

        private CustomCharaFile _charaFile;
        private CustomCoordinateFile _coordinateFile;

        /// <summary>Mode of the load panel that is open, or null when none is.</summary>
        public LoadMode? OpenMode { get; private set; }

        /// <summary>Card selected in the open load panel, or null.</summary>
        public string SelectedPath { get; private set; }

        /// <summary>Name shown for the selected card in the game's list (character name or outfit name).</summary>
        public string SelectedName { get; private set; }

        public void Reset()
        {
            _charaFile = null;
            _coordinateFile = null;
            OpenMode = null;
            SelectedPath = null;
            SelectedName = null;
        }

        public void Update()
        {
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
                OpenMode = null;
                SelectedPath = null;
                SelectedName = null;
            }
        }

        private void SetSelection(CustomFileListCtrl list)
        {
            var info = list?.GetSelectTopItem()?.info;
            SelectedPath = info?.FullPath;
            SelectedName = info?.name;
        }

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

        /// <summary>
        /// The button sits right above BrowserFolders' folder window when it is installed, or in its place when it is not.
        /// </summary>
        private static Rect ButtonRect => HasFolderWindow
            ? new Rect(FolderWindowX, FolderWindowY - Gap - ButtonHeight, FolderWindowWidth, ButtonHeight)
            : new Rect(FolderWindowX, FolderWindowY, FolderWindowWidth, ButtonHeight);

        /// <summary>
        /// Where the Advanced load window opens first: growing upwards from the button when the folder window is below it,
        /// otherwise down from the button.
        /// </summary>
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

        /// <returns>True when the "Advanced load" button was clicked.</returns>
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
