using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KKAPI.Maker;
using KKAPI.Utilities;
using UnityEngine;
#if HS2 || AI
using AIChara;
using CharaCustom;
#elif KKS || KK
using ChaCustom;
#endif

namespace AdvancedLoader
{
    internal sealed class AdvancedLoadWindow
    {
        private const int WindowId = 0x45434C32;
        private const float MinWidth = 360;
        private const float MaxHeight = 640;
        private const float CellWidth = 150;
        private static readonly string[] AccessoryModeNames = { "Keep current", "All from card", "Selected" };
        private static readonly string[] PlacementNames = { "Same slots", "Shift by", "Free slots" };
#if KKS || KK
        private static readonly string[] OutfitModeNames = { "All from card", "Selected" };
        private static readonly string[] OutfitPlacementNames = { "Same outfits", "Shift by", "New outfits" };

        // Card outfit -> character outfit for the current options, and the card outfit whose accessories are listed
        private Dictionary<int, int> _outfitTargets = new Dictionary<int, int>();
        private int _shownOutfit = -1;
#endif

        private enum Tab
        {
            Body,
            Clothes,
            Outfits,
            Accessories,
            Advanced,
            Plugins
        }

        private static readonly string[] TabNames = Enum.GetNames(typeof(Tab));

        private sealed class Parts
        {
            public string Name;
            public Func<LoadOptions, bool> IsOn;
            public Action<LoadOptions, bool> SetOn;
            public Func<LoadOptions, int> Get;
            public Action<LoadOptions, int> Set;
            public int All;
            public int Mask;
            public KeyValuePair<int, string>[] Names;

            public DotState State(LoadOptions o)
            {
                var on = IsOn(o) ? Get(o) & Mask : 0;
                return on == 0 ? DotState.None : on == Mask ? DotState.All : DotState.Some;
            }

            public void SetAll(LoadOptions o, bool on)
            {
                SetOn(o, on);
                if (on) Set(o, All);
            }

            public bool Has(LoadOptions o, int part) => IsOn(o) && (Get(o) & part) != 0;

            public void Flip(LoadOptions o, int part)
            {
                if (!IsOn(o))
                {
                    SetOn(o, true);
                    Set(o, part);
                    return;
                }
                var parts = Get(o) ^ part;
                if ((parts & Mask) != 0)
                {
                    Set(o, parts);
                    return;
                }
                SetOn(o, false);
                Set(o, All);
            }
        }

        private static Parts _faceParts;
        private static Parts _bodyParts;
        private static Parts _hairParts;
        private static Parts _clothesParts;
        private static Parts[] _bodyTabParts;

        private readonly AdvancedLoaderPlugin _plugin;
        private readonly NativeLoadPanels _panels;
        private readonly Dictionary<LoadMode, LoadOptions> _options = new Dictionary<LoadMode, LoadOptions>
        {
            { LoadMode.Character, LoadOptions.Default(LoadMode.Character) },
            { LoadMode.Coordinate, LoadOptions.Default(LoadMode.Coordinate) }
        };

        private LoadMode _mode;
        private CardData _card;
        private Rect _rect;
        private bool _placed;
        private string _placedAt;
        private Vector2 _scroll;
        // Card slot -> character slot for the current options, refreshed every frame for the preview
        private Dictionary<int, int> _targets = new Dictionary<int, int>();
        private bool _targetsDirty = true;
        private bool _wasShown;
        // Card slots the current options would load, and how many character slots are free
        private int _sourceCount;
        private int _freeSlots;
        private Tab _tab;
        private int _bodyTab;
        private bool _docked;
        private bool _buttonsBelow;
        private ChaControl _lastTarget;
        private float _contentWidth;
        private CardData _entriesCard;
        private ChaControl _entriesTarget;
        private int _entriesVersion = -1;
        private List<OtherPlugins.Entry> _entries = new List<OtherPlugins.Entry>();
        private string[] _entryLabels = new string[0];
        private CardData _bonesCard;
        private ChaControl _bonesTarget;
        private int _bonesVersion = -1;
        private List<BoneDifference> _bones = new List<BoneDifference>();
        private string[] _boneLabels = new string[0];
        private static readonly List<BoneDifference> NoBones = new List<BoneDifference>();
        private int _bonesFaceCount;
        private const int BonemodTab = 3;

        public AdvancedLoadWindow(AdvancedLoaderPlugin plugin, NativeLoadPanels panels)
        {
            _plugin = plugin;
            _panels = panels;
            if (Settings.RememberTicks.Value)
                foreach (var options in _options)
                    options.Value.Load(Settings.SavedTicks(options.Key));

            AccessoriesApi.AccessoryKindChanged += (sender, args) => _targetsDirty = true;
            AccessoriesApi.AccessoryTransferred += (sender, args) => _targetsDirty = true;
#if KKS || KK
            AccessoriesApi.AccessoriesCopied += (sender, args) => _targetsDirty = true;
#endif
            MakerAPI.ReloadCustomInterface += (sender, args) => _targetsDirty = true;
        }

        public void SaveTicks()
        {
            if (!Settings.RememberTicks.Value) return;
            foreach (var options in _options) Settings.SaveTicks(options.Key, options.Value.Save());
        }

        public bool Visible { get; set; }

        public bool Shown => Visible && _panels.OpenMode != null;
        private LoadOptions Options => _options[_mode];

        public void Reset()
        {
            Visible = false;
            SaveTicks();
            _card = null;
            foreach (var options in _options.Values) options.ClearSlots();
        }

        public void Update()
        {
            if (!Shown)
            {
                _wasShown = false;
                return;
            }
            if (!_wasShown)
            {
                _wasShown = true;
                _targetsDirty = true;
                OtherPlugins.CharacterChanged();
            }

            var mode = _panels.OpenMode.Value;
            var path = _panels.SelectedPath;
            if (_panels.Target != _lastTarget)
            {
                _lastTarget = _panels.Target;
                _targetsDirty = true;
            }
            if (mode != _mode)
            {
                _mode = mode;
                _card = null;
                _targetsDirty = true;
            }

            if (path == null)
            {
                if (_card != null) _targetsDirty = true;
                _card = null;
            }
            else if (_card == null || _card.Path != path)
            {
                var target = _panels.Target;
                _card = CardData.Read(path, _mode, target != null ? target.sex : _panels.DefaultSex);
#if KKS || KK
                _shownOutfit = -1;
                Options.SelectedOutfits.Clear();
#endif
                Options.ClearSlots();
                _targetsDirty = true;
            }

            if (_viewVersion != OtherPlugins.CharacterVersion)
            {
                _viewVersion = OtherPlugins.CharacterVersion;
                _targetsDirty = true;
            }

            if (!_targetsDirty) return;
            _targetsDirty = false;
            UpdateTargets();
            RebuildView();
        }

