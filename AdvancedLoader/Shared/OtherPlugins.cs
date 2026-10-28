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
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    /// <summary>
    /// Card data of other plugins, controlled through KKAPI: every plugin that saves data to the card registers a
    /// character controller, and a controller with MaintainState keeps its current data instead of reading the card.
    /// Plugins this window copies by itself (Material Editor, ABMX, clothes overlays...) are not listed here.
    /// </summary>
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

        // Handled by the window's own sections, so their controllers are left alone.
        // ABMX only when it is new enough to be copied by category; an older one is listed here like any other plugin.
        private static IEnumerable<string> HandledPlugins()
        {
            yield return Dependencies.MaterialEditor.GUID;
            if (AbmxPresence.Available) yield return Dependencies.Abmx.GUID;
            yield return Dependencies.MoreAccessories.GUID;
#if KKS || KK
            yield return Dependencies.MoreOutfits.GUID;
#endif
        }

        private static List<Entry> _entries;

        /// <summary>All plugins with card data that can be loaded or kept, sorted by name.</summary>
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

        /// <summary>
        /// For a character load: ticked plugins read the card, all others keep their current data.
        /// Clothes overlays follow the Clothes category. Load toggles in the game's window are turned on so they don't
        /// stop a ticked plugin. ABMX's are turned off because bonemod is copied by category afterwards, or, with an ABMX
        /// too old for that, follow its entry in this list.
        /// </summary>
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
            return state;
        }

        /// <summary>Ticked and on the card; a plugin without data on the card would only lose its current data.</summary>
        public static bool Loads(LoadOptions options, CardData card, Entry entry)
        {
            return card.PluginDataIds.Contains(entry.Id) && options.OtherPlugins.TryGetValue(entry.Id, out var load) && load;
        }

        public static bool AnyLoaded(LoadOptions options, CardData card) => Entries.Any(x => Loads(options, card, x));

        /// <summary>
        /// Current card data of the plugins that are not loaded. Some plugins (e.g. Uncensor Selector) read it straight
        /// from the character instead of following MaintainState, so it is put back after the game's load.
        /// </summary>
        public static Dictionary<string, PluginData> KeptData(ChaControl chaCtrl, LoadOptions options, CardData card)
        {
            var kept = new Dictionary<string, PluginData>();
            foreach (var entry in Entries.Where(x => !Loads(options, card, x)))
            {
                // The character's own copy is only written when a card is saved, so the plugin writes its current state first
                var controller = entry.Registration.Instances.FirstOrDefault(x => x.ChaControl == chaCtrl);
                if (controller != null && SaveMethod != null)
                {
                    try
                    {
                        SaveMethod.Invoke(controller, new object[] { GameMode.Maker });
                    }
                    catch (Exception ex)
                    {
                        AdvancedLoaderPlugin.Logger.LogWarning($"Could not read the current data of {entry.Name}: {ex.Message}");
                    }
                }
                kept[entry.Id] = ExtendedSave.GetExtendedDataById(chaCtrl.chaFile, entry.Id);
            }
            return kept;
        }

        private static readonly MethodInfo SaveMethod = AccessTools.Method(typeof(CharaCustomFunctionController), "OnCardBeingSavedInternal", new[] { typeof(GameMode) });

        /// <summary>
        /// Put the kept data on the character and on the loaded card's file, which is where plugins read card data from
        /// in maker (MakerAPI.LastLoadedChaFile).
        /// </summary>
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
