using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedLoader
{
    internal sealed class CardParts
    {
        public int HeadId = -1;
        public int[] Hair = new int[0];
        public readonly List<int[]> Clothes = new List<int[]>();

        public static CardParts Read(ChaFileControl temp, LoadMode mode)
        {
            var parts = new CardParts();
            if (mode == LoadMode.Character)
            {
                parts.HeadId = temp.custom.face.headId;
                parts.Hair = temp.custom.hair.parts.Select(x => x.id).ToArray();
                foreach (var coordinate in temp.coordinate) parts.Clothes.Add(coordinate.clothes.parts.Select(x => x.id).ToArray());
            }
            else
            {
                parts.Clothes.Add(AccessoryData.CardCoordinate(temp).clothes.parts.Select(x => x.id).ToArray());
            }
            return parts;
        }
    }

    internal static class ShapeFit
    {
        public static bool Fits(CardData card, ChaControl chaCtrl, ShapeEditorBridge.Part part, int sourceIndex, int sourceOutfit, int targetOutfit, int targetIndex,
            string targetPath, int vertices, bool subdivided)
        {
            var parts = card?.Parts;
            if (parts == null || chaCtrl == null) return true;
            var chaFile = chaCtrl.chaFile;
            switch (part)
            {
                case ShapeEditorBridge.Part.Head:
                    return parts.HeadId < 0 || parts.HeadId == chaFile.custom.face.headId;
                case ShapeEditorBridge.Part.Hair:
                    return sourceIndex < 0 || sourceIndex >= parts.Hair.Length || sourceIndex >= chaFile.custom.hair.parts.Length ||
                           parts.Hair[sourceIndex] == chaFile.custom.hair.parts[sourceIndex].id;
                case ShapeEditorBridge.Part.Clothes:
                {
                    var source = card.Mode == LoadMode.Coordinate ? 0 : Math.Max(sourceOutfit, 0);
                    var coordinate = Coordinate(chaCtrl, targetOutfit);
                    if (source >= parts.Clothes.Count || coordinate == null || sourceIndex < 0 || sourceIndex >= parts.Clothes[source].Length ||
                        sourceIndex >= coordinate.clothes.parts.Length) return false;
                    return parts.Clothes[source][sourceIndex] == coordinate.clothes.parts[sourceIndex].id;
                }
                case ShapeEditorBridge.Part.Accessory:
                {
                    var cardSlots = card.Mode == LoadMode.Coordinate
                        ? card.AccessorySlots
                        : Math.Max(sourceOutfit, 0) < card.CardOutfits.Count ? card.CardOutfits[Math.Max(sourceOutfit, 0)].Accessories : null;
                    var coordinate = Coordinate(chaCtrl, targetOutfit);
                    if (cardSlots == null || coordinate == null || sourceIndex < 0 || sourceIndex >= cardSlots.Count || targetIndex < 0 ||
                        targetIndex >= coordinate.accessory.parts.Length) return false;
                    var from = cardSlots[sourceIndex];
                    var to = coordinate.accessory.parts[targetIndex];
                    return from.type == to.type && from.id == to.id;
                }
                default:
                    return subdivided || vertices < 0 || VertexCount(chaCtrl, targetPath) == vertices;
            }
        }

        private static string Describe(CardData card, ChaControl chaCtrl, ShapeEditorBridge.Part part, ShapeEdit edit, Dictionary<int, int> targets)
        {
            switch (part)
            {
                case ShapeEditorBridge.Part.Clothes:
                    return string.Join("; ", edit.Outfits.Select(s =>
                    {
                        var cardId = s < card.Parts.Clothes.Count && edit.Index < card.Parts.Clothes[s].Length ? card.Parts.Clothes[s][edit.Index] : -1;
                        var coordinate = targets.TryGetValue(s, out var t) ? Coordinate(chaCtrl, t) : null;
                        var id = coordinate != null && edit.Index < coordinate.clothes.parts.Length ? coordinate.clothes.parts[edit.Index].id : -1;
                        return $"card outfit {s} id {cardId}, character id {id}";
                    }).ToArray());
                case ShapeEditorBridge.Part.Head:
                    return $"card face {card.Parts.HeadId}, character face {chaCtrl.chaFile.custom.face.headId}";
                case ShapeEditorBridge.Part.Body:
                    return $"vertices card {edit.Vertices}, character {VertexCount(chaCtrl, edit.Path)}, path {edit.Path}";
                default:
                    return "";
            }
        }

        private static ChaFileCoordinate Coordinate(ChaControl chaCtrl, int outfit)
        {
            if (outfit == chaCtrl.fileStatus.coordinateType) return chaCtrl.nowCoordinate;
            return outfit >= 0 && outfit < chaCtrl.chaFile.coordinate.Length ? chaCtrl.chaFile.coordinate[outfit] : null;
        }

        private static int VertexCount(ChaControl chaCtrl, string path)
        {
            var transform = string.IsNullOrEmpty(path) ? null : chaCtrl.transform.Find(path);
            if (transform == null) return -1;
            var skinned = transform.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null) return skinned.sharedMesh == null ? -1 : skinned.sharedMesh.vertexCount;
            var filter = transform.GetComponent<MeshFilter>();
            return filter == null || filter.sharedMesh == null ? -1 : filter.sharedMesh.vertexCount;
        }

        public static Dictionary<string, string> Unfit(CardData card, ChaControl chaCtrl, LoadOptions options)
        {
            var unfit = new Dictionary<string, string>();
            if (card == null || chaCtrl == null || card.ShapeEdits.Count == 0) return unfit;
            var character = card.Mode == LoadMode.Character;
            var current = chaCtrl.fileStatus.coordinateType;
            var loadOutfits = character && (options.Clothes || options.Accessories != AccessoryMode.Keep);
            Dictionary<int, int> targets;
            if (!character) targets = new Dictionary<int, int> { { 0, current } };
            else if (loadOutfits) targets = Outfits.MapTargets(options, card.CardOutfits.Count, chaCtrl.chaFile.coordinate.Length);
            else targets = Enumerable.Range(0, Math.Min(card.CardOutfits.Count, chaCtrl.chaFile.coordinate.Length)).ToDictionary(x => x, x => x);

            var cardBody = character && OtherPlugins.LoadsPlugin(options, card, OtherPlugins.UncensorSelectorGUID);
            foreach (var edit in card.ShapeEdits)
            {
                var part = (ShapeEditorBridge.Part)edit.Kind;
                bool fits;
                var reason = "different item";
                switch (part)
                {
                    case ShapeEditorBridge.Part.Head:
                        fits = character && options.Face && (options.FaceParts & FacePart.Overall) != 0 || Fits(card, chaCtrl, part, edit.Index, -1, current, edit.Index, edit.Path, edit.Vertices, edit.Subdivided);
                        break;
                    case ShapeEditorBridge.Part.Hair:
                        fits = character && options.Hair && (options.HairParts & (HairPart)(1 << edit.Index)) != 0 || Fits(card, chaCtrl, part, edit.Index, -1, current, edit.Index, edit.Path, edit.Vertices, edit.Subdivided);
                        break;
                    case ShapeEditorBridge.Part.Clothes:
                    case ShapeEditorBridge.Part.Accessory:
                        fits = false;
                        reason = "outfit not loaded";
                        foreach (var source in edit.Outfits.Count > 0 ? edit.Outfits : new List<int> { character ? current : 0 })
                        {
                            if (!targets.TryGetValue(source, out var target)) continue;
                            reason = "different item";
                            var loaded = part == ShapeEditorBridge.Part.Clothes
                                ? options.Clothes && (options.ClothesParts & (ClothesPart)(1 << edit.Index)) != 0 && (loadOutfits || !character)
                                : options.Accessories == AccessoryMode.All || options.Accessories == AccessoryMode.Selected && options.SelectedSlots.Contains(edit.Index);
                            if (loaded || Fits(card, chaCtrl, part, edit.Index, source, target, edit.Index, edit.Path, edit.Vertices, edit.Subdivided))
                            {
                                fits = true;
                                break;
                            }
                        }
                        break;
                    default:
                        fits = cardBody || Fits(card, chaCtrl, part, edit.Index, -1, current, edit.Index, edit.Path, edit.Vertices, edit.Subdivided);
                        break;
                }
                if (fits) continue;
                if (part == ShapeEditorBridge.Part.Head) reason = "different face type";
                else if (part == ShapeEditorBridge.Part.Hair) reason = "different hairstyle";
                else if (part == ShapeEditorBridge.Part.Body)
                    reason = card.PluginDataIds.Contains(OtherPlugins.UncensorSelectorGUID) || OtherPlugins.OnCharacter(chaCtrl).Contains(OtherPlugins.UncensorSelectorGUID)
                        ? "different uncensor" : "different body";
                unfit[edit.Key] = reason;
                Diag.Log($"Shape edit doesn't fit: {edit.Key} [{part} {edit.Index}, outfits {string.Join(",", edit.Outfits.Select(x => x.ToString()).ToArray())}], " +
                         $"targets {string.Join(",", targets.Select(x => $"{x.Key}->{x.Value}").ToArray())}, clothes {options.Clothes}/{options.ClothesParts}, accessories {options.Accessories}, " +
                         $"{Describe(card, chaCtrl, part, edit, targets)}");
            }
            return unfit;
        }
    }
}
