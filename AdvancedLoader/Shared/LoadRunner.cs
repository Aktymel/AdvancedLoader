using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine.UI;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal static partial class LoadRunner
    {
        public static bool Busy { get; private set; }

        private static void SetToggle(Toggle[] toggles, int index, bool value)
        {
            if (toggles != null && index < toggles.Length && toggles[index] != null) toggles[index].isOn = value;
        }

        private static bool[] HoldCharacterAccessories(ChaControl chaCtrl, CardData card, LoadOptions options)
        {
            if (options.ReplacesAllAccessories || !AacBridge.Available) return null;
            var before = AacBridge.Flags(chaCtrl);
            ICollection<int> replaced = new int[0];
            if (options.Accessories != AccessoryMode.Keep)
            {
                AccessoryData.Combine(options.Accessories, options.Placement, options.Shift, AccessoryData.ReadSlots(chaCtrl.nowCoordinate, chaCtrl.chaFile),
                    card.AccessorySlots ?? new List<ChaFileAccessory.PartsInfo>(), options.SelectedSlots, out var targetToSource,
                    AccessoryGroups.Get(chaCtrl, CurrentOutfit(chaCtrl) ?? 0), CardBlocks(card, options));
                replaced = targetToSource.Keys;
            }
            AacBridge.Hold(chaCtrl, replaced);
            return before;
        }

        private static ICollection<int> CardCharacterAccessories(CardData card, LoadOptions options) =>
            options.OtherPlugins.TryGetValue(AacBridge.GUID, out var load) && load ? card.CharacterAccessories : null;

        private static object TakeMaterialSnapshot(ChaControl chaCtrl, CardData card)
        {
            if (!MaterialEditorPresence.Available) return null;
            try
            {
                return MaterialEditorBridge.Snapshot(chaCtrl, card.Materials);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read current Material Editor data: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        // Material Editor data comes from the card only where its group is ticked (accessories: "follow their accessories"); everything else keeps the character's
        private static Func<MaterialGroup, int, bool> MaterialsFromCard(LoadOptions options, ClothesPart clothesFromCard, Func<IDictionary<int, int>> accessoryTargets)
        {
            return (group, slot) =>
            {
                switch (group)
                {
                    case MaterialGroup.Hair:
                        return (options.MaterialGroups & MaterialGroup.Hair) != 0 && (!options.Hair || (options.HairParts & (HairPart)(1 << slot)) != 0);
                    case MaterialGroup.Clothes:
                        return (options.MaterialGroups & MaterialGroup.Clothes) != 0 && (clothesFromCard == ClothesPart.None || (clothesFromCard & (ClothesPart)(1 << slot)) != 0);
                    case MaterialGroup.Accessories:
                        if (!options.MaterialFollowAccessories || options.Accessories == AccessoryMode.Keep) return false;
                        var map = accessoryTargets?.Invoke();
                        return map == null ? options.ReplacesAllAccessories : map.ContainsKey(slot);
                    default:
                        return (options.MaterialGroups & group) != 0;
                }
            };
        }

        private static object TakeBonemodSnapshot(ChaControl chaCtrl, LoadOptions options, bool always = false)
        {
            if (!AbmxPresence.Available || !(always || options.Face || options.Body)) return null;
            try
            {
                return AbmxBridge.Snapshot(chaCtrl);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read current bonemod data: {ex.Message}");
                return null;
            }
        }

        // allLoaded: outside maker ABMX loads the whole card, so the face and body that were not ticked come back from the snapshot
        private static void ApplyBonemod(ChaControl chaCtrl, CardData card, LoadOptions options, object snapshot, bool allLoaded = false)
        {
            try
            {
                AbmxBridge.Apply(chaCtrl, card.Bonemod, snapshot,
                    options.Face || allLoaded, options.Face ? options.FaceParts : FacePart.None,
                    options.Body || allLoaded, options.Body ? options.BodyParts : BodyPart.None);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to copy bonemod data: {ex}");
            }
        }

        private static void KeepMaterials(ChaControl chaCtrl, object snapshot, Func<MaterialGroup, int, bool> fromCard)
        {
            if (MaterialEditorPresence.Available && snapshot != null && fromCard != null) MaterialEditorBridge.KeepOnLoad(chaCtrl, snapshot, fromCard);
        }

        // Without load flags ME takes hair, clothes and accessories from the card too; the ones that were not loaded get their data back
        private static void KeepUnloadedPartMaterials(ChaControl chaCtrl, LoadOptions options, object snapshot)
        {
            if (snapshot == null) return;
            KeepUntickedClothesMaterials(chaCtrl, options.Clothes ? options.ClothesParts : ClothesPart.None, snapshot);
            if (options.Accessories == AccessoryMode.Keep) KeepAllAccessoryMaterials(chaCtrl, snapshot);
            if (!options.Hair) KeepHairMaterials(chaCtrl, snapshot);
        }

        private static void KeepHairMaterials(ChaControl chaCtrl, object snapshot)
        {
            if (snapshot == null) return;
            try
            {
                MaterialEditorBridge.ApplyGroups(chaCtrl, snapshot, MaterialGroup.Hair, true);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep hair Material Editor data: {ex}");
            }
        }

        private static bool KeepsHairParts(LoadOptions options) => options.Hair && options.HairParts != HairPart.All;

        private static void KeepUntickedHairMaterials(ChaControl chaCtrl, LoadOptions options, object snapshot)
        {
            if (snapshot == null || !KeepsHairParts(options)) return;
            try
            {
                MaterialEditorBridge.ApplyHairSlots(chaCtrl, HairParts.UntickedSlots(options.HairParts).ToDictionary(x => x, x => snapshot));
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep hair Material Editor data: {ex}");
            }
        }

        // A new face type brings a head whose materials have other names, so ME has nothing to put the kept data on
        private static void KeepHeadMaterialsOnNewHead(ChaControl chaCtrl, LoadOptions options, object snapshot)
        {
            if (snapshot == null || !options.Face) return;
            var keep = (MaterialGroup.Head | MaterialGroup.Eyes) & ~options.MaterialGroups;
            if (keep == MaterialGroup.None) return;
            try
            {
                MaterialEditorBridge.MoveToNewHead(chaCtrl, snapshot, keep);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to move Material Editor data to the new head: {ex}");
            }
        }

        private static void MoveMaterialsToNewParts(ChaControl chaCtrl, object snapshot, Func<MaterialGroup, int, bool> fromCard)
        {
            if (snapshot == null || fromCard == null) return;
            try
            {
                MaterialEditorBridge.MoveToNewParts(chaCtrl, snapshot, fromCard);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to move Material Editor data to the new parts: {ex}");
            }
        }

        private static void PasteMaterialsOnWornParts(ChaControl chaCtrl, CardData card, LoadOptions options, Func<MaterialGroup, int, bool> fromCard, int? sourceCoordinate = null)
        {
            if ((options.MaterialGroups & (MaterialGroup.Hair | MaterialGroup.Clothes)) == 0 || fromCard == null) return;
            try
            {
                MaterialEditorBridge.PasteOntoWornParts(chaCtrl, card.Materials, fromCard, sourceCoordinate);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to put the card's Material Editor data on the worn parts: {ex}");
            }
        }

        private static void StopKeepingMaterials()
        {
            if (MaterialEditorPresence.Available) MaterialEditorBridge.ClearKeep();
        }

        private static void KeepUntickedClothesMaterials(ChaControl chaCtrl, ClothesPart loaded, object snapshot)
        {
            if (snapshot == null || loaded == ClothesPart.All) return;
            try
            {
                MaterialEditorBridge.ApplyClothingSlots(chaCtrl, ClothesParts.UntickedSlots(loaded).ToDictionary(x => x, x => snapshot), CurrentOutfit(chaCtrl));
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep Material Editor data of clothes: {ex}");
            }
        }

        private static void KeepAllAccessoryMaterials(ChaControl chaCtrl, object snapshot)
        {
            if (snapshot == null) return;
            try
            {
                var count = Math.Max(AccessoryData.VanillaSlotCount, chaCtrl.nowCoordinate.accessory.parts.Length);
                MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, Enumerable.Range(0, count).ToDictionary(x => x, x => snapshot), CurrentOutfit(chaCtrl));
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep accessory Material Editor data: {ex}");
            }
        }

        private static object TakeOverlaySnapshot(ChaControl chaCtrl, LoadOptions options, bool always = false)
        {
            if (!Dependencies.ClothesOverlays.IsOk) return null;
            if (!always && (!options.Clothes || options.ClothesParts == ClothesPart.All)) return null;
            try
            {
                return ClothesOverlayBridge.TakeSnapshot(chaCtrl);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read current clothes overlays: {ex.Message}");
                return null;
            }
        }

        private static void KeepUntickedClothesOverlays(ChaControl chaCtrl, ClothesPart loaded, object snapshot)
        {
            if (snapshot == null || loaded == ClothesPart.All) return;
            try
            {
                ClothesOverlayBridge.Restore(chaCtrl, snapshot, ClothesParts.UntickedSlots(loaded));
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep clothes overlays: {ex}");
            }
        }

        private static void ApplyMaterialGroups(ChaControl chaCtrl, CardData card, MaterialGroup groups, bool clean)
        {
            if (groups == MaterialGroup.None) return;
            try
            {
                MaterialEditorBridge.ApplyGroups(chaCtrl, card.Materials, groups, clean);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to copy Material Editor data: {ex}");
            }
        }

        private static string CardGroups(CardData card, int outfit, LoadOptions options) =>
            options.Accessories != AccessoryMode.Selected
                ? card.GroupsOf(outfit)
                : AccessoryGroups.Format(AccessoryGroups.Parse(card.GroupsOf(outfit)).Where(g => options.SelectedGroups.Contains(g.First)));

        private static void KeepAccessoryGroups(ChaControl chaCtrl, int outfit, string before, string card, IDictionary<int, int> targetToSource, bool replacesAll,
            List<ChaFileAccessory.PartsInfo> slots)
        {
            if (!AccessoryGroups.Available) return;
            try
            {
                AccessoryGroups.Set(chaCtrl, outfit, AccessoryGroups.Combine(before, card, targetToSource, replacesAll, s => s >= slots.Count || AccessoryData.IsEmpty(slots[s])));
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep Advanced Accessories Menu groups: {ex}");
            }
        }

        private static AccessoryLoadContext NewContext(ChaControl chaCtrl, CardData card, LoadOptions options)
        {
            return new AccessoryLoadContext
            {
                Target = chaCtrl,
                Mode = options.Accessories,
                Placement = options.Placement,
                Shift = options.Shift,
                SelectedSlots = options.SelectedSlots.ToList(),
                CardSlots = card.AccessorySlots,
                Groups = AccessoryGroups.Get(chaCtrl, CurrentOutfit(chaCtrl) ?? 0),
                Blocks = CardBlocks(card, options)
            };
        }

        private static List<IList<int>> CardBlocks(CardData card, LoadOptions options) =>
            AccessoryGroups.Blocks(card.SlotGroups, options,
                AccessoryData.SourceSlots(options.Accessories, card.AccessorySlots ?? new List<ChaFileAccessory.PartsInfo>(), options.SelectedSlots));

        private static void FixAccessoryMaterials(ChaControl chaCtrl, CardData card, AccessoryLoadContext context, object snapshot, bool replacedByGame)
        {
            try
            {
                var targetToSource = context.TargetToSource ?? new Dictionary<int, int>();

                // When the game replaced all accessories, Material Editor did too, so kept slots need their old data back
                if (replacedByGame && snapshot != null)
                {
                    var kept = Enumerable.Range(0, Math.Max(context.Result.Count, AccessoryData.VanillaSlotCount)).Where(x => !targetToSource.ContainsKey(x));
                    MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, kept.ToDictionary(x => x, x => snapshot), CurrentOutfit(chaCtrl));
                }

                var fromCard = targetToSource.ToDictionary(x => x.Key, x => MaterialEditorBridge.RemapAccessorySlot(card.Materials, x.Value, x.Key));
                MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, fromCard);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to copy accessory Material Editor data: {ex}");
            }
        }
    }
}
