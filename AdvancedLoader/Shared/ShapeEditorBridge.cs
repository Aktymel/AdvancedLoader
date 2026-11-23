#if KKS || KK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
#endif

namespace AdvancedLoader
{
#if !(KKS || KK)
    internal static class ShapeEditorBridge
    {
        public static bool Available => false;
    }
#else
    internal static class ShapeEditorBridge
    {
        public const string DataId = "com.ghostendsky.kkshapeeditor";

        private const string LayersKey = "layers";
        private const string SubdivisionKey = "subdivision";
        private static readonly string[] DriverKeys = { "psd", "csb", "nbb" };
        private static readonly string[] SerializeDriverNames = { "SerializeDrivers", "SerializeCsb", "SerializeNbb" };
        private static readonly string[] DeserializeDriverNames = { "DeserializeDrivers", "DeserializeCsb", "DeserializeNbb" };

        private static bool _checked;
        private static bool _available;
        private static Type _controllerType;
        private static Type _deformDataType;
        private static CharacterApi.ControllerRegistration _registration;
        private static MethodInfo _serializeLayers;
        private static MethodInfo _deserializeLayers;
        private static MethodInfo _serializeSubdivision;
        private static MethodInfo _deserializeSubdivision;
        private static readonly MethodInfo[] SerializeDrivers = new MethodInfo[3];
        private static readonly MethodInfo[] DeserializeDrivers = new MethodInfo[3];
        private static MethodInfo _onReload;
        private static MethodInfo _onCoordinateLoaded;

        private static readonly MethodInfo SaveCard = AccessTools.Method(typeof(CharaCustomFunctionController), "OnCardBeingSavedInternal", new[] { typeof(GameMode) });
        private static readonly MethodInfo SaveCoordinate = AccessTools.Method(typeof(CharaCustomFunctionController), "OnCoordinateBeingSavedInternal", new[] { typeof(ChaFileCoordinate) });

        public static string PluginGuid { get; private set; }

        public static bool Available
        {
            get
            {
                if (!_checked) Check();
                return _available;
            }
        }

        private static void Check()
        {
            _checked = true;
            try
            {
                _controllerType = AccessTools.TypeByName("KKShapeEditor.ShapeEditorController");
                var serializer = AccessTools.TypeByName("KKShapeEditor.ShapeSerializer");
                _deformDataType = AccessTools.TypeByName("KKShapeEditor.DeformData");
                if (_controllerType == null || serializer == null || _deformDataType == null) return;

                _registration = CharacterApi.RegisteredHandlers.FirstOrDefault(x => x.ControllerType == _controllerType && x.ExtendedDataId == DataId);
                PluginGuid = Chainloader.PluginInfos.Values.FirstOrDefault(x => x.Instance != null && x.Instance.GetType().Assembly == _controllerType.Assembly)?.Metadata.GUID;
                if (_registration == null || PluginGuid == null) return;

                var flags = BindingFlags.Public | BindingFlags.Static;
                var dictionary = typeof(Dictionary<,>).MakeGenericType(typeof(string), _deformDataType);
                _serializeLayers = serializer.GetMethod("SerializeAllLayers", flags, null, new[] { dictionary }, null);
                _deserializeLayers = serializer.GetMethod("DeserializeAllLayers", flags, null, new[] { typeof(byte[]), typeof(byte).MakeByRefType() }, null);
                _serializeSubdivision = serializer.GetMethod("SerializeSubdivisionInfo", flags, null,
                    new[] { typeof(Dictionary<string, int>), typeof(Dictionary<string, List<int[]>>), typeof(Dictionary<string, bool[]>) }, null);
                _deserializeSubdivision = serializer.GetMethod("DeserializeSubdivisionInfo", flags, null,
                    new[] { typeof(byte[]), typeof(Dictionary<string, int>).MakeByRefType(), typeof(Dictionary<string, List<int[]>>).MakeByRefType(), typeof(Dictionary<string, bool[]>).MakeByRefType() }, null);
                for (var i = 0; i < DriverKeys.Length; i++)
                {
                    DeserializeDrivers[i] = serializer.GetMethod(DeserializeDriverNames[i], flags, null, new[] { typeof(byte[]) }, null);
                    var list = DeserializeDrivers[i]?.ReturnType;
                    SerializeDrivers[i] = list == null ? null : serializer.GetMethod(SerializeDriverNames[i], flags, null, new[] { list }, null);
                }

                var instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                _onReload = _controllerType.GetMethod("OnReload", instance, null, new[] { typeof(GameMode), typeof(bool) }, null);
                _onCoordinateLoaded = _controllerType.GetMethod("OnCoordinateBeingLoaded", instance, null, new[] { typeof(ChaFileCoordinate) }, null);

                _available = _serializeLayers != null && _deserializeLayers != null && _serializeSubdivision != null && _deserializeSubdivision != null &&
                             SerializeDrivers.All(x => x != null) && DeserializeDrivers.All(x => x != null) && _onReload != null && _onCoordinateLoaded != null &&
                             SaveCard != null && SaveCoordinate != null &&
                             AccessTools.Property(_deformDataType, "Layers") != null && AccessTools.Property(_deformDataType, "RendererPath") != null;
                if (!_available) AdvancedLoaderPlugin.Logger.LogWarning("This version of Shape Editor is not supported, its data is loaded as one block under Other plugins");
            }
            catch (Exception ex)
            {
                _available = false;
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Shape Editor: {ex.Message}");
            }
        }

