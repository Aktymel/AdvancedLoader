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
        /// <summary>The coordinate a clothes card is read into.</summary>
        public static ChaFileCoordinate CardCoordinate(ChaFileControl temp) => temp.coordinate;

        // protected ChaFile.LoadFile(string path, int lang, bool noLoadPNG, bool noLoadStatus)
        private static readonly MethodInfo ChaFileLoadFile = AccessTools.Method(typeof(ChaFile), "LoadFile", new[] { typeof(string), typeof(int), typeof(bool), typeof(bool) });

        public static string GetName(ChaFileAccessory.PartsInfo part)
        {
            var info = Singleton<Character>.Instance.chaListCtrl.GetListInfo((ChaListDefine.CategoryNo)part.type, part.id);
            return info != null ? info.Name : $"Unknown accessory ({part.type}/{part.id})";
        }

        /// <summary>
        /// Deep copy of all accessory slots of a character.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> ReadCharacter(ChaFile file)
        {
            return ReadSlots(file.coordinate, file);
        }

        /// <summary>
        /// Deep copy of vanilla slots from the given coordinate and MoreAccessories slots of its owner.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> ReadSlots(ChaFileCoordinate coordinate, ChaFile owner)
        {
            var result = coordinate.accessory.parts.Select(Clone).ToList();
            var extra = MoreAccessoriesBridge.GetParts(owner);
            if (extra != null) result.AddRange(extra.Select(Clone));
            return result;
        }

        /// <summary>
        /// Load a coordinate (clothes) card and return a deep copy of its accessory slots, or null on failure.
        /// The coordinate is loaded into a temporary ChaFile so MoreAccessories and Sideloader know whose coordinate it is.
        /// </summary>
        /// <param name="inspect">Called with the loaded temporary file while its extended data is still available.</param>
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

        /// <summary>
        /// Load a character card into a temporary ChaFile and return a deep copy of its accessory slots, or null on failure.
        /// </summary>
        /// <param name="inspect">Called with the loaded temporary file while its extended data is still available.</param>
        public static List<ChaFileAccessory.PartsInfo> ReadCard(string fullPath, byte sex, Action<ChaFileControl> inspect = null)
        {
            var character = Singleton<Character>.Instance;
            var prevIsMod = character.isMod;
            var prevLoadAdditional = MoreAccessoriesBridge.LoadAdditionalAccessories;
            MoreAccessoriesBridge.LoadAdditionalAccessories = true;

            var temp = new ChaFileControl();
            try
            {
                // Same read as LoadFileLimited. LoadCharaFile is avoided because other plugins
                // treat it as "this card is now the maker character".
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

        /// <summary>
        /// Which character slots are taken, without copying the accessory data.
        /// </summary>
        public static List<bool> ReadOccupancy(ChaFileCoordinate coordinate, ChaFile owner)
        {
            var result = coordinate.accessory.parts.Select(x => !IsEmpty(x)).ToList();
            var extra = MoreAccessoriesBridge.GetParts(owner);
            if (extra != null) result.AddRange(extra.Select(x => !IsEmpty(x)));
            return result;
        }

        /// <summary>
        /// Write the full slot list into a character file (vanilla slots into the coordinate, the rest into MoreAccessories).
        /// </summary>
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
