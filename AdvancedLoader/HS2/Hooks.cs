using System;
using System.Collections.Generic;
using AIChara;
using HarmonyLib;

namespace AdvancedLoader
{
    /// <summary>
    /// Both maker load flows end with ChaControl.Reload:
    /// - "Load character" (CvsO_CharaLoad): LoadFileLimited -> ChangeNowCoordinate -> Reload
    /// - "Clothes > Load" (CvsC_ClothesLoad): nowCoordinate.LoadFile -> (restore clothes or accessories) -> Reload -> AssignCoordinate
    /// We snapshot accessories before the file is read, compute the result right after,
    /// and write it in Reload, after the game and MoreAccessories have finished replacing slots.
    /// </summary>
    internal static class Hooks
    {
        internal static AccessoryLoadContext Active;
        internal static PartsLoadContext Parts;

        /// <summary>
        /// Set while the plugin reads a card for the window, so that read is not treated as a load.
        /// </summary>
        internal static bool ReadingCard;

        private static List<ChaFileAccessory.PartsInfo> _before;
        private static ChaFileBody _bodyBefore;
        private static ChaFileFace _faceBefore;
        private static ChaFileClothes.PartsInfo[] _clothesBefore;
        private static ChaFile _pendingTarget;
        private static List<ChaFileAccessory.PartsInfo> _pendingResult;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChaFileControl), nameof(ChaFileControl.LoadFileLimited), typeof(string), typeof(byte), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
        private static void LoadFileLimitedPrefix(ChaFileControl __instance)
        {
            _before = null;
            _bodyBefore = null;
            _faceBefore = null;
            if (Parts != null && !ReadingCard && Parts.Target == __instance)
            {
                // LoadFileLimited replaces these objects instead of changing them, so keeping the references is enough
                if (Parts.BodyParts != BodyPart.All) _bodyBefore = __instance.custom.body;
                if (Parts.FaceParts != FacePart.All) _faceBefore = __instance.custom.face;
                _clothesBefore = Parts.ClothesParts != ClothesPart.All ? ClothesParts.Copy(__instance.coordinate.clothes) : null;
            }

            if (Active == null || ReadingCard || Active.Target.chaFile != __instance) return;
            Snapshot(__instance.coordinate, __instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaFileControl), nameof(ChaFileControl.LoadFileLimited), typeof(string), typeof(byte), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
        private static void LoadFileLimitedPostfix(ChaFileControl __instance, bool face, bool body, bool coordinate)
        {
            RestoreUntickedParts(__instance, face, body);
            if (coordinate) RestoreUntickedClothes(__instance.coordinate.clothes);
            if (_before == null) return;
            var result = PrepareResult(__instance);
            if (result != null) AccessoryData.Write(__instance, result);
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
            if (Parts != null && !ReadingCard && Parts.CoordinateTarget == __instance)
                _clothesBefore = Parts.ClothesParts != ClothesPart.All ? ClothesParts.Copy(__instance.clothes) : null;
            if (Active == null || ReadingCard || Active.Target.nowCoordinate != __instance) return;
            Snapshot(__instance, Active.Target.chaFile);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaFileCoordinate), nameof(ChaFileCoordinate.LoadFile), typeof(string))]
        private static void CoordinateLoadFilePostfix(ChaFileCoordinate __instance)
        {
            if (Parts != null && !ReadingCard && Parts.CoordinateTarget == __instance) RestoreUntickedClothes(__instance.clothes);
            if (_before == null) return;
            PrepareResult(Active.Target.chaFile);
        }

        private static void Snapshot(ChaFileCoordinate coordinate, ChaFile owner)
        {
            try
            {
                _before = AccessoryData.ReadSlots(coordinate, owner);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to read current accessories: {ex}");
            }
        }

        /// <summary>
        /// Combine the snapshot with the card slots and queue the result for Reload. Returns null on failure.
        /// </summary>
        private static List<ChaFileAccessory.PartsInfo> PrepareResult(ChaFile target)
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
                return result;
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to load selected accessories: {ex}");
                return null;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.Reload))]
        private static void ReloadPostfix(ChaControl __instance)
        {
            if (_pendingTarget == null || __instance.chaFile != _pendingTarget) return;
            var result = _pendingResult;
            _pendingTarget = null;
            _pendingResult = null;

            try
            {
                AccessoryData.Write(__instance.chaFile, result);
                AccessoryData.WriteVanilla(__instance.nowCoordinate, result);
                __instance.ChangeAccessory(true);
                MoreAccessoriesBridge.UpdateUI();
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to refresh accessories: {ex}");
            }
        }
    }
}