        private sealed class ShapeData
        {
            public Dictionary<string, object> Layers = new Dictionary<string, object>();
            public byte Version = 255;
            public Dictionary<string, int> Levels = new Dictionary<string, int>();
            public Dictionary<string, List<int[]>> Faces = new Dictionary<string, List<int[]>>();
            public Dictionary<string, bool[]> Smooth = new Dictionary<string, bool[]>();
            public readonly List<object>[] Drivers = { new List<object>(), new List<object>(), new List<object>() };
        }

        private static ShapeData Read(PluginData data)
        {
            var result = new ShapeData();
            if (data?.data == null) return result;
            if (data.data.TryGetValue(LayersKey, out var layers) && layers is byte[] layerBytes)
            {
                var args = new object[] { layerBytes, (byte)0 };
                if (_deserializeLayers.Invoke(null, args) is IDictionary map)
                {
                    result.Version = (byte)args[1];
                    foreach (DictionaryEntry entry in map) result.Layers[(string)entry.Key] = entry.Value;
                }
            }
            if (data.data.TryGetValue(SubdivisionKey, out var subdivision) && subdivision is byte[] subdivisionBytes)
            {
                var args = new object[] { subdivisionBytes, null, null, null };
                _deserializeSubdivision.Invoke(null, args);
                result.Levels = args[1] as Dictionary<string, int> ?? result.Levels;
                result.Faces = args[2] as Dictionary<string, List<int[]>> ?? result.Faces;
                result.Smooth = args[3] as Dictionary<string, bool[]> ?? result.Smooth;
            }
            for (var i = 0; i < DriverKeys.Length; i++)
                if (data.data.TryGetValue(DriverKeys[i], out var drivers) && drivers is byte[] driverBytes && DeserializeDrivers[i].Invoke(null, new object[] { driverBytes }) is IList list)
                    result.Drivers[i].AddRange(list.Cast<object>());
            return result;
        }

