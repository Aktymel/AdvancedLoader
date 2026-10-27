using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIChara;
using CharaCustom;
using HarmonyLib;
using KKAPI.Maker;
using KKAPI.Maker.UI;
using Manager;
using UnityEngine.UI;

namespace AdvancedLoader
{
    /// <summary>
    /// Runs a load from the Advanced Load window through the game's own load code (the same delegates the original
    /// Load buttons call), so HS2API, Material Editor, overlays and other plugins see a normal load.
    /// Accessory slots are adjusted by <see cref="Hooks"/>, Material Editor groups are copied afterwards.
    /// </summary>
    internal static class LoadRunner
    {
        // Material Editor and other controllers apply card data a frame or more after Reload
        private const int FramesToWaitForPlugins = 6;

        public static bool Busy { get; private set; }

        public static IEnumerator LoadCharacter(CardData card, LoadOptions options)
        {
            if (Busy) yield break;
            Busy = true;
            Toggle[] toggles = null;
            bool[] previousToggles = null;
            Dictionary<MakerLoadToggle, bool> previousPluginToggles = null;
            try
            {
                var chaCtrl = MakerAPI.GetCharacterControl();
                var materialSnapshot = TakeMaterialSnapshot(chaCtrl, options);
                var overlaySnapshot = TakeOverlaySnapshot(chaCtrl, options);
                var bonemodSnapshot = TakeBonemodSnapshot(chaCtrl, options);

                // The game's "Clothes" flag loads clothes and all accessories together; everything else goes through the hook
                var accessoryMode = options.Accessories;
                var needsHook = !(options.Clothes && options.ReplacesAllAccessories) && !(!options.Clothes && accessoryMode == AccessoryMode.Keep);
                // Other plugins' data (e.g. overlays) comes with the game's load even when no game part is ticked
                var anyNative = options.Face || options.Body || options.Hair || options.Parameters || options.Clothes || options.PluginToggles.Values.Any(x => x);
                AccessoryLoadContext context = null;

                if (anyNative || needsHook)
                {
                    var charaLoad = UnityEngine.Object.FindObjectOfType<CvsO_CharaLoad>();
                    if (charaLoad == null)
                    {
                        AdvancedLoaderPlugin.Logger.LogMessage("Open \"Load character\" in maker once, then try again");
                        yield break;
                    }
                    var window = Traverse.Create(charaLoad).Field("charaLoadWin").GetValue<CustomCharaWindow>();

                    // HS2API and Material Editor read the load flags from the original window's toggles
                    toggles = Traverse.Create(window).Field("tglLoadOption").GetValue<Toggle[]>();
                    previousToggles = toggles?.Select(x => x != null && x.isOn).ToArray();
                    SetToggle(toggles, 0, options.Face);
                    SetToggle(toggles, 1, options.Body);
                    SetToggle(toggles, 2, options.Hair);
                    SetToggle(toggles, 3, options.Clothes);
                    SetToggle(toggles, 4, options.Parameters);
                    previousPluginToggles = ApplyPluginToggles(options);

                    var flags = (options.Face ? 1 : 0) | (options.Body ? 2 : 0) | (options.Hair ? 4 : 0) | (options.Clothes ? 8 : 0) | (options.Parameters ? 0x10 : 0);
                    if (needsHook)
                    {
                        context = NewContext(chaCtrl, card, options);
                        Hooks.Active = context;
                    }

                    Hooks.Parts = new PartsLoadContext { Target = chaCtrl.chaFile, BodyParts = options.BodyParts, FaceParts = options.FaceParts, ClothesParts = options.ClothesParts };
                    try
                    {
                        var info = new CustomCharaFileInfo { FullPath = card.Path, FileName = System.IO.Path.GetFileName(card.Path) };
                        if (window.onClick03 != null) window.onClick03(info, flags);
                        else LoadCharacterLikeGame(card.Path, options);
                    }
                    finally
                    {
                        Hooks.Active = null;
                        Hooks.Parts = null;
                    }
                }

                for (var i = 0; i < FramesToWaitForPlugins; i++) yield return null;

                KeepUntickedClothesOverlays(chaCtrl, options, overlaySnapshot);

                if (bonemodSnapshot != null) ApplyBonemod(chaCtrl, card, options, bonemodSnapshot);

                if (MaterialEditorPresence.Available)
                {
                    KeepUntickedCharacterMaterials(chaCtrl, options, materialSnapshot);
                    if (options.Clothes) KeepUntickedClothesMaterials(chaCtrl, options, materialSnapshot);
                    ApplyMaterialGroups(chaCtrl, card, options.MaterialGroups, options.MaterialCleanCopy);
                    // With "Clothes" the game (and Material Editor) replaced every accessory slot
                    if (options.MaterialFollowAccessories && context?.Result != null)
                        FixAccessoryMaterials(chaCtrl, card, context, materialSnapshot, replacedByGame: options.Clothes);
                }

                AdvancedLoaderPlugin.Logger.LogMessage($"Loaded {System.IO.Path.GetFileName(card.Path)}");
            }
            finally
            {
                if (toggles != null && previousToggles != null)
                    for (var i = 0; i < toggles.Length && i < previousToggles.Length; i++) SetToggle(toggles, i, previousToggles[i]);
                RestorePluginToggles(previousPluginToggles);
                Busy = false;
            }
        }

