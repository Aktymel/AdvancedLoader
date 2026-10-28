using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace AdvancedLoader
{
    /// <summary>
    /// Outfits to take from a character card, for a "Load character" with Clothes started from the Advanced Load window.
    /// </summary>
    internal sealed class OutfitLoadContext
    {
        public ChaFileControl Target;
        public List<CardOutfit> CardOutfits;
        /// <summary>Card outfit -> character outfit.</summary>
        public Dictionary<int, int> SourceToTarget;
        public ClothesPart ClothesParts = ClothesPart.All;
        public AccessoryMode Accessories;
        public AccessoryPlacement Placement;
        public int Shift;
        public IEnumerable<int> SelectedSlots;

        /// <summary>Every outfit of the character before the load.</summary>
        public List<byte[]> Before;
        /// <summary>Character outfit -> (character slot -> card slot) for accessories taken from the card.</summary>
        public Dictionary<int, Dictionary<int, int>> AccessoryTargets = new Dictionary<int, Dictionary<int, int>>();
        /// <summary>Number of outfits after the load.</summary>
        public int FinalCount;
    }

    /// <summary>
    /// Both maker load flows end with ChaControl.Reload:
    /// - "Load character" (CustomCharaFile): LoadFileLimited -> ChangeCoordinateType -> Reload
    /// - "Clothes > Load" (CustomCoordinateFile): nowCoordinate.LoadFile -> (restore clothes or accessories) -> Reload -> AssignCoordinate
    /// For a character, LoadFileLimited replaces every outfit; right after it the outfit list is rebuilt from the
    /// character's previous outfits and the card outfits that were chosen. For clothes, the accessory slots of the worn
    /// outfit are combined right after the file is read and written in Reload.
    /// </summary>
    internal static class Hooks
    {
        internal static AccessoryLoadContext Active;
        internal static PartsLoadContext Parts;
        internal static OutfitLoadContext Outfits;

        /// <summary>
        /// Set while the plugin reads a card for the window, so that read is not treated as a load.
        /// </summary>
        internal static bool ReadingCard;

        private static List<ChaFileAccessory.PartsInfo> _before;
        private static ChaFileBody _bodyBefore;
        private static ChaFileFace _faceBefore;
        private static ChaFileClothes _clothesBefore;
        private static ChaFile _pendingTarget;
        private static List<ChaFileAccessory.PartsInfo> _pendingResult;
        private static ChaFile _pendingOutfitTarget;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChaFileControl), nameof(ChaFileControl.LoadFileLimited), typeof(string), typeof(byte), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
        private static void LoadFileLimitedPrefix(ChaFileControl __instance)
        {
            _bodyBefore = null;
            _faceBefore = null;
            if (ReadingCard) return;

            if (Parts != null && Parts.Target == __instance)
            {
                // LoadFileLimited replaces these objects instead of changing them, so keeping the references is enough
                if (Parts.BodyParts != BodyPart.All) _bodyBefore = __instance.custom.body;
                if (Parts.FaceParts != FacePart.All) _faceBefore = __instance.custom.face;
            }

            if (Outfits != null && Outfits.Target == __instance)
                Outfits.Before = __instance.coordinate.Select(x => x.SaveBytes()).ToList();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaFileControl), nameof(ChaFileControl.LoadFileLimited), typeof(string), typeof(byte), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
        private static void LoadFileLimitedPostfix(ChaFileControl __instance, bool face, bool body, bool coordinate)
        {
            if (ReadingCard) return;
            RestoreUntickedParts(__instance, face, body);
            if (coordinate && Outfits != null && Outfits.Target == __instance && Outfits.Before != null) RebuildOutfits(__instance, Outfits);
        }

        private static void RestoreUntickedParts(ChaFileControl target, bool face, bool body)
        {
            try
            {
                if (body && _bodyBefore != null && target.custom.body != _bodyBefore)
                    BodyFaceParts.RestoreUnticked(target.custom.body, _bodyBefore, Parts.BodyParts);
                if (face && _faceBefore != null && target.custom.face != _faceBefore)
                    BodyFaceParts.RestoreUnticked(target.custom.face, _faceBefore, Parts.FaceParts);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep body/face parts: {ex}");
            }
            finally
            {
                _bodyBefore = null;
                _faceBefore = null;
            }
        }

        /// <summary>
        /// Character outfits after the load: card outfits in their target slots (with unticked clothes pieces and
        /// accessories kept from the outfit that was there), every other outfit as it was before.
        /// </summary>
        private static void RebuildOutfits(ChaFileControl target, OutfitLoadContext context)
        {
            try
            {
                var version = ChaFileDefine.ChaFileCoordinateVersion;
                var sourceFor = context.SourceToTarget.ToDictionary(x => x.Value, x => x.Key);
                var count = Math.Max(context.Before.Count, sourceFor.Count == 0 ? 0 : sourceFor.Keys.Max() + 1);
                var outfits = new ChaFileCoordinate[count];

                for (var t = 0; t < count; t++)
                {
                    var kept = new ChaFileCoordinate();
                    if (t < context.Before.Count) kept.LoadBytes(context.Before[t], version);

                    if (!sourceFor.TryGetValue(t, out var s) || s >= context.CardOutfits.Count)
                    {
                        outfits[t] = kept;
                        continue;
                    }

                    var loaded = new ChaFileCoordinate();
                    loaded.LoadBytes(context.CardOutfits[s].Bytes, version);
                    if (context.ClothesParts != ClothesPart.All) ClothesParts.RestoreUnticked(loaded.clothes, kept.clothes, context.ClothesParts);

                    var accessories = AccessoryData.Combine(context.Accessories, context.Placement, context.Shift, AccessoryData.ReadSlots(kept),
                        context.CardOutfits[s].Accessories, context.SelectedSlots, out var targetToSource);
                    AccessoryData.Write(loaded, accessories);
                    context.AccessoryTargets[t] = targetToSource;
                    outfits[t] = loaded;
                }

                target.coordinate = outfits;
                context.FinalCount = count;
                _pendingOutfitTarget = target;
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to rebuild outfits: {ex}");
            }
        }

        private static void RestoreUntickedClothes(ChaFileClothes loaded)
        {
            if (_clothesBefore == null) return;
            try
            {
                ClothesParts.RestoreUnticked(loaded, _clothesBefore, Parts.ClothesParts);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep clothes pieces: {ex}");
            }
            finally
            {
                _clothesBefore = null;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChaFileCoordinate), nameof(ChaFileCoordinate.LoadFile), typeof(string))]
        private static void CoordinateLoadFilePrefix(ChaFileCoordinate __instance)
        {
            _before = null;
            if (ReadingCard) return;
            if (Parts != null && Parts.CoordinateTarget == __instance)
                _clothesBefore = Parts.ClothesParts != ClothesPart.All ? ClothesParts.Copy(__instance.clothes) : null;
            if (Active == null || Active.Target.nowCoordinate != __instance) return;
            Snapshot(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaFileCoordinate), nameof(ChaFileCoordinate.LoadFile), typeof(string))]
        private static void CoordinateLoadFilePostfix(ChaFileCoordinate __instance)
        {
            if (ReadingCard) return;
            if (Parts != null && Parts.CoordinateTarget == __instance) RestoreUntickedClothes(__instance.clothes);
            if (_before == null) return;
            PrepareResult(Active.Target.chaFile);
        }

        private static void Snapshot(ChaFileCoordinate coordinate)
        {
            try
            {
                _before = AccessoryData.ReadSlots(coordinate);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to read current accessories: {ex}");
            }
        }

        /// <summary>
        /// Combine the snapshot with the card slots and queue the result for Reload.
        /// </summary>
        private static void PrepareResult(ChaFile target)
        {
            var before = _before;
            _before = null;

            try
            {
                var context = Active;
                var result = AccessoryData.Combine(context.Mode, context.Placement, context.Shift, before,
                    context.CardSlots ?? new List<ChaFileAccessory.PartsInfo>(), context.SelectedSlots, out var targetToSource);
                context.Result = result;
                context.TargetToSource = targetToSource;
                _pendingTarget = target;
                _pendingResult = result;
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to load selected accessories: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.Reload))]
        private static void ReloadPostfix(ChaControl __instance)
        {
            try
            {
                if (_pendingTarget != null && __instance.chaFile == _pendingTarget)
                {
                    var result = _pendingResult;
                    _pendingTarget = null;
                    _pendingResult = null;
                    // The game copies the worn outfit back into the character's outfit list after Reload (AssignCoordinate)
                    AccessoryData.Write(__instance.nowCoordinate, result);
                    MoreAccessoriesBridge.Sync(__instance);
                    __instance.ChangeAccessory(true);
                }
                else if (_pendingOutfitTarget != null && __instance.chaFile == _pendingOutfitTarget)
                {
                    _pendingOutfitTarget = null;
                    MoreAccessoriesBridge.Sync(__instance);
                    __instance.ChangeAccessory(true);
                }
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to refresh accessories: {ex}");
            }
        }
    }
}
