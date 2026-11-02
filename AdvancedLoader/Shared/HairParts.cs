using System;
using System.Collections.Generic;
using System.Linq;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    [Flags]
    internal enum HairPart
    {
        None = 0,
        Back = 1 << 0,
        Front = 1 << 1,
        Side = 1 << 2,
        Extension = 1 << 3,
        All = (1 << 4) - 1
    }

    internal static class HairParts
    {
        public const int Count = 4;

        private static readonly string[] Names = { "Back", "Front", "Side", "Extension" };

        public static IEnumerable<KeyValuePair<HairPart, string>> PartNames =>
            Enumerable.Range(0, Count).Select(i => new KeyValuePair<HairPart, string>((HairPart)(1 << i), Names[i]));

        public static string Describe(HairPart parts) =>
            string.Join(", ", Enumerable.Range(0, Count).Where(i => (parts & (HairPart)(1 << i)) != 0).Select(i => Names[i]).ToArray());

        public static IEnumerable<int> UntickedSlots(HairPart ticked) => Enumerable.Range(0, Count).Where(i => (ticked & (HairPart)(1 << i)) == 0);

        public static void RestoreUnticked(ChaFileHair loaded, ChaFileHair before, HairPart ticked)
        {
            foreach (var slot in UntickedSlots(ticked))
                if (slot < loaded.parts.Length && slot < before.parts.Length) loaded.parts[slot] = before.parts[slot];
        }
    }
}
