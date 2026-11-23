using System;
using System.Linq;
using ChaCustom;
using HarmonyLib;
using UnityEngine;

namespace AdvancedLoader
{
    internal static partial class LoadRunner
    {
        private static bool RestoreInMaker(ChaControl chaCtrl, string path, out Action restoreToggles)
        {
            restoreToggles = null;
            var parameters = Singleton<CustomBase>.Instance.modeNew;
            var window = LoadPageWindow(true);
            if (window == null) return false;
            var toggles = new[] { window.tglChaLoadFace, window.tglChaLoadBody, window.tglChaLoadHair, window.tglChaLoadParam, window.tglChaLoadCoorde };
            restoreToggles = KeepToggles(toggles);
            for (var i = 0; i < toggles.Length; i++) SetToggle(toggles, i, i != 3 || parameters);
            LoadCharacterLikeGame(chaCtrl, path, true, true, true, parameters, true);
            return true;
        }
    }
}
