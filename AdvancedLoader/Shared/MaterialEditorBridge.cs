using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ExtensibleSaveFormat;
using HarmonyLib;
using KK_Plugins;
using KK_Plugins.MaterialEditor;
using MaterialEditorAPI;
using MessagePack;
using UnityEngine;
using static KK_Plugins.MaterialEditor.MaterialEditorCharaController;
#if HS2 || AI
using AIChara;
#elif KKS || KK
#endif

namespace AdvancedLoader
{
    // Separate so the check never loads Material Editor types
    internal static class MaterialEditorPresence
    {
        public const string GUID = "com.deathweasel.bepinex.materialeditor";

        public static bool Available => Dependencies.MaterialEditor.IsOk;
    }

    internal sealed class MaterialEditorData
    {
        public List<MaterialShader> Shaders = new List<MaterialShader>();
        public List<MaterialFloatProperty> Floats = new List<MaterialFloatProperty>();
        public List<MaterialColorProperty> Colors = new List<MaterialColorProperty>();
        public List<MaterialKeywordProperty> Keywords = new List<MaterialKeywordProperty>();
        public List<MaterialTextureProperty> Textures = new List<MaterialTextureProperty>();
        public List<RendererProperty> Renderers = new List<RendererProperty>();
        public Dictionary<int, byte[]> TextureData = new Dictionary<int, byte[]>();

        // Character's own originals; ME would reset to the card character's
        public Dictionary<string, object> OriginalValues = new Dictionary<string, object>();

        // Head renderer -> material names, to follow a new face type
        public Dictionary<string, List<string>> HeadMaterials = new Dictionary<string, List<string>>();

        // "type/slot" of hair, clothes and accessories -> material names
        public Dictionary<string, List<string>> PartMaterials = new Dictionary<string, List<string>>();

        // ME 5.0 lists (vectors, cubemaps) by list name
        public Dictionary<string, IList> Extra = new Dictionary<string, IList>();
    }

    // Only use when MaterialEditorPresence.Available
    internal static class MaterialEditorBridge
    {
        private const string ShaderListKey = "MaterialShaderList";
        private const string FloatListKey = "MaterialFloatPropertyList";
        private const string ColorListKey = "MaterialColorPropertyList";
        private const string KeywordListKey = "MaterialKeywordPropertyList";
        private const string TextureListKey = "MaterialTexturePropertyList";
        private const string RendererListKey = "RendererPropertyList";

