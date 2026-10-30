using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    // Outfit list of the selected Studio character (MPCharCtrl.CostumeInfo), set by Hooks when Studio starts.
    // The class is private in the game and Mono blocks direct access to it, so it is read by reflection like BrowserFolders does
    internal static class StudioOutfitPanel
    {
        private static Traverse _panel;

        public static bool Exists => _panel != null;

        public static void Set(object panel) => _panel = panel == null ? null : Traverse.Create(panel);

        public static bool IsShown
        {
            get
            {
                var root = _panel?.Field("objRoot").GetValue<GameObject>();
                return root != null && root.activeInHierarchy;
            }
        }

        public static Studio.OCIChar Character => _panel?.Property("ociChar").GetValue<Studio.OCIChar>();

        public static Studio.CharaFileInfo Selected
        {
            get
            {
                var sort = _panel?.Field("fileSort").GetValue<Studio.CharaFileSort>();
                return sort != null && sort.select >= 0 && sort.select < sort.cfiList.Count ? sort.cfiList[sort.select] : null;
            }
        }

        public static Studio.OCIChar CharacterOf(ChaControl chaCtrl)
        {
            var character = Character;
            return character != null && character.charInfo == chaCtrl ? character : null;
        }

        // Where BrowserFolders puts its folder window (StudioOutfitFolders)
        public static Rect FolderWindow => new Rect(Screen.width * 0.06f, Screen.height * 0.32f, Screen.width * 0.13f, Screen.height * 0.4f);

        // The game's load button of the list ("Switch Costume") in GUI coordinates
        public static Rect? LoadButtonRect
        {
            get
            {
                var button = _panel?.Field("buttonLoad").GetValue<Button>();
                return button == null || !button.gameObject.activeInHierarchy ? (Rect?)null : ScreenRect(button.transform);
            }
        }

        public static Rect ScreenRect(Transform transform)
        {
            var canvas = transform.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            ((RectTransform)transform).GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
        }
    }
}
