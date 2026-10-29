using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExtensibleSaveFormat;
using HarmonyLib;
using MessagePack;

namespace AdvancedLoader
{
    internal enum OutfitMode
    {
        All,
        Selected
    }

    internal enum OutfitPlacement
    {
        SameSlots,
        Shift,
        // Needs More Outfit Slots
        NewSlots
    }

    internal sealed class CardOutfit
    {
        public int Index;
        public string Name;
        public byte[] Bytes;
        public List<ChaFileAccessory.PartsInfo> Accessories;
        public List<KeyValuePair<int, string>> SlotNames;
    }

    internal static class Outfits
    {
        private const string MoreOutfitsDataKey = "CoordinateNames";
        private static readonly string[] BaseNames = { "Casual", "Swimsuit", "Pajamas", "Bath towel" };

        private static bool _resolved;
        private static MethodInfo _getName;
        private static MethodInfo _setName;
        private static MethodInfo _updateMakerUI;

        public static bool CanAddSlots => Dependencies.MoreOutfits.IsOk;

        public static string DefaultName(int index) => index < BaseNames.Length ? BaseNames[index] : $"Outfit {index + 1}";

        public static List<CardOutfit> ReadCard(ChaFileControl temp)
        {
            Dictionary<int, string> names = null;
            var data = ExtendedSave.GetExtendedDataById(temp, Dependencies.MoreOutfits.GUID);
            if (data != null && data.data.TryGetValue(MoreOutfitsDataKey, out var value) && value is byte[] bytes)
                names = MessagePackSerializer.Deserialize<Dictionary<int, string>>(bytes);

            return temp.coordinate.Select((coordinate, i) =>
            {
                var accessories = AccessoryData.ReadSlots(coordinate);
                return new CardOutfit
                {
                    Index = i,
                    Name = names != null && names.TryGetValue(i, out var name) && !string.IsNullOrEmpty(name) ? name : DefaultName(i),
                    Bytes = coordinate.SaveBytes(),
                    Accessories = accessories,
                    SlotNames = Enumerable.Range(0, accessories.Count)
                        .Where(x => !AccessoryData.IsEmpty(accessories[x]))
                        .Select(x => new KeyValuePair<int, string>(x, $"{x + 1:00}.  {AccessoryData.GetName(accessories[x])}"))
                        .ToList()
                };
            }).ToList();
        }

        public static IEnumerable<int> SourceOutfits(LoadOptions options, int cardCount)
        {
            return options.OutfitSelection == OutfitMode.All
                ? Enumerable.Range(0, cardCount)
                : options.SelectedOutfits.Where(x => x < cardCount).OrderBy(x => x);
        }

        public static Dictionary<int, int> MapTargets(LoadOptions options, int cardCount, int characterCount)
        {
            var map = new Dictionary<int, int>();
            var next = characterCount;
            foreach (var source in SourceOutfits(options, cardCount))
            {
                int target;
                switch (options.OutfitPlacement)
                {
                    case OutfitPlacement.Shift:
                        target = source + options.OutfitShift;
                        break;
                    case OutfitPlacement.NewSlots:
                        target = next++;
                        break;
                    default:
                        target = source;
                        break;
                }
                if (target >= characterCount && !CanAddSlots) continue;
                map[source] = target;
            }
            return map;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!CanAddSlots) return;
            var plugin = AccessTools.TypeByName("KK_Plugins.MoreOutfits.Plugin");
            var makerUI = AccessTools.TypeByName("KK_Plugins.MoreOutfits.MakerUI");
            _getName = plugin == null ? null : AccessTools.Method(plugin, "GetCoodinateName");
            _setName = plugin == null ? null : AccessTools.Method(plugin, "SetCoordinateName");
            _updateMakerUI = makerUI == null ? null : AccessTools.Method(makerUI, "UpdateMakerUI");
        }

        public static string GetName(ChaControl chaCtrl, int index)
        {
            Resolve();
            return _getName?.Invoke(null, new object[] { chaCtrl, index }) as string;
        }

        public static void SetName(ChaControl chaCtrl, int index, string name)
        {
            Resolve();
            if (!string.IsNullOrEmpty(name)) _setName?.Invoke(null, new object[] { chaCtrl, index, name });
        }

        public static void UpdateMakerUI()
        {
            Resolve();
            _updateMakerUI?.Invoke(null, null);
        }
    }
}