        // ME 5.0 lists; older versions don't have them
        private const string VectorListKey = "MaterialVectorPropertyList";
        private const string CubemapListKey = "MaterialCubemapPropertyList";
        private static readonly Dictionary<string, Type> ExtraLists = new[] { VectorListKey, CubemapListKey }
            .Select(x => new KeyValuePair<string, FieldInfo>(x, typeof(MaterialEditorCharaController).GetField(x, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)))
            .Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value.FieldType);
        private static readonly MethodInfo SetVectorMethod = ControllerMethod("SetMaterialVectorProperty", 7);
        private static readonly MethodInfo RemoveVectorMethod = ControllerMethod("RemoveMaterialVectorProperty", 6);
        private static readonly MethodInfo SetCubemapMethod = ControllerMethod("SetMaterialCubemap", 6);
        private static readonly MethodInfo RemoveCubemapMethod = ControllerMethod("RemoveMaterialCubemap", 6);
        private static readonly MethodInfo SerializeMethod = typeof(MessagePackSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(x => x.Name == "Serialize" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1);
        private static readonly MethodInfo DeserializeMethod = typeof(MessagePackSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(x => x.Name == "Deserialize" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(byte[]));

        private static MethodInfo ControllerMethod(string name, int parameters) => typeof(MaterialEditorCharaController)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == parameters);

        private static readonly MethodInfo FindGameObjectMethod = AccessTools.Method(typeof(MaterialEditorCharaController), "FindGameObject");

        public static object ReadFromCharacterFile(ChaFile file) => Read(ExtendedSave.GetExtendedDataById(file, MaterialEditorPresence.GUID));

        public static object ReadFromCoordinate(ChaFileCoordinate coordinate) => Read(ExtendedSave.GetExtendedDataById(coordinate, MaterialEditorPresence.GUID));

        private static MaterialEditorData Read(PluginData pluginData)
        {
            var data = new MaterialEditorData();
            if (pluginData == null) return data;

            data.Shaders = ReadList<MaterialShader>(pluginData, ShaderListKey);
            data.Floats = ReadList<MaterialFloatProperty>(pluginData, FloatListKey);
            data.Colors = ReadList<MaterialColorProperty>(pluginData, ColorListKey);
            data.Keywords = ReadList<MaterialKeywordProperty>(pluginData, KeywordListKey);
            data.Textures = ReadList<MaterialTextureProperty>(pluginData, TextureListKey);
            data.Renderers = ReadList<RendererProperty>(pluginData, RendererListKey);
            foreach (var pair in ExtraLists)
                if (pluginData.data.TryGetValue(pair.Key, out var extra) && extra is byte[] extraBytes && extraBytes.Length > 0)
                    data.Extra[pair.Key] = DeserializeList(pair.Value, extraBytes);

            // Textures may be bundled, deduplicated or stored as local files; Material Editor's own handler knows all formats
            var handlerType = typeof(MaterialEditorCharaController).Assembly.GetType("KK_Plugins.MaterialEditor.TextureSaveHandler");
            var handler = handlerType == null ? null : AccessTools.Field(handlerType, "Instance")?.GetValue(null);
            if (handler != null)
            {
                var load = AccessTools.Method(handlerType, "Load").MakeGenericMethod(typeof(Dictionary<int, TextureContainer>));
                if (load.Invoke(handler, new object[] { pluginData, "TextureDictionary", true }) is Dictionary<int, TextureContainer> textures)
                {
                    foreach (var texture in textures)
                        data.TextureData[texture.Key] = texture.Value.Data;
                }
            }
            return data;
        }

        private static List<T> ReadList<T>(PluginData pluginData, string key)
        {
            if (!pluginData.data.TryGetValue(key, out var value) || !(value is byte[] bytes)) return new List<T>();
            return MessagePackSerializer.Deserialize<List<T>>(bytes) ?? new List<T>();
        }

        public static object Snapshot(ChaControl chaCtrl, object card)
        {
            var controller = GetController(chaCtrl);
            var data = new MaterialEditorData();
            if (controller == null) return data;

            var traverse = Traverse.Create(controller);
            data.Shaders = Copy(traverse.Field(ShaderListKey).GetValue<List<MaterialShader>>());
            data.Floats = Copy(traverse.Field(FloatListKey).GetValue<List<MaterialFloatProperty>>());
            data.Colors = Copy(traverse.Field(ColorListKey).GetValue<List<MaterialColorProperty>>());
            data.Keywords = Copy(traverse.Field(KeywordListKey).GetValue<List<MaterialKeywordProperty>>());
            data.Textures = Copy(traverse.Field(TextureListKey).GetValue<List<MaterialTextureProperty>>());
            data.Renderers = Copy(traverse.Field(RendererListKey).GetValue<List<RendererProperty>>());
            foreach (var key in ExtraLists.Keys) data.Extra[key] = CopyExtra(key, ExtraList(controller, key));
            var textures = traverse.Field("TextureDictionary").GetValue<Dictionary<int, TextureContainer>>();
            if (textures != null)
                foreach (var texture in textures)
                    data.TextureData[texture.Key] = texture.Value.Data;
            data.HeadMaterials = HeadMaterialNames(chaCtrl);
            data.PartMaterials = PartMaterialNames(controller, chaCtrl);
            if (card is MaterialEditorData cardData) RecordOriginalValues(controller, data, cardData);
            return data;
        }

        // Head renderer -> its material names
        private static Dictionary<string, List<string>> HeadMaterialNames(ChaControl chaCtrl)
        {
            var result = new Dictionary<string, List<string>>();
            if (chaCtrl.objHead == null) return result;
            foreach (var renderer in chaCtrl.objHead.GetComponentsInChildren<Renderer>(true))
            {
                var name = FormatName(renderer.name);
                if (!IsPartRenderer(renderer) && !result.ContainsKey(name))
                    result[name] = renderer.sharedMaterials.Select(x => FormatName(x == null ? null : x.name)).ToList();
            }
            return result;
        }

        // Puts the kept head and eye data of the old head's materials on the same renderers' materials of the new head
        public static void MoveToNewHead(ChaControl chaCtrl, object snapshot, MaterialGroup groups)
        {
            var controller = GetController(chaCtrl);
            if (!(snapshot is MaterialEditorData source) || controller == null) return;

            var now = HeadMaterialNames(chaCtrl);
            var present = new HashSet<string>(now.Values.SelectMany(x => x));
            var renames = new Dictionary<string, string>();
            foreach (var renderer in source.HeadMaterials)
            {
                if (!now.TryGetValue(renderer.Key, out var materials) || materials.Count != renderer.Value.Count) continue;
                for (var i = 0; i < materials.Count; i++)
                {
                    var old = renderer.Value[i];
                    var group = IsEyeName(old) ? MaterialGroup.Eyes : MaterialGroup.Head;
                    if (old != materials[i] && old.Length > 0 && !present.Contains(old) && (groups & group) != 0 && !renames.ContainsKey(old))
                        renames[old] = materials[i];
                }
            }
            if (renames.Count == 0) return;

            bool Moved(ObjectType type, string material) => type == ObjectType.Character && renames.ContainsKey(material);

            // The old entries have nothing to apply to any more
            var traverse = Traverse.Create(controller);
            traverse.Field(ShaderListKey).GetValue<List<MaterialShader>>()?.RemoveAll(x => Moved(x.ObjectType, x.MaterialName));
            traverse.Field(FloatListKey).GetValue<List<MaterialFloatProperty>>()?.RemoveAll(x => Moved(x.ObjectType, x.MaterialName));
            traverse.Field(ColorListKey).GetValue<List<MaterialColorProperty>>()?.RemoveAll(x => Moved(x.ObjectType, x.MaterialName));
            traverse.Field(KeywordListKey).GetValue<List<MaterialKeywordProperty>>()?.RemoveAll(x => Moved(x.ObjectType, x.MaterialName));
            traverse.Field(TextureListKey).GetValue<List<MaterialTextureProperty>>()?.RemoveAll(x => Moved(x.ObjectType, x.MaterialName));
            foreach (var key in ExtraLists.Keys) RemoveWhere(ExtraList(controller, key), x => Moved(EntryType(x), EntryMaterial(x)));

            var moved = new MaterialEditorData { TextureData = source.TextureData };
            moved.Shaders = Copy(source.Shaders.Where(x => Moved(x.ObjectType, x.MaterialName)).ToList());
            moved.Floats = Copy(source.Floats.Where(x => Moved(x.ObjectType, x.MaterialName)).ToList());
            moved.Colors = Copy(source.Colors.Where(x => Moved(x.ObjectType, x.MaterialName)).ToList());
            moved.Keywords = Copy(source.Keywords.Where(x => Moved(x.ObjectType, x.MaterialName)).ToList());
            moved.Textures = Copy(source.Textures.Where(x => Moved(x.ObjectType, x.MaterialName)).ToList());
            foreach (var x in moved.Shaders) x.MaterialName = renames[x.MaterialName];
            foreach (var x in moved.Floats) x.MaterialName = renames[x.MaterialName];
            foreach (var x in moved.Colors) x.MaterialName = renames[x.MaterialName];
            foreach (var x in moved.Keywords) x.MaterialName = renames[x.MaterialName];
            foreach (var x in moved.Textures) x.MaterialName = renames[x.MaterialName];
            foreach (var key in ExtraLists.Keys)
            {
                if (!source.Extra.TryGetValue(key, out var from)) continue;
                var copies = CopyExtra(key, from, x => Moved(EntryType(x), EntryMaterial(x)));
                foreach (var x in copies) SetEntry(x, "MaterialName", renames[EntryMaterial(x)]);
                moved.Extra[key] = copies;
            }

            var targets = new HashSet<string>(renames.Values);
            Apply(controller, moved, (type, slot, material) => type == ObjectType.Character && targets.Contains(material), (type, slot, renderer) => false, false);
        }

        private static string PartKey(ObjectType type, int slot) => $"{(int)type}/{slot}";

        private static Dictionary<string, List<string>> PartMaterialNames(MaterialEditorCharaController controller, ChaControl chaCtrl)
        {
            var result = new Dictionary<string, List<string>>();
            void Add(ObjectType type, int count)
            {
                for (var slot = 0; slot < count; slot++)
                {
                    var go = FindGameObject(controller, type, slot);
                    if (go == null) continue;
                    result[PartKey(type, slot)] = MaterialAPI.GetRendererList(go)
                        .SelectMany(renderer => MaterialAPI.GetMaterials(go, renderer)).Select(x => FormatName(x == null ? null : x.name)).ToList();
                }
            }
            Add(ObjectType.Hair, chaCtrl.objHair?.Length ?? 0);
            Add(ObjectType.Clothing, chaCtrl.objClothes?.Length ?? 0);
            Add(ObjectType.Accessory, Math.Max(AccessoryData.VanillaSlotCount, chaCtrl.nowCoordinate.accessory.parts.Length));
            return result;
        }

        private static MaterialGroup PartGroup(ObjectType type) =>
            type == ObjectType.Hair ? MaterialGroup.Hair : type == ObjectType.Clothing ? MaterialGroup.Clothes : MaterialGroup.Accessories;

        private static string MaterialKey(ObjectType type, int slot, string material) => $"{(int)type}/{slot}/{material}";

        // Kept data of a replaced hair, clothing or accessory part goes on the new part's materials at the same positions
        public static void MoveToNewParts(ChaControl chaCtrl, object snapshot, Func<MaterialGroup, int, bool> fromCard)
        {
            var controller = GetController(chaCtrl);
            if (!(snapshot is MaterialEditorData source) || controller == null) return;

            var now = PartMaterialNames(controller, chaCtrl);
            var renames = new Dictionary<string, string>();
            foreach (var type in new[] { ObjectType.Hair, ObjectType.Clothing, ObjectType.Accessory })
            {
                foreach (var part in source.PartMaterials.Where(x => x.Key.StartsWith((int)type + "/")))
                {
                    var slot = int.Parse(part.Key.Substring(part.Key.IndexOf('/') + 1));
                    if (fromCard(PartGroup(type), slot) || !now.TryGetValue(part.Key, out var current)) continue;
                    var old = part.Value;
                    var present = new HashSet<string>(current);
                    for (var i = 0; i < old.Count && i < current.Count; i++)
                    {
                        var key = MaterialKey(type, slot, old[i]);
                        if (old[i] != current[i] && old[i].Length > 0 && !present.Contains(old[i]) && !renames.ContainsKey(key))
                            renames[key] = current[i];
                    }
                }
            }
            Diag.Log($"ME parts moved to new materials: {string.Join(", ", renames.Select(x => x.Key + " -> " + x.Value).ToArray())}");
            ApplyRenamed(controller, source, renames, (type, coordinate) => IsCurrentOutfit(controller, type, coordinate));
        }

        // Like ME's copy and paste: the card's data of a hair or clothing part goes on the worn part even when its materials have other names
        public static void PasteOntoWornParts(ChaControl chaCtrl, object card, Func<MaterialGroup, int, bool> fromCard, int? sourceCoordinate = null)
        {
            var controller = GetController(chaCtrl);
            if (!(card is MaterialEditorData source) || controller == null) return;

            var cardMaterials = new Dictionary<string, List<string>>();
            void Collect(ObjectType type, int slot, string material, int coordinate)
            {
                if ((type != ObjectType.Hair && type != ObjectType.Clothing) || !FromCoordinate(type, coordinate, sourceCoordinate)) return;
                var key = PartKey(type, slot);
                if (!cardMaterials.TryGetValue(key, out var list)) cardMaterials[key] = list = new List<string>();
                if (!list.Contains(material)) list.Add(material);
            }
            foreach (var x in source.Shaders) Collect(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex);
            foreach (var x in source.Textures) Collect(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex);
            foreach (var x in source.Floats) Collect(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex);
            foreach (var x in source.Colors) Collect(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex);
            foreach (var x in source.Keywords) Collect(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex);
            foreach (var extra in source.Extra.Values)
                foreach (var x in extra) Collect(EntryType(x), EntrySlot(x), EntryMaterial(x), EntryCoordinate(x));

            var now = PartMaterialNames(controller, chaCtrl);
            var renames = new Dictionary<string, string>();
            foreach (var part in cardMaterials)
            {
                var separator = part.Key.IndexOf('/');
                var type = (ObjectType)int.Parse(part.Key.Substring(0, separator));
                var slot = int.Parse(part.Key.Substring(separator + 1));
                if (!fromCard(PartGroup(type), slot) || !now.TryGetValue(part.Key, out var current)) continue;
                var worn = current.Distinct().ToList();
                var unmatched = part.Value.Where(x => !worn.Contains(x)).ToList();
                var free = worn.Where(x => !part.Value.Contains(x)).ToList();
                for (var i = 0; i < unmatched.Count && i < free.Count; i++) renames[MaterialKey(type, slot, unmatched[i])] = free[i];
            }
            Diag.Log($"ME card data pasted on worn parts: {string.Join(", ", renames.Select(x => x.Key + " -> " + x.Value).ToArray())}");
            ApplyRenamed(controller, source, renames, (type, coordinate) => FromCoordinate(type, coordinate, sourceCoordinate));
        }

        // Entries named in renames: the old names leave the worn outfit, the source's entries come back under the new names
        private static void ApplyRenamed(MaterialEditorCharaController controller, MaterialEditorData source, Dictionary<string, string> renames, Func<ObjectType, int, bool> fromSource)
        {
            if (renames.Count == 0) return;
            bool Renamed(ObjectType type, int slot, string material) => renames.ContainsKey(MaterialKey(type, slot, material));
            bool Worn(ObjectType type, int slot, string material, int coordinate) => Renamed(type, slot, material) && IsCurrentOutfit(controller, type, coordinate);
            bool Taken(ObjectType type, int slot, string material, int coordinate) => Renamed(type, slot, material) && fromSource(type, coordinate);

            var traverse = Traverse.Create(controller);
            traverse.Field(ShaderListKey).GetValue<List<MaterialShader>>()?.RemoveAll(x => Worn(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex));
            traverse.Field(FloatListKey).GetValue<List<MaterialFloatProperty>>()?.RemoveAll(x => Worn(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex));
            traverse.Field(ColorListKey).GetValue<List<MaterialColorProperty>>()?.RemoveAll(x => Worn(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex));
            traverse.Field(KeywordListKey).GetValue<List<MaterialKeywordProperty>>()?.RemoveAll(x => Worn(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex));
            traverse.Field(TextureListKey).GetValue<List<MaterialTextureProperty>>()?.RemoveAll(x => Worn(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex));

            var moved = new MaterialEditorData { TextureData = source.TextureData };
            moved.Shaders = Copy(source.Shaders.Where(x => Taken(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex)).ToList());
            moved.Floats = Copy(source.Floats.Where(x => Taken(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex)).ToList());
            moved.Colors = Copy(source.Colors.Where(x => Taken(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex)).ToList());
            moved.Keywords = Copy(source.Keywords.Where(x => Taken(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex)).ToList());
            moved.Textures = Copy(source.Textures.Where(x => Taken(x.ObjectType, x.Slot, x.MaterialName, x.CoordinateIndex)).ToList());
            foreach (var x in moved.Shaders) x.MaterialName = renames[MaterialKey(x.ObjectType, x.Slot, x.MaterialName)];
            foreach (var x in moved.Floats) x.MaterialName = renames[MaterialKey(x.ObjectType, x.Slot, x.MaterialName)];
            foreach (var x in moved.Colors) x.MaterialName = renames[MaterialKey(x.ObjectType, x.Slot, x.MaterialName)];
            foreach (var x in moved.Keywords) x.MaterialName = renames[MaterialKey(x.ObjectType, x.Slot, x.MaterialName)];
            foreach (var x in moved.Textures) x.MaterialName = renames[MaterialKey(x.ObjectType, x.Slot, x.MaterialName)];

            foreach (var key in ExtraLists.Keys)
            {
                RemoveWhere(ExtraList(controller, key), x => Worn(EntryType(x), EntrySlot(x), EntryMaterial(x), EntryCoordinate(x)));
                if (!source.Extra.TryGetValue(key, out var from)) continue;
                var copies = CopyExtra(key, from, x => Taken(EntryType(x), EntrySlot(x), EntryMaterial(x), EntryCoordinate(x)));
                foreach (var x in copies) SetEntry(x, "MaterialName", renames[MaterialKey(EntryType(x), EntrySlot(x), EntryMaterial(x))]);
                moved.Extra[key] = copies;
            }

            var targets = new HashSet<string>(renames.Select(x => x.Key.Substring(0, x.Key.LastIndexOf('/') + 1) + x.Value));
            Apply(controller, moved, (type, slot, material) => targets.Contains(MaterialKey(type, slot, material)), (type, slot, renderer) => false, false);
        }

        private static void RecordOriginalValues(MaterialEditorCharaController controller, MaterialEditorData snapshot, MaterialEditorData card)
        {
            foreach (var entry in card.Floats)
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property);
                if (snapshot.OriginalValues.ContainsKey(key)) continue;
                // A property the character changed itself already has its original
                var own = snapshot.Floats.FirstOrDefault(x => PropertyKey(x.ObjectType, x.Slot, x.MaterialName, x.Property) == key && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex));
                if (own != null)
                {
                    if (!string.IsNullOrEmpty(own.ValueOriginal)) snapshot.OriginalValues[key] = own.ValueOriginal;
                }
                else if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out _, out var material) && material.HasProperty("_" + entry.Property))
                    snapshot.OriginalValues[key] = material.GetFloat("_" + entry.Property).ToString(CultureInfo.InvariantCulture);
            }

            foreach (var entry in card.Colors)
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property);
                if (snapshot.OriginalValues.ContainsKey(key)) continue;
                var own = snapshot.Colors.FirstOrDefault(x => PropertyKey(x.ObjectType, x.Slot, x.MaterialName, x.Property) == key && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex));
                if (own != null) snapshot.OriginalValues[key] = own.ValueOriginal;
                else if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out _, out var material) && material.HasProperty("_" + entry.Property))
                    snapshot.OriginalValues[key] = material.GetColor("_" + entry.Property);
            }

            foreach (var entry in card.Keywords)
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property);
                if (snapshot.OriginalValues.ContainsKey(key)) continue;
                var own = snapshot.Keywords.FirstOrDefault(x => PropertyKey(x.ObjectType, x.Slot, x.MaterialName, x.Property) == key && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex));
                if (own != null) snapshot.OriginalValues[key] = own.ValueOriginal;
                else if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out _, out var material))
                    snapshot.OriginalValues[key] = material.IsKeywordEnabled("_" + entry.Property);
            }

            foreach (var entry in card.Textures)
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property);
                if (snapshot.OriginalValues.ContainsKey(key + TextureSuffix) || snapshot.OriginalValues.ContainsKey(key + OffsetSuffix) || snapshot.OriginalValues.ContainsKey(key + ScaleSuffix)) continue;
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out _, out var material) || !material.HasProperty("_" + entry.Property)) continue;
                var own = snapshot.Textures.FirstOrDefault(x => PropertyKey(x.ObjectType, x.Slot, x.MaterialName, x.Property) == key && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex));
                // ME keeps no original texture; the current one is it
                snapshot.OriginalValues[key + TextureSuffix] = material.GetTexture("_" + entry.Property);
                snapshot.OriginalValues[key + OffsetSuffix] = own?.OffsetOriginal ?? material.GetTextureOffset("_" + entry.Property);
                snapshot.OriginalValues[key + ScaleSuffix] = own?.ScaleOriginal ?? material.GetTextureScale("_" + entry.Property);
            }

            foreach (var entry in card.Shaders)
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, ShaderKey);
                if (snapshot.OriginalValues.ContainsKey(key)) continue;
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out _, out var material)) continue;
                var own = snapshot.Shaders.FirstOrDefault(x => PropertyKey(x.ObjectType, x.Slot, x.MaterialName, ShaderKey) == key && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex));
                snapshot.OriginalValues[key] = string.IsNullOrEmpty(own?.ShaderNameOriginal) ? FormatName(material.shader.name) : own.ShaderNameOriginal;
                snapshot.OriginalValues[key + RenderQueueSuffix] = own?.RenderQueueOriginal ?? material.renderQueue;
            }

            if (card.Extra.TryGetValue(VectorListKey, out var cardVectors))
            {
                snapshot.Extra.TryGetValue(VectorListKey, out var ownVectors);
                foreach (var entry in cardVectors)
                {
                    var key = PropertyKey(EntryType(entry), EntrySlot(entry), EntryMaterial(entry), EntryProperty(entry));
                    if (snapshot.OriginalValues.ContainsKey(key)) continue;
                    var own = ownVectors?.Cast<object>().FirstOrDefault(x => PropertyKey(EntryType(x), EntrySlot(x), EntryMaterial(x), EntryProperty(x)) == key && IsCurrentOutfit(controller, EntryType(x), EntryCoordinate(x)));
                    if (own != null) snapshot.OriginalValues[key] = Traverse.Create(own).Field("ValueOriginal").GetValue();
                    else if (TryFindMaterial(controller, EntryType(entry), EntrySlot(entry), EntryMaterial(entry), out _, out var material) && material.HasProperty("_" + EntryProperty(entry)))
                        snapshot.OriginalValues[key] = material.GetVector("_" + EntryProperty(entry));
                }
            }

            foreach (var entry in card.Renderers)
            {
                var key = RendererKey(entry.ObjectType, entry.Slot, entry.RendererName, entry.Property);
                if (snapshot.OriginalValues.ContainsKey(key)) continue;
                if (!TryFindRenderer(controller, entry.ObjectType, entry.Slot, entry.RendererName, out _, out var renderer)) continue;
                var own = snapshot.Renderers.FirstOrDefault(x => RendererKey(x.ObjectType, x.Slot, x.RendererName, x.Property) == key && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex));
                var value = string.IsNullOrEmpty(own?.ValueOriginal) ? RendererValue(renderer, entry.Property) : own.ValueOriginal;
                if (!string.IsNullOrEmpty(value)) snapshot.OriginalValues[key] = value;
            }
        }

        private const string TextureSuffix = "#texture";
        private const string OffsetSuffix = "#offset";
        private const string ScaleSuffix = "#scale";
        private const string ShaderKey = "#shader";
        private const string RenderQueueSuffix = "#queue";

        private static string PropertyKey(ObjectType type, int slot, string material, string property) => $"{(int)type}/{slot}/{material}/{property}";

        private static string RendererKey(ObjectType type, int slot, string renderer, MaterialAPI.RendererProperties property) => $"{(int)type}/{slot}/{renderer}/#renderer{(int)property}";

        private static string RendererValue(Renderer renderer, MaterialAPI.RendererProperties property)
        {
            switch (property)
            {
                case MaterialAPI.RendererProperties.Enabled: return renderer.enabled ? "1" : "0";
                case MaterialAPI.RendererProperties.ReceiveShadows: return renderer.receiveShadows ? "1" : "0";
                case MaterialAPI.RendererProperties.ShadowCastingMode: return ((int)renderer.shadowCastingMode).ToString();
                case MaterialAPI.RendererProperties.UpdateWhenOffscreen: return renderer is SkinnedMeshRenderer skinned && skinned.updateWhenOffscreen ? "1" : "0";
                default: return null;
            }
        }

        private static IList DeserializeList(Type listType, byte[] bytes) => DeserializeMethod?.MakeGenericMethod(listType).Invoke(null, new object[] { bytes }) as IList;

        // Deep copy of the entries of an ME 5.0 list that match
        private static IList CopyExtra(string key, IList list, Func<object, bool> match = null)
        {
            var type = ExtraLists[key];
            var subset = (IList)Activator.CreateInstance(type);
            if (list != null)
                foreach (var x in list)
                    if (match == null || match(x)) subset.Add(x);
            try
            {
                var bytes = (byte[])SerializeMethod.MakeGenericMethod(type).Invoke(null, new object[] { subset });
                return DeserializeList(type, bytes) ?? subset;
            }
            catch (Exception ex)
            {
                Diag.Log($"ME {key} copied without cloning: {ex.Message}");
                return subset;
            }
        }

        private static IList ExtraList(MaterialEditorCharaController controller, string key) => Traverse.Create(controller).Field(key).GetValue() as IList;

        private static void RemoveWhere(IList list, Func<object, bool> match)
        {
            if (list == null) return;
            for (var i = list.Count - 1; i >= 0; i--)
                if (match(list[i])) list.RemoveAt(i);
        }

        private static ObjectType EntryType(object x) => (ObjectType)Traverse.Create(x).Field("ObjectType").GetValue();
        private static int EntrySlot(object x) => Traverse.Create(x).Field("Slot").GetValue<int>();
        private static string EntryMaterial(object x) => Traverse.Create(x).Field("MaterialName").GetValue<string>();
        private static int EntryCoordinate(object x) => Traverse.Create(x).Field("CoordinateIndex").GetValue<int>();
        private static string EntryProperty(object x) => Traverse.Create(x).Field("Property").GetValue<string>();
        private static void SetEntry(object x, string field, object value) => Traverse.Create(x).Field(field).SetValue(value);

        // Texture ids are per character
        private static void FixCubemapId(object x, MaterialEditorData source, MaterialEditorCharaController controller)
        {
            var id = Traverse.Create(x).Field("TexID").GetValue() as int?;
            if (!id.HasValue) return;
            var setTexture = AccessTools.Method(typeof(MaterialEditorCharaController), "SetAndGetTextureID");
            SetEntry(x, "TexID", source.TextureData.TryGetValue(id.Value, out var bytes) && bytes != null && setTexture != null
                ? (int?)(int)setTexture.Invoke(controller, new object[] { bytes })
                : null);
        }

        private static void ApplyExtra(MaterialEditorCharaController controller, MaterialEditorData source, Func<ObjectType, int, string, bool> materialFilter, int? sourceCoordinate)
        {
            bool Wanted(object x) => materialFilter(EntryType(x), EntrySlot(x), EntryMaterial(x)) && FromCoordinate(EntryType(x), EntryCoordinate(x), sourceCoordinate);

            if (SetVectorMethod != null && source.Extra.TryGetValue(VectorListKey, out var vectors))
            {
                foreach (var entry in vectors.Cast<object>().Where(Wanted).ToList())
                {
                    if (!TryFindMaterial(controller, EntryType(entry), EntrySlot(entry), EntryMaterial(entry), out var go, out var material) || !material.HasProperty("_" + EntryProperty(entry))) continue;
                    SetVectorMethod.Invoke(controller, new object[] { EntrySlot(entry), EntryType(entry), material, EntryProperty(entry), Traverse.Create(entry).Field("Value").GetValue(), go, true });
                }
            }

            if (SetCubemapMethod != null && source.Extra.TryGetValue(CubemapListKey, out var cubemaps))
            {
                foreach (var entry in cubemaps.Cast<object>().Where(Wanted).ToList())
                {
                    var id = Traverse.Create(entry).Field("TexID").GetValue() as int?;
                    if (!id.HasValue || !source.TextureData.TryGetValue(id.Value, out var bytes) || bytes == null) continue;
                    if (!TryFindMaterial(controller, EntryType(entry), EntrySlot(entry), EntryMaterial(entry), out var go, out var material)) continue;
                    SetCubemapMethod.Invoke(controller, new object[] { EntrySlot(entry), EntryType(entry), material, EntryProperty(entry), bytes, go });
                }
            }
        }

        private static void RemoveExtra(MaterialEditorCharaController controller, Func<ObjectType, int, string, bool> materialFilter, Dictionary<string, object> originalValues)
        {
            bool Worn(object x) => materialFilter(EntryType(x), EntrySlot(x), EntryMaterial(x)) && IsCurrentOutfit(controller, EntryType(x), EntryCoordinate(x));

            var vectors = ExtraLists.ContainsKey(VectorListKey) ? ExtraList(controller, VectorListKey) : null;
            if (vectors != null && RemoveVectorMethod != null)
            {
                foreach (var entry in vectors.Cast<object>().Where(Worn).ToList())
                {
                    if (originalValues.TryGetValue(PropertyKey(EntryType(entry), EntrySlot(entry), EntryMaterial(entry), EntryProperty(entry)), out var original))
                        SetEntry(entry, "ValueOriginal", original);
                    if (TryFindMaterial(controller, EntryType(entry), EntrySlot(entry), EntryMaterial(entry), out var go, out var material))
                        RemoveVectorMethod.Invoke(controller, new object[] { EntrySlot(entry), EntryType(entry), material, EntryProperty(entry), go, true });
                    vectors.Remove(entry);
                }
            }

            var cubemaps = ExtraLists.ContainsKey(CubemapListKey) ? ExtraList(controller, CubemapListKey) : null;
            if (cubemaps != null && RemoveCubemapMethod != null)
            {
                foreach (var entry in cubemaps.Cast<object>().Where(Worn).ToList())
                {
                    if (TryFindMaterial(controller, EntryType(entry), EntrySlot(entry), EntryMaterial(entry), out var go, out var material))
                        RemoveCubemapMethod.Invoke(controller, new object[] { EntrySlot(entry), EntryType(entry), material, EntryProperty(entry), go, true });
                    cubemaps.Remove(entry);
                }
            }
        }

        private static List<T> Copy<T>(List<T> list)
        {
            return list == null ? new List<T>() : MessagePackSerializer.Deserialize<List<T>>(MessagePackSerializer.Serialize(list));
        }

        private static MaterialEditorCharaController GetController(ChaControl chaCtrl) => chaCtrl == null ? null : chaCtrl.GetComponent<MaterialEditorCharaController>();

        public static void ApplyGroups(ChaControl chaCtrl, object source, MaterialGroup groups, bool clean, int? sourceCoordinate = null)
        {
            if (groups == MaterialGroup.None) return;
            var controller = GetController(chaCtrl);
            if (controller == null) return;

            var characterGroups = CollectCharacterGroups(chaCtrl, out var rendererGroups);

            bool Matches(ObjectType type, string name, Dictionary<string, MaterialGroup> lookup)
            {
                switch (type)
                {
                    case ObjectType.Character:
                        if (!lookup.TryGetValue(name, out var group)) group = GuessCharacterGroup(name);
                        return (groups & group) != 0;
                    case ObjectType.Hair:
                        return (groups & MaterialGroup.Hair) != 0;
                    case ObjectType.Clothing:
                        return (groups & MaterialGroup.Clothes) != 0;
                    default:
                        return false;
                }
            }

            Apply(controller, source as MaterialEditorData ?? new MaterialEditorData(),
                (type, slot, material) => Matches(type, material, characterGroups),
                (type, slot, renderer) => Matches(type, renderer, rendererGroups),
                clean, sourceCoordinate);
        }

        public static void ApplyAccessorySlots(ChaControl chaCtrl, IDictionary<int, object> sourcePerSlot, int? sourceCoordinate = null) =>
            ApplySlots(chaCtrl, ObjectType.Accessory, sourcePerSlot, sourceCoordinate);

        public static void ApplyClothingSlots(ChaControl chaCtrl, IDictionary<int, object> sourcePerSlot, int? sourceCoordinate = null) =>
            ApplySlots(chaCtrl, ObjectType.Clothing, sourcePerSlot, sourceCoordinate);

        public static void ApplyHairSlots(ChaControl chaCtrl, IDictionary<int, object> sourcePerSlot) =>
            ApplySlots(chaCtrl, ObjectType.Hair, sourcePerSlot, null);

        private static void ApplySlots(ChaControl chaCtrl, ObjectType objectType, IDictionary<int, object> sourcePerSlot, int? sourceCoordinate)
        {
            var controller = GetController(chaCtrl);
            if (controller == null) return;

            foreach (var bySource in sourcePerSlot.GroupBy(x => x.Value))
            {
                var slots = new HashSet<int>(bySource.Select(x => x.Key));
                Apply(controller, bySource.Key as MaterialEditorData ?? new MaterialEditorData(),
                    (type, slot, material) => type == objectType && slots.Contains(slot),
                    (type, slot, renderer) => type == objectType && slots.Contains(slot),
                    true, sourceCoordinate);
            }
        }

        // ME API only works on the worn outfit, so lists are edited directly
        public static void CopyOutfit(ChaControl chaCtrl, object sourceObject, int? sourceCoordinate, int targetCoordinate, Func<ObjectType, int, bool> include = null)
        {
            var controller = GetController(chaCtrl);
            if (controller == null) return;
            var source = sourceObject as MaterialEditorData ?? new MaterialEditorData();
            var traverse = Traverse.Create(controller);
            var setTexture = AccessTools.Method(typeof(MaterialEditorCharaController), "SetAndGetTextureID");

            bool Included(ObjectType type, int slot) => PerOutfit(type) && (include == null || include(type, slot));
            bool FromSource(ObjectType type, int coordinate) => !sourceCoordinate.HasValue || coordinate == sourceCoordinate.Value;

            void Replace<T>(string key, List<T> from, Func<T, ObjectType> type, Func<T, int> slot, Func<T, int> coordinate, Action<T, int> setCoordinate, Action<T> fix = null)
            {
                var list = traverse.Field(key).GetValue<List<T>>();
                if (list == null) return;
                list.RemoveAll(x => Included(type(x), slot(x)) && coordinate(x) == targetCoordinate);
                foreach (var entry in Copy(from.Where(x => Included(type(x), slot(x)) && FromSource(type(x), coordinate(x))).ToList()))
                {
                    setCoordinate(entry, targetCoordinate);
                    fix?.Invoke(entry);
                    list.Add(entry);
                }
            }

            Replace(ShaderListKey, source.Shaders, x => x.ObjectType, x => x.Slot, x => x.CoordinateIndex, (x, c) => x.CoordinateIndex = c);
            Replace(FloatListKey, source.Floats, x => x.ObjectType, x => x.Slot, x => x.CoordinateIndex, (x, c) => x.CoordinateIndex = c);
            Replace(ColorListKey, source.Colors, x => x.ObjectType, x => x.Slot, x => x.CoordinateIndex, (x, c) => x.CoordinateIndex = c);
            Replace(KeywordListKey, source.Keywords, x => x.ObjectType, x => x.Slot, x => x.CoordinateIndex, (x, c) => x.CoordinateIndex = c);
            Replace(RendererListKey, source.Renderers, x => x.ObjectType, x => x.Slot, x => x.CoordinateIndex, (x, c) => x.CoordinateIndex = c);
            // Texture ids are per character
            Replace(TextureListKey, source.Textures, x => x.ObjectType, x => x.Slot, x => x.CoordinateIndex, (x, c) => x.CoordinateIndex = c, x =>
            {
                if (!x.TexID.HasValue) return;
                x.TexID = source.TextureData.TryGetValue(x.TexID.Value, out var bytes) && bytes != null && setTexture != null
                    ? (int?)(int)setTexture.Invoke(controller, new object[] { bytes })
                    : null;
            });

            foreach (var key in ExtraLists.Keys)
            {
                var list = ExtraList(controller, key);
                if (list == null) continue;
                RemoveWhere(list, x => Included(EntryType(x), EntrySlot(x)) && EntryCoordinate(x) == targetCoordinate);
                if (!source.Extra.TryGetValue(key, out var from)) continue;
                foreach (var entry in CopyExtra(key, from, x => Included(EntryType(x), EntrySlot(x)) && FromSource(EntryType(x), EntryCoordinate(x))))
                {
                    SetEntry(entry, "CoordinateIndex", targetCoordinate);
                    if (key == CubemapListKey) FixCubemapId(entry, source, controller);
                    list.Add(entry);
                }
            }
        }

        private sealed class KeepContext
        {
            public ChaControl Target;
            public MaterialEditorData Snapshot;
            public Func<MaterialGroup, int, bool> FromCard;
        }

        private static KeepContext _keep;

        // Swapped in after ME reads the card, before it applies anything; slot is -1 for body, head and eyes
        public static void KeepOnLoad(ChaControl chaCtrl, object snapshot, Func<MaterialGroup, int, bool> fromCard)
        {
            _keep = new KeepContext { Target = chaCtrl, Snapshot = snapshot as MaterialEditorData ?? new MaterialEditorData(), FromCard = fromCard };
        }

        public static void ClearKeep() => _keep = null;

        public static void Patch(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(MaterialEditorCharaController), "LoadCharacterExtSaveData");
            if (method == null)
            {
                AdvancedLoaderPlugin.Logger.LogWarning("Material Editor's character loading was not found, unticked Material Editor groups may change on Face/Body loads");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(MaterialEditorBridge), nameof(LoadCharacterExtSaveDataPostfix)));
            var coordinate = AccessTools.Method(typeof(MaterialEditorCharaController), "LoadCoordinateExtSaveData");
            if (coordinate == null) AdvancedLoaderPlugin.Logger.LogWarning("Material Editor's outfit loading was not found, unticked Material Editor groups may change on outfit loads");
            else harmony.Patch(coordinate, postfix: new HarmonyMethod(typeof(MaterialEditorBridge), nameof(LoadCharacterExtSaveDataPostfix)));
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogHair(ChaControl chaCtrl, string when)
        {
            var controller = GetController(chaCtrl);
            if (controller == null) return;
            var traverse = Traverse.Create(controller);
            int Count<T>(string key, Func<T, ObjectType> type) => traverse.Field(key).GetValue<List<T>>()?.Count(x => type(x) == ObjectType.Hair) ?? -1;
            var shaders = traverse.Field(ShaderListKey).GetValue<List<MaterialShader>>()?.Where(x => x.ObjectType == ObjectType.Hair)
                .Select(x => $"{x.Slot}:{x.MaterialName}:{x.ShaderName}").ToArray() ?? new string[0];
            var materials = PartMaterialNames(controller, chaCtrl).Where(x => x.Key.StartsWith((int)ObjectType.Hair + "/")).Select(x => x.Key + "=" + string.Join("|", x.Value.ToArray())).ToArray();
            Diag.Log($"ME hair {when}: [{string.Join(", ", shaders)}] on [{string.Join(", ", materials)}]");
            Diag.Log($"ME hair {when}: shaders {Count<MaterialShader>(ShaderListKey, x => x.ObjectType)}, floats {Count<MaterialFloatProperty>(FloatListKey, x => x.ObjectType)}, " +
                     $"colors {Count<MaterialColorProperty>(ColorListKey, x => x.ObjectType)}, textures {Count<MaterialTextureProperty>(TextureListKey, x => x.ObjectType)}, " +
                     $"renderers {Count<RendererProperty>(RendererListKey, x => x.ObjectType)}");
        }

        private static void LoadCharacterExtSaveDataPostfix(MaterialEditorCharaController __instance)
        {
            var keep = _keep;
            Diag.Log($"ME load on {__instance.ChaControl?.name}, keep {(keep == null ? "none" : keep.Target == __instance.ChaControl ? "on" : "other character")}");
            LogHair(__instance.ChaControl, "loaded by ME");
            if (keep == null || __instance.ChaControl != keep.Target) return;
            try
            {
                var characterGroups = CollectCharacterGroups(keep.Target, out var rendererGroups);
                var traverse = Traverse.Create(__instance);
                var setTexture = AccessTools.Method(typeof(MaterialEditorCharaController), "SetAndGetTextureID");

                bool Kept(ObjectType type, int slot, string name, Dictionary<string, MaterialGroup> lookup)
                {
                    switch (type)
                    {
                        case ObjectType.Character:
                            if (!lookup.TryGetValue(name, out var group)) group = GuessCharacterGroup(name);
                            return !keep.FromCard(group, -1);
                        case ObjectType.Clothing:
                        case ObjectType.Accessory:
                        case ObjectType.Hair:
                            return !keep.FromCard(PartGroup(type), slot);
                        default:
                            return false;
                    }
                }

                void Swap<T>(string key, List<T> from, Func<T, ObjectType> type, Func<T, int> slot, Func<T, string> name, Dictionary<string, MaterialGroup> lookup, Action<T> fix = null)
                {
                    var list = traverse.Field(key).GetValue<List<T>>();
                    if (list == null) return;
                    list.RemoveAll(x => Kept(type(x), slot(x), name(x), lookup));
                    foreach (var entry in Copy(from.Where(x => Kept(type(x), slot(x), name(x), lookup)).ToList()))
                    {
                        fix?.Invoke(entry);
                        list.Add(entry);
                    }
                }

                var source = keep.Snapshot;
                Swap(ShaderListKey, source.Shaders, x => x.ObjectType, x => x.Slot, x => x.MaterialName, characterGroups);
                Swap(FloatListKey, source.Floats, x => x.ObjectType, x => x.Slot, x => x.MaterialName, characterGroups);
                Swap(ColorListKey, source.Colors, x => x.ObjectType, x => x.Slot, x => x.MaterialName, characterGroups);
                Swap(KeywordListKey, source.Keywords, x => x.ObjectType, x => x.Slot, x => x.MaterialName, characterGroups);
                Swap(RendererListKey, source.Renderers, x => x.ObjectType, x => x.Slot, x => x.RendererName, rendererGroups);
                // ME may have dropped the old textures
                Swap(TextureListKey, source.Textures, x => x.ObjectType, x => x.Slot, x => x.MaterialName, characterGroups, x =>
                {
                    if (!x.TexID.HasValue) return;
                    x.TexID = source.TextureData.TryGetValue(x.TexID.Value, out var bytes) && bytes != null && setTexture != null
                        ? (int?)(int)setTexture.Invoke(__instance, new object[] { bytes })
                        : null;
                });
                foreach (var key in ExtraLists.Keys)
                {
                    var list = ExtraList(__instance, key);
                    if (list == null) continue;
                    bool KeptEntry(object x) => Kept(EntryType(x), EntrySlot(x), EntryMaterial(x), characterGroups);
                    RemoveWhere(list, KeptEntry);
                    if (!source.Extra.TryGetValue(key, out var from)) continue;
                    foreach (var entry in CopyExtra(key, from, KeptEntry))
                    {
                        if (key == CubemapListKey) FixCubemapId(entry, source, __instance);
                        list.Add(entry);
                    }
                }
                LogHair(keep.Target, "after keeping");
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to keep current Material Editor data: {ex}");
            }
        }

        public static object RemapAccessorySlot(object data, int fromSlot, int toSlot)
        {
            var source = data as MaterialEditorData ?? new MaterialEditorData();
            bool Pick(ObjectType type, int slot) => type == ObjectType.Accessory && slot == fromSlot;

            var result = new MaterialEditorData { TextureData = source.TextureData };
            result.Shaders = Copy(source.Shaders.Where(x => Pick(x.ObjectType, x.Slot)).ToList());
            result.Floats = Copy(source.Floats.Where(x => Pick(x.ObjectType, x.Slot)).ToList());
            result.Colors = Copy(source.Colors.Where(x => Pick(x.ObjectType, x.Slot)).ToList());
            result.Keywords = Copy(source.Keywords.Where(x => Pick(x.ObjectType, x.Slot)).ToList());
            result.Textures = Copy(source.Textures.Where(x => Pick(x.ObjectType, x.Slot)).ToList());
            result.Renderers = Copy(source.Renderers.Where(x => Pick(x.ObjectType, x.Slot)).ToList());

            foreach (var x in result.Shaders) x.Slot = toSlot;
            foreach (var x in result.Floats) x.Slot = toSlot;
            foreach (var x in result.Colors) x.Slot = toSlot;
            foreach (var x in result.Keywords) x.Slot = toSlot;
            foreach (var x in result.Textures) x.Slot = toSlot;
            foreach (var x in result.Renderers) x.Slot = toSlot;
            foreach (var key in ExtraLists.Keys)
            {
                if (!source.Extra.TryGetValue(key, out var from)) continue;
                var copies = CopyExtra(key, from, x => Pick(EntryType(x), EntrySlot(x)));
                foreach (var x in copies) SetEntry(x, "Slot", toSlot);
                result.Extra[key] = copies;
            }
            return result;
        }

        private static bool PerOutfit(ObjectType type) => type == ObjectType.Clothing || type == ObjectType.Accessory;

        private static bool FromCoordinate(ObjectType type, int coordinateIndex, int? sourceCoordinate) =>
            !sourceCoordinate.HasValue || !PerOutfit(type) || coordinateIndex == sourceCoordinate.Value;

        private static bool IsCurrentOutfit(MaterialEditorCharaController controller, ObjectType type, int coordinateIndex) =>
            !PerOutfit(type) || coordinateIndex == controller.CurrentCoordinateIndex;

        private static void Apply(MaterialEditorCharaController controller, MaterialEditorData source,
            Func<ObjectType, int, string, bool> materialFilter, Func<ObjectType, int, string, bool> rendererFilter, bool clean, int? sourceCoordinate = null)
        {
            if (clean) RemoveCurrent(controller, materialFilter, rendererFilter, source.OriginalValues);

            foreach (var entry in source.Shaders.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && FromCoordinate(x.ObjectType, x.CoordinateIndex, sourceCoordinate)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                if (!string.IsNullOrEmpty(entry.ShaderName)) controller.SetMaterialShader(entry.Slot, entry.ObjectType, material, entry.ShaderName, go);
                if (entry.RenderQueue.HasValue) controller.SetMaterialShaderRenderQueue(entry.Slot, entry.ObjectType, material, entry.RenderQueue.Value, go);
            }

            foreach (var entry in source.Textures.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && FromCoordinate(x.ObjectType, x.CoordinateIndex, sourceCoordinate)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                if (entry.TexID.HasValue && source.TextureData.TryGetValue(entry.TexID.Value, out var bytes) && bytes != null)
                    controller.SetMaterialTexture(entry.Slot, entry.ObjectType, material, entry.Property, bytes, go);
                if (entry.Offset.HasValue) controller.SetMaterialTextureOffset(entry.Slot, entry.ObjectType, material, entry.Property, entry.Offset.Value, go);
                if (entry.Scale.HasValue) controller.SetMaterialTextureScale(entry.Slot, entry.ObjectType, material, entry.Property, entry.Scale.Value, go);
            }

            foreach (var entry in source.Floats.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && FromCoordinate(x.ObjectType, x.CoordinateIndex, sourceCoordinate)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                if (float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    controller.SetMaterialFloatProperty(entry.Slot, entry.ObjectType, material, entry.Property, value, go);
            }

            foreach (var entry in source.Colors.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && FromCoordinate(x.ObjectType, x.CoordinateIndex, sourceCoordinate)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                controller.SetMaterialColorProperty(entry.Slot, entry.ObjectType, material, entry.Property, entry.Value, go);
            }

            foreach (var entry in source.Keywords.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && FromCoordinate(x.ObjectType, x.CoordinateIndex, sourceCoordinate)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                controller.SetMaterialKeywordProperty(entry.Slot, entry.ObjectType, material, entry.Property, entry.Value, go);
            }

            ApplyExtra(controller, source, materialFilter, sourceCoordinate);

            foreach (var entry in source.Renderers.Where(x => rendererFilter(x.ObjectType, x.Slot, x.RendererName) && FromCoordinate(x.ObjectType, x.CoordinateIndex, sourceCoordinate)))
            {
                if (!TryFindRenderer(controller, entry.ObjectType, entry.Slot, entry.RendererName, out var go, out var renderer)) continue;
                controller.SetRendererProperty(entry.Slot, entry.ObjectType, renderer, entry.Property, entry.Value, go);
            }
        }

        private static void RemoveCurrent(MaterialEditorCharaController controller,
            Func<ObjectType, int, string, bool> materialFilter, Func<ObjectType, int, string, bool> rendererFilter, Dictionary<string, object> originalValues)
        {
            var traverse = Traverse.Create(controller);

            var floats = traverse.Field(FloatListKey).GetValue<List<MaterialFloatProperty>>();
            foreach (var entry in floats.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex)).ToList())
            {
                if (originalValues.TryGetValue(PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property), out var original))
                    entry.ValueOriginal = (string)original;
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                    controller.RemoveMaterialFloatProperty(entry.Slot, entry.ObjectType, material, entry.Property, go);
                floats.Remove(entry);
            }

            var colors = traverse.Field(ColorListKey).GetValue<List<MaterialColorProperty>>();
            foreach (var entry in colors.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex)).ToList())
            {
                if (originalValues.TryGetValue(PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property), out var original))
                    entry.ValueOriginal = (Color)original;
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                    controller.RemoveMaterialColorProperty(entry.Slot, entry.ObjectType, material, entry.Property, go);
                colors.Remove(entry);
            }

            var keywords = traverse.Field(KeywordListKey).GetValue<List<MaterialKeywordProperty>>();
            foreach (var entry in keywords.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex)).ToList())
            {
                if (originalValues.TryGetValue(PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property), out var original))
                    entry.ValueOriginal = (bool)original;
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                    controller.RemoveMaterialKeywordProperty(entry.Slot, entry.ObjectType, material, entry.Property, go);
                keywords.Remove(entry);
            }

            var textures = traverse.Field(TextureListKey).GetValue<List<MaterialTextureProperty>>();
            foreach (var entry in textures.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex)).ToList())
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, entry.Property);
                if (originalValues.TryGetValue(key + OffsetSuffix, out var offset)) entry.OffsetOriginal = (Vector2)offset;
                if (originalValues.TryGetValue(key + ScaleSuffix, out var scale)) entry.ScaleOriginal = (Vector2)scale;
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                {
                    if (entry.TexID.HasValue)
                    {
                        controller.RemoveMaterialTexture(entry.Slot, entry.ObjectType, material, entry.Property, go, false);
                        // ME leaves a removed texture until reload; skip destroyed ones
                        if (originalValues.TryGetValue(key + TextureSuffix, out var texture) && (texture == null || (Texture)texture != null))
                            material.SetTexture("_" + entry.Property, (Texture)texture);
                        else Diag.Log($"ME texture left on the material, no original known: {key}");
                    }
                    if (entry.Offset.HasValue) controller.RemoveMaterialTextureOffset(entry.Slot, entry.ObjectType, material, entry.Property, go);
                    if (entry.Scale.HasValue) controller.RemoveMaterialTextureScale(entry.Slot, entry.ObjectType, material, entry.Property, go);
                }
                textures.Remove(entry);
            }

            RemoveExtra(controller, materialFilter, originalValues);

            // Shaders last, after the values that belong to them are back to their originals
            var shaders = traverse.Field(ShaderListKey).GetValue<List<MaterialShader>>();
            foreach (var entry in shaders.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName) && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex)).ToList())
            {
                var key = PropertyKey(entry.ObjectType, entry.Slot, entry.MaterialName, ShaderKey);
                if (originalValues.TryGetValue(key, out var shader)) entry.ShaderNameOriginal = (string)shader;
                if (originalValues.TryGetValue(key + RenderQueueSuffix, out var renderQueue)) entry.RenderQueueOriginal = (int)renderQueue;
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                {
                    if (entry.RenderQueue.HasValue) controller.RemoveMaterialShaderRenderQueue(entry.Slot, entry.ObjectType, material, go);
                    if (!string.IsNullOrEmpty(entry.ShaderName)) controller.RemoveMaterialShader(entry.Slot, entry.ObjectType, material, go);
                }
                shaders.Remove(entry);
            }

            var renderers = traverse.Field(RendererListKey).GetValue<List<RendererProperty>>();
            foreach (var entry in renderers.Where(x => rendererFilter(x.ObjectType, x.Slot, x.RendererName) && IsCurrentOutfit(controller, x.ObjectType, x.CoordinateIndex)).ToList())
            {
                if (originalValues.TryGetValue(RendererKey(entry.ObjectType, entry.Slot, entry.RendererName, entry.Property), out var original))
                    entry.ValueOriginal = (string)original;
                if (TryFindRenderer(controller, entry.ObjectType, entry.Slot, entry.RendererName, out var go, out var renderer))
                    controller.RemoveRendererProperty(entry.Slot, entry.ObjectType, renderer, entry.Property, go);
                renderers.Remove(entry);
            }
        }

        private static GameObject FindGameObject(MaterialEditorCharaController controller, ObjectType type, int slot)
        {
            // ME doesn't check the slot range, and a card can name slots this character doesn't have
            try
            {
                return FindGameObjectMethod?.Invoke(controller, new object[] { type, slot }) as GameObject;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private static bool TryFindMaterial(MaterialEditorCharaController controller, ObjectType type, int slot, string materialName, out GameObject go, out Material material)
        {
            material = null;
            go = FindGameObject(controller, type, slot);
            if (go == null) return false;
            foreach (var renderer in MaterialAPI.GetRendererList(go))
            {
                foreach (var candidate in MaterialAPI.GetMaterials(go, renderer))
                {
                    if (FormatName(candidate?.name) != materialName) continue;
                    material = candidate;
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindRenderer(MaterialEditorCharaController controller, ObjectType type, int slot, string rendererName, out GameObject go, out Renderer renderer)
        {
            go = FindGameObject(controller, type, slot);
            renderer = go == null ? null : MaterialAPI.GetRendererList(go).FirstOrDefault(x => FormatName(x.name) == rendererName);
            return renderer != null;
        }

        // Same as Material Editor's NameFormatted extension
        private static string FormatName(string name) => name == null ? "" : name.Replace("(Instance)", "").Replace(" Instance", "").Trim();

        private static Dictionary<string, MaterialGroup> CollectCharacterGroups(ChaControl chaCtrl, out Dictionary<string, MaterialGroup> rendererGroups)
        {
            var materialGroups = new Dictionary<string, MaterialGroup>();
            rendererGroups = new Dictionary<string, MaterialGroup>();
            var head = chaCtrl.objHead == null ? null : chaCtrl.objHead.transform;

            foreach (var renderer in chaCtrl.GetComponentsInChildren<Renderer>(true))
            {
                if (IsPartRenderer(renderer)) continue;

                var rendererName = FormatName(renderer.name);
                var group = head != null && renderer.transform.IsChildOf(head)
                    ? (IsEyeName(rendererName) ? MaterialGroup.Eyes : MaterialGroup.Head)
                    : MaterialGroup.Body;

                if (!rendererGroups.ContainsKey(rendererName)) rendererGroups[rendererName] = group;
                foreach (var material in renderer.sharedMaterials)
                {
                    var materialName = FormatName(material?.name);
                    if (materialName.Length > 0 && !materialGroups.ContainsKey(materialName))
                        materialGroups[materialName] = group == MaterialGroup.Head && IsEyeName(materialName) ? MaterialGroup.Eyes : group;
                }
            }
            return materialGroups;
        }

        // Clothes, hair and accessories have their own ME data
        private static bool IsPartRenderer(Renderer renderer)
        {
#if HS2 || AI
            return renderer.GetComponentInParent<CmpClothes>() != null || renderer.GetComponentInParent<CmpHair>() != null || renderer.GetComponentInParent<CmpAccessory>() != null;
#elif KKS || KK
            return renderer.GetComponentInParent<ChaClothesComponent>() != null || renderer.GetComponentInParent<ChaCustomHairComponent>() != null || renderer.GetComponentInParent<ChaAccessoryComponent>() != null;
#endif
        }

        private static MaterialGroup GuessCharacterGroup(string name)
        {
            var lower = name.ToLowerInvariant();
            if (IsEyeName(lower)) return MaterialGroup.Eyes;
            if (lower.Contains("head") || lower.Contains("face") || lower.Contains("mayu") || lower.Contains("tooth") || lower.Contains("tang") || lower.Contains("mouth"))
                return MaterialGroup.Head;
            return MaterialGroup.Body;
        }

        private static bool IsEyeName(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("eye") || lower.Contains("hitomi") || lower.Contains("sirome") || lower.Contains("namida") || lower.Contains("matuge");
        }
    }
}
