using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KKAPI.Maker.UI;
using UnityEngine.UI;
#if HS2
using AIChara;
#endif

namespace AdvancedLoader
{
    /// <summary>
    /// Runs a load from the Advanced Load window through the game's own load code, so the game's API, Material Editor,
    /// overlays and other plugins see a normal load. Accessory slots are adjusted by <see cref="Hooks"/>, plugin data is
    /// fixed up afterwards. The game specific part (which load code to call) is in LoadRunner.Game.cs.
    /// </summary>
    internal static partial class LoadRunner
    {
        // Material Editor and other controllers apply card data a frame or more after Reload
        private const int FramesToWaitForPlugins = 6;

        public static bool Busy { get; private set; }

        /// <summary>All load toggles other plugins added to the game's "Load character" window.</summary>
        public static List<MakerLoadToggle> GetPluginToggles()
        {
            return Traverse.Create(typeof(MakerLoadToggle)).Field("Toggles").GetValue<List<MakerLoadToggle>>() ?? new List<MakerLoadToggle>();
        }

        /// <summary>
        /// Put this window's choices on other plugins' load toggles for the duration of the load.
        /// Their values are what the original window shows, and e.g. "Skin/eye overlays" is on by default there,
        /// which would load the card's eye overlays even when only an accessory is loaded.
        /// </summary>
        private static Dictionary<MakerLoadToggle, bool> ApplyPluginToggles(LoadOptions options)
        {
            var previous = new Dictionary<MakerLoadToggle, bool>();
            foreach (var toggle in GetPluginToggles())
            {
                previous[toggle] = toggle.Value;
                options.PluginToggles.TryGetValue(toggle.Text, out var value);
                // Bonemod is copied by category after the load instead (see ApplyBonemod)
                if (AbmxPresence.Available && AbmxPresence.LoadToggleTexts.Contains(toggle.Text)) value = false;
                if (toggle.Value != value) toggle.Value = value;
            }
            return previous;
        }

        private static void RestorePluginToggles(Dictionary<MakerLoadToggle, bool> previous)
        {
            if (previous == null) return;
            foreach (var pair in previous)
                if (pair.Key.Value != pair.Value) pair.Key.Value = pair.Value;
        }

        private static void SetToggle(Toggle[] toggles, int index, bool value)
        {
            if (toggles != null && index < toggles.Length && toggles[index] != null) toggles[index].isOn = value;
        }

        private static object TakeMaterialSnapshot(ChaControl chaCtrl, LoadOptions options)
        {
            // Needed for accessories, and for Face/Body because Material Editor then replaces all body/head/eye materials
            if (!MaterialEditorPresence.Available) return null;
            var keepsClothes = options.Clothes && options.ClothesParts != ClothesPart.All;
            if (!(options.MaterialFollowAccessories || options.Face || options.Body || keepsClothes)) return null;
            try
            {
                return MaterialEditorBridge.Snapshot(chaCtrl);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read current Material Editor data: {ex.Message}");
                return null;
            }
        }

        private static object TakeBonemodSnapshot(ChaControl chaCtrl, LoadOptions options)
        {
            if (!AbmxPresence.Available || !(options.Face || options.Body)) return null;
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

        private static void ApplyBonemod(ChaControl chaCtrl, CardData card, LoadOptions options, object snapshot)
        {
            try
            {
                AbmxBridge.Apply(chaCtrl, card.Bonemod, snapshot, options.Face, options.FaceParts, options.Body, options.BodyParts);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to copy bonemod data: {ex}");
            }
        }

        /// <summary>
        /// Loading Face or Body makes Material Editor take every body, head and eye material change from the card.
        /// The groups not ticked under "Material Editor" get the character's previous changes back.
        /// Hair and clothes are not restored: their materials belong to the hair or clothes that came with the load.
        /// </summary>
        private static void KeepUntickedCharacterMaterials(ChaControl chaCtrl, LoadOptions options, object snapshot)
        {
            if (snapshot == null || !(options.Face || options.Body)) return;
            var keep = (MaterialGroup.Body | MaterialGroup.Head | MaterialGroup.Eyes) & ~options.MaterialGroups;
            if (keep == MaterialGroup.None) return;
            try
            {
                MaterialEditorBridge.ApplyGroups(chaCtrl, snapshot, keep, true);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep current Material Editor data: {ex}");
            }
        }

        /// <summary>
        /// Loading clothes makes Material Editor take the material changes of all eight pieces from the card;
        /// the pieces that were kept get their previous changes back.
        /// </summary>
        private static void KeepUntickedClothesMaterials(ChaControl chaCtrl, LoadOptions options, object snapshot)
        {
            if (snapshot == null || options.ClothesParts == ClothesPart.All) return;
            try
            {
                MaterialEditorBridge.ApplyClothingSlots(chaCtrl, ClothesParts.UntickedSlots(options.ClothesParts).ToDictionary(x => x, x => snapshot), CurrentOutfit(chaCtrl));
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep Material Editor data of clothes: {ex}");
            }
        }

        private static object TakeOverlaySnapshot(ChaControl chaCtrl, LoadOptions options)
        {
            if (!Dependencies.ClothesOverlays.IsOk || !options.Clothes || options.ClothesParts == ClothesPart.All) return null;
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

        /// <summary>
        /// Clothes Overlay Mod replaces the overlays of every clothes piece on a load; kept pieces get theirs back.
        /// </summary>
        private static void KeepUntickedClothesOverlays(ChaControl chaCtrl, LoadOptions options, object snapshot)
        {
            if (snapshot == null) return;
            try
            {
                ClothesOverlayBridge.Restore(chaCtrl, snapshot, ClothesParts.UntickedSlots(options.ClothesParts));
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

        /// <summary>
        /// Make every accessory slot carry the Material Editor data of the accessory now in it:
        /// slots filled from the card get the card's data (renumbered when the accessory moved to another slot),
        /// kept slots get back what they had before the load.
        /// </summary>
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
