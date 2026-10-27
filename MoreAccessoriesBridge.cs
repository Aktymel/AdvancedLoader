using System;
using System.Collections;
using System.Collections.Generic;
using AIChara;
using HarmonyLib;
using UnityEngine;

namespace AdvancedLoader
{
    /// <summary>
    /// Reflection access to MoreAccessories internals, which hold accessory slots 21 and up.
    /// MoreAccessories keys its data by ChaFile, so slot N (N >= 20) lives at index N - 20 of that character's list.
    /// Every member is a no-op when MoreAccessories is not installed.
    /// </summary>
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

        /// <summary>
        /// MoreAccessories skips reading its card data while this is false (it is false during a load without "Clothes").
        /// </summary>
        public static bool LoadAdditionalAccessories
        {
            get => !Available || Traverse.Create(_instance).Field("_loadAdditionalAccessories").GetValue<bool>();
            set
            {
                if (Available) Traverse.Create(_instance).Field("_loadAdditionalAccessories").SetValue(value);
            }
        }

        /// <summary>
        /// Accessory slots 21+ of the character, or null if there are none.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> GetParts(ChaFile file)
        {
            if (!Available) return null;
            var data = CharAdditionalData;
            if (!data.Contains(file)) return null;
            return Traverse.Create(data[file]).Field("parts").GetValue<List<ChaFileAccessory.PartsInfo>>();
        }

        /// <summary>
        /// Replace accessory slots 21+ of the character. Spawned objects are destroyed and get recreated by ChaControl.ChangeAccessory.
        /// </summary>
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

        /// <summary>
        /// Drop MoreAccessories bookkeeping for a temporary ChaFile so it does not linger until the next purge.
        /// </summary>
        public static void Forget(ChaFile file)
        {
            if (!Available) return;
            CharAdditionalData.Remove(file);
            var byCoordinate = Traverse.Create(_instance).Field("_charByCoordinates").GetValue<IDictionary>();
            byCoordinate?.Remove(file.coordinate);
        }

        /// <summary>
        /// Rebuild MoreAccessories slot buttons in maker (slot count may have changed).
        /// </summary>
        public static void UpdateUI()
        {
            if (!Available) return;
            AccessTools.Method(_type, "UpdateUI")?.Invoke(_instance, null);
        }
    }
}
