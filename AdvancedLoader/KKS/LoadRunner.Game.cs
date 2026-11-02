using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChaCustom;
using HarmonyLib;
using KKAPI.Maker;
using MessagePack;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedLoader
{
    internal static partial class LoadRunner
    {
        private static int? CurrentOutfit(ChaControl chaCtrl) => chaCtrl.fileStatus.coordinateType;

        public static IEnumerator LoadCharacter(CardData card, LoadOptions options, ChaControl target)
        {
            if (Busy) yield break;
            Busy = true;
            Toggle[] toggles = null;
            bool[] previousToggles = null;
            OtherPlugins.State otherPlugins = null;
            ReloadWait reload = null;
            try
            {
                var chaCtrl = target;
                // Studio's Change has no load flags: plugins take everything from the card, so what was not loaded is put back afterwards
                var studioCharacter = MakerAPI.InsideAndLoaded ? null : StudioCharaPanel.CharacterOf(chaCtrl);
                var inStudio = studioCharacter != null;
                if (chaCtrl == null || (!MakerAPI.InsideAndLoaded && !inStudio)) yield break;

                // Accessories without Clothes are loaded through the outfits too, with every clothes piece kept
                var loadOutfits = options.Clothes || options.Accessories != AccessoryMode.Keep;
                var clothesParts = options.Clothes ? options.ClothesParts : ClothesPart.None;

                var materialSnapshot = TakeMaterialSnapshot(chaCtrl, card);
                var overlaySnapshot = loadOutfits && Dependencies.ClothesOverlays.IsOk ? ClothesOverlayBridge.TakeSnapshot(chaCtrl) : null;
                var bonemodSnapshot = TakeBonemodSnapshot(chaCtrl, options, inStudio);
                Diag.Log($"Character load {System.IO.Path.GetFileName(card.Path)}: studio={inStudio} face={options.Face} body={options.Body} hair={options.Hair} clothes={options.Clothes} " +
                         $"accessories={options.Accessories}; height {chaCtrl.chaFile.custom.body.shapeValueBody[0]}, bonemod snapshot {(bonemodSnapshot == null ? "none" : "taken")}");
                if (AbmxPresence.Available) Diag.Log($"Bonemod before: {AbmxBridge.DescribeAll(chaCtrl)}");
                var groupsBefore = AccessoryGroups.Snapshot(chaCtrl, chaCtrl.chaFile.coordinate.Length);
                var outfitNames = loadOutfits ? Enumerable.Range(0, chaCtrl.chaFile.coordinate.Length).Select(i => Outfits.GetName(chaCtrl, i)).ToList() : null;

                var anyNative = options.Face || options.Body || options.Hair || options.Parameters || loadOutfits || OtherPlugins.AnyLoaded(options, card);
                OutfitLoadContext outfitContext = null;
                Func<MaterialGroup, int, bool> materialsFromCard = null;

                if (anyNative)
                {
                    // The maker only loads parameters into a new character
                    var parameters = options.Parameters && (inStudio || Singleton<CustomBase>.Instance.modeNew);
                    Action load;
                    if (inStudio)
                    {
                        // The game's Change keeps bones, FK and hair bones right; only its card read becomes a limited load
                        Hooks.Limited = new LimitedLoadContext
                        {
                            Target = chaCtrl.chaFile, Character = chaCtrl, Sex = chaCtrl.sex, Face = options.Face, Body = options.Body, Hair = options.Hair,
                            Parameter = parameters, Coordinate = loadOutfits
                        };
                        load = () => studioCharacter.ChangeChara(card.Path);
                    }
                    else
                    {
                        var page = Resources.FindObjectsOfTypeAll<CustomCharaFile>().FirstOrDefault(x => x.gameObject.scene.IsValid());
                        var window = page == null ? null : Traverse.Create(page).Field("fileWindow").GetValue<CustomFileWindow>();
                        if (window == null)
                        {
                            AdvancedLoaderPlugin.Logger.LogMessage("Open \"Load character\" in maker once, then try again");
                            yield break;
                        }

                        // The game's API and Material Editor read the load flags from the original window's toggles
                        toggles = new[] { window.tglChaLoadFace, window.tglChaLoadBody, window.tglChaLoadHair, window.tglChaLoadParam, window.tglChaLoadCoorde };
                        previousToggles = toggles.Select(x => x != null && x.isOn).ToArray();
                        SetToggle(toggles, 0, options.Face);
                        SetToggle(toggles, 1, options.Body);
                        SetToggle(toggles, 2, options.Hair);
                        SetToggle(toggles, 3, parameters);
                        SetToggle(toggles, 4, loadOutfits);
                        load = () => LoadCharacterLikeGame(chaCtrl, card.Path, options.Face, options.Body, options.Hair, parameters, loadOutfits);
                    }
                    otherPlugins = OtherPlugins.Apply(options, card);

                    if (loadOutfits)
                    {
                        outfitContext = new OutfitLoadContext
                        {
                            Target = chaCtrl.chaFile,
                            CardOutfits = card.CardOutfits,
                            SourceToTarget = Outfits.MapTargets(options, card.CardOutfits.Count, chaCtrl.chaFile.coordinate.Length),
                            ClothesParts = clothesParts,
                            Accessories = options.Accessories,
                            Placement = options.Placement,
                            Shift = options.Shift,
                            SelectedSlots = options.SelectedSlots.ToList(),
                            Groups = groupsBefore,
                            Blocks = Enumerable.Range(0, card.CardOutfits.Count).ToDictionary(s => s, s => AccessoryGroups.Blocks(AccessoryGroups.Parse(card.GroupsOf(s)), options,
                                AccessoryData.SourceSlots(options.Accessories, card.CardOutfits[s].Accessories, options.SelectedSlots)))
                        };
                        Hooks.Outfits = outfitContext;
                    }

                    var current = chaCtrl.fileStatus.coordinateType;
                    var currentFromCard = outfitContext != null && outfitContext.SourceToTarget.ContainsValue(current);
                    materialsFromCard = MaterialsFromCard(options, currentFromCard ? clothesParts : ClothesPart.None,
                        () => outfitContext == null ? null : outfitContext.AccessoryTargets.TryGetValue(chaCtrl.fileStatus.coordinateType, out var map) ? map : new Dictionary<int, int>());
                    KeepMaterials(chaCtrl, materialSnapshot, materialsFromCard);
                    Hooks.Parts = new PartsLoadContext
                    {
                        Target = chaCtrl.chaFile, BodyParts = options.BodyParts, FaceParts = options.FaceParts, HairParts = options.HairParts,
                        KeptPluginData = OtherPlugins.KeptData(chaCtrl, options, card)
                    };
                    try
                    {
                        reload = ReloadWait.Start(chaCtrl);
                        load();
                    }
                    finally
                    {
                        Hooks.Outfits = null;
                        Hooks.Parts = null;
                        Hooks.Limited = null;
                    }
                }

                for (var frame = 0; !ReloadWait.Ready(reload, frame); frame++) yield return null;

                if (outfitContext != null && outfitContext.FinalCount > 0)
                {
                    KeepOutfitNames(chaCtrl, card, outfitContext, outfitNames);
                    // Clothes Overlay Mod only loads the card's overlays together with Clothes; otherwise it kept them
                    if (overlaySnapshot != null && options.Clothes) RebuildOutfitOverlays(chaCtrl, outfitContext, overlaySnapshot);
                }

                if (anyNative) KeepOutfitAccessoryGroups(chaCtrl, card, options, outfitContext, groupsBefore);

                Diag.Log($"After the wait: height {chaCtrl.chaFile.custom.body.shapeValueBody[0]}");
                if (AbmxPresence.Available) Diag.Log($"Bonemod loaded: {AbmxBridge.DescribeAll(chaCtrl)}");
                if (bonemodSnapshot != null) ApplyBonemod(chaCtrl, card, options, bonemodSnapshot, inStudio);
                if (AbmxPresence.Available) Diag.Log($"Bonemod restored: {AbmxBridge.DescribeAll(chaCtrl)}");
#if DEBUG
                for (var i = 0; i < 60; i++) yield return null;
                Diag.Log($"60 frames later: height {chaCtrl.chaFile.custom.body.shapeValueBody[0]}");
                if (AbmxPresence.Available) Diag.Log($"Bonemod 60 frames later: {AbmxBridge.DescribeAll(chaCtrl)}");
#endif

                if (materialsFromCard == null) materialsFromCard = MaterialsFromCard(options, ClothesPart.None, () => null);
                if (MaterialEditorPresence.Available)
                {
                    if (outfitContext != null && outfitContext.FinalCount > 0) FixOutfitMaterials(chaCtrl, card, options, outfitContext, materialSnapshot);
                    else if (inStudio) KeepAllOutfitMaterials(chaCtrl, card, materialSnapshot);
                    if (inStudio && !options.Hair) KeepHairMaterials(chaCtrl, materialSnapshot);
                    KeepHeadMaterialsOnNewHead(chaCtrl, options, materialSnapshot);
                    MoveMaterialsToNewParts(chaCtrl, materialSnapshot, materialsFromCard);
                    ApplyCharacterMaterialGroups(chaCtrl, card, options, outfitContext, materialsFromCard);
                    KeepUntickedHairMaterials(chaCtrl, options, materialSnapshot);
                }

                AdvancedLoaderPlugin.Logger.LogMessage($"Loaded {System.IO.Path.GetFileName(card.Path)}");
            }
            finally
            {
                if (toggles != null && previousToggles != null)
                    for (var i = 0; i < toggles.Length && i < previousToggles.Length; i++) SetToggle(toggles, i, previousToggles[i]);
                OtherPlugins.Restore(otherPlugins);
                StopKeepingMaterials();
                reload?.Stop();
                Busy = false;
            }
        }

        public static IEnumerator LoadCoordinate(CardData card, LoadOptions options, ChaControl target)
        {
            if (Busy) yield break;
            Busy = true;
            ReloadWait reload = null;
            Toggle[] toggles = null;
            bool[] previousToggles = null;
            try
            {
                var chaCtrl = target;
                var inMaker = MakerAPI.InsideAndLoaded;
                // No load toggles outside maker, so plugins take all clothes and accessories from the card
                var pluginsLoadAll = !inMaker;
                var materialSnapshot = TakeMaterialSnapshot(chaCtrl, card);
                var overlaySnapshot = TakeOverlaySnapshot(chaCtrl, options, pluginsLoadAll);
                var clothesFromCard = options.Clothes ? options.ClothesParts : ClothesPart.None;
                var studioCharacter = StudioOutfitPanel.CharacterOf(chaCtrl);
                var outfit = chaCtrl.fileStatus.coordinateType;
                var groupsBefore = AccessoryGroups.Get(chaCtrl, outfit);

                // The original window has one Load button with Clothes and Accessories toggles
                var mode = options.Accessories;
                var loadClothes = options.Clothes;
                var loadAccessories = mode != AccessoryMode.Keep;
                var needsHook = loadAccessories && !options.ReplacesAllAccessories;
                AccessoryLoadContext context = null;
                var materialsFromCard = MaterialsFromCard(options, clothesFromCard, () => context?.TargetToSource);

                if (loadClothes || loadAccessories) reload = ReloadWait.Start(chaCtrl);
                if (loadClothes || loadAccessories)
                {
                    var page = inMaker ? Resources.FindObjectsOfTypeAll<CustomCoordinateFile>().FirstOrDefault(x => x.gameObject.scene.IsValid()) : null;
                    var window = page == null ? null : Traverse.Create(page).Field("fileWindow").GetValue<CustomFileWindow>();
                    if (window != null)
                    {
                        // The game's API reports these toggles to plugins as the coordinate load flags
                        toggles = new[] { window.tglCoordeLoadClothes, window.tglCoordeLoadAcs };
                        previousToggles = toggles.Select(x => x != null && x.isOn).ToArray();
                        SetToggle(toggles, 0, loadClothes);
                        SetToggle(toggles, 1, loadAccessories);
                    }

                    // Studio's own load takes everything, so the hooks also keep what is not loaded
                    if (needsHook || (studioCharacter != null && !loadAccessories))
                    {
                        context = NewContext(chaCtrl, card, options);
                        Hooks.Active = context;
                    }
                    if (loadClothes || studioCharacter != null)
                        Hooks.Parts = new PartsLoadContext { CoordinateTarget = chaCtrl.nowCoordinate, ClothesParts = clothesFromCard };
                    KeepMaterials(chaCtrl, materialSnapshot, materialsFromCard);
                    try
                    {
                        if (studioCharacter != null)
                        {
                            studioCharacter.LoadClothesFile(card.Path);
                            // Studio stores the outfit before Reload, the accessories are only final after it
                            chaCtrl.AssignCoordinate((ChaFileDefine.CoordinateType)chaCtrl.fileStatus.coordinateType);
                        }
                        else
                        {
                            LoadCoordinateLikeGame(chaCtrl, card.Path, loadClothes, loadAccessories, inMaker);
                        }
                    }
                    finally
                    {
                        Hooks.Active = null;
                        Hooks.Parts = null;
                    }
                }

                for (var frame = 0; !ReloadWait.Ready(reload, frame); frame++) yield return null;

                KeepUntickedClothesOverlays(chaCtrl, clothesFromCard, overlaySnapshot);
                if (loadClothes || loadAccessories)
                    KeepAccessoryGroups(chaCtrl, outfit, groupsBefore, CardGroups(card, 0, options), context?.TargetToSource, options.ReplacesAllAccessories,
                        AccessoryData.ReadSlots(chaCtrl.nowCoordinate));

                if (MaterialEditorPresence.Available)
                {
                    if (options.Clothes || pluginsLoadAll) KeepUntickedClothesMaterials(chaCtrl, clothesFromCard, materialSnapshot);
                    if (pluginsLoadAll && !loadAccessories) KeepAllAccessoryMaterials(chaCtrl, materialSnapshot);
                    MoveMaterialsToNewParts(chaCtrl, materialSnapshot, materialsFromCard);
                    ApplyMaterialGroups(chaCtrl, card, options.MaterialGroups & MaterialGroup.Clothes, options.MaterialCleanCopy);
                    PasteMaterialsOnWornParts(chaCtrl, card, options, materialsFromCard);
                    if (options.MaterialFollowAccessories && context?.Result != null)
                        FixAccessoryMaterials(chaCtrl, card, context, materialSnapshot, replacedByGame: loadAccessories);
                }

                AdvancedLoaderPlugin.Logger.LogMessage($"Loaded {System.IO.Path.GetFileName(card.Path)}");
            }
            finally
            {
                if (toggles != null && previousToggles != null)
                    for (var i = 0; i < toggles.Length && i < previousToggles.Length; i++) SetToggle(toggles, i, previousToggles[i]);
                StopKeepingMaterials();
                reload?.Stop();
                Busy = false;
            }
        }

        private static void KeepOutfitAccessoryGroups(ChaControl chaCtrl, CardData card, LoadOptions options, OutfitLoadContext context, Dictionary<int, string> before)
        {
            if (!AccessoryGroups.Available) return;
            var sourceFor = context != null && context.FinalCount > 0 ? context.SourceToTarget.ToDictionary(x => x.Value, x => x.Key) : new Dictionary<int, int>();
            var count = chaCtrl.chaFile.coordinate.Length;
            for (var t = 0; t < count; t++)
            {
                var coordinate = t == chaCtrl.fileStatus.coordinateType ? chaCtrl.nowCoordinate : chaCtrl.chaFile.coordinate[t];
                before.TryGetValue(t, out var kept);
                if (sourceFor.TryGetValue(t, out var s) && context.AccessoryTargets.TryGetValue(t, out var targetToSource))
                    KeepAccessoryGroups(chaCtrl, t, kept, CardGroups(card, s, options), targetToSource, options.ReplacesAllAccessories, AccessoryData.ReadSlots(coordinate));
                else
                    KeepAccessoryGroups(chaCtrl, t, kept, null, null, false, AccessoryData.ReadSlots(coordinate));
            }
            foreach (var outfit in card.OutfitGroups.Keys.Where(x => x >= count).ToList()) AccessoryGroups.Set(chaCtrl, outfit, null);
        }

        private static void KeepOutfitNames(ChaControl chaCtrl, CardData card, OutfitLoadContext context, List<string> namesBefore)
        {
            if (!Outfits.CanAddSlots) return;
            try
            {
                var sourceFor = context.SourceToTarget.ToDictionary(x => x.Value, x => x.Key);
                for (var t = 0; t < context.FinalCount; t++)
                {
                    if (sourceFor.TryGetValue(t, out var s) && s < card.CardOutfits.Count) Outfits.SetName(chaCtrl, t, card.CardOutfits[s].Name);
                    else if (namesBefore != null && t < namesBefore.Count) Outfits.SetName(chaCtrl, t, namesBefore[t]);
                }
                if (MakerAPI.InsideAndLoaded) Outfits.UpdateMakerUI();
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not set outfit names: {ex.Message}");
            }
        }

        private static void RebuildOutfitOverlays(ChaControl chaCtrl, OutfitLoadContext context, object snapshot)
        {
            try
            {
                var targetToSource = context.SourceToTarget.ToDictionary(x => x.Value, x => x.Key);
                ClothesOverlayBridge.RebuildOutfits(chaCtrl, snapshot, targetToSource, ClothesParts.UntickedSlots(context.ClothesParts), context.FinalCount);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep clothes overlays: {ex}");
            }
        }

        private static void FixOutfitMaterials(ChaControl chaCtrl, CardData card, LoadOptions options, OutfitLoadContext context, object snapshot)
        {
            if (snapshot == null) return;
            try
            {
                var current = chaCtrl.fileStatus.coordinateType;
                var sourceFor = context.SourceToTarget.ToDictionary(x => x.Value, x => x.Key);
                var keptPieces = new HashSet<int>((options.MaterialGroups & MaterialGroup.Clothes) != 0
                    ? ClothesParts.UntickedSlots(context.ClothesParts) : Enumerable.Range(0, ClothesParts.Count));
                var allPieces = Enumerable.Range(0, ClothesParts.Count).ToList();

                for (var t = 0; t < context.FinalCount; t++)
                {
                    var hasSource = sourceFor.TryGetValue(t, out var s);
                    var accessoryMap = hasSource && options.MaterialFollowAccessories && context.AccessoryTargets.TryGetValue(t, out var map) ? map : new Dictionary<int, int>();
                    var slotCount = Math.Max(AccessoryData.VanillaSlotCount, t < chaCtrl.chaFile.coordinate.Length ? chaCtrl.chaFile.coordinate[t].accessory.parts.Length : 0);
                    var keptAccessories = Enumerable.Range(0, slotCount).Where(x => !accessoryMap.ContainsKey(x)).ToList();

                    if (t == current)
                    {
                        var piecesFromCard = hasSource ? allPieces.Where(x => !keptPieces.Contains(x)).ToList() : new List<int>();
                        MaterialEditorBridge.ApplyClothingSlots(chaCtrl, allPieces.Except(piecesFromCard).ToDictionary(x => x, x => snapshot), t);
                        if (piecesFromCard.Count > 0) MaterialEditorBridge.ApplyClothingSlots(chaCtrl, piecesFromCard.ToDictionary(x => x, x => card.Materials), s);
                        MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, keptAccessories.ToDictionary(x => x, x => snapshot), t);
                        if (accessoryMap.Count > 0)
                            MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, accessoryMap.ToDictionary(x => x.Key, x => MaterialEditorBridge.RemapAccessorySlot(card.Materials, x.Value, x.Key)), s);
                        continue;
                    }

                    if (!hasSource)
                    {
                        MaterialEditorBridge.CopyOutfit(chaCtrl, snapshot, t, t);
                        continue;
                    }

                    MaterialEditorBridge.CopyOutfit(chaCtrl, card.Materials, s, t, (type, slot) => IsClothing(type) && !keptPieces.Contains(slot));
                    MaterialEditorBridge.CopyOutfit(chaCtrl, snapshot, t, t, (type, slot) => IsClothing(type) && keptPieces.Contains(slot));
                    MaterialEditorBridge.CopyOutfit(chaCtrl, snapshot, t, t, (type, slot) => !IsClothing(type) && !accessoryMap.ContainsKey(slot));
                    foreach (var pair in accessoryMap)
                    {
                        var slot = pair.Key;
                        MaterialEditorBridge.CopyOutfit(chaCtrl, MaterialEditorBridge.RemapAccessorySlot(card.Materials, pair.Value, slot), s, t, (type, x) => !IsClothing(type) && x == slot);
                    }
                }

                // Outfits of the card past the character's outfit count
                for (var t = context.FinalCount; t < Math.Max(card.CardOutfits.Count, context.Before?.Count ?? 0); t++)
                    MaterialEditorBridge.CopyOutfit(chaCtrl, null, null, t);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to fix Material Editor data of outfits: {ex}");
            }
        }

        // Without load flags ME took the clothes and accessories of every outfit from the card
        private static void KeepAllOutfitMaterials(ChaControl chaCtrl, CardData card, object snapshot)
        {
            if (snapshot == null) return;
            try
            {
                var current = chaCtrl.fileStatus.coordinateType;
                var count = chaCtrl.chaFile.coordinate.Length;
                for (var t = 0; t < count; t++)
                {
                    if (t != current)
                    {
                        MaterialEditorBridge.CopyOutfit(chaCtrl, snapshot, t, t);
                        continue;
                    }
                    var slotCount = Math.Max(AccessoryData.VanillaSlotCount, chaCtrl.chaFile.coordinate[t].accessory.parts.Length);
                    MaterialEditorBridge.ApplyClothingSlots(chaCtrl, Enumerable.Range(0, ClothesParts.Count).ToDictionary(x => x, x => snapshot), t);
                    MaterialEditorBridge.ApplyAccessorySlots(chaCtrl, Enumerable.Range(0, slotCount).ToDictionary(x => x, x => snapshot), t);
                }

                // Outfits of the card past the character's outfit count
                for (var t = count; t < card.CardOutfits.Count; t++)
                    MaterialEditorBridge.CopyOutfit(chaCtrl, null, null, t);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep Material Editor data of outfits: {ex}");
            }
        }

        private static bool IsClothing(KK_Plugins.MaterialEditor.MaterialEditorCharaController.ObjectType type) =>
            type == KK_Plugins.MaterialEditor.MaterialEditorCharaController.ObjectType.Clothing;

        private static void ApplyCharacterMaterialGroups(ChaControl chaCtrl, CardData card, LoadOptions options, OutfitLoadContext context, Func<MaterialGroup, int, bool> fromCard)
        {
            if (options.MaterialGroups == MaterialGroup.None) return;
            try
            {
                var current = chaCtrl.fileStatus.coordinateType;
                var source = context?.SourceToTarget.Where(x => x.Value == current).Select(x => (int?)x.Key).FirstOrDefault() ?? 0;
                MaterialEditorBridge.ApplyGroups(chaCtrl, card.Materials, options.MaterialGroups, options.MaterialCleanCopy, source);
                PasteMaterialsOnWornParts(chaCtrl, card, options, fromCard, source);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to copy Material Editor data: {ex}");
            }
        }

        private static void LoadCharacterLikeGame(ChaControl chaCtrl, string path, bool face, bool body, bool hair, bool parameters, bool coordinate)
        {
#if KK
            chaCtrl.chaFile.LoadFileLimited(path, chaCtrl.sex, face, body, hair, parameters, coordinate);
#else
            chaCtrl.chaFile.LoadFileLimited(path, chaCtrl.sex, face, body, hair, parameters, coordinate, false);
#endif
            chaCtrl.ChangeCoordinateType();
            chaCtrl.Reload(!coordinate, !face && !coordinate, !hair, !body);
            Singleton<CustomBase>.Instance.updateCustomUI = true;
        }

        // In H the game only changes the worn clothes, the outfit list stays
        private static void LoadCoordinateLikeGame(ChaControl chaCtrl, string path, bool clothes, bool accessories, bool inMaker)
        {
            var nowCoordinate = chaCtrl.nowCoordinate;
            var keepClothes = clothes ? null : MessagePackSerializer.Serialize(nowCoordinate.clothes);
            var keepAccessories = accessories ? null : MessagePackSerializer.Serialize(nowCoordinate.accessory);

            nowCoordinate.LoadFile(path);
            if (keepClothes != null) nowCoordinate.clothes = MessagePackSerializer.Deserialize<ChaFileClothes>(keepClothes);
            if (keepAccessories != null) nowCoordinate.accessory = MessagePackSerializer.Deserialize<ChaFileAccessory>(keepAccessories);

            chaCtrl.Reload(false, true, true, true);
            if (!inMaker) return;
            chaCtrl.AssignCoordinate((ChaFileDefine.CoordinateType)chaCtrl.chaFile.status.coordinateType);
            Singleton<CustomBase>.Instance.updateCustomUI = true;
        }
    }
}