        private static PluginData Write(ShapeData shapes)
        {
            var result = new PluginData();
            if (shapes.Layers.Count > 0)
            {
                var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), _deformDataType));
                foreach (var pair in shapes.Layers) map[pair.Key] = pair.Value;
                result.data[LayersKey] = _serializeLayers.Invoke(null, new object[] { map });
            }
            if (shapes.Levels.Count > 0)
                result.data[SubdivisionKey] = _serializeSubdivision.Invoke(null, new object[] { shapes.Levels, shapes.Faces, shapes.Smooth });
            for (var i = 0; i < DriverKeys.Length; i++)
            {
                if (shapes.Drivers[i].Count == 0) continue;
                var list = (IList)Activator.CreateInstance(DeserializeDrivers[i].ReturnType);
                foreach (var driver in shapes.Drivers[i]) list.Add(driver);
                if (SerializeDrivers[i].Invoke(null, new object[] { list }) is byte[] bytes) result.data[DriverKeys[i]] = bytes;
            }
            return result;
        }

        internal enum Part
        {
            Body,
            Head,
            Hair,
            Clothes,
            Accessory
        }

        private static readonly string[] ClothesNames = { "ct_clothesTop", "ct_clothesBot", "ct_bra", "ct_shorts", "ct_gloves", "ct_panst", "ct_socks", "ct_shoes_inner", "ct_shoes_outer" };
        private static readonly string[] HairNames = { "ct_hairB", "ct_hairF", "ct_hairS" };
        private const string SlotPrefix = "ca_slot";

        internal static Part Classify(string path, out int index)
        {
            var segments = path.Split('/');
            for (var i = segments.Length - 1; i >= 0; i--)
                if (TrySlot(segments[i], out index)) return Part.Accessory;
            foreach (var segment in segments)
            {
                index = Array.IndexOf(ClothesNames, segment);
                if (index >= 0) return Part.Clothes;
            }
            foreach (var segment in segments)
            {
                index = segment.StartsWith("ct_hairO", StringComparison.Ordinal) ? 3 : Array.IndexOf(HairNames, segment);
                if (index >= 0) return Part.Hair;
            }
            index = 0;
            return segments.Contains("ct_head") ? Part.Head : Part.Body;
        }

        private static bool TrySlot(string segment, out int slot)
        {
            slot = -1;
            return segment.Length > SlotPrefix.Length && segment.StartsWith(SlotPrefix, StringComparison.Ordinal) &&
                   int.TryParse(segment.Substring(SlotPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out slot);
        }

        internal static string RemapSlots(string path, IDictionary<int, int> sourceToTarget)
        {
            if (string.IsNullOrEmpty(path) || sourceToTarget == null || path.IndexOf(SlotPrefix, StringComparison.Ordinal) < 0) return path;
            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
                if (TrySlot(segments[i], out var slot) && sourceToTarget.TryGetValue(slot, out var moved))
                    segments[i] = SlotPrefix + moved.ToString("00", CultureInfo.InvariantCulture);
            return string.Join("/", segments);
        }

        private static bool TryParseOutfitKey(string key, out int outfit, out string path)
        {
            outfit = -1;
            path = key;
            var at = key.IndexOf('/');
            if (at <= 0 || !int.TryParse(key.Substring(0, at), NumberStyles.None, CultureInfo.InvariantCulture, out outfit)) return false;
            path = key.Substring(at + 1);
            return true;
        }

        private static string OutfitKey(int outfit, string path) => outfit.ToString(CultureInfo.InvariantCulture) + "/" + path;

        private static IEnumerable<string> LayerIds(object deformData)
        {
            if (Traverse.Create(deformData).Property("Layers").GetValue() is IList layers)
                foreach (var layer in layers)
                    if (Traverse.Create(layer).Property("Id").GetValue() is string id) yield return id;
        }

        private static string DriverString(object driver, string name) => Traverse.Create(driver).Property(name).GetValue() as string;

        private static void SetDriver(object driver, string name, object value)
        {
            var property = Traverse.Create(driver).Property(name);
            if (property.PropertyExists()) property.SetValue(value);
        }

        private static CharaCustomFunctionController Controller(ChaControl chaCtrl) =>
            chaCtrl == null ? null : _registration.Instances.FirstOrDefault(x => x != null && x.ChaControl == chaCtrl);

        internal delegate bool FitCheck(Part part, int sourceIndex, int sourceOutfit, int targetOutfit, int targetIndex, string path, int vertices, bool subdivided);

        internal sealed class Plan
        {
            public HashSet<string> Selected = new HashSet<string>();
            public bool HairLoaded;
            public HairPart HairParts = HairPart.All;
            public bool ClothesLoaded;
            public ClothesPart ClothesParts = ClothesPart.All;
            public int CurrentOutfit;
            public bool OutfitsLoaded;
            // Target outfit -> card outfit
            public Dictionary<int, int> SourceFor = new Dictionary<int, int>();
            // Target outfit -> target slot -> card slot
            public Dictionary<int, Dictionary<int, int>> AccessoryTargets = new Dictionary<int, Dictionary<int, int>>();

            internal bool Takes(string key, Part part, int index, int outfit)
            {
                if (key == null || !Selected.Contains(key)) return false;
                switch (part)
                {
                    case Part.Clothes:
                        return SourceFor.ContainsKey(outfit);
                    default:
                        return true;
                }
            }

            internal bool SlotFor(int outfit, int cardSlot, out int slot)
            {
                var moved = SlotsFromSource(outfit);
                if (moved != null && moved.TryGetValue(cardSlot, out slot)) return true;
                slot = cardSlot;
                return !AccessoryTargets.TryGetValue(outfit, out var map) || !map.ContainsKey(cardSlot);
            }

            internal bool Keeps(Part part, int index, int outfit)
            {
                switch (part)
                {
                    case Part.Hair:
                        return !HairLoaded || (HairParts & (HairPart)(1 << index)) == 0;
                    case Part.Clothes:
                        return !OutfitsLoaded || !ClothesLoaded || !SourceFor.ContainsKey(outfit) || (ClothesParts & (ClothesPart)(1 << index)) == 0;
                    case Part.Accessory:
                        return !AccessoryTargets.TryGetValue(outfit, out var map) || !map.ContainsKey(index);
                    default:
                        return true;
                }
            }

            public FitCheck Fits;

            internal bool Fit(Part part, int sourceIndex, int sourceOutfit, int targetOutfit, int targetIndex, string path, int vertices, bool subdivided)
            {
                var fits = Fits == null || Fits(part, sourceIndex, sourceOutfit, targetOutfit, targetIndex, path, vertices, subdivided);
                if (!fits) Diag.Log($"Shape edit doesn't fit, skipped: {path} [{part} {sourceIndex}, outfit {sourceOutfit} -> {targetOutfit}]");
                return fits;
            }

            public IDictionary<int, int> SlotsFromSource(int outfit) =>
                AccessoryTargets.TryGetValue(outfit, out var map) ? map.GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.First().Key) : null;
        }

        public const string PluginName = "Shape Editor";

        private static string EditKey(string path) => PluginName + "|" + path;

        public static List<ShapeEdit> ListEdits(PluginData data)
        {
            var edits = new List<ShapeEdit>();
            if (!Available || data == null) return edits;
            try
            {
                var shapes = Read(data);
                foreach (var pair in shapes.Layers)
                {
                    var outfit = -1;
                    var path = pair.Key;
                    if (shapes.Version < 6 || !TryParseOutfitKey(pair.Key, out outfit, out path))
                    {
                        outfit = -1;
                        path = pair.Key;
                    }
                    var part = Classify(path, out var index);
                    var names = LayerNames(pair.Value).ToArray();
                    ShapeEdits.Add(edits, PluginName, EditKey(path), part, index, ShapeEdits.Leaf(path) + (names.Length > 0 ? " - " + string.Join(", ", names) : ""), outfit,
                        path, Vertices(pair.Value));
                }
                foreach (var path in shapes.Levels.Keys)
                {
                    var part = Classify(path, out var index);
                    ShapeEdits.Add(edits, PluginName, EditKey(path), part, index, ShapeEdits.Leaf(path) + " - subdivision", -1, path, -1).Subdivided = true;
                }
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not list Shape Editor data: {(ex.InnerException ?? ex).Message}");
            }
            return edits;
        }

        private static int Vertices(object deformData)
        {
            if (!(Traverse.Create(deformData).Property("Layers").GetValue() is IList layers) || layers.Count == 0) return -1;
            return Traverse.Create(layers[0]).Property("Deltas").GetValue() is Array deltas ? deltas.Length : -1;
        }

        private static IEnumerable<string> LayerNames(object deformData)
        {
            if (Traverse.Create(deformData).Property("Layers").GetValue() is IList layers)
                foreach (var layer in layers)
                    if (Traverse.Create(layer).Property("Name").GetValue() is string name && name.Length > 0) yield return name;
        }

        internal sealed class Pending
        {
            public ChaControl Character;
            public bool Coordinate;
            public PluginData Kept;
            public bool Maintain;
            public bool MaintainCoordinate;
            public bool Done;
        }

        public static Pending Begin(ChaControl chaCtrl, bool coordinate)
        {
            if (!Available || chaCtrl == null) return null;
            var controller = Controller(chaCtrl);
            if (controller == null) return null;
            try
            {
                PluginData kept;
                if (coordinate)
                {
                    var temp = new ChaFileCoordinate();
                    SaveCoordinate.Invoke(controller, new object[] { temp });
                    kept = ExtendedSave.GetExtendedDataById(temp, DataId);
                }
                else
                {
                    SaveCard.Invoke(controller, new object[] { GameMode.Maker });
                    kept = ExtendedSave.GetExtendedDataById(chaCtrl.chaFile, DataId);
                }
                var pending = new Pending
                {
                    Character = chaCtrl, Coordinate = coordinate, Kept = kept,
                    Maintain = _registration.MaintainState, MaintainCoordinate = _registration.MaintainCoordinateState
                };
                _registration.MaintainState = true;
                _registration.MaintainCoordinateState = true;
                Diag.Log($"Shape Editor kept ({(coordinate ? "outfit" : "character")}): {Describe(Read(kept))}");
                return pending;
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read current Shape Editor data: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        public static void Release(Pending pending)
        {
            if (pending == null || pending.Done) return;
            pending.Done = true;
            _registration.MaintainState = pending.Maintain;
            _registration.MaintainCoordinateState = pending.MaintainCoordinate;
        }

        public static void Finish(Pending pending, PluginData card, Plan plan)
        {
            if (pending == null || pending.Done) return;
            Release(pending);
            try
            {
                var controller = Controller(pending.Character);
                if (controller == null) return;
                var kept = Read(pending.Kept);
                var fromCard = Read(card);
                Diag.Log($"Shape Editor card: {Describe(fromCard)}; selected {string.Join("; ", plan.Selected.ToArray())}, current outfit {plan.CurrentOutfit}, " +
                         $"outfits from card {string.Join(", ", plan.SourceFor.Select(x => $"{x.Value}->{x.Key}").ToArray())}");
                if (pending.Coordinate)
                {
                    var merged = Write(MergeCoordinate(kept, fromCard, plan));
                    var temp = new ChaFileCoordinate();
                    ExtendedSave.SetExtendedDataById(temp, DataId, merged);
                    _onCoordinateLoaded.Invoke(controller, new object[] { temp });
                    Diag.Log($"Shape Editor loaded outfit: {Describe(Read(merged))}");
                }
                else
                {
                    var merged = Write(MergeCharacter(kept, fromCard, plan));
                    var data = merged.data.Count > 0 ? merged : null;
                    var chaFile = pending.Character.chaFile;
                    ExtendedSave.SetExtendedDataById(chaFile, DataId, data);
                    var loaded = KKAPI.Maker.MakerAPI.LastLoadedChaFile;
                    if (loaded != null && loaded != chaFile) ExtendedSave.SetExtendedDataById(loaded, DataId, data);
                    _onReload.Invoke(controller, new object[] { KoikatuAPI.GetCurrentGameMode(), false });
                    Diag.Log($"Shape Editor loaded character: {Describe(Read(data))}");
                }
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to load Shape Editor data: {ex}");
            }
        }

        private static ShapeData MergeCharacter(ShapeData kept, ShapeData card, Plan plan)
        {
            var result = new ShapeData();
            var cardLayers = new Dictionary<string, string>();
            var keptLayers = new HashSet<string>();
            var taken = new HashSet<string>();
            var legacyOutfit = plan.SourceFor.TryGetValue(plan.CurrentOutfit, out var currentSource) ? currentSource : -1;

            foreach (var pair in card.Layers)
            {
                var outfit = -1;
                var path = pair.Key;
                var perOutfit = card.Version >= 6 && TryParseOutfitKey(pair.Key, out outfit, out path);
                var part = Classify(path, out var index);
                if (!perOutfit && (part == Part.Clothes || part == Part.Accessory)) outfit = legacyOutfit;

                var editKey = EditKey(path);
                string key;
                if (part == Part.Body || part == Part.Head || part == Part.Hair)
                {
                    if (!plan.Takes(editKey, part, index, plan.CurrentOutfit) ||
                        !plan.Fit(part, index, -1, plan.CurrentOutfit, index, path, Vertices(pair.Value), card.Levels.ContainsKey(path))) continue;
                    key = path;
                    taken.Add(path);
                }
                else
                {
                    if (outfit < 0) continue;
                    var target = plan.SourceFor.Where(x => x.Value == outfit).Select(x => (int?)x.Key).FirstOrDefault();
                    if (target == null) continue;
                    if (part == Part.Accessory)
                    {
                        var moved = plan.SlotsFromSource(target.Value);
                        if (!plan.SlotFor(target.Value, index, out var slot) || !plan.Takes(editKey, part, slot, target.Value) ||
                            !plan.Fit(part, index, outfit, target.Value, slot, path, Vertices(pair.Value), card.Levels.ContainsKey(path))) continue;
                        path = RemapSlots(path, moved);
                    }
                    else if (!plan.Takes(editKey, part, index, target.Value) ||
                             !plan.Fit(part, index, outfit, target.Value, index, path, Vertices(pair.Value), card.Levels.ContainsKey(path))) continue;
                    key = OutfitKey(target.Value, path);
                    if (target.Value == plan.CurrentOutfit) taken.Add(path);
                }
                SetPath(pair.Value, path);
                result.Layers[key] = pair.Value;
                foreach (var id in LayerIds(pair.Value)) cardLayers[id] = key;
            }

            foreach (var pair in kept.Layers)
            {
                var outfit = TryParseOutfitKey(pair.Key, out var o, out var path) ? o : plan.CurrentOutfit;
                var part = Classify(path, out var index);
                if (!plan.Keeps(part, index, outfit) || result.Layers.ContainsKey(pair.Key)) continue;
                result.Layers[pair.Key] = pair.Value;
                keptLayers.UnionWith(LayerIds(pair.Value));
            }

            MergeSubdivision(result, kept, card, plan, plan.CurrentOutfit, taken);

            for (var i = 0; i < DriverKeys.Length; i++)
            {
                var seen = new HashSet<string>();
                foreach (var driver in card.Drivers[i])
                {
                    var layer = DriverString(driver, "TargetLayerId");
                    if (layer == null || !cardLayers.TryGetValue(layer, out var key)) continue;
                    var outfit = TryParseOutfitKey(key, out var o, out var path) ? o : plan.CurrentOutfit;
                    var moved = plan.SlotsFromSource(outfit);
                    SetDriver(driver, "TargetRendererPath", path);
                    SetDriver(driver, "SourceRendererPath", RemapSlots(DriverString(driver, "SourceRendererPath"), moved));
                    SetDriver(driver, "SourceBonePath", RemapSlots(DriverString(driver, "SourceBonePath"), moved));
                    var scope = Traverse.Create(driver).Property("CoordinateScope");
                    if (scope.PropertyExists() && scope.GetValue() is int source && source >= 0)
                    {
                        var target = plan.SourceFor.Where(x => x.Value == source).Select(x => (int?)x.Key).FirstOrDefault();
                        if (target != null) scope.SetValue(target.Value);
                    }
                    if (seen.Add(path + "|" + layer)) result.Drivers[i].Add(driver);
                }
                foreach (var driver in kept.Drivers[i])
                {
                    var layer = DriverString(driver, "TargetLayerId");
                    if (layer == null || !keptLayers.Contains(layer)) continue;
                    if (seen.Add(DriverString(driver, "TargetRendererPath") + "|" + layer)) result.Drivers[i].Add(driver);
                }
            }
            return result;
        }

        private static ShapeData MergeCoordinate(ShapeData kept, ShapeData card, Plan plan)
        {
            var result = new ShapeData();
            var outfit = plan.CurrentOutfit;
            var moved = plan.SlotsFromSource(outfit);
            var taken = new HashSet<string>();
            foreach (var pair in card.Layers)
            {
                var path = pair.Key;
                var part = Classify(path, out var index);
                var editKey = EditKey(path);
                if (part == Part.Accessory)
                {
                    if (!plan.SlotFor(outfit, index, out var slot) || !plan.Takes(editKey, part, slot, outfit) ||
                        !plan.Fit(part, index, 0, outfit, slot, path, Vertices(pair.Value), card.Levels.ContainsKey(path))) continue;
                    path = RemapSlots(path, moved);
                }
                else if (part != Part.Clothes || !plan.Takes(editKey, part, index, outfit) ||
                         !plan.Fit(part, index, 0, outfit, index, path, Vertices(pair.Value), card.Levels.ContainsKey(path))) continue;
                SetPath(pair.Value, path);
                result.Layers[path] = pair.Value;
                taken.Add(path);
            }
            foreach (var pair in kept.Layers)
            {
                var part = Classify(pair.Key, out var index);
                if ((part == Part.Clothes || part == Part.Accessory) && !plan.Keeps(part, index, outfit) || result.Layers.ContainsKey(pair.Key)) continue;
                result.Layers[pair.Key] = pair.Value;
            }
            MergeSubdivision(result, kept, card, plan, outfit, taken);
            return result;
        }

        private static void MergeSubdivision(ShapeData result, ShapeData kept, ShapeData card, Plan plan, int outfit, HashSet<string> taken)
        {
            var moved = plan.SlotsFromSource(outfit);
            var source = plan.SourceFor.TryGetValue(outfit, out var s) ? s : -1;
            foreach (var pair in card.Levels)
            {
                var path = pair.Key;
                var part = Classify(path, out var index);
                var editKey = EditKey(path);
                if (part == Part.Accessory)
                {
                    if (!plan.SlotFor(outfit, index, out var slot) || !plan.Takes(editKey, part, slot, outfit) || !plan.Fit(part, index, source, outfit, slot, path, -1, true)) continue;
                    path = RemapSlots(path, moved);
                }
                else if (!plan.Takes(editKey, part, index, outfit) || !plan.Fit(part, index, source, outfit, index, path, -1, true)) continue;
                taken.Add(path);
                result.Levels[path] = pair.Value;
                if (card.Faces.TryGetValue(pair.Key, out var faces)) result.Faces[path] = faces;
                if (card.Smooth.TryGetValue(pair.Key, out var smooth)) result.Smooth[path] = smooth;
            }
            foreach (var pair in kept.Levels)
            {
                var part = Classify(pair.Key, out var index);
                if (!plan.Keeps(part, index, outfit) || taken.Contains(pair.Key) || result.Levels.ContainsKey(pair.Key)) continue;
                result.Levels[pair.Key] = pair.Value;
                if (kept.Faces.TryGetValue(pair.Key, out var faces)) result.Faces[pair.Key] = faces;
                if (kept.Smooth.TryGetValue(pair.Key, out var smooth)) result.Smooth[pair.Key] = smooth;
            }
        }

        private static void SetPath(object deformData, string path)
        {
            var property = Traverse.Create(deformData).Property("RendererPath");
            if (property.PropertyExists()) property.SetValue(path);
        }

        private static string Describe(ShapeData shapes)
        {
            var layers = shapes.Layers.Keys.Select(x =>
            {
                TryParseOutfitKey(x, out _, out var path);
                return $"{x} [{Classify(path, out var index)} {index}]";
            });
            return $"layers {{{string.Join("; ", layers.ToArray())}}}, subdivision {{{string.Join("; ", shapes.Levels.Select(x => $"{x.Key}={x.Value}").ToArray())}}}, " +
                   $"drivers {string.Join("/", shapes.Drivers.Select(x => x.Count.ToString()).ToArray())}";
        }
    }
#endif
}
