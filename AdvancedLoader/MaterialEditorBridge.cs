using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AIChara;
using ExtensibleSaveFormat;
using HarmonyLib;
using KK_Plugins;
using KK_Plugins.MaterialEditor;
using MaterialEditorAPI;
using MessagePack;
using UnityEngine;
using static KK_Plugins.MaterialEditor.MaterialEditorCharaController;

namespace AdvancedLoader
{
    /// <summary>
    /// Kept apart from <see cref="MaterialEditorBridge"/> so checking for Material Editor never loads its types.
    /// </summary>
    internal static class MaterialEditorPresence
    {
        public const string GUID = "com.deathweasel.bepinex.materialeditor";

        /// <summary>Installed in a version this plugin works with.</summary>
        public static bool Available => Dependencies.MaterialEditor.IsOk;
    }

    /// <summary>
    /// Material Editor changes of a card or a character, in Material Editor's own save format.
    /// </summary>
    internal sealed class MaterialEditorData
    {
        public List<MaterialShader> Shaders = new List<MaterialShader>();
        public List<MaterialFloatProperty> Floats = new List<MaterialFloatProperty>();
        public List<MaterialColorProperty> Colors = new List<MaterialColorProperty>();
        public List<MaterialKeywordProperty> Keywords = new List<MaterialKeywordProperty>();
        public List<MaterialTextureProperty> Textures = new List<MaterialTextureProperty>();
        public List<RendererProperty> Renderers = new List<RendererProperty>();
        public Dictionary<int, byte[]> TextureData = new Dictionary<int, byte[]>();
    }

    /// <summary>
    /// Reads Material Editor data from cards and copies it onto the maker character through Material Editor's public API,
    /// so Material Editor's own UI, saving and "changed" markers stay correct.
    /// Only call into this class when <see cref="MaterialEditorPresence.Available"/> is true.
    /// Not copied yet: material copies (duplicated materials), material renames and projectors.
    /// </summary>
    internal static class MaterialEditorBridge
    {
        private const string ShaderListKey = "MaterialShaderList";
        private const string FloatListKey = "MaterialFloatPropertyList";
        private const string ColorListKey = "MaterialColorPropertyList";
        private const string KeywordListKey = "MaterialKeywordPropertyList";
        private const string TextureListKey = "MaterialTexturePropertyList";
        private const string RendererListKey = "RendererPropertyList";

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

        /// <summary>
        /// Copy of the character's current Material Editor changes, so they can be put back after a load replaced them.
        /// </summary>
        public static object Snapshot(ChaControl chaCtrl)
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
            var textures = traverse.Field("TextureDictionary").GetValue<Dictionary<int, TextureContainer>>();
            if (textures != null)
                foreach (var texture in textures)
                    data.TextureData[texture.Key] = texture.Value.Data;
            return data;
        }

        private static List<T> Copy<T>(List<T> list)
        {
            return list == null ? new List<T>() : MessagePackSerializer.Deserialize<List<T>>(MessagePackSerializer.Serialize(list));
        }

        private static MaterialEditorCharaController GetController(ChaControl chaCtrl) => chaCtrl == null ? null : chaCtrl.GetComponent<MaterialEditorCharaController>();

