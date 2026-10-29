using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Manager;

namespace AdvancedLoader
{
    internal static partial class AccessoryData
    {
        // protected ChaFile.LoadFile(string path, bool noLoadPNG, bool noLoadStatus)
        private static readonly MethodInfo ChaFileLoadFile = AccessTools.Method(typeof(ChaFile), "LoadFile", new[] { typeof(string), typeof(bool), typeof(bool) });

        public static ChaFileCoordinate CardCoordinate(ChaFileControl temp) => temp.coordinate[0];

        public static string GetName(ChaFileAccessory.PartsInfo part)
        {
#if KK
            var lists = Singleton<Character>.Instance.chaListCtrl;
#else
            var lists = Character.chaListCtrl;
#endif
            var info = lists.GetListInfo((ChaListDefine.CategoryNo)part.type, part.id);
            return info != null ? info.Name : $"Unknown accessory ({part.type}/{part.id})";
        }

        public static List<ChaFileAccessory.PartsInfo> ReadSlots(ChaFileCoordinate coordinate, ChaFile owner = null)
        {
            return coordinate.accessory.parts.Select(Clone).ToList();
        }

        public static List<bool> ReadOccupancy(ChaFileCoordinate coordinate, ChaFile owner)
        {
            return coordinate.accessory.parts.Select(x => !IsEmpty(x)).ToList();
        }

        // Call MoreAccessoriesBridge.Sync afterwards for the worn outfit
        public static void Write(ChaFileCoordinate coordinate, List<ChaFileAccessory.PartsInfo> slots)
        {
            var count = MoreAccessoriesBridge.Available ? Math.Max(slots.Count, VanillaSlotCount) : VanillaSlotCount;
            while (count > VanillaSlotCount && (count > slots.Count || IsEmpty(slots[count - 1]))) count--;
            coordinate.accessory.parts = Enumerable.Range(0, count)
                .Select(i => i < slots.Count ? Clone(slots[i]) : new ChaFileAccessory.PartsInfo())
                .ToArray();
        }

        public static List<ChaFileAccessory.PartsInfo> ReadCoordinateCard(string fullPath, Action<ChaFileControl> inspect = null)
        {
            Hooks.ReadingCard = true;
            var temp = new ChaFileControl();
            try
            {
                if (!CardCoordinate(temp).LoadFile(fullPath)) return null;
                inspect?.Invoke(temp);
                return ReadSlots(CardCoordinate(temp));
            }
            finally
            {
                Hooks.ReadingCard = false;
            }
        }

        public static List<ChaFileAccessory.PartsInfo> ReadCard(string fullPath, byte sex, Action<ChaFileControl> inspect = null)
        {
#if KKS
            var prevIsMod = Character.isMod;
#endif
            var temp = new ChaFileControl();
            try
            {
                // Not LoadCharaFile: plugins treat that as a maker load
                var path = temp.ConvertCharaFilePath(fullPath, sex);
                var loaded = (bool)ChaFileLoadFile.Invoke(temp, new object[] { path, true, true });
                if (!loaded) return null;
                inspect?.Invoke(temp);
                return ReadSlots(temp.coordinate[0]);
            }
            finally
            {
#if KKS
                // Loading a card overwrites the "modded card" flag of the character being edited
                Character.isMod = prevIsMod;
#endif
            }
        }
    }
}