        private int _viewVersion = -1;
        private List<Tab> _tabs = new List<Tab>();
        private readonly Dictionary<Tab, DotState> _tabStates = new Dictionary<Tab, DotState>();
        private int _materialsOn;
        private string _summary = "";
        private string _cardTitle = "";
        private string _bonesSelected = "";
        private string _slotsSelected = "";
        private string _shiftText = "";
        private string _slotDataWarning;
        private string _slotWarning;
        private string _targetSummary = "";
#if KKS || KK
        private string _outfitAccessoryWarning;
#endif
        private readonly List<SlotGroupView> _slotGroups = new List<SlotGroupView>();
        private readonly Dictionary<int, string> _slotLabels = new Dictionary<int, string>();
        private readonly List<string> _outfitLabels = new List<string>();

        private sealed class SlotGroupView
        {
            public string Title;
            public List<KeyValuePair<int, string>> Slots;
            public int Group;
            public bool On;
        }

        private void RebuildView()
        {
            var o = Options;
            _tabs = Tabs.ToList();
            _tabStates.Clear();
            foreach (var tab in _tabs) _tabStates[tab] = StateOf(tab, o);
            _materialsOn = MaterialsOn(o);
            _summary = Summary();
            _cardTitle = _card == null ? "Select a card in the list." : CardTitle();
            _bonesSelected = $"{o.SelectedBones.Count} / {Bones.Count} selected";
            _slotsSelected = _card == null ? "" : $"{o.SelectedSlots.Count} / {_card.SlotNames.Count} selected";
            _shiftText = $"slots (slot 1 → {o.Shift + 1})";
            var names = _card == null ? null : OtherPlugins.AccessorySlotDataNames(_card);
            _slotDataWarning = names != null && names.Count > 0 ? $"{string.Join(", ", names.ToArray())}: accessory settings don't follow the selected accessories." : null;
            _slotWarning = SlotWarning(o);
            _targetSummary = TargetSummary();

            _slotGroups.Clear();
            _slotLabels.Clear();
            if (_card != null)
            {
                foreach (var slot in _card.SlotNames) _slotLabels[slot.Key] = slot.Value + TargetText(slot.Key);
                var groups = _card.SlotGroups;
                foreach (var group in groups)
                    AddSlotGroup(o, string.IsNullOrEmpty(group.Name) ? "Group" : group.Name, _card.SlotNames.Where(x => group.Contains(x.Key)).ToList(), group.First);
                if (groups.Count > 0) AddSlotGroup(o, "Ungrouped", _card.SlotNames.Where(x => !groups.Any(g => g.Contains(x.Key))).ToList(), -1);
            }

            _outfitLabels.Clear();
#if KKS || KK
            _outfitAccessoryWarning = null;
            if (_card != null)
            {
                var chaCtrl = _panels.Target;
                foreach (var outfit in _card.CardOutfits)
                {
                    var target = _outfitTargets.TryGetValue(outfit.Index, out var t)
                        ? $"  → {t + 1:00}. {(chaCtrl != null && t < chaCtrl.chaFile.coordinate.Length ? Outfits.GetName(chaCtrl, t) ?? Outfits.DefaultName(t) : "new outfit")}"
                        : "";
                    _outfitLabels.Add($"{outfit.Index + 1:00}.  {outfit.Name}{target}");
                }
            }
            if (o.Accessories == AccessoryMode.Selected && _outfitTargets.Count > 1)
                _outfitAccessoryWarning = $"Accessories are listed for outfit {_shownOutfit + 1}; the ticked slots are used for every loaded outfit.";
#endif
        }

        private void AddSlotGroup(LoadOptions o, string name, List<KeyValuePair<int, string>> slots, int group)
        {
            if (slots.Count == 0) return;
            var on = group >= 0 ? o.SelectedGroups.Contains(group) : slots.All(x => o.SelectedSlots.Contains(x.Key));
            _slotGroups.Add(new SlotGroupView { Title = $" {name} ({slots.Count})", Slots = slots, Group = group, On = on });
        }

        private static readonly Dictionary<string, string> Warnings = new Dictionary<string, string>();

        private static string Warning(Dependency dependency, string consequence)
        {
            var key = dependency.Name + "|" + consequence;
            if (!Warnings.TryGetValue(key, out var text)) Warnings[key] = text = dependency.Warning(consequence);
            return text;
        }

        private void UpdateTargets()
        {
            var options = Options;
            CountShapes(options);
            var chaCtrl = _panels.Target;
            var occupancyOf = chaCtrl == null ? null : chaCtrl.nowCoordinate;
            var groupsOutfit = 0;
#if KKS || KK
            if (chaCtrl != null) groupsOutfit = chaCtrl.fileStatus.coordinateType;
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
                    options.SelectedGroups.Clear();
                }

                // Free slots are counted in the outfit the shown card outfit is loaded into
                occupancyOf = _outfitTargets.TryGetValue(shown, out var target) && target < chaCtrl.chaFile.coordinate.Length
                    ? chaCtrl.chaFile.coordinate[target]
                    : new ChaFileCoordinate();
                groupsOutfit = _outfitTargets.TryGetValue(shown, out var groupsTarget) ? groupsTarget : -1;
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
            if (groupsOutfit >= 0) AccessoryGroups.Reserve(occupied, AccessoryGroups.Get(chaCtrl, groupsOutfit));
            var sources = AccessoryData.SourceSlots(options.Accessories, _card.AccessorySlots, options.SelectedSlots);
            var sourceList = sources.ToList();
            _sourceCount = sourceList.Count;
            _freeSlots = occupied.Take(AccessoryData.VanillaSlotCount).Count(x => !x) + Mathf.Max(0, AccessoryData.VanillaSlotCount - occupied.Count);
            _targets = AccessoryData.MapTargets(options.Placement, options.Shift, occupied, sourceList, AccessoryGroups.Blocks(_card.SlotGroups, options, sourceList));
        }

        public void OnGUI()
        {
            if (!Shown) return;
            GuiTheme.Begin();
            var dock = _panels.DockRect;
            _docked = dock != null;
            if (_docked)
            {
                var rect = GuiTheme.ScreenToGui(dock.Value);
                _buttonsBelow = _panels.DockButtonsBelow;
                _contentWidth = rect.width - 30 - (_buttonsBelow ? 0 : ButtonColumnWidth + 8);
                GUILayout.Window(WindowId, rect, DrawWindow, GUIContent.none, GuiTheme.PanelWindow, GUILayout.Width(rect.width), GUILayout.Height(rect.height));
                IMGUIUtils.EatInputInRect(rect);
            }
            else
            {
                // Maker, H and Studio each get their own first position
                if (!_placed || _placedAt != _panels.Location)
                {
                    _rect = _panels.DefaultWindowRect(MinWidth, MaxHeight);
                    _placed = true;
                    _placedAt = _panels.Location;
                }
                _contentWidth = _rect.width - 40;
                var title = _mode == LoadMode.Character ? "Advanced load: character" : "Advanced load: clothes";
                _rect = GUILayout.Window(WindowId, _rect, DrawWindow, title);
            }
            GuiTheme.End();
        }

