using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIChara;
using HarmonyLib;
using Manager;

namespace AdvancedLoader
{
    internal static partial class AccessoryData
    {
        public static ChaFileCoordinate CardCoordinate(ChaFileControl temp) => temp.coordinate;

        // protected ChaFile.LoadFile(string path, int lang, bool noLoadPNG, bool noLoadStatus)
        private static readonly MethodInfo ChaFileLoadFile = AccessTools.Method(typeof(ChaFile), "LoadFile", new[] { typeof(string), typeof(int), typeof(bool), typeof(bool) });

        public static string GetName(ChaFileAccessory.PartsInfo part)
        {
            var info = Singleton<Character>.Instance.chaListCtrl.GetListInfo((ChaListDefine.CategoryNo)part.type, part.id);
            return info != null ? info.Name : $"Unknown accessory ({part.type}/{part.id})";
        }

        public static List<ChaFileAccessory.PartsInfo> ReadCharacter(ChaFile file)
        {
            return ReadSlots(file.coordinate, file);
        }

        public static List<ChaFileAccessory.PartsInfo> ReadSlots(ChaFileCoordinate coordinate, ChaFile owner)
        {
            var result = coordinate.accessory.parts.Select(Clone).ToList();
            var extra = MoreAccessoriesBridge.GetParts(owner);
            if (extra != null) result.AddRange(extra.Select(Clone));
            return result;
        }

        // Temporary ChaFile so MoreAccessories and Sideloader know the owner
        public static List<ChaFileAccessory.PartsInfo> ReadCoordinateCard(string fullPath, Action<ChaFileControl> inspect = null)
        {
            var prevLoadAdditional = MoreAccessoriesBridge.LoadAdditionalAccessories;
            MoreAccessoriesBridge.LoadAdditionalAccessories = true;
            Hooks.ReadingCard = true;

            var temp = new ChaFileControl();
            try
            {
                if (!temp.coordinate.LoadFile(fullPath)) return null;
                inspect?.Invoke(temp);
                return ReadCharacter(temp);
            }
            finally
            {
                Hooks.ReadingCard = false;
                MoreAccessoriesBridge.LoadAdditionalAccessories = prevLoadAdditional;
                MoreAccessoriesBridge.Forget(temp);
            }
        }

        public static List<ChaFileAccessory.PartsInfo> ReadCard(string fullPath, byte sex, Action<ChaFileControl> inspect = null)
        {
            var character = Singleton<Character>.Instance;
            var prevIsMod = character.isMod;
            var prevLoadAdditional = MoreAccessoriesBridge.LoadAdditionalAccessories;
            MoreAccessoriesBridge.LoadAdditionalAccessories = true;

            var temp = new ChaFileControl();
            try
            {
                // Not LoadCharaFile: plugins treat that as a maker load
                var path = temp.ConvertCharaFilePath(fullPath, sex);
                var loaded = (bool)ChaFileLoadFile.Invoke(temp, new object[] { path, (int)Singleton<GameSystem>.Instance.language, true, true });
                if (!loaded) return null;
                inspect?.Invoke(temp);
                return ReadCharacter(temp);
            }
            finally
            {
                MoreAccessoriesBridge.LoadAdditionalAccessories = prevLoadAdditional;
                MoreAccessoriesBridge.Forget(temp);
                // LoadCharaFile overwrites the "modded card" flag of the character being edited
                character.isMod = prevIsMod;
            }
        }

        public static List<bool> ReadOccupancy(ChaFileCoordinate coordinate, ChaFile owner)
        {
            var result = coordinate.accessory.parts.Select(x => !IsEmpty(x)).ToList();
            var extra = MoreAccessoriesBridge.GetParts(owner);
            if (extra != null) result.AddRange(extra.Select(x => !IsEmpty(x)));
            return result;
        }

        public static void Write(ChaFile file, List<ChaFileAccessory.PartsInfo> slots)
        {
            WriteVanilla(file.coordinate, slots);
            if (MoreAccessoriesBridge.Available)
                MoreAccessoriesBridge.SetParts(file, slots.Skip(VanillaSlotCount).Select(Clone).ToList());
        }

        public static void WriteVanilla(ChaFileCoordinate coordinate, List<ChaFileAccessory.PartsInfo> slots)
        {
            var parts = coordinate.accessory.parts;
            for (var i = 0; i < parts.Length; i++)
                parts[i] = i < slots.Count ? Clone(slots[i]) : new ChaFileAccessory.PartsInfo();
        }
    }
}