        /// <summary>
        /// Copy body, head, eye, hair or clothes material changes from the card.
        /// </summary>
        public static void ApplyGroups(ChaControl chaCtrl, object source, MaterialGroup groups, bool clean)
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
                clean);
        }

        /// <summary>
        /// Give each accessory slot the Material Editor changes from its source (the card, or the snapshot taken before the load).
        /// Always clean per slot, because leftovers from the accessory that was in the slot before are the problem being fixed.
        /// </summary>
        public static void ApplyAccessorySlots(ChaControl chaCtrl, IDictionary<int, object> sourcePerSlot) => ApplySlots(chaCtrl, ObjectType.Accessory, sourcePerSlot);

        /// <summary>
        /// Same for clothes pieces (slot = ChaFileDefine.ClothesKind): pieces that were kept get their previous data back.
        /// </summary>
        public static void ApplyClothingSlots(ChaControl chaCtrl, IDictionary<int, object> sourcePerSlot) => ApplySlots(chaCtrl, ObjectType.Clothing, sourcePerSlot);

        private static void ApplySlots(ChaControl chaCtrl, ObjectType objectType, IDictionary<int, object> sourcePerSlot)
        {
            var controller = GetController(chaCtrl);
            if (controller == null) return;

            foreach (var bySource in sourcePerSlot.GroupBy(x => x.Value))
            {
                var slots = new HashSet<int>(bySource.Select(x => x.Key));
                Apply(controller, bySource.Key as MaterialEditorData ?? new MaterialEditorData(),
                    (type, slot, material) => type == objectType && slots.Contains(slot),
                    (type, slot, renderer) => type == objectType && slots.Contains(slot),
                    true);
            }
        }

        /// <summary>
        /// The accessory entries of one card slot, renumbered to the character slot the accessory is loaded into.
        /// </summary>
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
            return result;
        }

        private static void Apply(MaterialEditorCharaController controller, MaterialEditorData source,
            Func<ObjectType, int, string, bool> materialFilter, Func<ObjectType, int, string, bool> rendererFilter, bool clean)
        {
            if (clean) RemoveCurrent(controller, materialFilter, rendererFilter);

            foreach (var entry in source.Shaders.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                if (!string.IsNullOrEmpty(entry.ShaderName)) controller.SetMaterialShader(entry.Slot, entry.ObjectType, material, entry.ShaderName, go);
                if (entry.RenderQueue.HasValue) controller.SetMaterialShaderRenderQueue(entry.Slot, entry.ObjectType, material, entry.RenderQueue.Value, go);
            }

            foreach (var entry in source.Textures.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                if (entry.TexID.HasValue && source.TextureData.TryGetValue(entry.TexID.Value, out var bytes) && bytes != null)
                    controller.SetMaterialTexture(entry.Slot, entry.ObjectType, material, entry.Property, bytes, go);
                if (entry.Offset.HasValue) controller.SetMaterialTextureOffset(entry.Slot, entry.ObjectType, material, entry.Property, entry.Offset.Value, go);
                if (entry.Scale.HasValue) controller.SetMaterialTextureScale(entry.Slot, entry.ObjectType, material, entry.Property, entry.Scale.Value, go);
            }

            foreach (var entry in source.Floats.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                if (float.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    controller.SetMaterialFloatProperty(entry.Slot, entry.ObjectType, material, entry.Property, value, go);
            }

            foreach (var entry in source.Colors.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                controller.SetMaterialColorProperty(entry.Slot, entry.ObjectType, material, entry.Property, entry.Value, go);
            }

            foreach (var entry in source.Keywords.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)))
            {
                if (!TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material)) continue;
                controller.SetMaterialKeywordProperty(entry.Slot, entry.ObjectType, material, entry.Property, entry.Value, go);
            }

            foreach (var entry in source.Renderers.Where(x => rendererFilter(x.ObjectType, x.Slot, x.RendererName)))
            {
                if (!TryFindRenderer(controller, entry.ObjectType, entry.Slot, entry.RendererName, out var go, out var renderer)) continue;
                controller.SetRendererProperty(entry.Slot, entry.ObjectType, renderer, entry.Property, entry.Value, go);
            }
        }

        /// <summary>
        /// Undo every Material Editor change in the filtered group: the original values are put back on the materials
        /// and the entries are dropped, so nothing from an earlier edit survives the copy.
        /// </summary>
        private static void RemoveCurrent(MaterialEditorCharaController controller,
            Func<ObjectType, int, string, bool> materialFilter, Func<ObjectType, int, string, bool> rendererFilter)
        {
            var traverse = Traverse.Create(controller);

            var floats = traverse.Field(FloatListKey).GetValue<List<MaterialFloatProperty>>();
            foreach (var entry in floats.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)).ToList())
            {
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                    controller.RemoveMaterialFloatProperty(entry.Slot, entry.ObjectType, material, entry.Property, go);
                floats.Remove(entry);
            }

            var colors = traverse.Field(ColorListKey).GetValue<List<MaterialColorProperty>>();
            foreach (var entry in colors.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)).ToList())
            {
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                    controller.RemoveMaterialColorProperty(entry.Slot, entry.ObjectType, material, entry.Property, go);
                colors.Remove(entry);
            }

            var keywords = traverse.Field(KeywordListKey).GetValue<List<MaterialKeywordProperty>>();
            foreach (var entry in keywords.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)).ToList())
            {
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                    controller.RemoveMaterialKeywordProperty(entry.Slot, entry.ObjectType, material, entry.Property, go);
                keywords.Remove(entry);
            }

            var textures = traverse.Field(TextureListKey).GetValue<List<MaterialTextureProperty>>();
            foreach (var entry in textures.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)).ToList())
            {
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                {
                    if (entry.TexID.HasValue) controller.RemoveMaterialTexture(entry.Slot, entry.ObjectType, material, entry.Property, go, false);
                    if (entry.Offset.HasValue) controller.RemoveMaterialTextureOffset(entry.Slot, entry.ObjectType, material, entry.Property, go);
                    if (entry.Scale.HasValue) controller.RemoveMaterialTextureScale(entry.Slot, entry.ObjectType, material, entry.Property, go);
                }
                textures.Remove(entry);
            }

            // Shaders last, after the values that belong to them are back to their originals
            var shaders = traverse.Field(ShaderListKey).GetValue<List<MaterialShader>>();
            foreach (var entry in shaders.Where(x => materialFilter(x.ObjectType, x.Slot, x.MaterialName)).ToList())
            {
                if (TryFindMaterial(controller, entry.ObjectType, entry.Slot, entry.MaterialName, out var go, out var material))
                {
                    if (entry.RenderQueue.HasValue) controller.RemoveMaterialShaderRenderQueue(entry.Slot, entry.ObjectType, material, go);
                    if (!string.IsNullOrEmpty(entry.ShaderName)) controller.RemoveMaterialShader(entry.Slot, entry.ObjectType, material, go);
                }
                shaders.Remove(entry);
            }

            var renderers = traverse.Field(RendererListKey).GetValue<List<RendererProperty>>();
            foreach (var entry in renderers.Where(x => rendererFilter(x.ObjectType, x.Slot, x.RendererName)).ToList())
            {
                if (TryFindRenderer(controller, entry.ObjectType, entry.Slot, entry.RendererName, out var go, out var renderer))
                    controller.RemoveRendererProperty(entry.Slot, entry.ObjectType, renderer, entry.Property, go);
                renderers.Remove(entry);
            }
        }

        private static GameObject FindGameObject(MaterialEditorCharaController controller, ObjectType type, int slot)
        {
            return FindGameObjectMethod?.Invoke(controller, new object[] { type, slot }) as GameObject;
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

        /// <summary>
        /// Body, head or eyes for every material and renderer of the character itself (not hair, clothes or accessories),
        /// decided by where the renderer sits in the character's hierarchy.
        /// </summary>
        private static Dictionary<string, MaterialGroup> CollectCharacterGroups(ChaControl chaCtrl, out Dictionary<string, MaterialGroup> rendererGroups)
        {
            var materialGroups = new Dictionary<string, MaterialGroup>();
            rendererGroups = new Dictionary<string, MaterialGroup>();
            var head = chaCtrl.objHead == null ? null : chaCtrl.objHead.transform;

            foreach (var renderer in chaCtrl.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<CmpClothes>() != null || renderer.GetComponentInParent<CmpHair>() != null || renderer.GetComponentInParent<CmpAccessory>() != null)
                    continue;

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

        /// <summary>
        /// Fallback for materials that are on the card but not on the current character.
        /// </summary>
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
