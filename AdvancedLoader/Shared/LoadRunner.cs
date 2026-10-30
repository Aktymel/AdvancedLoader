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
        // Material Editor and other controllers apply card data a frame or more after Reload
        private const int FramesToWaitForPlugins = 6;

        public static bool Busy { get; private set; }

        private static void SetToggle(Toggle[] toggles, int index, bool value)
        {
            if (toggles != null && index < toggles.Length && toggles[index] != null) toggles[index].isOn = value;
        }

        private static object TakeMaterialSnapshot(ChaControl chaCtrl, CardData card, LoadOptions options, bool always = false)
        {
            // Accessories, and Face/Body (ME replaces body/head/eye materials)
            if (!MaterialEditorPresence.Available) return null;
            var keepsClothes = options.Clothes && options.ClothesParts != ClothesPart.All;
            if (!always && !(options.MaterialFollowAccessories || options.Face || options.Body || keepsClothes)) return null;
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

        // allLoaded: without load flags ME replaces body/head/eye materials on every load
        private static void KeepUntickedCharacterMaterials(ChaControl chaCtrl, LoadOptions options, object snapshot, bool allLoaded = false)
        {
            if (!MaterialEditorPresence.Available || snapshot == null || !(allLoaded || options.Face || options.Body)) return;
            var keep = (MaterialGroup.Body | MaterialGroup.Head | MaterialGroup.Eyes) & ~options.MaterialGroups;
            if (keep != MaterialGroup.None) MaterialEditorBridge.KeepCharacterGroupsOnLoad(chaCtrl, snapshot, keep);
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

        private static void StopKeepingCharacterMaterials()
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

        private static AccessoryLoadContext NewContext(ChaControl chaCtrl, CardData card, LoadOptions options)
        {
            return new AccessoryLoadContext
            {
                Target = chaCtrl,
                Mode = options.Accessories,
                Placement = options.Placement,
                Shift = options.Shift,
                SelectedSlots = options.SelectedSlots.ToList(),
                CardSlots = card.AccessorySlots
            };
        }

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