        public static IEnumerator LoadCoordinate(CardData card, LoadOptions options)
        {
            if (Busy) yield break;
            Busy = true;
            try
            {
                var chaCtrl = MakerAPI.GetCharacterControl();
                var materialSnapshot = TakeMaterialSnapshot(chaCtrl, options);
                var overlaySnapshot = TakeOverlaySnapshot(chaCtrl, options);

                // The original window has three buttons: 1 = clothes only, 2 = accessories only, 3 = both
                var mode = options.Accessories;
                int button;
                if (options.Clothes) button = options.ReplacesAllAccessories ? 3 : 1;
                else button = mode == AccessoryMode.Keep ? 0 : 2;
                var needsHook = mode != AccessoryMode.Keep && !options.ReplacesAllAccessories;
                AccessoryLoadContext context = null;

                if (button != 0)
                {
                    var clothesLoad = UnityEngine.Object.FindObjectOfType<CvsC_ClothesLoad>();
                    var window = clothesLoad == null ? null : Traverse.Create(clothesLoad).Field("clothesLoadWin").GetValue<CustomClothesWindow>();
                    var onClick = window == null ? null : button == 1 ? window.onClick01 : button == 2 ? window.onClick02 : window.onClick03;

                    if (needsHook)
                    {
                        context = NewContext(chaCtrl, card, options);
                        Hooks.Active = context;
                    }

                    // Button 2 keeps all current clothes anyway
                    if (button != 2) Hooks.Parts = new PartsLoadContext { CoordinateTarget = chaCtrl.nowCoordinate, ClothesParts = options.ClothesParts };
                    try
                    {
                        SetCoordinateButtonClicked(button);
                        var info = new CustomClothesFileInfo { FullPath = card.Path, FileName = System.IO.Path.GetFileName(card.Path) };
                        if (onClick != null) onClick(info);
                        else LoadCoordinateLikeGame(chaCtrl, card.Path, button);
                    }
                    finally
                    {
                        Hooks.Active = null;
                        Hooks.Parts = null;
                    }
                }

                for (var i = 0; i < FramesToWaitForPlugins; i++) yield return null;

                KeepUntickedClothesOverlays(chaCtrl, options, overlaySnapshot);

                if (MaterialEditorPresence.Available)
                {
                    if (options.Clothes) KeepUntickedClothesMaterials(chaCtrl, options, materialSnapshot);
                    ApplyMaterialGroups(chaCtrl, card, options.MaterialGroups & MaterialGroup.Clothes, options.MaterialCleanCopy);
                    if (options.MaterialFollowAccessories && context?.Result != null)
                        FixAccessoryMaterials(chaCtrl, card, context, materialSnapshot, replacedByGame: button == 2 || button == 3);
                }

                AdvancedLoaderPlugin.Logger.LogMessage($"Loaded {System.IO.Path.GetFileName(card.Path)}");
            }
            finally
            {
                Busy = false;
            }
        }

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
                MaterialEditorBridge.ApplyClothingSlots(chaCtrl, ClothesParts.UntickedSlots(options.ClothesParts).ToDictionary(x => x, x => snapshot));
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
                var sources = new Dictionary<int, object>();

