using System.Collections.Generic;
using System.Linq;
using KKAPI.Maker;
using KKAPI.Utilities;
using UnityEngine;
#if HS2
using CharaCustom;
#elif KKS
using ChaCustom;
#endif

namespace AdvancedLoader
{
    /// <summary>
    /// The "Advanced load" window. It works with the game's open load panel: the card is the one selected there,
    /// and the options follow the panel (character options in "Load character", clothes options in "Clothes > Load").
    /// </summary>
    internal sealed class AdvancedLoadWindow
    {
        private const int WindowId = 0x45434C32;
        private const float MinWidth = 360;
        private const float MaxHeight = 640;
        private static readonly string[] AccessoryModeNames = { "Keep current", "All from card", "Selected" };
        private static readonly string[] PlacementNames = { "Same slots", "Shift by", "Free slots" };
#if KKS
        private static readonly string[] OutfitModeNames = { "All from card", "Selected" };
        private static readonly string[] OutfitPlacementNames = { "Same outfits", "Shift by", "New outfits" };

        // Card outfit -> character outfit for the current options, and the card outfit whose accessories are listed
        private Dictionary<int, int> _outfitTargets = new Dictionary<int, int>();
        private int _shownOutfit = -1;
#endif

        private readonly AdvancedLoaderPlugin _plugin;
        private readonly NativeLoadPanels _panels;
        private readonly Dictionary<LoadMode, LoadOptions> _options = new Dictionary<LoadMode, LoadOptions>
        {
            { LoadMode.Character, new LoadOptions { Face = true, Body = true, Hair = true, Clothes = true, Accessories = AccessoryMode.All } },
            { LoadMode.Coordinate, new LoadOptions { Clothes = true, Accessories = AccessoryMode.All } }
        };

        private LoadMode _mode;
        private CardData _card;
        private Rect _rect;
        private bool _placed;
        private Vector2 _scroll;
        // Card slot -> character slot for the current options, refreshed every frame for the preview
        private Dictionary<int, int> _targets = new Dictionary<int, int>();
        // Card slots the current options would load, and how many character slots are free
        private int _sourceCount;
        private int _freeSlots;
        private GUIStyle _warningStyle;
        private GUIStyle _headerStyle;
        private bool _faceExpanded;
        private bool _bodyExpanded;
        private bool _clothesExpanded;

        public AdvancedLoadWindow(AdvancedLoaderPlugin plugin, NativeLoadPanels panels)
        {
            _plugin = plugin;
            _panels = panels;
        }

        /// <summary>Opened by the user. Only drawn while a game load panel is open.</summary>
        public bool Visible { get; set; }

        private bool Shown => Visible && _panels.OpenMode != null;
        private LoadOptions Options => _options[_mode];

        public void Reset()
        {
            Visible = false;
            _card = null;
            foreach (var options in _options.Values) options.SelectedSlots.Clear();
        }

        public void Update()
        {
            if (!Shown) return;

            var mode = _panels.OpenMode.Value;
            var path = _panels.SelectedPath;
            if (mode != _mode)
            {
                _mode = mode;
                _card = null;
            }

            if (path == null)
            {
                _card = null;
            }
            else if (_card == null || _card.Path != path)
            {
                _card = CardData.Read(path, _mode, (byte)MakerAPI.GetMakerSex());
#if KKS
                _shownOutfit = -1;
                Options.SelectedOutfits.Clear();
#endif
                Options.SelectedSlots.Clear();
            }

            UpdateTargets();
        }

