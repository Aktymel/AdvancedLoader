using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    // Studio's character lists (Add > Female / Male), set by Hooks. Their fields are private, so they are read by reflection
    internal static class StudioCharaPanel
    {
        private static readonly List<Studio.CharaList> Lists = new List<Studio.CharaList>();

        public static bool Exists => Lists.Count > 0;

        public static void Add(Studio.CharaList list)
        {
            if (!Lists.Contains(list)) Lists.Add(list);
        }

        // Same check as BrowserFolders
        private static Studio.CharaList Shown => Lists.FirstOrDefault(x => x != null && x.isActiveAndEnabled);

        public static bool IsShown => Shown != null;

        public static Studio.CharaFileInfo Selected
        {
            get
            {
                var list = Shown;
                var sort = list == null ? null : Traverse.Create(list).Field("charaFileSort").GetValue<Studio.CharaFileSort>();
                return sort != null && sort.select >= 0 && sort.select < sort.cfiList.Count ? sort.cfiList[sort.select] : null;
            }
        }

        // The character selected in the scene, when it has the list's sex (the game's Change button needs the same)
        public static Studio.OCIChar Character
        {
            get
            {
                var list = Shown;
                if (list == null || !Singleton<Studio.Studio>.IsInstance()) return null;
                var character = Studio.Studio.GetCtrlInfo(Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNode) as Studio.OCIChar;
                return character != null && character.oiCharInfo.sex == Traverse.Create(list).Field("sex").GetValue<int>() ? character : null;
            }
        }

        public static Studio.OCIChar CharacterOf(ChaControl chaCtrl)
        {
            var character = Character;
            return character != null && character.charInfo == chaCtrl ? character : null;
        }

        // The game's "Change" button of the shown list in GUI coordinates
        public static Rect? ChangeButtonRect
        {
            get
            {
                var list = Shown;
                var button = list == null ? null : Traverse.Create(list).Field("buttonChange").GetValue<Button>();
                return button == null || !button.gameObject.activeInHierarchy ? (Rect?)null : StudioOutfitPanel.ScreenRect(button.transform);
            }
        }
    }
}