                // When the game replaced all accessories, Material Editor did too, so kept slots need their old data back
                if (replacedByGame && snapshot != null)
                    for (var slot = 0; slot < Math.Max(context.Result.Count, AccessoryData.VanillaSlotCount); slot++)
                        sources[slot] = snapshot;

                foreach (var pair in targetToSource)
                    sources[pair.Key] = MaterialEditorBridge.RemapAccessorySlot(card.Materials, pair.Value, pair.Key);

                MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, sources);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to copy accessory Material Editor data: {ex}");
            }
        }

        private static void SetCoordinateButtonClicked(int button)
        {
            // HS2API remembers which coordinate button was pressed and reports it to plugins as load flags
            var hooksType = AccessTools.Inner(typeof(MakerAPI), "Hooks");
            var field = hooksType == null ? null : AccessTools.Field(hooksType, "CoordinateButtonClicked");
            field?.SetValue(null, button);
        }

        /// <summary>
        /// Same steps as CvsO_CharaLoad's load button, for when the original window was never opened.
        /// </summary>
        private static void LoadCharacterLikeGame(string path, LoadOptions options)
        {
            var customBase = Singleton<CustomBase>.Instance;
            var chaCtrl = customBase.chaCtrl;
            chaCtrl.chaFile.LoadFileLimited(path, chaCtrl.sex, options.Face, options.Body, options.Hair, options.Parameters && customBase.modeNew, options.Clothes);
            chaCtrl.ChangeNowCoordinate();
            Singleton<Character>.Instance.customLoadGCClear = false;
            chaCtrl.Reload(!options.Clothes, !options.Face, !options.Hair, !options.Body);
            Singleton<Character>.Instance.customLoadGCClear = true;
            customBase.updateCustomUI = true;
            for (var i = 0; i < AccessoryData.VanillaSlotCount; i++) customBase.ChangeAcsSlotName(i);
            customBase.SetUpdateToggleSetting();
            customBase.forceUpdateAcsList = true;
        }

        /// <summary>
        /// Same steps as CvsC_ClothesLoad's three load buttons, for when the original window was never opened.
        /// </summary>
        private static void LoadCoordinateLikeGame(ChaControl chaCtrl, string path, int button)
        {
            var customBase = Singleton<CustomBase>.Instance;
            var keepAccessories = button == 1 ? MessagePack.MessagePackSerializer.Serialize(chaCtrl.nowCoordinate.accessory) : null;
            var keepClothes = button == 2 ? MessagePack.MessagePackSerializer.Serialize(chaCtrl.nowCoordinate.clothes) : null;

            chaCtrl.nowCoordinate.LoadFile(path);
            if (keepAccessories != null) chaCtrl.nowCoordinate.accessory = MessagePack.MessagePackSerializer.Deserialize<ChaFileAccessory>(keepAccessories);
            if (keepClothes != null) chaCtrl.nowCoordinate.clothes = MessagePack.MessagePackSerializer.Deserialize<ChaFileClothes>(keepClothes);

            Singleton<Character>.Instance.customLoadGCClear = false;
            chaCtrl.Reload(false, true, true, true);
            Singleton<Character>.Instance.customLoadGCClear = true;
            customBase.updateCustomUI = true;
            if (button != 1)
            {
                customBase.ChangeAcsSlotName();
                customBase.forceUpdateAcsList = true;
            }
            chaCtrl.AssignCoordinate();
        }
    }
}