        private void UpdateTargets()
        {
            var options = Options;
            var chaCtrl = MakerAPI.GetCharacterControl();
            var occupancyOf = chaCtrl == null ? null : chaCtrl.nowCoordinate;
#if KKS
            if (chaCtrl != null && _mode == LoadMode.Character && _card != null && _card.CardOutfits.Count > 0)
            {
                _outfitTargets = Outfits.MapTargets(options, _card.CardOutfits.Count, chaCtrl.chaFile.coordinate.Length);

                // Accessories are listed for one card outfit: the first selected one, or the one matching the worn outfit
                var shown = options.OutfitSelection == OutfitMode.Selected && options.SelectedOutfits.Count > 0
                    ? options.SelectedOutfits.Min()
                    : Mathf.Min(chaCtrl.fileStatus.coordinateType, _card.CardOutfits.Count - 1);
                if (shown != _shownOutfit)
                {
                    _shownOutfit = shown;
                    _card.ShowOutfit(shown);
                    options.SelectedSlots.Clear();
                }

                // Free slots are counted in the outfit the shown card outfit is loaded into
                occupancyOf = _outfitTargets.TryGetValue(shown, out var target) && target < chaCtrl.chaFile.coordinate.Length
                    ? chaCtrl.chaFile.coordinate[target]
                    : new ChaFileCoordinate();
            }
            else
            {
                _outfitTargets = new Dictionary<int, int>();
            }
#endif
            if (_card?.AccessorySlots == null || chaCtrl == null || options.Accessories == AccessoryMode.Keep)
            {
                _targets = new Dictionary<int, int>();
                _sourceCount = 0;
                return;
            }
            var occupied = AccessoryData.ReadOccupancy(occupancyOf, chaCtrl.chaFile);
            var sources = AccessoryData.SourceSlots(options.Accessories, _card.AccessorySlots, options.SelectedSlots);
            var sourceList = sources.ToList();
            _sourceCount = sourceList.Count;
            _freeSlots = occupied.Take(AccessoryData.VanillaSlotCount).Count(x => !x) + Mathf.Max(0, AccessoryData.VanillaSlotCount - occupied.Count);
            _targets = AccessoryData.MapTargets(options.Placement, options.Shift, occupied, sourceList);
        }

        public void OnGUI()
        {
            if (!Shown) return;
            if (!_placed)
            {
                _rect = NativeLoadPanels.DefaultWindowRect(MinWidth, MaxHeight);
                _placed = true;
            }

            var skin = GUI.skin;
            GUI.skin = IMGUIUtils.SolidBackgroundGuiSkin;
            if (_headerStyle == null) _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            if (_warningStyle == null)
            {
                _warningStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
                _warningStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
            }
            var title = _mode == LoadMode.Character ? "Advanced load: character" : "Advanced load: clothes";
            _rect = GUILayout.Window(WindowId, _rect, DrawWindow, title);
            GUI.skin = skin;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(_card == null ? "Select a card in the list." : CardTitle());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(26))) _plugin.SetWindowVisible(false);
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawOptions();
            GUILayout.EndScrollView();

            GUILayout.Label(Summary());
            var enabled = GUI.enabled;
            GUI.enabled = CanLoad();
            if (GUILayout.Button(LoadRunner.Busy ? "Loading…" : "Load", GUILayout.Height(30))) StartLoad();
            GUI.enabled = enabled;

