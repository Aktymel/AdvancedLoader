#if KKS || KK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using MessagePack;
using UnityEngine;
#endif

namespace AdvancedLoader
{
#if !(KKS || KK)
    internal static class BlendshapeCreatorBridge
    {
        public static bool Available => false;
    }
#else
    internal static class BlendshapeCreatorBridge
    {
        public const string DataId = "com.shallty.blendshapecreator";

        private const string ShapesKey = "CharaBlendShapesData";
        private const string ConversionsKey = "CharaConvertedRenderers";
        private const string CoordinateShapesKey = "CoordBlendShapesData";
        private const string CoordinateConversionsKey = "CoordConvertedRenderers";

        private static bool _checked;
        private static bool _available;
        private static Type _shapesType;
        private static Type _conversionsType;
        private static Type _clothingOwner;
        private static Type _accessoryOwner;
        private static CharacterApi.ControllerRegistration _registration;
        private static MethodInfo _onReload;
        private static MethodInfo _onCoordinateLoaded;
        private static MethodInfo _deleteShape;
        private static MethodInfo _revertConversion;
        private static Type _legacyType;
        private static ConstructorInfo _newShape;

        private static readonly MethodInfo SerializeMethod = typeof(MessagePackSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(x => x.Name == "Serialize" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1);
        private static readonly MethodInfo DeserializeMethod = typeof(MessagePackSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(x => x.Name == "Deserialize" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(byte[]));
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
                var controllerType = AccessTools.TypeByName("BlendshapeCreator.CharacterController");
                _shapesType = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+OCIBlendShapeData");
                _conversionsType = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+OCIConversionData");
                var shapeType = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+BlendShape");
                var conversionType = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+ConvertedRenderer");
                _clothingOwner = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+BlendShape+Owner+Clothing");
                _accessoryOwner = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+BlendShape+Owner+Accessory");
                if (controllerType == null || _shapesType == null || _conversionsType == null || shapeType == null || conversionType == null ||
                    _clothingOwner == null || _accessoryOwner == null) return;

                _registration = CharacterApi.RegisteredHandlers.FirstOrDefault(x => x.ControllerType == controllerType && x.ExtendedDataId == DataId);
                PluginGuid = Chainloader.PluginInfos.Values.FirstOrDefault(x => x.Instance != null && x.Instance.GetType().Assembly == controllerType.Assembly)?.Metadata.GUID;
                if (_registration == null || PluginGuid == null) return;

                var instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                _onReload = controllerType.GetMethod("OnReload", instance, null, new[] { typeof(GameMode), typeof(bool) }, null);
                _onCoordinateLoaded = controllerType.GetMethod("OnCoordinateBeingLoaded", instance, null, new[] { typeof(ChaFileCoordinate), typeof(bool) }, null);
                _deleteShape = shapeType.GetMethod("DeleteBlendShape", new[] { typeof(Transform) });
                _revertConversion = conversionType.GetMethod("RevertConversion", new[] { typeof(Transform) });
                _legacyType = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+BlendShapeData");
                var ownerType = AccessTools.TypeByName("BlendshapeCreator.BlendshapeCreator+BlendShape+IOwner");
                _newShape = ownerType == null ? null : shapeType.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(float), ownerType });

