using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI.Maker;
using MessagePack;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal static class AacBridge
    {
        public const string GUID = "orange.spork.additionalaccessorycontrolsplugin";

        private static bool _resolved;
        private static Type _controllerType;
        private static Type _pluginType;
        private static Type _slotType;
        private static MethodInfo _copySlot;
        private static MethodInfo _plainSlot;

        public static bool Available
        {
            get
            {
                Resolve();
                return _controllerType != null;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!Chainloader.PluginInfos.ContainsKey(GUID)) return;
            _controllerType = AccessTools.TypeByName("AdditionalAccessoryControls.AdditionalAccessoryControlsController");
            _pluginType = AccessTools.TypeByName("AdditionalAccessoryControls.AdditionalAccessoryControlsPlugin");
            _slotType = AccessTools.TypeByName("AdditionalAccessoryControls.AdditionalAccessorySlotData");
            if (_slotType == null) return;
            _copySlot = AccessTools.Method(_slotType, "Copy");
            _plainSlot = AccessTools.Method(_slotType, "NonCharacterAccessorySlot");
        }

        private static object[] SlotData(ChaControl chaCtrl)
        {
            if (!Available) return null;
            var controller = chaCtrl.GetComponent(_controllerType);
            return controller == null ? null : Traverse.Create(controller).Property("SlotData").GetValue() as object[];
        }

        public static HashSet<int> ReadCardFlags(ChaFile file)
        {
            var flags = new HashSet<int>();
            var data = ExtendedSave.GetExtendedDataById(file, GUID);
            if (data == null || !data.data.TryGetValue("accessoryData", out var value) || !(value is byte[] bytes) || bytes.Length == 0) return flags;
            if (!(MessagePackSerializer.Deserialize<object>(bytes) is object[] slots)) return flags;
            foreach (var slot in slots.OfType<object[]>())
                if (slot.Length > 3 && slot[3] is bool flag && flag) flags.Add(Convert.ToInt32(slot[1]));
            return flags;
        }

        public static bool[] Flags(ChaControl chaCtrl)
        {
            try
            {
                var slots = SlotData(chaCtrl);
                return slots?.Select(x => x != null && Traverse.Create(x).Property("CharacterAccessory").GetValue<bool>()).ToArray();
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Additional Accessory Controls data: {ex.Message}");
                return null;
            }
        }

        public static void SetFlags(ChaControl chaCtrl, bool[] before, IDictionary<int, int> targetToSource, bool replacesAll, ICollection<int> cardFlags)
        {
            if (before == null || chaCtrl == null) return;
            try
            {
                var slots = SlotData(chaCtrl);
                if (slots == null) return;
                var toggle = MakerAPI.InsideAndLoaded && _pluginType != null
                    ? Traverse.Create(_pluginType).Property("Instance").Property("CharacterAccessoryControlWrapper").GetValue()
                    : null;
                for (var i = 0; i < slots.Length; i++)
                {
                    var source = i;
                    var loaded = replacesAll || (targetToSource != null && targetToSource.TryGetValue(i, out source));
                    var value = loaded ? cardFlags != null && cardFlags.Contains(source) : i < before.Length && before[i];
                    if (slots[i] != null) Traverse.Create(slots[i]).Property("CharacterAccessory").SetValue(value);
                    if (toggle == null) continue;
                    try
                    {
                        Traverse.Create(toggle).Method("SetValue", i, value).GetValue();
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not set Additional Accessory Controls character accessories: {ex.Message}");
            }
        }

        public static void LogFlags(ChaControl chaCtrl, string when)
        {
#if DEBUG
            try
            {
                var slots = SlotData(chaCtrl);
                if (slots == null) return;
                var flagged = Enumerable.Range(0, slots.Length).Where(i => slots[i] != null && Traverse.Create(slots[i]).Property("CharacterAccessory").GetValue<bool>());
                Diag.Log($"AAC character accessories {when}: {string.Join(", ", flagged.Select(x => x.ToString()).ToArray())} of {slots.Length}");
            }
            catch (Exception ex)
            {
                Diag.Log("AAC flags: " + ex.Message);
            }
#endif
        }

        public static void Hold(ChaControl chaCtrl, ICollection<int> replaced)
        {
            try
            {
                var slots = SlotData(chaCtrl);
                if (slots == null) return;
                for (var i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || replaced.Contains(i)) continue;
                    var flag = Traverse.Create(slots[i]).Property("CharacterAccessory");
                    if (flag.GetValue<bool>()) flag.SetValue(false);
                }
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Additional Accessory Controls data: {ex.Message}");
            }
        }

        public static object[] ReadCardSlots(ChaFile file) => ReadSlots(ExtendedSave.GetExtendedDataById(file, GUID), "accessoryData");

        public static object[] ReadCardSlots(ChaFileCoordinate coordinate) => ReadSlots(ExtendedSave.GetExtendedDataById(coordinate, GUID), "coordinateAccessoryData");

        private static object[] ReadSlots(PluginData data, string key)
        {
            if (!Available || _slotType == null || data == null || !data.data.TryGetValue(key, out var value) || !(value is byte[] bytes) || bytes.Length == 0) return null;
            var deserialize = typeof(MessagePackSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(x => x.Name == "Deserialize" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(byte[]));
            return deserialize.MakeGenericMethod(_slotType.MakeArrayType()).Invoke(null, new object[] { bytes }) as object[];
        }

        public static object[] TakeSlots(ChaControl chaCtrl)
        {
            try
            {
                if (!Available || _copySlot == null) return null;
                return SlotData(chaCtrl)?.Select(x => x == null ? null : _copySlot.Invoke(null, new[] { x, Slot(x).Property("SlotNumber").GetValue(), false })).ToArray();
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Additional Accessory Controls data: {ex.Message}");
                return null;
            }
        }

        // Kept slots get their own settings back, loaded slots the settings of the card slot they came from (or none)
        public static void PlaceSlots(ChaControl chaCtrl, object[] before, object[] card, IDictionary<int, int> targetToSource, bool fromCard, bool outfitLoad)
        {
            if (before == null || targetToSource == null || chaCtrl == null || !Available || _copySlot == null || _plainSlot == null) return;
            try
            {
                var controller = chaCtrl.GetComponent(_controllerType);
                var slots = SlotData(chaCtrl);
                if (controller == null || slots == null) return;
                var sourceToTarget = new Dictionary<int, int>();
                foreach (var pair in targetToSource.OrderBy(x => x.Key))
                    if (!sourceToTarget.ContainsKey(pair.Value)) sourceToTarget[pair.Value] = pair.Key;

                for (var t = 0; t < slots.Length; t++)
                {
                    var now = slots[t];
                    if (now == null) continue;
                    var partsInfo = Slot(now).Property("PartsInfo").GetValue();
                    object placed;
                    if (targetToSource.TryGetValue(t, out var s))
                    {
                        // AAC put a character accessory here itself
                        if (outfitLoad && Slot(now).Property("CharacterAccessory").GetValue<bool>()) continue;
                        var cardSlot = fromCard && card != null && s < card.Length ? card[s] : null;
                        placed = cardSlot != null && !Slot(cardSlot).Property("IsEmpty").GetValue<bool>()
                            ? _copySlot.Invoke(null, new object[] { cardSlot, t, true })
                            : _plainSlot.Invoke(null, new[] { t, Slot(now).Property("AccessoryName").GetValue(), partsInfo });
                        RemapSlotReferences(placed, sourceToTarget);
                    }
                    else if (t < before.Length && before[t] != null && !Slot(before[t]).Property("IsEmpty").GetValue<bool>())
                    {
                        placed = _copySlot.Invoke(null, new object[] { before[t], t, false });
                    }
                    else continue;
                    Slot(placed).Property("PartsInfo").SetValue(partsInfo);
                    slots[t] = placed;
                }

                var hairColor = MakerAPI.InsideAndLoaded && _pluginType != null
                    ? Traverse.Create(_pluginType).Property("Instance").Property("AutoMatchHairColorWrapper").GetValue()
                    : null;
                if (hairColor != null)
                {
                    for (var t = 0; t < slots.Length; t++)
                    {
                        if (slots[t] == null) continue;
                        try
                        {
                            Traverse.Create(hairColor).Method("SetValue", t, Slot(slots[t]).Property("AutoMatchBackHairColor").GetValue<bool>()).GetValue();
                        }
                        catch
                        {
                        }
                    }
                }

                Traverse.Create(controller).Method("RefreshAdvancedParents").GetValue();
                Traverse.Create(controller).Method("HandleVisibilityRules", new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(int) })
                    .GetValue(true, false, false, false, false, -1);
                Diag.Log($"AAC slot settings placed: {string.Join(", ", targetToSource.Select(x => x.Value + "->" + x.Key).ToArray())}, from card {fromCard}");
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not move Additional Accessory Controls settings: {ex}");
            }
        }

        private static Traverse Slot(object slot) => Traverse.Create(slot);

        // Links store the 1-based slot, Advanced Parent a path with "ca_slotNN" (0-based)
        private static void RemapSlotReferences(object slot, IDictionary<int, int> sourceToTarget)
        {
            if (Slot(slot).Property("VisibilityRules").GetValue() is IList rules)
            {
                for (var i = rules.Count - 1; i >= 0; i--)
                {
                    var rule = Traverse.Create(rules[i]);
                    var name = rule.Property("Rule").GetValue()?.ToString();
                    if (name != "ACCESSORY_LINK" && name != "ACCESSORY_INVERSE_LINK") continue;
                    if (int.TryParse(rule.Property("Modifier").GetValue<string>(), out var linked) && sourceToTarget.TryGetValue(linked - 1, out var target))
                        rule.Property("Modifier").SetValue((target + 1).ToString());
                    else
                        rules.RemoveAt(i);
                }
            }

            var parent = Slot(slot).Property("AdvancedParent");
            var path = parent.GetValue<string>();
            var at = path?.LastIndexOf("ca_slot", StringComparison.Ordinal) ?? -1;
            if (at < 0) return;
            var start = at + "ca_slot".Length;
            var end = start;
            while (end < path.Length && char.IsDigit(path[end])) end++;
            if (int.TryParse(path.Substring(start, end - start), out var source) && sourceToTarget.TryGetValue(source, out var moved))
                parent.SetValue(path.Substring(0, start) + moved.ToString("00") + path.Substring(end));
            else
                parent.SetValue(null);
        }
    }
}