            _rect = IMGUIUtils.DragResizeEatWindow(id, _rect);
        }

        /// <summary>Name from the card as the game's list shows it, with the file name for telling cards apart.</summary>
        private string CardTitle()
        {
            var file = System.IO.Path.GetFileNameWithoutExtension(_card.Path);
            var name = _panels.SelectedName;
            return string.IsNullOrEmpty(name) || name == file ? file : $"{name}  ({file})";
        }

        private void DrawOptions()
        {
            var options = Options;

            BeginSection(_mode == LoadMode.Character ? "Character" : "Clothes");
            if (_mode == LoadMode.Character)
            {
                DrawFace(options);
                DrawBody(options);
                if (options.Face || options.Body)
                    DrawWarning(Dependencies.Abmx.Warning(Dependencies.Abmx.State == DependencyState.TooOld
                        ? "bonemod sliders are not loaded by category. Use the \"Face Bonemod\" / \"Body Bonemod\" toggles under Other plugins instead."
                        : "bonemod sliders are not loaded."));
                options.Hair = GUILayout.Toggle(options.Hair, " Hair");
                var canLoadParameters = Singleton<CustomBase>.Instance.modeNew;
                var enabled = GUI.enabled;
                GUI.enabled = canLoadParameters;
                options.Parameters = GUILayout.Toggle(options.Parameters && canLoadParameters, canLoadParameters ? " Parameters" : " Parameters (new characters only)");
                GUI.enabled = enabled;
            }
            DrawClothes(options);
            EndSection();

#if KKS
            if (_mode == LoadMode.Character && (options.Clothes || options.Accessories != AccessoryMode.Keep)) DrawOutfits(options);
#endif

            BeginSection("Accessories");
            options.Accessories = (AccessoryMode)GUILayout.Toolbar((int)options.Accessories, AccessoryModeNames);
            if (options.Accessories != AccessoryMode.Keep)
                DrawWarning(Dependencies.MoreAccessories.Warning("only accessory slots 1-20 can be used."));
            if (options.Accessories != AccessoryMode.Keep) DrawPlacement(options);
            if (options.Accessories == AccessoryMode.Selected) DrawAccessorySlots(options);
            else if (options.Accessories == AccessoryMode.All && options.Placement != AccessoryPlacement.SameSlots) DrawTargetSummary();
            EndSection();

            if (!MaterialEditorPresence.Available)
            {
                BeginSection("Material Editor");
                DrawWarning(Dependencies.MaterialEditor.Warning("materials are not copied, and loading Face, Body, Hair or Clothes keeps Material Editor's own behaviour."));
                EndSection();
            }
            else
            {
                BeginSection("Material Editor");
                if (_mode == LoadMode.Character)
                {
                    DrawGroupToggle(options, MaterialGroup.Body, " Body");
                    DrawGroupToggle(options, MaterialGroup.Head, " Head");
                    DrawGroupToggle(options, MaterialGroup.Eyes, " Eyes");
                    DrawGroupToggle(options, MaterialGroup.Hair, " Hair");
                }
                DrawGroupToggle(options, MaterialGroup.Clothes, " Clothes");
                GUILayout.Space(4);
                options.MaterialCleanCopy = GUILayout.Toggle(options.MaterialCleanCopy, " Clean copy (reset the group first)");
                options.MaterialFollowAccessories = GUILayout.Toggle(options.MaterialFollowAccessories, " Accessory materials follow their accessories");
                EndSection();
            }

            if (_mode == LoadMode.Character) DrawPluginToggles(options);
        }

        private void DrawFace(LoadOptions options)
        {
            GUILayout.BeginHorizontal();
            options.Face = GUILayout.Toggle(options.Face, " Face" + PartCount(options.FaceParts == FacePart.All, Describe(options.FaceParts)));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_faceExpanded ? "Hide parts" : "Parts...", GUILayout.Width(80))) _faceExpanded = !_faceExpanded;
            GUILayout.EndHorizontal();
            if (!_faceExpanded) return;

            var enabled = GUI.enabled;
            GUI.enabled = options.Face;
            foreach (var part in BodyFaceParts.FacePartNames(AbmxPresence.Available))
            {
                var isOn = (options.FaceParts & part.Key) != 0;
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                if (GUILayout.Toggle(isOn, " " + part.Value) != isOn)
                    options.FaceParts = isOn ? options.FaceParts & ~part.Key : options.FaceParts | part.Key;
                GUILayout.EndHorizontal();
            }
            DrawAllNone(() => options.FaceParts = FacePart.All, () => options.FaceParts = FacePart.None);
            GUI.enabled = enabled;
        }

        private void DrawBody(LoadOptions options)
        {
            GUILayout.BeginHorizontal();
            options.Body = GUILayout.Toggle(options.Body, " Body" + PartCount(options.BodyParts == BodyPart.All, Describe(options.BodyParts)));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_bodyExpanded ? "Hide parts" : "Parts...", GUILayout.Width(80))) _bodyExpanded = !_bodyExpanded;
            GUILayout.EndHorizontal();
            if (!_bodyExpanded) return;

            var enabled = GUI.enabled;
            GUI.enabled = options.Body;
            foreach (var part in BodyFaceParts.BodyPartNames(AbmxPresence.Available))
            {
                var isOn = (options.BodyParts & part.Key) != 0;
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                if (GUILayout.Toggle(isOn, " " + part.Value) != isOn)
                    options.BodyParts = isOn ? options.BodyParts & ~part.Key : options.BodyParts | part.Key;
                GUILayout.EndHorizontal();
            }
            DrawAllNone(() => options.BodyParts = BodyPart.All, () => options.BodyParts = BodyPart.None);
            GUI.enabled = enabled;
        }

        private void DrawClothes(LoadOptions options)
        {
            GUILayout.BeginHorizontal();
            options.Clothes = GUILayout.Toggle(options.Clothes, " Clothes" + PartCount(options.ClothesParts == ClothesPart.All, ClothesParts.Describe(options.ClothesParts)));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_clothesExpanded ? "Hide parts" : "Parts...", GUILayout.Width(80))) _clothesExpanded = !_clothesExpanded;
            GUILayout.EndHorizontal();
            if (!_clothesExpanded) return;

            var enabled = GUI.enabled;
            GUI.enabled = options.Clothes;
            foreach (var part in ClothesParts.PartNames)
            {
                var isOn = (options.ClothesParts & part.Key) != 0;
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                if (GUILayout.Toggle(isOn, " " + part.Value) != isOn)
                    options.ClothesParts = isOn ? options.ClothesParts & ~part.Key : options.ClothesParts | part.Key;
                GUILayout.EndHorizontal();
            }
            DrawAllNone(() => options.ClothesParts = ClothesPart.All, () => options.ClothesParts = ClothesPart.None);
            GUI.enabled = enabled;
        }

        private static void DrawAllNone(System.Action all, System.Action none)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            if (GUILayout.Button("All", GUILayout.Width(50))) all();
            if (GUILayout.Button("None", GUILayout.Width(50))) none();
            GUILayout.EndHorizontal();
        }

        private static string PartCount(bool all, string description) => all ? "" : description.Length == 0 ? " (nothing)" : " (some parts)";

        private static string Describe(FacePart parts) => BodyFaceParts.Describe(parts);
        private static string Describe(BodyPart parts) => BodyFaceParts.Describe(parts);

        private void DrawWarning(string text)
        {
            if (!string.IsNullOrEmpty(text)) GUILayout.Label(text, _warningStyle);
        }

        private void BeginSection(string title)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(title, _headerStyle);
        }

        private static void EndSection()
        {
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawPlacement(LoadOptions options)
        {
            options.Placement = (AccessoryPlacement)GUILayout.Toolbar((int)options.Placement, PlacementNames);
            // Without MoreAccessories there are only 20 slots, so a bigger shift could never fit anything
            var maxShift = MoreAccessoriesBridge.Available ? 500 : AccessoryData.VanillaSlotCount - 1;
            options.Shift = Mathf.Clamp(options.Shift, 0, maxShift);
            if (options.Placement == AccessoryPlacement.Shift)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Shift by", GUILayout.ExpandWidth(false));
                if (GUILayout.Button("-", GUILayout.Width(26))) options.Shift = Mathf.Max(0, options.Shift - 1);
                if (int.TryParse(GUILayout.TextField(options.Shift.ToString(), GUILayout.Width(50)), out var shift)) options.Shift = Mathf.Clamp(shift, 0, maxShift);
                if (GUILayout.Button("+", GUILayout.Width(26))) options.Shift = Mathf.Min(maxShift, options.Shift + 1);
                GUILayout.Label($"slots (slot 1 → {options.Shift + 1})");
                GUILayout.EndHorizontal();
            }

            string hint;
            switch (options.Placement)
            {
                case AccessoryPlacement.Shift:
                    hint = "Current accessories stay; loaded ones move by the shift and overwrite what is there.";
                    break;
                case AccessoryPlacement.FreeSlots:
                    hint = "Current accessories stay; loaded ones fill the empty slots in order.";
                    break;
                default:
                    hint = options.Accessories == AccessoryMode.All
                        ? "All current accessories are replaced by the card's."
                        : "Ticked slots replace the same slot numbers.";
                    break;
            }
            GUILayout.Label(hint);
            DrawSlotWarning(options);
        }

        /// <summary>
        /// Yellow warning when some loaded accessories have no slot to go to (only slots 1-20 exist without MoreAccessories).
        /// </summary>
        private void DrawSlotWarning(LoadOptions options)
        {
            if (MoreAccessoriesBridge.Available || options.Placement == AccessoryPlacement.SameSlots) return;

            var skipped = _sourceCount - _targets.Count;
            string warning = null;
            if (options.Placement == AccessoryPlacement.FreeSlots && _freeSlots == 0)
                warning = "All 20 accessory slots are taken, so there is no free slot. Nothing will be loaded with \"Free slots\".";
            else if (skipped > 0 && options.Placement == AccessoryPlacement.FreeSlots)
                warning = $"Only {_freeSlots} of 20 slots are free, {skipped} accessories will not be loaded.";
            else if (skipped > 0)
                warning = $"{skipped} accessories would go past slot 20 and will not be loaded.";

            DrawWarning(warning);
        }

        private void DrawTargetSummary()
        {
            if (_card == null) return;
            var skipped = _card.SlotNames.Count - _targets.Count;
            GUILayout.Label(_targets.Count == 0
                ? "Nothing to load."
                : $"{_targets.Count} accessories → slots {string.Join(", ", _targets.Values.OrderBy(x => x).Select(x => (x + 1).ToString()).ToArray())}" +
                  (skipped > 0 ? $" ({skipped} do not fit)" : ""));
        }

        private string TargetText(int sourceSlot)
        {
            if (Options.Placement == AccessoryPlacement.SameSlots) return "";
            return _targets.TryGetValue(sourceSlot, out var target) ? $"  → {target + 1:00}" : "  → no slot";
        }