        private void DrawWindow(int id)
        {
            if (!_docked)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(_cardTitle, GuiTheme.Line, GUILayout.MinWidth(0));
                GUILayout.FlexibleSpace();
                if (GuiTheme.TitleButtons(AdvancedLoaderPlugin.PluginName, true)) Close();
                GUILayout.EndHorizontal();
            }

            var column = _docked && !_buttonsBelow;
            if (column)
            {
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
            }

            DrawTabs();
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(_contentWidth));
            DrawTab(Options);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            if (column)
            {
                GUILayout.EndVertical();
                GUILayout.Space(8);
                GUILayout.BeginVertical(GUILayout.Width(ButtonColumnWidth));
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GuiTheme.TitleButtons(AdvancedLoaderPlugin.PluginName, true)) Close();
                GUILayout.EndHorizontal();
                DrawLoadButtons(GUILayout.Width(ButtonColumnWidth), GUILayout.ExpandHeight(true));
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            else if (_docked)
            {
                GUILayout.BeginHorizontal();
                DrawLoadButtons(GUILayout.Width(90), GUILayout.Height(26));
                GUILayout.FlexibleSpace();
                if (GuiTheme.TitleButtons(AdvancedLoaderPlugin.PluginName, true)) Close();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(_summary, GuiTheme.Line, GUILayout.MinWidth(0));
                DrawLoadButtons(GUILayout.Width(100), GUILayout.Height(26));
                GUILayout.EndHorizontal();
            }

            if (GUI.changed) _targetsDirty = true;
            if (!_docked) _rect = IMGUIUtils.DragResizeEatWindow(id, _rect);
        }

        private const float ButtonColumnWidth = 110;

        private void DrawLoadButtons(GUILayoutOption width, GUILayoutOption loadHeight)
        {
            var enabled = GUI.enabled;
            GUI.enabled = _panels.SelectedPath != null && !LoadRunner.Busy;
            if (_panels.StudioAdds)
            {
                if (GUILayout.Button("Add", width, GUILayout.Height(26)))
                {
                    _panels.AddStudioCharacter();
                    _targetsDirty = true;
                }
            }
            else
            {
                GUI.enabled &= LoadRunner.LoadAllAvailable;
                if (GUILayout.Button("Load all", width, GUILayout.Height(26))) _plugin.StartCoroutine(LoadAllAndFinish());
            }
            GUI.enabled = CanLoad();
            if (GUILayout.Button(LoadRunner.Busy ? "Loading…" : "Load", width, loadHeight)) StartLoad();
            GUI.enabled = _panels.Target != null && LoadRunner.CanUndo(_panels.Target);
            if (GUILayout.Button("Undo", width, GUILayout.Height(26))) _plugin.StartCoroutine(UndoAndFinish());
            GUI.enabled = enabled;
        }

        private void Close()
        {
            _plugin.SetWindowVisible(false);
            SaveTicks();
        }

        private string CardTitle()
        {
            var file = System.IO.Path.GetFileNameWithoutExtension(_card.Path);
            var name = _panels.SelectedName;
            return string.IsNullOrEmpty(name) || name == file ? file : $"{name}  ({file})";
        }

        private IEnumerable<Tab> Tabs
        {
            get
            {
                if (_mode == LoadMode.Character) yield return Tab.Body;
                yield return Tab.Clothes;
#if KKS || KK
                if (_mode == LoadMode.Character) yield return Tab.Outfits;
#endif
                yield return Tab.Accessories;
                if (AdvancedTabs.Count > 0) yield return Tab.Advanced;
                if (_mode == LoadMode.Character) yield return Tab.Plugins;
            }
        }

        private void DrawTabs()
        {
            var options = Options;
            if (_tabs.Count == 0) return;
            if (!_tabs.Contains(_tab)) _tab = _tabs[0];
            GUILayout.BeginHorizontal();
            foreach (var tab in _tabs)
            {
                _tabStates.TryGetValue(tab, out var state);
                if (GuiTheme.TabButton(TabNames[(int)tab], tab == _tab, state, CanSet(tab), out var dotClicked, GuiTheme.Tab)) _tab = tab;
                if (dotClicked) SetAll(tab, options, StateOf(tab, options) != DotState.All);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawTab(LoadOptions options)
        {
            switch (_tab)
            {
                case Tab.Body:
                    DrawBody(options);
                    break;
                case Tab.Clothes:
                    DrawNoOutfitWarning();
                    DrawParts(options, ClothesPartsInfo);
                    DrawOutfitBonemod(options);
                    break;
#if KKS || KK
                case Tab.Outfits:
                    DrawOutfits(options);
                    break;
#endif
                case Tab.Accessories:
                    DrawNoOutfitWarning();
                    DrawAccessories(options);
                    break;
                case Tab.Advanced:
                    DrawAdvanced(options);
                    break;
                case Tab.Plugins:
                    DrawPlugins(options);
                    break;
            }
        }

        private DotState StateOf(Tab tab, LoadOptions o)
        {
            switch (tab)
            {
                case Tab.Body:
                    var parts = BodyTabParts;
                    var bones = Bones.Count > 0 ? BonesState(o) : (DotState?)null;
                    var all = parts.Count(x => x.State(o) == DotState.All) + (CanLoadParameters && o.Parameters ? 1 : 0) + (bones == DotState.All ? 1 : 0);
                    var none = parts.Count(x => x.State(o) == DotState.None) + (CanLoadParameters && !o.Parameters ? 1 : 0) + (bones == DotState.None ? 1 : 0);
                    var count = parts.Length + (CanLoadParameters ? 1 : 0) + (bones != null ? 1 : 0);
                    return all == count ? DotState.All : none == count ? DotState.None : DotState.Some;
                case Tab.Clothes:
                    return ClothesPartsInfo.State(o);
#if KKS || KK
                case Tab.Outfits:
                    if (o.OutfitSelection == OutfitMode.All) return DotState.All;
                    return GuiTheme.StateOf(o.SelectedOutfits.Count, _card == null || _card.CardOutfits.Count == 0 ? int.MaxValue : _card.CardOutfits.Count);
#endif
                case Tab.Accessories:
                    if (o.Accessories == AccessoryMode.Keep) return DotState.None;
                    if (o.Accessories == AccessoryMode.All) return DotState.All;
                    return GuiTheme.StateOf(o.SelectedSlots.Count, _card == null || _card.SlotNames.Count == 0 ? int.MaxValue : _card.SlotNames.Count);
                case Tab.Advanced:
                    var advancedOn = MaterialsOn(o) + _shapesOn;
                    var advancedCount = MaterialsCount + ShapesCount;
                    return advancedCount == 0 ? DotState.None : GuiTheme.StateOf(advancedOn, advancedCount);
                case Tab.Plugins:
                    var entries = PluginEntries;
                    return entries.Count == 0 ? DotState.None : GuiTheme.StateOf(entries.Count(x => o.OtherPlugins.TryGetValue(x.Id, out var on) && on), entries.Count);
            }
            return DotState.None;
        }

        private bool CanSet(Tab tab)
        {
            switch (tab)
            {
#if KKS || KK
                case Tab.Outfits:
                    return _card != null && _card.CardOutfits.Count > 0;
#endif
                case Tab.Advanced:
                    return MaterialsCount + ShapesCount > 0;
                case Tab.Plugins:
                    return PluginEntries.Count > 0;
            }
            return true;
        }

        private void SetAll(Tab tab, LoadOptions o, bool on)
        {
            switch (tab)
            {
                case Tab.Body:
                    foreach (var parts in BodyTabParts) parts.SetAll(o, on);
                    o.Parameters = on && CanLoadParameters;
                    SetAllBones(o, on);
                    break;
                case Tab.Clothes:
                    ClothesPartsInfo.SetAll(o, on);
                    break;
#if KKS || KK
                case Tab.Outfits:
                    o.OutfitSelection = on ? OutfitMode.All : OutfitMode.Selected;
                    o.SelectedOutfits.Clear();
                    break;
#endif
                case Tab.Accessories:
                    o.Accessories = on ? AccessoryMode.All : AccessoryMode.Keep;
                    break;
                case Tab.Advanced:
                    SetAllMaterials(o, on);
                    SetAllShapes(o, on);
                    break;
                case Tab.Plugins:
                    foreach (var entry in PluginEntries) o.OtherPlugins[entry.Id] = on;
                    break;
            }
            _targetsDirty = true;
        }

        private List<OtherPlugins.Entry> PluginEntries
        {
            get
            {
                var target = _panels.Target;
                if (_entriesCard == _card && _entriesTarget == target && _entriesVersion == OtherPlugins.CharacterVersion) return _entries;
                _entriesCard = _card;
                _entriesTarget = target;
                _entriesVersion = OtherPlugins.CharacterVersion;
                _entries = _card == null ? new List<OtherPlugins.Entry>() : OtherPlugins.Listed(_card, target);
                _entryLabels = _entries.Select(x => OtherPlugins.OnCard(_card, x) ? " " + x.Name : $" {x.Name} (not on card)").ToArray();
                return _entries;
            }
        }

        private List<BoneDifference> Bones
        {
            get
            {
                if (_mode != LoadMode.Character || !AbmxPresence.Available) return NoBones;
                var target = _panels.Target;
                if (_bonesCard == _card && _bonesTarget == target && _bonesVersion == OtherPlugins.CharacterVersion) return _bones;
                _bonesCard = _card;
                _bonesTarget = target;
                _bonesVersion = OtherPlugins.CharacterVersion;
                _bones = AbmxPresence.Differences(target, _card).OrderBy(x => !x.Face).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                _boneLabels = _bones.Select(x => $" {x.Name}:  {x.Current} → {x.Card}").ToArray();
                _bonesFaceCount = _bones.Count(x => x.Face);
                var names = new HashSet<string>(_bones.Select(x => x.Name));
                _options[LoadMode.Character].SelectedBones.RemoveWhere(x => !names.Contains(x));
                return _bones;
            }
        }

        private DotState BonesState(LoadOptions o) => GuiTheme.StateOf(o.SelectedBones.Count, Bones.Count);

        private void SetAllBones(LoadOptions o, bool on)
        {
            o.SelectedBones.Clear();
            if (on) o.SelectedBones.UnionWith(Bones.Select(x => x.Name));
        }

        // The maker only loads parameters into a new character; Studio's Change always can
        private static bool CanLoadParameters => !MakerAPI.InsideAndLoaded || Singleton<CustomBase>.Instance.modeNew;

        private static Parts[] BodyTabParts
        {
            get
            {
                InitParts();
                return _bodyTabParts;
            }
        }

        private static Parts ClothesPartsInfo
        {
            get
            {
                InitParts();
                return _clothesParts;
            }
        }

        private static void InitParts()
        {
            if (_faceParts != null) return;
            _faceParts = NewParts("Face", BodyFaceParts.FacePartNames(false), (int)FacePart.All,
                o => o.Face, (o, v) => o.Face = v, o => (int)o.FaceParts, (o, v) => o.FaceParts = (FacePart)v);
            _bodyParts = NewParts("Body", BodyFaceParts.BodyPartNames(false), (int)BodyPart.All,
                o => o.Body, (o, v) => o.Body = v, o => (int)o.BodyParts, (o, v) => o.BodyParts = (BodyPart)v);
            _hairParts = NewParts("Hair", HairParts.PartNames, (int)HairPart.All,
                o => o.Hair, (o, v) => o.Hair = v, o => (int)o.HairParts, (o, v) => o.HairParts = (HairPart)v);
            _clothesParts = NewParts("Clothes", ClothesParts.PartNames, (int)ClothesPart.All,
                o => o.Clothes, (o, v) => o.Clothes = v, o => (int)o.ClothesParts, (o, v) => o.ClothesParts = (ClothesPart)v);
            _bodyTabParts = new[] { _faceParts, _bodyParts, _hairParts };
        }

        private static Parts NewParts<T>(string name, IEnumerable<KeyValuePair<T, string>> names, int all, Func<LoadOptions, bool> isOn, Action<LoadOptions, bool> setOn,
            Func<LoadOptions, int> get, Action<LoadOptions, int> set)
        {
            var list = names.Select(x => new KeyValuePair<int, string>(Convert.ToInt32(x.Key), x.Value)).ToArray();
            return new Parts
            {
                Name = name, IsOn = isOn, SetOn = setOn, Get = get, Set = set, All = all, Names = list,
                Mask = list.Aggregate(0, (mask, x) => mask | x.Key)
            };
        }

        private void DrawBody(LoadOptions options)
        {
            var parts = BodyTabParts;
            GUILayout.BeginHorizontal();
            for (var i = 0; i < parts.Length; i++)
            {
                if (GuiTheme.TabButton(parts[i].Name, i == _bodyTab, parts[i].State(options), true, out var dotClicked, GuiTheme.SubTab)) _bodyTab = i;
                if (dotClicked) parts[i].SetAll(options, parts[i].State(options) != DotState.All);
            }
            if (AbmxPresence.Available)
            {
                var bones = Bones.Count > 0;
                if (GuiTheme.TabButton("Bonemod", _bodyTab == BonemodTab, BonesState(options), bones, out var bonesDot, GuiTheme.SubTab)) _bodyTab = BonemodTab;
                if (bonesDot && bones) SetAllBones(options, BonesState(options) != DotState.All);
            }
            else if (_bodyTab == BonemodTab) _bodyTab = 0;
            var canLoadParameters = CanLoadParameters;
            var enabled = GUI.enabled;
            GUI.enabled = canLoadParameters;
            var parameters = options.Parameters && canLoadParameters;
            if (GuiTheme.TabButton(canLoadParameters ? "Parameters" : "Parameters (new characters only)", false, parameters ? DotState.All : DotState.None, true, out var parametersDot, GuiTheme.SubTab) || parametersDot)
                options.Parameters = !parameters;
            GUI.enabled = enabled;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_bodyTab == BonemodTab)
            {
                DrawBones(options);
                return;
            }
            DrawParts(options, parts[Mathf.Clamp(_bodyTab, 0, parts.Length - 1)]);
            if (options.Face || options.Body)
                GuiTheme.DrawWarning(Warning(Dependencies.Abmx, Dependencies.Abmx.State == DependencyState.TooOld
                    ? "bonemod sliders are not loaded by category. Use the \"Face Bonemod\" / \"Body Bonemod\" toggles under Plugins instead."
                    : "bonemod sliders are not loaded."));
        }

        private void DrawBones(LoadOptions options)
        {
            var bones = Bones;
            if (bones.Count == 0)
            {
                GUILayout.Label(_card == null ? "Select a card in the list." : "No advanced-window bones differ from this card.");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", GUILayout.Width(50))) SetAllBones(options, true);
            if (GUILayout.Button("None", GUILayout.Width(50))) SetAllBones(options, false);
            GUILayout.Label(_bonesSelected);
            GUILayout.EndHorizontal();

            DrawBoneGroup(options, "Face", 0, _bonesFaceCount);
            DrawBoneGroup(options, "Body", _bonesFaceCount, bones.Count - _bonesFaceCount);
        }

        private void DrawBoneGroup(LoadOptions options, string title, int start, int count)
        {
            if (count == 0) return;
            GUILayout.Label(title, GuiTheme.Header);
            DrawGrid(count, _contentWidth, (i, width) =>
            {
                var bone = _bones[start + i];
                var isOn = options.SelectedBones.Contains(bone.Name);
                if (GUILayout.Toggle(isOn, _boneLabels[start + i], GuiTheme.Cell, width) == isOn) return;
                if (isOn) options.SelectedBones.Remove(bone.Name);
                else options.SelectedBones.Add(bone.Name);
            }, 340);
        }

        private void DrawOutfitBonemod(LoadOptions options)
        {
            if (_mode != LoadMode.Coordinate || !AbmxPresence.OutfitBonemod) return;
            GUILayout.Space(6);
            var onCard = _card != null && _card.HasOutfitBonemod;
            options.OutfitBonemod = GUILayout.Toggle(options.OutfitBonemod, onCard ? " Bonemod" : " Bonemod (not on card)", GuiTheme.Cell);
        }

        private void DrawParts(LoadOptions options, Parts parts)
        {
            DrawGrid(parts.Names.Length, _contentWidth, (i, width) =>
            {
                var part = parts.Names[i];
                var isOn = parts.Has(options, part.Key);
                if (GUILayout.Toggle(isOn, " " + part.Value, GuiTheme.Cell, width) != isOn) parts.Flip(options, part.Key);
            });
        }

        private static void DrawGrid(int count, float width, Action<int, GUILayoutOption> drawCell, float cellWidth = CellWidth)
        {
            var columns = Mathf.Max(1, (int)(width / cellWidth));
            var cell = GUILayout.Width(width / columns - 4);
            for (var i = 0; i < count; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (var j = i; j < i + columns && j < count; j++) drawCell(j, cell);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        // Two columns side by side, or one under the other when the window is narrow
        private float LeftColumnWidth => _contentWidth < 460 ? _contentWidth : Mathf.Min(280, _contentWidth * 0.45f);
        private float RightColumnWidth => _contentWidth < 460 ? _contentWidth : _contentWidth - LeftColumnWidth - 10;
        private bool Stacked => _contentWidth < 460;

        private void BeginColumns()
        {
            if (Stacked) GUILayout.BeginVertical();
            else GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(LeftColumnWidth));
        }

        private void NextColumn()
        {
            GUILayout.EndVertical();
            GUILayout.Space(Stacked ? 4 : 10);
            GUILayout.BeginVertical(GUILayout.Width(RightColumnWidth));
        }

        private void EndColumns()
        {
            GUILayout.EndVertical();
            if (Stacked) GUILayout.EndVertical();
            else GUILayout.EndHorizontal();
        }

        private void DrawAccessories(LoadOptions options)
        {
            BeginColumns();
            options.Accessories = (AccessoryMode)GUILayout.Toolbar((int)options.Accessories, AccessoryModeNames);
            if (options.Accessories != AccessoryMode.Keep)
            {
                GuiTheme.DrawWarning(Warning(Dependencies.MoreAccessories, "only accessory slots 1-20 can be used."));
                DrawPlacement(options);
                if (!options.ReplacesAllAccessories) GuiTheme.DrawWarning(_slotDataWarning);
            }
            NextColumn();
            if (options.Accessories == AccessoryMode.Selected) DrawAccessorySlots(options);
            else if (options.Accessories == AccessoryMode.All && options.Placement != AccessoryPlacement.SameSlots) DrawTargetSummary();
            EndColumns();
        }

        private static readonly KeyValuePair<MaterialGroup, string>[] CharacterMaterialGroups =
        {
            new KeyValuePair<MaterialGroup, string>(MaterialGroup.Body, "Body"),
            new KeyValuePair<MaterialGroup, string>(MaterialGroup.Head, "Head"),
            new KeyValuePair<MaterialGroup, string>(MaterialGroup.Eyes, "Eyes"),
            new KeyValuePair<MaterialGroup, string>(MaterialGroup.Hair, "Hair"),
            new KeyValuePair<MaterialGroup, string>(MaterialGroup.Clothes, "Clothes")
        };

        private static readonly KeyValuePair<MaterialGroup, string>[] CoordinateMaterialGroups =
        {
            new KeyValuePair<MaterialGroup, string>(MaterialGroup.Clothes, "Clothes")
        };

        private static KeyValuePair<MaterialGroup, string>[] MaterialGroupsOf(LoadMode mode) => mode == LoadMode.Character ? CharacterMaterialGroups : CoordinateMaterialGroups;

        private static bool ShapeAvailable => ShapeEditorBridge.Available || BlendshapeCreatorBridge.Available;

        private sealed class ShapePluginView
        {
            public string Plugin;
            public string Label;
            public List<ShapeEdit> Edits = new List<ShapeEdit>();
            public string[] Labels = new string[0];
            public bool[] Fits = new bool[0];
            public int FitCount;
            public int On;
            public string Selected = "";
        }

#if KKS || KK
        private static readonly ShapePluginView[] ShapePlugins =
        {
            new ShapePluginView { Plugin = ShapeEditorBridge.PluginName },
            new ShapePluginView { Plugin = BlendshapeCreatorBridge.PluginName }
        };

        private static bool ShapePluginAvailable(int index) => index == 0 ? ShapeEditorBridge.Available : BlendshapeCreatorBridge.Available;

        private static string ShapePluginLabel(int index) =>
            index == 0 ? PluginLabel(ShapeEditorBridge.PluginGuid, ShapeEditorBridge.PluginName) : PluginLabel(BlendshapeCreatorBridge.PluginGuid, BlendshapeCreatorBridge.PluginName);
#else
        private static readonly ShapePluginView[] ShapePlugins =
        {
            new ShapePluginView { Plugin = "Shape Editor" },
            new ShapePluginView { Plugin = "Blendshape Creator" }
        };

        private static bool ShapePluginAvailable(int index) => false;

        private static string ShapePluginLabel(int index) => ShapePlugins[index].Plugin;
#endif

        private CardData _shapesCard;
        private static readonly List<ShapeEdit> NoShapes = new List<ShapeEdit>();
        private List<ShapeEdit> CardShapes => ShapeAvailable && _card != null ? _card.ShapeEdits : NoShapes;
        private int _shapesOn;
        private int _shapesFit;

        private void CountShapes(LoadOptions o)
        {
            if (_shapesCard != _card)
            {
                _shapesCard = _card;
                for (var i = 0; i < ShapePlugins.Length; i++)
                {
                    var view = ShapePlugins[i];
                    view.Label = view.Label ?? ShapePluginLabel(i);
                    view.Edits = ShapePluginAvailable(i) ? CardShapes.Where(x => x.Plugin == view.Plugin).ToList() : new List<ShapeEdit>();
                }
            }
            var unfit = CardShapes.Count > 0 && _panels.Target != null ? ShapeEdits.Unfit(_card, _panels.Target, o) : null;
            _shapesOn = 0;
            _shapesFit = 0;
            foreach (var view in ShapePlugins)
            {
                view.On = 0;
                view.FitCount = 0;
                view.Fits = new bool[view.Edits.Count];
                view.Labels = new string[view.Edits.Count];
                for (var i = 0; i < view.Edits.Count; i++)
                {
                    var edit = view.Edits[i];
                    var reason = unfit != null && unfit.TryGetValue(edit.Key, out var r) ? r : null;
                    var fits = reason == null;
                    view.Fits[i] = fits;
                    view.Labels[i] = fits ? " " + edit.Label : $" {edit.Label} (doesn't fit: {reason})";
                    if (!fits) continue;
                    view.FitCount++;
                    if (o.SelectedShapes.Contains(edit.Key)) view.On++;
                }
                view.Selected = $"{view.On} / {view.FitCount} selected";
                _shapesOn += view.On;
                _shapesFit += view.FitCount;
            }
        }

        private void SetPluginShapes(LoadOptions o, ShapePluginView view, bool on)
        {
            for (var i = 0; i < view.Edits.Count; i++)
            {
                if (on && i < view.Fits.Length && view.Fits[i]) o.SelectedShapes.Add(view.Edits[i].Key);
                else if (!on) o.SelectedShapes.Remove(view.Edits[i].Key);
            }
            _targetsDirty = true;
        }

        private const int MaterialsTab = 0;
        private int _advancedTab;

        private int MaterialsCount => MaterialEditorPresence.Available ? MaterialGroupsOf(_mode).Length : 0;
        private int ShapesCount => _shapesFit;
        private int MaterialsOn(LoadOptions o) => MaterialEditorPresence.Available ? MaterialGroupsOf(_mode).Count(x => (o.MaterialGroups & x.Key) != 0) : 0;

        private void SetAllMaterials(LoadOptions o, bool on)
        {
            if (!MaterialEditorPresence.Available) return;
            foreach (var group in MaterialGroupsOf(_mode))
                o.MaterialGroups = on ? o.MaterialGroups | group.Key : o.MaterialGroups & ~group.Key;
        }

        private void SetAllShapes(LoadOptions o, bool on)
        {
            o.SelectedShapes.Clear();
            if (on)
                foreach (var view in ShapePlugins) SetPluginShapes(o, view, true);
            _targetsDirty = true;
        }


        private static string _materialsLabel;

        private static string PluginLabel(string guid, string fallback) =>
            guid != null && BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(guid, out var info) ? $"{info.Metadata.Name} {info.Metadata.Version}" : fallback;

        private static string MaterialsLabel => _materialsLabel ?? (_materialsLabel = PluginLabel(Dependencies.MaterialEditor.GUID, Dependencies.MaterialEditor.Name));

        private static List<int> _advancedTabs;

        private static List<int> AdvancedTabs
        {
            get
            {
                if (_advancedTabs != null) return _advancedTabs;
                _advancedTabs = new List<int>();
                if (MaterialEditorPresence.Available) _advancedTabs.Add(MaterialsTab);
                for (var i = 0; i < ShapePlugins.Length; i++)
                    if (ShapePluginAvailable(i)) _advancedTabs.Add(i + 1);
                return _advancedTabs;
            }
        }

        private void DrawAdvanced(LoadOptions options)
        {
            var tabs = AdvancedTabs;
            if (tabs.Count == 0) return;
            if (!tabs.Contains(_advancedTab)) _advancedTab = tabs[0];

            GUILayout.BeginHorizontal();
            if (MaterialEditorPresence.Available)
            {
                var materials = MaterialsCount > 0;
                if (GuiTheme.TabButton(MaterialsLabel, _advancedTab == MaterialsTab, GuiTheme.StateOf(_materialsOn, materials ? MaterialsCount : int.MaxValue),
                        materials, out var materialsDot, GuiTheme.SubTab)) _advancedTab = MaterialsTab;
                if (materialsDot && materials) SetAllMaterials(options, _materialsOn < MaterialsCount);
            }
            for (var i = 0; i < ShapePlugins.Length; i++)
            {
                if (!ShapePluginAvailable(i)) continue;
                var view = ShapePlugins[i];
                var any = view.FitCount > 0;
                if (GuiTheme.TabButton(view.Label, _advancedTab == i + 1, GuiTheme.StateOf(view.On, any ? view.FitCount : int.MaxValue), any,
                        out var shapeDot, GuiTheme.SubTab)) _advancedTab = i + 1;
                if (shapeDot && any) SetPluginShapes(options, view, view.On < view.FitCount);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_advancedTab > MaterialsTab) DrawShapes(options, ShapePlugins[_advancedTab - 1]);
            else DrawMaterials(options);
        }

        private void DrawShapes(LoadOptions options, ShapePluginView view)
        {
            if (_card == null)
            {
                GUILayout.Label("Select a card in the list.");
                return;
            }
            if (view.Edits.Count == 0)
            {
                GUILayout.Label($"This card has no {view.Plugin} edits.");
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("All", GUILayout.Width(50))) SetPluginShapes(options, view, true);
            if (GUILayout.Button("None", GUILayout.Width(50))) SetPluginShapes(options, view, false);
            GUILayout.Label(view.Selected);
            GUILayout.EndHorizontal();

            DrawGrid(view.Edits.Count, _contentWidth, (i, width) =>
            {
                var edit = view.Edits[i];
                var isOn = options.SelectedShapes.Contains(edit.Key);
                var enabled = GUI.enabled;
                GUI.enabled = enabled && (isOn || i >= view.Fits.Length || view.Fits[i]);
                var on = GUILayout.Toggle(isOn, i < view.Labels.Length ? view.Labels[i] : " " + edit.Label, GuiTheme.Cell, width);
                GUI.enabled = enabled;
                if (on == isOn) return;
                if (isOn) options.SelectedShapes.Remove(edit.Key);
                else options.SelectedShapes.Add(edit.Key);
            }, 380);
        }

        private void DrawMaterials(LoadOptions options)
        {
            if (!MaterialEditorPresence.Available) return;
            var groups = MaterialGroupsOf(_mode);
            DrawGrid(groups.Length, _contentWidth, (i, width) =>
            {
                var group = groups[i].Key;
                var isOn = (options.MaterialGroups & group) != 0;
                if (GUILayout.Toggle(isOn, " " + groups[i].Value, GuiTheme.Cell, width) != isOn)
                    options.MaterialGroups = isOn ? options.MaterialGroups & ~group : options.MaterialGroups | group;
            });
            GUILayout.Space(4);
            options.MaterialCleanCopy = GUILayout.Toggle(options.MaterialCleanCopy, " Clean copy (reset the group first)", GuiTheme.Cell);
            options.MaterialFollowAccessories = GUILayout.Toggle(options.MaterialFollowAccessories, " Accessory materials follow their accessories", GuiTheme.Cell);
        }

        private void DrawPlugins(LoadOptions options)
        {
            var entries = PluginEntries;
            if (_card == null)
            {
                GUILayout.Label("Select a card in the list.");
                return;
            }
            if (entries.Count == 0)
            {
                GUILayout.Label("Neither the card nor the character has data of other plugins.");
                return;
            }
            DrawGrid(entries.Count, _contentWidth, (i, width) =>
            {
                var entry = entries[i];
                options.OtherPlugins.TryGetValue(entry.Id, out var isOn);
                options.OtherPlugins[entry.Id] = GUILayout.Toggle(isOn, _entryLabels[i], GuiTheme.Cell, width);
            }, 230);
        }

        private void DrawPlacement(LoadOptions options)
        {
            options.Placement = (AccessoryPlacement)GUILayout.Toolbar((int)options.Placement, PlacementNames);
            // Only 20 slots without MoreAccessories
            var maxShift = MoreAccessoriesBridge.Available ? 500 : AccessoryData.VanillaSlotCount - 1;
            options.Shift = Mathf.Clamp(options.Shift, 0, maxShift);
            if (options.Placement == AccessoryPlacement.Shift)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Shift by", GUILayout.ExpandWidth(false));
                if (GUILayout.Button("-", GUILayout.Width(26))) options.Shift = Mathf.Max(0, options.Shift - 1);
                if (int.TryParse(GUILayout.TextField(options.Shift.ToString(), GUILayout.Width(50)), out var shift)) options.Shift = Mathf.Clamp(shift, 0, maxShift);
                if (GUILayout.Button("+", GUILayout.Width(26))) options.Shift = Mathf.Min(maxShift, options.Shift + 1);
                GUILayout.Label(_shiftText);
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
            GuiTheme.DrawWarning(_slotWarning);
        }

        private string SlotWarning(LoadOptions options)
        {
            if (MoreAccessoriesBridge.Available || options.Placement == AccessoryPlacement.SameSlots) return null;

            var skipped = _sourceCount - _targets.Count;
            string warning = null;
            if (options.Placement == AccessoryPlacement.FreeSlots && _freeSlots == 0)
                warning = "All 20 accessory slots are taken, so there is no free slot. Nothing will be loaded with \"Free slots\".";
            else if (skipped > 0 && options.Placement == AccessoryPlacement.FreeSlots)
                warning = $"Only {_freeSlots} of 20 slots are free, {skipped} accessories will not be loaded.";
            else if (skipped > 0)
                warning = $"{skipped} accessories would go past slot 20 and will not be loaded.";
            return warning;
        }

        private void DrawTargetSummary()
        {
            if (_card != null) GUILayout.Label(_targetSummary);
        }

        private string TargetSummary()
        {
            if (_card == null) return "";
            var skipped = _card.SlotNames.Count - _targets.Count;
            return _targets.Count == 0
                ? "Nothing to load."
                : $"{_targets.Count} accessories → slots {string.Join(", ", _targets.Values.OrderBy(x => x).Select(x => (x + 1).ToString()).ToArray())}" +
                  (skipped > 0 ? $" ({skipped} do not fit)" : "");
        }

        private string TargetText(int sourceSlot)
        {
            if (Options.Placement == AccessoryPlacement.SameSlots) return "";
            return _targets.TryGetValue(sourceSlot, out var target) ? $"  → {target + 1:00}" : "  → no slot";
        }

#if KKS || KK
        private void DrawOutfits(LoadOptions options)
        {
            if (_card == null || _card.CardOutfits.Count == 0)
            {
                GUILayout.Label("Select a card to see its outfits.");
                return;
            }

            BeginColumns();
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
            GuiTheme.DrawWarning(Warning(Dependencies.MoreOutfits, "only the 4 base outfits exist, card outfits past them are not loaded."));
            GuiTheme.DrawWarning(_outfitAccessoryWarning);

            NextColumn();
            for (var i = 0; i < _card.CardOutfits.Count && i < _outfitLabels.Count; i++)
            {
                var outfit = _card.CardOutfits[i];
                var label = _outfitLabels[i];
                if (options.OutfitSelection == OutfitMode.Selected)
                {
                    var isOn = options.SelectedOutfits.Contains(outfit.Index);
                    if (GUILayout.Toggle(isOn, label, GuiTheme.Cell) == isOn) continue;
                    if (isOn) options.SelectedOutfits.Remove(outfit.Index);
                    else options.SelectedOutfits.Add(outfit.Index);
                }
                else
                {
                    GUILayout.Label(label, GuiTheme.Line);
                }
            }
            EndColumns();
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
            if (GUILayout.Button("All", GUILayout.Width(50)))
            {
                options.SelectedSlots.UnionWith(_card.SlotNames.Select(x => x.Key));
                options.SelectedGroups.UnionWith(_card.SlotGroups.Select(x => x.First));
            }
            if (GUILayout.Button("None", GUILayout.Width(50)))
            {
                options.SelectedSlots.Clear();
                options.SelectedGroups.Clear();
            }
            GUILayout.Label(_slotsSelected);
            GUILayout.EndHorizontal();

            if (_card.SlotGroups.Count == 0)
            {
                DrawSlots(options, _card.SlotNames);
                return;
            }

            foreach (var group in _slotGroups) DrawSlotGroup(options, group);
        }

        private GUIStyle _groupToggleStyle;
        private GUIStyle _groupToggleSource;

        private void DrawSlotGroup(LoadOptions options, SlotGroupView view)
        {
            var slots = view.Slots;
            var group = view.Group;
            if (_groupToggleSource != GuiTheme.Cell)
            {
                _groupToggleSource = GuiTheme.Cell;
                _groupToggleStyle = new GUIStyle(GuiTheme.Cell) { fontStyle = FontStyle.Bold };
            }
            var isOn = view.On;
            if (GUILayout.Toggle(isOn, view.Title, _groupToggleStyle) != isOn)
            {
                if (isOn) options.SelectedSlots.ExceptWith(slots.Select(x => x.Key));
                else options.SelectedSlots.UnionWith(slots.Select(x => x.Key));
                if (group >= 0 && isOn) options.SelectedGroups.Remove(group);
                else if (group >= 0) options.SelectedGroups.Add(group);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.BeginVertical();
            DrawSlots(options, slots);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawSlots(LoadOptions options, List<KeyValuePair<int, string>> slots)
        {
            DrawGrid(slots.Count, RightColumnWidth - 20, (i, width) =>
            {
                var slot = slots[i];
                var isOn = options.SelectedSlots.Contains(slot.Key);
                var on = GUILayout.Toggle(isOn, isOn && _slotLabels.TryGetValue(slot.Key, out var label) ? label : slot.Value, GuiTheme.Cell, width);
                if (on == isOn) return;
                if (isOn) options.SelectedSlots.Remove(slot.Key);
                else options.SelectedSlots.Add(slot.Key);
            });
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
                if (options.Hair && options.HairParts != HairPart.None)
                    parts.Add(options.HairParts == HairPart.All ? "Hair" : $"Hair ({HairParts.Describe(options.HairParts)})");
                if (options.Parameters && CanLoadParameters) parts.Add("Parameters");
                if (AbmxPresence.Available && options.SelectedBones.Count > 0)
                    parts.Add(options.SelectedBones.Count == 1 ? "bonemod (1 bone)" : $"bonemod ({options.SelectedBones.Count} bones)");
            }
            if (options.Clothes && options.ClothesParts != ClothesPart.None)
                parts.Add(options.ClothesParts == ClothesPart.All ? "Clothes" : $"Clothes ({ClothesParts.Describe(options.ClothesParts)})");
#if KKS || KK
            if (_mode == LoadMode.Character && (options.Clothes || options.Accessories != AccessoryMode.Keep))
                parts.Add(_outfitTargets.Count == 0 ? "no outfits" : $"outfits {string.Join(", ", _outfitTargets.Keys.OrderBy(x => x).Select(x => (x + 1).ToString()).ToArray())}");
#endif
            if (options.ReplacesAllAccessories) parts.Add("all accessories");
            else if (options.Accessories != AccessoryMode.Keep && _targets.Count > 0)
                parts.Add("accessories into slots " + string.Join(", ", _targets.Values.OrderBy(x => x).Select(x => (x + 1).ToString()).ToArray()));
            var groups = _mode == LoadMode.Character ? options.MaterialGroups : options.MaterialGroups & MaterialGroup.Clothes;
            if (MaterialEditorPresence.Available && groups != MaterialGroup.None) parts.Add($"materials ({groups})");
            if (_mode == LoadMode.Coordinate && AbmxPresence.OutfitBonemod && options.OutfitBonemod) parts.Add("bonemod");
            var shapes = _shapesOn;
            if (shapes > 0) parts.Add(shapes == 1 ? "1 shape edit" : $"{shapes} shape edits");
            if (_mode == LoadMode.Character)
                parts.AddRange(OtherPlugins.Entries.Where(x => OtherPlugins.Loads(options, _card, x)).Select(x => x.Name));
            return parts.Count == 0 ? "Nothing selected to load." : "Load: " + string.Join(", ", parts.ToArray());
        }

        // Clothes and accessories of a character card go into the card's outfits
        private bool NoOutfitSelected
        {
            get
            {
#if KKS || KK
                return _mode == LoadMode.Character && _card != null && _card.CardOutfits.Count > 0 &&
                       Options.OutfitSelection == OutfitMode.Selected && Options.SelectedOutfits.Count == 0;
#else
                return false;
#endif
            }
        }

        private bool NoOutfitTarget
        {
            get
            {
#if KKS || KK
                return _mode == LoadMode.Character && _card != null && _card.CardOutfits.Count > 0 && _outfitTargets.Count == 0;
#else
                return false;
#endif
            }
        }

        private void DrawNoOutfitWarning()
        {
            if (NoOutfitSelected) GuiTheme.DrawWarning("No outfit is selected in Outfits, so clothes and accessories are not loaded. Select an outfit first.");
        }

        private bool CanLoad()
        {
            if (_card == null || _card.AccessorySlots == null || LoadRunner.Busy || _panels.Target == null) return false;
            var o = Options;
            // Accessories count only when at least one of them has a slot to go to
            var accessories = o.ReplacesAllAccessories || (o.Accessories != AccessoryMode.Keep && _targets.Count > 0);
            var outfits = !NoOutfitTarget;
            var anything = (outfits && (o.Clothes || accessories)) || (MaterialEditorPresence.Available && o.MaterialGroups != MaterialGroup.None) ||
                           _shapesOn > 0;
            if (_mode == LoadMode.Character)
                anything |= o.Face || o.Body || o.Hair || o.Parameters || (AbmxPresence.Available && o.SelectedBones.Count > 0) || OtherPlugins.AnyLoaded(o, _card);
            else
                anything |= AbmxPresence.OutfitBonemod && o.OutfitBonemod;
            return anything;
        }

        private void StartLoad()
        {
            var card = _card;
            var options = Options;
            var load = _mode == LoadMode.Character ? LoadRunner.LoadCharacter(card, options, _panels.Target) : LoadRunner.LoadCoordinate(card, options, _panels.Target);
            _plugin.StartCoroutine(LoadAndFinish(_mode, load));
        }

        private IEnumerator UndoAndFinish()
        {
            yield return _plugin.StartCoroutine(LoadRunner.Undo(_panels.Target));
            _targetsDirty = true;
        }

        private IEnumerator LoadAllAndFinish()
        {
            yield return _plugin.StartCoroutine(LoadRunner.LoadAll(_mode, _panels.Target, _panels.SelectedPath));
            _targetsDirty = true;
        }

        private IEnumerator LoadAndFinish(LoadMode mode, IEnumerator load)
        {
            yield return _plugin.StartCoroutine(load);
            _targetsDirty = true;
            switch (Settings.AfterLoadAction.Value)
            {
                case AfterLoad.ClearSelection:
                    _options[mode].ClearSlots();
#if KKS || KK
                    _options[mode].SelectedOutfits.Clear();
#endif
                    break;
                case AfterLoad.ResetToDefault:
                    _options[mode] = LoadOptions.Default(mode);
                    break;
            }
            SaveTicks();
        }
    }
}
