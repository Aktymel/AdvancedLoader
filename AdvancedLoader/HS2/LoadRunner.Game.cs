using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIChara;
using CharaCustom;
using HarmonyLib;
using KKAPI.Maker;
using Manager;
using UnityEngine.UI;

namespace AdvancedLoader
{
    internal static partial class LoadRunner
    {
        private static int? CurrentOutfit(ChaControl chaCtrl) => 0;

        public static IEnumerator LoadCharacter(CardData card, LoadOptions options)
        {
            if (Busy) yield break;
            Busy = true;
            Toggle[] toggles = null;
            bool[] previousToggles = null;
            OtherPlugins.State otherPlugins = null;
            try
            {
                var chaCtrl = MakerAPI.GetCharacterControl();
                var materialSnapshot = TakeMaterialSnapshot(chaCtrl, card, options);
                var overlaySnapshot = TakeOverlaySnapshot(chaCtrl, options);
                var bonemodSnapshot = TakeBonemodSnapshot(chaCtrl, options);

                // The game's "Clothes" flag loads clothes and all accessories together; everything else goes through the hook
                var accessoryMode = options.Accessories;
                var needsHook = !(options.Clothes && options.ReplacesAllAccessories) && !(!options.Clothes && accessoryMode == AccessoryMode.Keep);
                // Other plugins' data (e.g. overlays) comes with the game's load even when no game part is ticked
                var anyNative = options.Face || options.Body || options.Hair || options.Parameters || options.Clothes || OtherPlugins.AnyLoaded(options, card);
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
                    otherPlugins = OtherPlugins.Apply(options, card);

                    var flags = (options.Face ? 1 : 0) | (options.Body ? 2 : 0) | (options.Hair ? 4 : 0) | (options.Clothes ? 8 : 0) | (options.Parameters ? 0x10 : 0);
                    if (needsHook)
                    {
                        context = NewContext(chaCtrl, card, options);
                        Hooks.Active = context;
                    }

                    KeepUntickedCharacterMaterials(chaCtrl, options, materialSnapshot);
                    Hooks.Parts = new PartsLoadContext
                    {
                        Target = chaCtrl.chaFile, BodyParts = options.BodyParts, FaceParts = options.FaceParts, ClothesParts = options.ClothesParts,
                        KeptPluginData = OtherPlugins.KeptData(chaCtrl, options, card)
                    };
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

                KeepUntickedClothesOverlays(chaCtrl, options.ClothesParts, overlaySnapshot);

                if (bonemodSnapshot != null) ApplyBonemod(chaCtrl, card, options, bonemodSnapshot);

                if (MaterialEditorPresence.Available)
                {
                    if (options.Clothes) KeepUntickedClothesMaterials(chaCtrl, options.ClothesParts, materialSnapshot);
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
                OtherPlugins.Restore(otherPlugins);
                StopKeepingCharacterMaterials();
                Busy = false;
            }
        }

        public static IEnumerator LoadCoordinate(CardData card, LoadOptions options, ChaControl target)
        {
            if (Busy) yield break;
            Busy = true;
            try
            {
                var chaCtrl = target;
                var inMaker = MakerAPI.InsideAndLoaded;
                // No load buttons outside maker, so plugins take all clothes and accessories from the card
                var pluginsLoadAll = !inMaker;
                var materialSnapshot = TakeMaterialSnapshot(chaCtrl, card, options, pluginsLoadAll);
                var overlaySnapshot = TakeOverlaySnapshot(chaCtrl, options, pluginsLoadAll);
                var clothesFromCard = options.Clothes ? options.ClothesParts : ClothesPart.None;

                // The original window has three buttons: 1 = clothes only, 2 = accessories only, 3 = both
                var mode = options.Accessories;
                int button;
                if (options.Clothes) button = options.ReplacesAllAccessories ? 3 : 1;
                else button = mode == AccessoryMode.Keep ? 0 : 2;
                var needsHook = mode != AccessoryMode.Keep && !options.ReplacesAllAccessories;
                AccessoryLoadContext context = null;

                if (button != 0)
                {
                    var clothesLoad = inMaker ? UnityEngine.Object.FindObjectOfType<CvsC_ClothesLoad>() : null;
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
                        if (inMaker) SetCoordinateButtonClicked(button);
                        var info = new CustomClothesFileInfo { FullPath = card.Path, FileName = System.IO.Path.GetFileName(card.Path) };
                        if (onClick != null) onClick(info);
                        else if (inMaker) LoadCoordinateLikeGame(chaCtrl, card.Path, button);
                        else LoadCoordinateInH(chaCtrl, card.Path, button);
                    }
                    finally
                    {
                        Hooks.Active = null;
                        Hooks.Parts = null;
                    }
                }

                for (var i = 0; i < FramesToWaitForPlugins; i++) yield return null;

                KeepUntickedClothesOverlays(chaCtrl, clothesFromCard, overlaySnapshot);

                if (MaterialEditorPresence.Available)
                {
                    if (options.Clothes || pluginsLoadAll) KeepUntickedClothesMaterials(chaCtrl, clothesFromCard, materialSnapshot);
                    if (pluginsLoadAll && mode == AccessoryMode.Keep) KeepAllAccessoryMaterials(chaCtrl, materialSnapshot);
                    ApplyMaterialGroups(chaCtrl, card, options.MaterialGroups & MaterialGroup.Clothes, options.MaterialCleanCopy);
                    if (options.MaterialFollowAccessories && context?.Result != null)
                        FixAccessoryMaterials(chaCtrl, card, context, materialSnapshot, replacedByGame: pluginsLoadAll || button == 2 || button == 3);
                }

                AdvancedLoaderPlugin.Logger.LogMessage($"Loaded {System.IO.Path.GetFileName(card.Path)}");
            }
            finally
            {
                Busy = false;
            }
        }

        private static void SetCoordinateButtonClicked(int button)
        {
            // HS2API remembers which coordinate button was pressed and reports it to plugins as load flags
            var hooksType = AccessTools.Inner(typeof(MakerAPI), "Hooks");
            var field = hooksType == null ? null : AccessTools.Field(hooksType, "CoordinateButtonClicked");
            field?.SetValue(null, button);
        }

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

        // In H the game only changes the worn clothes and refreshes its clothes buttons
        private static void LoadCoordinateInH(ChaControl chaCtrl, string path, int button)
        {
            var keepAccessories = button == 1 ? MessagePack.MessagePackSerializer.Serialize(chaCtrl.nowCoordinate.accessory) : null;
            var keepClothes = button == 2 ? MessagePack.MessagePackSerializer.Serialize(chaCtrl.nowCoordinate.clothes) : null;

            chaCtrl.nowCoordinate.LoadFile(path);
            if (keepAccessories != null) chaCtrl.nowCoordinate.accessory = MessagePack.MessagePackSerializer.Deserialize<ChaFileAccessory>(keepAccessories);
            if (keepClothes != null) chaCtrl.nowCoordinate.clothes = MessagePack.MessagePackSerializer.Deserialize<ChaFileClothes>(keepClothes);
            chaCtrl.Reload(false, true, true, true);

            var panel = NativeLoadPanels.FreeHPanel;
            if (panel != null) Traverse.Create(panel).Field("hSceneSpriteCloth").Method("SetClothCharacter").GetValue();
        }

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
