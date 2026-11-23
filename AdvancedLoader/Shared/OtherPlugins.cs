using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
using KKAPI.Maker.UI;
using UnityEngine;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal static class OtherPlugins
    {
        internal sealed class Entry
        {
            public string Id;
            public string Name;
            public CharacterApi.ControllerRegistration Registration;
        }

        internal sealed class State
        {
            public readonly Dictionary<CharacterApi.ControllerRegistration, bool> Maintain = new Dictionary<CharacterApi.ControllerRegistration, bool>();
            public readonly Dictionary<MakerLoadToggle, bool> Toggles = new Dictionary<MakerLoadToggle, bool>();
        }

        // Handled by the window; old ABMX is listed like any plugin
        private static IEnumerable<string> HandledPlugins()
        {
            yield return Dependencies.MaterialEditor.GUID;
            if (AbmxPresence.Available) yield return Dependencies.Abmx.GUID;
            yield return Dependencies.MoreAccessories.GUID;
            if (AccessoryGroups.Available) yield return AccessoryGroups.GUID;
#if KKS || KK
            yield return Dependencies.MoreOutfits.GUID;
#endif
#if KKS || KK
            if (ShapeEditorBridge.Available) yield return ShapeEditorBridge.PluginGuid;
            if (BlendshapeCreatorBridge.Available) yield return BlendshapeCreatorBridge.PluginGuid;
#endif
        }

        private static List<Entry> _entries;

        public static List<Entry> Entries => _entries ?? (_entries = FindEntries());

        private static List<Entry> FindEntries()
        {
            var plugins = Chainloader.PluginInfos.Values.Where(x => x.Instance != null).ToList();
            var handled = HandledPlugins().ToList();
            var entries = new List<Entry>();
            foreach (var registration in CharacterApi.RegisteredHandlers)
            {
                var id = registration.ExtendedDataId;
                if (string.IsNullOrEmpty(id) || id == Dependencies.ClothesOverlays.GUID) continue;

                var assembly = registration.ControllerType.Assembly;
                var owners = plugins.Where(x => x.Instance.GetType().Assembly == assembly).ToList();
                if (owners.Any(x => handled.Contains(x.Metadata.GUID))) continue;

                var owner = owners.FirstOrDefault(x => x.Metadata.GUID == id) ?? (owners.Count == 1 ? owners[0] : null);
                entries.Add(new Entry { Id = id, Name = owner?.Metadata.Name ?? id, Registration = registration });
            }

            // Two controllers of one plugin get the data ID added to tell them apart
            foreach (var group in entries.GroupBy(x => x.Name).Where(x => x.Count() > 1))
                foreach (var entry in group)
                    entry.Name = $"{entry.Name} ({entry.Id})";

            AdvancedLoaderPlugin.Logger.LogDebug("Other plugins with card data: " + string.Join(", ", entries.Select(x => x.Id).ToArray()));
            return entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static State Apply(LoadOptions options, CardData card)
        {
            var state = new State();
            var bonemod = !AbmxPresence.Available && Entries.Any(x => x.Id == Dependencies.Abmx.GUID && Loads(options, card, x));
            foreach (var toggle in GetLoadToggles())
            {
                state.Toggles[toggle] = toggle.Value;
                var value = AbmxPresence.LoadToggleTexts.Contains(toggle.Text) ? bonemod : true;
                if (toggle.Value != value) toggle.Value = value;
            }

            foreach (var entry in Entries)
                SetMaintain(state, entry.Registration, !Loads(options, card, entry));

            var overlays = CharacterApi.RegisteredHandlers.FirstOrDefault(x => x.ExtendedDataId == Dependencies.ClothesOverlays.GUID);
            if (overlays != null) SetMaintain(state, overlays, !options.Clothes);
            UncensorBridge.Loading = LoadsPlugin(options, card, UncensorSelectorGUID);
            return state;
        }

        // Follows Body > Breast
        private const string PushupGUID = "com.deathweasel.bepinex.pushup";

        public static bool Loads(LoadOptions options, CardData card, Entry entry)
        {
            if (entry.Id == PushupGUID) return options.Body && (options.BodyParts & BodyPart.Breast) != 0;
            return (OnCard(card, entry) || _onCharacter.Contains(entry.Id)) && options.OtherPlugins.TryGetValue(entry.Id, out var load) && load;
        }

        public const string UncensorSelectorGUID = "com.deathweasel.bepinex.uncensorselector";

        public static bool LoadsPlugin(LoadOptions options, CardData card, string id) => Entries.Any(x => x.Id == id && Loads(options, card, x));

        public static bool OnCard(CardData card, Entry entry) => card != null && card.PluginDataIds.Contains(entry.Id);

        public static List<Entry> Listed(CardData card, ChaControl chaCtrl)
        {
            var onCharacter = OnCharacter(chaCtrl);
            return Entries.Where(x => x.Id != PushupGUID && (OnCard(card, x) || onCharacter.Contains(x.Id))).ToList();
        }

        private static ChaControl _owner;
        private static HashSet<string> _onCharacter = new HashSet<string>();
        private static bool _stale = true;

        private static int _version;
        private static int _refreshAt = -1;

        // Plugins finish loading a few frames after the reload
        private const int FramesAfterReload = 30;

        public static int CharacterVersion
        {
            get
            {
                if (_refreshAt >= 0 && Time.frameCount >= _refreshAt)
                {
                    _refreshAt = -1;
                    CharacterChanged();
                }
                return _version;
            }
        }

        public static void CharacterChanged()
        {
            _stale = true;
            _version++;
        }

        public static void Init()
        {
            CharacterApi.CharacterReloaded += (sender, e) => Reloaded(e.ReloadedCharacter);
            CharacterApi.CoordinateLoaded += (sender, e) => Reloaded(e.Character);
        }

        private static void Reloaded(ChaControl chaCtrl)
        {
            if (_owner == null || chaCtrl != _owner) return;
            _stale = true;
            _refreshAt = Time.frameCount + FramesAfterReload;
        }

        public static HashSet<string> OnCharacter(ChaControl chaCtrl)
        {
            if (chaCtrl == null) return new HashSet<string>();
            if (chaCtrl == _owner && !_stale) return _onCharacter;
            _owner = chaCtrl;
            _stale = false;
            _onCharacter = new HashSet<string>(Entries.Where(x => HasData(chaCtrl, x)).Select(x => x.Id));
            Diag.Log($"Plugins with data on the character: {string.Join(", ", _onCharacter.ToArray())}");
            return _onCharacter;
        }

        private static bool HasData(ChaControl chaCtrl, Entry entry)
        {
            var controller = entry.Registration.Instances.FirstOrDefault(x => x.ChaControl == chaCtrl);
            if (controller == null) return false;
            Save(controller, entry);
            return ExtendedSave.GetExtendedDataById(chaCtrl.chaFile, entry.Id) != null;
        }

        private static void Save(CharaCustomFunctionController controller, Entry entry)
        {
            if (SaveMethod == null) return;
            try
            {
                SaveMethod.Invoke(controller, new object[] { GameMode.Maker });
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read the current data of {entry.Name}: {(ex.InnerException ?? ex).Message}");
            }
        }

        public static bool AnyLoaded(LoadOptions options, CardData card) => Entries.Any(x => Loads(options, card, x));

        private static readonly string[] AccessorySlotDataIds = { "com.deathweasel.bepinex.hairaccessorycustomizer", "Accessory_States", "madevil.kk.ass" };

        public static List<string> AccessorySlotDataNames(CardData card) =>
            Entries.Where(x => AccessorySlotDataIds.Contains(x.Id) && card.PluginDataIds.Contains(x.Id)).Select(x => x.Name).ToList();

        // Some plugins (Uncensor Selector) ignore MaintainState
        public static Dictionary<string, PluginData> KeptData(ChaControl chaCtrl, LoadOptions options, CardData card)
        {
            var kept = new Dictionary<string, PluginData>();
            foreach (var entry in Entries.Where(x => !Loads(options, card, x)))
            {
                // Data is only written on save
                var controller = entry.Registration.Instances.FirstOrDefault(x => x.ChaControl == chaCtrl);
                if (controller != null) Save(controller, entry);
                kept[entry.Id] = ExtendedSave.GetExtendedDataById(chaCtrl.chaFile, entry.Id);
            }
            return kept;
        }

        private static readonly MethodInfo SaveMethod = AccessTools.Method(typeof(CharaCustomFunctionController), "OnCardBeingSavedInternal", new[] { typeof(GameMode) });

        // Plugins read card data in maker from MakerAPI.LastLoadedChaFile
        public static void PutBack(ChaFile chaFile, Dictionary<string, PluginData> kept)
        {
            var loaded = MakerAPI.LastLoadedChaFile;
            foreach (var pair in kept)
            {
                ExtendedSave.SetExtendedDataById(chaFile, pair.Key, pair.Value);
                if (loaded != null && loaded != chaFile) ExtendedSave.SetExtendedDataById(loaded, pair.Key, pair.Value);
            }
        }

        public static void Restore(State state)
        {
            UncensorBridge.Loading = false;
            if (state == null) return;
            foreach (var pair in state.Maintain) pair.Key.MaintainState = pair.Value;
            foreach (var pair in state.Toggles)
                if (pair.Key.Value != pair.Value) pair.Key.Value = pair.Value;
        }

        private static void SetMaintain(State state, CharacterApi.ControllerRegistration registration, bool maintain)
        {
            state.Maintain[registration] = registration.MaintainState;
            registration.MaintainState = maintain;
        }

        private static List<MakerLoadToggle> GetLoadToggles()
        {
            return Traverse.Create(typeof(MakerLoadToggle)).Field("Toggles").GetValue<List<MakerLoadToggle>>() ?? new List<MakerLoadToggle>();
        }
    }
}
