using System.Collections.Generic;
using System.Linq;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal sealed class ShapeEdit
    {
        public string Key { get; set; }
        public string Plugin { get; set; }
        public string Part { get; set; }
        public string Name { get; set; }
        public readonly List<int> Outfits = new List<int>();
        public int AccessorySlot = -1;
        public int Kind { get; set; }
        public int Index { get; set; } = -1;
        public string Path { get; set; }
        public int Vertices { get; set; } = -1;
        public bool Subdivided { get; set; }

        public string Label => Outfits.Count == 0 ? $"{Part}: {Name}" : $"{Part}: {Name} (outfit {string.Join(", ", Outfits.Select(x => (x + 1).ToString()).ToArray())})";
    }

    internal static class ShapeEdits
    {
        public static Dictionary<string, string> Unfit(CardData card, ChaControl chaCtrl, LoadOptions options) =>
#if KKS || KK
            ShapeFit.Unfit(card, chaCtrl, options);
#else
            new Dictionary<string, string>();
#endif

#if KKS || KK
        private static readonly string[] ClothesNames = { "Top", "Bottom", "Bra", "Underwear", "Gloves", "Pantyhose", "Socks", "Indoor shoes", "Outdoor shoes" };
        private static readonly string[] HairNames = { "Back hair", "Front hair", "Side hair", "Hair extension" };

        public static string PartName(ShapeEditorBridge.Part part, int index)
        {
            switch (part)
            {
                case ShapeEditorBridge.Part.Head:
                    return "Head";
                case ShapeEditorBridge.Part.Hair:
                    return index >= 0 && index < HairNames.Length ? HairNames[index] : "Hair";
                case ShapeEditorBridge.Part.Clothes:
                    return index >= 0 && index < ClothesNames.Length ? ClothesNames[index] : "Clothes";
                case ShapeEditorBridge.Part.Accessory:
                    return $"Accessory {index + 1}";
                default:
                    return "Body";
            }
        }

        public static string Leaf(string path)
        {
            var at = path?.LastIndexOf('/') ?? -1;
            return at >= 0 ? path.Substring(at + 1) : path ?? "";
        }

        public static ShapeEdit Add(List<ShapeEdit> edits, string plugin, string key, ShapeEditorBridge.Part part, int index, string name, int outfit, string path, int vertices)
        {
            var edit = edits.FirstOrDefault(x => x.Key == key);
            if (edit == null)
            {
                edit = new ShapeEdit
                {
                    Key = key, Plugin = plugin, Part = PartName(part, index), Name = name, AccessorySlot = part == ShapeEditorBridge.Part.Accessory ? index : -1,
                    Kind = (int)part, Index = index, Path = path, Vertices = vertices
                };
                edits.Add(edit);
            }
            if (edit.Vertices < 0) edit.Vertices = vertices;
            if (outfit >= 0 && !edit.Outfits.Contains(outfit))
            {
                edit.Outfits.Add(outfit);
                edit.Outfits.Sort();
            }
            return edit;
        }
#endif
    }
}
