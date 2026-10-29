using System;
using System.Collections;
using System.Collections.Generic;
using AIChara;
using HarmonyLib;
using UnityEngine;

namespace AdvancedLoader
{
    // Slot N (N >= 20) is at index N - 20; no-op without MoreAccessories
    internal static class MoreAccessoriesBridge
    {
        public const string GUID = "com.joan6694.illusionplugins.moreaccessories";

        private static bool _initialized;
        private static object _instance;
        private static Type _type;
        private static Type _additionalDataType;
        private static Type _accessoryObjectType;

        public static bool Available
        {
            get
            {
                Init();
                return _instance != null;
            }
        }

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            // Older versions keep their slot data differently
            if (!Dependencies.MoreAccessories.IsOk) return;

            _type = AccessTools.TypeByName("MoreAccessoriesAI.MoreAccessories");
            if (_type == null) return;
            _instance = AccessTools.Field(_type, "_self")?.GetValue(null);
            _additionalDataType = AccessTools.Inner(_type, "AdditionalData");
            _accessoryObjectType = AccessTools.Inner(_additionalDataType, "AccessoryObject");
        }

        private static IDictionary CharAdditionalData => (IDictionary)Traverse.Create(_instance).Field("_charAdditionalData").GetValue();

        // MoreAccessories skips its card data while false
        public static bool LoadAdditionalAccessories
        {
            get => !Available || Traverse.Create(_instance).Field("_loadAdditionalAccessories").GetValue<bool>();
            set
            {
                if (Available) Traverse.Create(_instance).Field("_loadAdditionalAccessories").SetValue(value);
            }
        }

        public static List<ChaFileAccessory.PartsInfo> GetParts(ChaFile file)
        {
            if (!Available) return null;
            var data = CharAdditionalData;
            if (!data.Contains(file)) return null;
            return Traverse.Create(data[file]).Field("parts").GetValue<List<ChaFileAccessory.PartsInfo>>();
        }

        public static void SetParts(ChaFile file, List<ChaFileAccessory.PartsInfo> parts)
        {
            if (!Available) return;
            var dict = CharAdditionalData;
            var data = dict.Contains(file) ? dict[file] : null;
            if (data == null)
            {
                data = Activator.CreateInstance(_additionalDataType, true);
                dict[file] = data;
            }

            var traverse = Traverse.Create(data);
            var dataParts = traverse.Field("parts").GetValue<List<ChaFileAccessory.PartsInfo>>();
            var objects = traverse.Field("objects").GetValue<IList>();

            foreach (var obj in objects)
            {
                var go = Traverse.Create(obj).Field("obj").GetValue<GameObject>();
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            objects.Clear();
            dataParts.Clear();

            dataParts.AddRange(parts);
            for (var i = 0; i < parts.Count; i++)
                objects.Add(Activator.CreateInstance(_accessoryObjectType, true));
        }

        public static void Forget(ChaFile file)
        {
            if (!Available) return;
            CharAdditionalData.Remove(file);
            var byCoordinate = Traverse.Create(_instance).Field("_charByCoordinates").GetValue<IDictionary>();
            byCoordinate?.Remove(file.coordinate);
        }

        public static void UpdateUI()
        {
            if (!Available) return;
            AccessTools.Method(_type, "UpdateUI")?.Invoke(_instance, null);
        }
    }
}