#if KKS
        /// <summary>
        /// Which card outfits are loaded and into which of the character's outfits.
        /// </summary>
        private void DrawOutfits(LoadOptions options)
        {
            BeginSection("Outfits");
            if (_card == null || _card.CardOutfits.Count == 0)
            {
                GUILayout.Label("Select a card to see its outfits.");
                EndSection();
                return;
            }

            options.OutfitSelection = (OutfitMode)GUILayout.Toolbar((int)options.OutfitSelection, OutfitModeNames);
            options.OutfitPlacement = (OutfitPlacement)GUILayout.Toolbar((int)options.OutfitPlacement, OutfitPlacementNames);
            if (options.OutfitPlacement == OutfitPlacement.Shift)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Shift by", GUILayout.ExpandWidth(false));
                if (GUILayout.Button("-", GUILayout.Width(26))) options.OutfitShift = Mathf.Max(0, options.OutfitShift - 1);
                if (int.TryParse(GUILayout.TextField(options.OutfitShift.ToString(), GUILayout.Width(50)), out var shift)) options.OutfitShift = Mathf.Clamp(shift, 0, 100);
                if (GUILayout.Button("+", GUILayout.Width(26))) options.OutfitShift = Mathf.Min(100, options.OutfitShift + 1);
                GUILayout.Label("outfits");
                GUILayout.EndHorizontal();
            }
            DrawWarning(Dependencies.MoreOutfits.Warning("only the 4 base outfits exist, card outfits past them are not loaded."));

            var chaCtrl = MakerAPI.GetCharacterControl();
            foreach (var outfit in _card.CardOutfits)
            {
                var target = _outfitTargets.TryGetValue(outfit.Index, out var t)
                    ? $"  → {t + 1:00}. {(t < chaCtrl.chaFile.coordinate.Length ? Outfits.GetName(chaCtrl, t) ?? Outfits.DefaultName(t) : "new outfit")}"
                    : "";
                var label = $"{outfit.Index + 1:00}.  {outfit.Name}{target}";
                if (options.OutfitSelection == OutfitMode.Selected)
                {
                    var isOn = options.SelectedOutfits.Contains(outfit.Index);
                    if (GUILayout.Toggle(isOn, label) == isOn) continue;
                    if (isOn) options.SelectedOutfits.Remove(outfit.Index);
                    else options.SelectedOutfits.Add(outfit.Index);
                }
                else
                {
                    GUILayout.Label(label);
                }
            }

            if (options.Accessories == AccessoryMode.Selected && _outfitTargets.Count > 1)
                DrawWarning($"Accessories are listed for outfit {_shownOutfit + 1}; the ticked slots are used for every loaded outfit.");
            EndSection();
        }