                _available = SerializeMethod != null && DeserializeMethod != null && SaveCard != null && SaveCoordinate != null &&
                             _onReload != null && _onCoordinateLoaded != null && _deleteShape != null && _revertConversion != null &&
                             AccessTools.Field(controllerType, ShapesKey) != null && AccessTools.Field(controllerType, "CharaConversionData") != null &&
                             AccessTools.Field(_shapesType, "blendShapes") != null && AccessTools.Field(_conversionsType, "convertedRenderers") != null &&
                             AccessTools.Field(shapeType, "rendererPath") != null && AccessTools.Field(shapeType, "owner") != null &&
                             AccessTools.Field(conversionType, "rendererPath") != null && AccessTools.Field(conversionType, "owner") != null;
                if (!_available) AdvancedLoaderPlugin.Logger.LogWarning("This version of Blendshape Creator is not supported, its data is loaded as one block under Other plugins");
            }
            catch (Exception ex)
            {
                _available = false;
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Blendshape Creator: {ex.Message}");
            }
        }

        private sealed class Data
        {
            public readonly List<object> Shapes = new List<object>();
            public readonly List<object> Conversions = new List<object>();
            public readonly Dictionary<object, string> LegacyKeys = new Dictionary<object, string>();
        }

        public const string PluginName = "Blendshape Creator";

        private static string EditKey(string path, string name) => PluginName + "|" + path + "|" + name;

        private static string LegacyKey(string renderer, string name) => PluginName + "|old:" + renderer + "|" + name;

        public static List<ShapeEdit> ListEdits(PluginData data, bool coordinate)
        {
            var edits = new List<ShapeEdit>();
            if (!Available || data == null) return edits;
            try
            {
                foreach (var shape in Read(data, coordinate).Shapes)
                {
                    var part = Classify(shape, out var index, out var outfit);
                    var path = RendererPath(shape);
                    ShapeEdits.Add(edits, PluginName, EditKey(path, ShapeName(shape)), part, index, ShapeEdits.Leaf(path) + " - " + ShapeName(shape), coordinate ? -1 : outfit,
                        path, Vertices(shape));
                }
                if (!coordinate)
                    foreach (var legacy in LegacyEntries(data))
                        if (edits.All(x => x.Key != LegacyKey(legacy.Renderer, legacy.Name)))
                            edits.Add(new ShapeEdit { Key = LegacyKey(legacy.Renderer, legacy.Name), Plugin = PluginName, Part = "Mesh", Name = legacy.Renderer + " - " + legacy.Name });
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not list Blendshape Creator data: {(ex.InnerException ?? ex).Message}");
            }
            return edits;
        }

        private sealed class LegacyEntry
        {
            public string Renderer;
            public string Name;
            public string Deltas;
            public float Weight;
        }

        private static List<LegacyEntry> LegacyEntries(PluginData data)
        {
            var result = new List<LegacyEntry>();
            if (_legacyType == null || data?.data == null || !data.data.TryGetValue(LegacyShapesKey, out var value) || !(value is byte[] bytes)) return result;
            if (!(Deserialize(typeof(List<>).MakeGenericType(_legacyType), bytes) is IList list)) return result;
            var weights = data.data.TryGetValue(LegacyWeightsKey, out var weightValue) && weightValue is byte[] weightBytes ? Deserialize(typeof(List<float>), weightBytes) as List<float> : null;
            weights = weights ?? new List<float>();
            foreach (var item in list)
            {
                var names = Traverse.Create(item).Property("Names").GetValue() as List<string>;
                var renderers = Traverse.Create(item).Property("Renderers").GetValue() as List<string>;
                var deltas = Traverse.Create(item).Property("deltaVertices").GetValue() as List<string>;
                if (names == null || renderers == null || deltas == null) continue;
                for (var i = 0; i < names.Count && i < renderers.Count && i < deltas.Count; i++)
                    result.Add(new LegacyEntry { Renderer = renderers[i], Name = names[i], Deltas = deltas[i], Weight = i < weights.Count ? weights[i] : 0f });
            }
            return result;
        }

        private static Data Read(PluginData data, bool coordinate)
        {
            var result = new Data();
            if (data?.data == null) return result;
            if (data.data.TryGetValue(coordinate ? CoordinateShapesKey : ShapesKey, out var shapes) && shapes is byte[] shapeBytes &&
                Deserialize(_shapesType, shapeBytes) is object shapeData && Traverse.Create(shapeData).Field("blendShapes").GetValue() is IList shapeList)
                result.Shapes.AddRange(shapeList.Cast<object>().Where(x => x != null));
            if (data.data.TryGetValue(coordinate ? CoordinateConversionsKey : ConversionsKey, out var conversions) && conversions is byte[] conversionBytes &&
                Deserialize(_conversionsType, conversionBytes) is object conversionData && Traverse.Create(conversionData).Field("convertedRenderers").GetValue() is IList conversionList)
                result.Conversions.AddRange(conversionList.Cast<object>().Where(x => x != null));
            return result;
        }

        private const string LegacyShapesKey = "blendShapeDataList";
        private const string LegacyWeightsKey = "blendShapeWeightList";

        private static void AddLegacy(Data result, PluginData data, Transform root)
        {
            if (_newShape == null) return;
            var entries = LegacyEntries(data);
            if (entries.Count == 0) return;
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var entry in entries)
            {
                var renderer = renderers.LastOrDefault(x => x.name == entry.Renderer);
                if (renderer == null) continue;
                var shape = _newShape.Invoke(new object[] { RelativePath(root, renderer.transform), entry.Name, entry.Deltas, null, null, entry.Weight, null });
                result.Shapes.Add(shape);
                result.LegacyKeys[shape] = LegacyKey(entry.Renderer, entry.Name);
            }
        }

        private static string RelativePath(Transform root, Transform target)
        {
            var names = new List<string>();
            for (var t = target; t != null && t != root; t = t.parent) names.Insert(0, t.name);
            return string.Join("/", names.ToArray());
        }

        private static PluginData Write(Data data, bool coordinate)
        {
            var shapes = Activator.CreateInstance(_shapesType);
            var shapeList = (IList)Traverse.Create(shapes).Field("blendShapes").GetValue();
            shapeList.Clear();
            foreach (var shape in data.Shapes) shapeList.Add(shape);
            var conversions = Activator.CreateInstance(_conversionsType);
            var conversionList = (IList)Traverse.Create(conversions).Field("convertedRenderers").GetValue();
            conversionList.Clear();
            foreach (var conversion in data.Conversions) conversionList.Add(conversion);

            var result = new PluginData();
            result.data[coordinate ? CoordinateShapesKey : ShapesKey] = SerializeMethod.MakeGenericMethod(_shapesType).Invoke(null, new[] { shapes });
            result.data[coordinate ? CoordinateConversionsKey : ConversionsKey] = SerializeMethod.MakeGenericMethod(_conversionsType).Invoke(null, new[] { conversions });
            return result;
        }

        private static object Deserialize(Type type, byte[] bytes)
        {
            try
            {
                return DeserializeMethod.MakeGenericMethod(type).Invoke(null, new object[] { bytes });
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Blendshape Creator data: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        private static string RendererPath(object item) => Traverse.Create(item).Field("rendererPath").GetValue<string>() ?? "";

        private static string ShapeName(object item) => Traverse.Create(item).Field("name").GetValue() as string;

        private static ShapeEditorBridge.Part Classify(object item, out int index, out int outfit)
        {
            var owner = Traverse.Create(item).Field("owner").GetValue();
            var part = ShapeEditorBridge.Classify(RendererPath(item), out index);
            outfit = -1;
            if (owner != null && _clothingOwner.IsInstanceOfType(owner))
            {
                index = Convert.ToInt32(Traverse.Create(owner).Field("ClothesKind").GetValue());
                outfit = Traverse.Create(owner).Field("OutfitSlot").GetValue<int>();
                return ShapeEditorBridge.Part.Clothes;
            }
            if (owner != null && _accessoryOwner.IsInstanceOfType(owner))
            {
                index = Traverse.Create(owner).Field("AccessorySlot").GetValue<int>();
                outfit = Traverse.Create(owner).Field("OutfitSlot").GetValue<int>();
                return ShapeEditorBridge.Part.Accessory;
            }
            return part;
        }

        private static void MoveTo(object item, int outfit, IDictionary<int, int> moved, int slot)
        {
            var path = Traverse.Create(item).Field("rendererPath");
            path.SetValue(ShapeEditorBridge.RemapSlots(path.GetValue<string>(), moved));
            var owner = Traverse.Create(item).Field("owner").GetValue();
            if (owner == null) return;
            if (_clothingOwner.IsInstanceOfType(owner) || _accessoryOwner.IsInstanceOfType(owner)) Traverse.Create(owner).Field("OutfitSlot").SetValue(outfit);
            if (_accessoryOwner.IsInstanceOfType(owner) && slot >= 0) Traverse.Create(owner).Field("AccessorySlot").SetValue(slot);
        }

        private static Data Merge(Data kept, Data card, ShapeEditorBridge.Plan plan, bool coordinate)
        {
            var result = new Data();
            var legacyOutfit = plan.SourceFor.TryGetValue(plan.CurrentOutfit, out var currentSource) ? currentSource : -1;
            var selectedPaths = new HashSet<string>(card.Shapes.Where(x => plan.Selected.Contains(CardKey(card, x))).Select(RendererPath));
            foreach (var item in card.Shapes.Concat(card.Conversions))
            {
                var isShape = ShapeName(item) != null;
                var part = Classify(item, out var index, out var outfit);
                var chosen = isShape ? plan.Selected.Contains(CardKey(card, item)) : selectedPaths.Contains(RendererPath(item));
                if (!chosen) continue;
                var to = isShape ? result.Shapes : result.Conversions;
                if (part == ShapeEditorBridge.Part.Body || part == ShapeEditorBridge.Part.Head || part == ShapeEditorBridge.Part.Hair)
                {
                    if (plan.Fit(part, index, -1, plan.CurrentOutfit, index, RendererPath(item), isShape ? Vertices(item) : -1, !isShape)) to.Add(item);
                    continue;
                }
                if (coordinate || outfit < 0) outfit = coordinate ? 0 : legacyOutfit;
                var target = plan.SourceFor.Where(x => x.Value == outfit).Select(x => (int?)x.Key).FirstOrDefault();
                if (target == null) continue;
                var moved = plan.SlotsFromSource(target.Value);
                var slot = -1;
                if (part == ShapeEditorBridge.Part.Accessory)
                {
                    if (!plan.SlotFor(target.Value, index, out slot) || !plan.Fit(part, index, outfit, target.Value, slot, RendererPath(item), -1, true)) continue;
                }
                else if (!plan.SourceFor.ContainsKey(target.Value) || !plan.Fit(part, index, outfit, target.Value, index, RendererPath(item), -1, true)) continue;
                MoveTo(item, target.Value, moved, slot);
                to.Add(item);
            }

            var takenShapes = new HashSet<string>(result.Shapes.Select(x => RendererPath(x) + "|" + ShapeName(x) + "|" + Outfit(x)));
            var takenConversions = new HashSet<string>(result.Conversions.Select(x => RendererPath(x) + "|" + Outfit(x)));
            foreach (var item in kept.Shapes.Concat(kept.Conversions))
            {
                var isShape = ShapeName(item) != null;
                var part = Classify(item, out var index, out var outfit);
                if (coordinate || outfit < 0) outfit = plan.CurrentOutfit;
                if (!plan.Keeps(part, index, outfit)) continue;
                if (isShape ? takenShapes.Contains(RendererPath(item) + "|" + ShapeName(item) + "|" + Outfit(item)) : takenConversions.Contains(RendererPath(item) + "|" + Outfit(item))) continue;
                (isShape ? result.Shapes : result.Conversions).Add(item);
            }
            return result;
        }

        private static int Vertices(object item)
        {
            if (!(Traverse.Create(item).Field("deltaVertices").GetValue() is string deltas) || deltas.Length == 0) return -1;
            var commas = 0;
            foreach (var c in deltas)
                if (c == ',') commas++;
            return (commas + 1) / 3;
        }

        private static string CardKey(Data card, object shape) =>
            card.LegacyKeys.TryGetValue(shape, out var legacy) ? legacy : EditKey(RendererPath(shape), ShapeName(shape));

        private static int Outfit(object item)
        {
            Classify(item, out _, out var outfit);
            return outfit;
        }

        private static CharaCustomFunctionController Controller(ChaControl chaCtrl) =>
            chaCtrl == null ? null : _registration.Instances.FirstOrDefault(x => x != null && x.ChaControl == chaCtrl);

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
                Diag.Log($"Blendshape Creator kept ({(coordinate ? "outfit" : "character")}): {Describe(Read(kept, coordinate))}");
                return pending;
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read current Blendshape Creator data: {(ex.InnerException ?? ex).Message}");
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

        public static void Finish(Pending pending, PluginData card, ShapeEditorBridge.Plan plan)
        {
            if (pending == null || pending.Done) return;
            Release(pending);
            try
            {
                var controller = Controller(pending.Character);
                if (controller == null) return;
                var root = pending.Character.transform;
                var fromCard = Read(card, pending.Coordinate);
                if (!pending.Coordinate) AddLegacy(fromCard, card, root);
                Diag.Log($"Blendshape Creator card: {Describe(fromCard)}");
                var merged = Merge(Read(pending.Kept, pending.Coordinate), fromCard, plan, pending.Coordinate);
                Diag.Log($"Blendshape Creator loaded: {Describe(merged)}");
                var data = Write(merged, pending.Coordinate);
                if (pending.Coordinate)
                {
                    var temp = new ChaFileCoordinate();
                    ExtendedSave.SetExtendedDataById(temp, DataId, data);
                    _onCoordinateLoaded.Invoke(controller, new object[] { temp, false });
                    return;
                }

                var current = Traverse.Create(controller).Field(ShapesKey).GetValue();
                if (current != null && Traverse.Create(current).Field("blendShapes").GetValue() is IList shapes)
                    foreach (var shape in shapes.Cast<object>().Where(x => x != null).ToList())
                        _deleteShape.Invoke(shape, new object[] { root });
                var keptPaths = new HashSet<string>(merged.Conversions.Select(RendererPath));
                var conversions = Traverse.Create(controller).Field("CharaConversionData").GetValue();
                if (conversions != null && Traverse.Create(conversions).Field("convertedRenderers").GetValue() is IList list)
                    foreach (var conversion in list.Cast<object>().Where(x => x != null && !keptPaths.Contains(RendererPath(x))).ToList())
                        _revertConversion.Invoke(conversion, new object[] { root });

                var chaFile = pending.Character.chaFile;
                ExtendedSave.SetExtendedDataById(chaFile, DataId, data);
                var loaded = KKAPI.Maker.MakerAPI.LastLoadedChaFile;
                if (loaded != null && loaded != chaFile) ExtendedSave.SetExtendedDataById(loaded, DataId, data);
                _onReload.Invoke(controller, new object[] { KoikatuAPI.GetCurrentGameMode(), false });
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to load Blendshape Creator data: {ex}");
            }
        }

        private static string Describe(Data data)
        {
            string Item(object x)
            {
                var part = Classify(x, out var index, out var outfit);
                var path = RendererPath(x);
                var at = path.LastIndexOf('/');
                return $"{ShapeName(x)} on {(at >= 0 ? path.Substring(at + 1) : path)} [{part} {index}, outfit {outfit}]";
            }
            return $"shapes {{{string.Join("; ", data.Shapes.Select(Item).ToArray())}}}, converted {data.Conversions.Count}";
        }
    }
#endif
}