#endif

        private void DrawAccessorySlots(LoadOptions options)
        {
            if (_card == null)
            {
                GUILayout.Label("Select a card to see its accessories.");
                return;
            }
            if (_card.SlotNames.Count == 0)
            {
                GUILayout.Label(_card.AccessorySlots == null ? "Could not read this card." : "This card has no accessories.");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", GUILayout.Width(50))) options.SelectedSlots.UnionWith(_card.SlotNames.Select(x => x.Key));
            if (GUILayout.Button("None", GUILayout.Width(50))) options.SelectedSlots.Clear();
            GUILayout.Label($"{options.SelectedSlots.Count} / {_card.SlotNames.Count} selected");
            GUILayout.EndHorizontal();

            foreach (var slot in _card.SlotNames)
            {
                var isOn = options.SelectedSlots.Contains(slot.Key);
                if (GUILayout.Toggle(isOn, isOn ? slot.Value + TargetText(slot.Key) : slot.Value) == isOn) continue;
                if (isOn) options.SelectedSlots.Remove(slot.Key);
                else options.SelectedSlots.Add(slot.Key);
            }
        }

        private static void DrawGroupToggle(LoadOptions options, MaterialGroup group, string text)
        {
            var isOn = (options.MaterialGroups & group) != 0;
            if (GUILayout.Toggle(isOn, text) == isOn) return;
            options.MaterialGroups = isOn ? options.MaterialGroups & ~group : options.MaterialGroups | group;
        }

        /// <summary>
        /// Load toggles other plugins added to the game's "Load character" window (e.g. overlays). This window keeps its
        /// own values (off until ticked here) and only puts them on those toggles while it loads.
        /// </summary>
        private void DrawPluginToggles(LoadOptions options)
        {
            // Bonemod follows the Face and Body categories, so ABMX's own toggles are not shown
            var toggles = LoadRunner.GetPluginToggles()
                .Where(x => !(AbmxPresence.Available && AbmxPresence.LoadToggleTexts.Contains(x.Text)))
                .ToList();
            if (toggles.Count == 0) return;

            BeginSection("Other plugins");
            foreach (var toggle in toggles)
            {
                options.PluginToggles.TryGetValue(toggle.Text, out var isOn);
                options.PluginToggles[toggle.Text] = GUILayout.Toggle(isOn, " " + toggle.Text);
            }
            EndSection();
        }

        private string Summary()
        {
            if (_card == null) return "Select a card.";
            var options = Options;
            var parts = new List<string>();
            if (_mode == LoadMode.Character)
            {
                if (options.Face && options.FaceParts != FacePart.None)
                    parts.Add(options.FaceParts == FacePart.All ? "Face" : $"Face ({BodyFaceParts.Describe(options.FaceParts)})");
                if (options.Body && options.BodyParts != BodyPart.None)
                    parts.Add(options.BodyParts == BodyPart.All ? "Body" : $"Body ({BodyFaceParts.Describe(options.BodyParts)})");
                if (options.Hair) parts.Add("Hair");
                if (options.Parameters && Singleton<CustomBase>.Instance.modeNew) parts.Add("Parameters");
            }
            if (options.Clothes && options.ClothesParts != ClothesPart.None)
                parts.Add(options.ClothesParts == ClothesPart.All ? "Clothes" : $"Clothes ({ClothesParts.Describe(options.ClothesParts)})");
#if KKS
            if (_mode == LoadMode.Character && (options.Clothes || options.Accessories != AccessoryMode.Keep))
                parts.Add(_outfitTargets.Count == 0 ? "no outfits" : $"outfits {string.Join(", ", _outfitTargets.Keys.OrderBy(x => x).Select(x => (x + 1).ToString()).ToArray())}");
#endif
            if (options.ReplacesAllAccessories) parts.Add("all accessories");
            else if (options.Accessories != AccessoryMode.Keep && _targets.Count > 0)
                parts.Add("accessories into slots " + string.Join(", ", _targets.Values.OrderBy(x => x).Select(x => (x + 1).ToString()).ToArray()));
            var groups = _mode == LoadMode.Character ? options.MaterialGroups : options.MaterialGroups & MaterialGroup.Clothes;
            if (MaterialEditorPresence.Available && groups != MaterialGroup.None) parts.Add($"materials ({groups})");
            if (_mode == LoadMode.Character)
                parts.AddRange(options.PluginToggles.Where(x => x.Value && !(AbmxPresence.Available && AbmxPresence.LoadToggleTexts.Contains(x.Key))).Select(x => x.Key));
            return parts.Count == 0 ? "Nothing selected to load." : "Load: " + string.Join(", ", parts.ToArray());
        }

        private bool CanLoad()
        {
            if (_card == null || _card.AccessorySlots == null || LoadRunner.Busy) return false;
            var o = Options;
            // Accessories count only when at least one of them has a slot to go to
            var accessories = o.ReplacesAllAccessories || (o.Accessories != AccessoryMode.Keep && _targets.Count > 0);
            var anything = o.Clothes || accessories || (MaterialEditorPresence.Available && o.MaterialGroups != MaterialGroup.None);
            if (_mode == LoadMode.Character) anything |= o.Face || o.Body || o.Hair || o.Parameters || o.PluginToggles.Values.Any(x => x);
            return anything;
        }

        private void StartLoad()
        {
            var card = _card;
            var options = Options;
            _plugin.StartCoroutine(_mode == LoadMode.Character ? LoadRunner.LoadCharacter(card, options) : LoadRunner.LoadCoordinate(card, options));
        }
    }
}
