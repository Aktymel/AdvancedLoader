using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using ExtensibleSaveFormat;
using HarmonyLib;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal sealed class AccessoryGroup
    {
        public int First;
        public int Last;
        public bool Collapsed;
        public string Name;

        public bool Contains(int slot) => slot >= First && slot <= Last;
        public bool Overlaps(AccessoryGroup other) => First <= other.Last && Last >= other.First;
    }

    internal static class AccessoryGroups
    {
        public const string GUID = "com.aktymel.advancedaccessoriesmenu";
        private const string OutfitKeyPrefix = "outfit";
        private const string CoordinateKey = "groups";

        private static bool _resolved;
        private static MethodInfo _get;
        private static MethodInfo _set;

        public static bool Available
        {
            get
            {
                Resolve();
                return _get != null && _set != null;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!Chainloader.PluginInfos.ContainsKey(GUID)) return;
            var api = AccessTools.TypeByName("AdvancedAccessoriesMenu.AdvancedAccessoriesMenuApi");
            if (api == null) return;
            _get = AccessTools.Method(api, "GetGroups", new[] { typeof(ChaControl), typeof(int) });
            _set = AccessTools.Method(api, "SetGroups", new[] { typeof(ChaControl), typeof(int), typeof(string) });
        }

        public static List<AccessoryGroup> Parse(string text)
        {
            var groups = new List<AccessoryGroup>();
            if (string.IsNullOrEmpty(text)) return groups;
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split(new[] { '\t' }, 4);
                if (parts.Length < 4 || !int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var last) || first < 0 || last < first) continue;
                groups.Add(new AccessoryGroup { First = first, Last = last, Collapsed = parts[2] == "1", Name = parts[3] });
            }
            return groups.OrderBy(x => x.First).ToList();
        }

        public static string Format(IEnumerable<AccessoryGroup> groups)
        {
            var sb = new StringBuilder();
            foreach (var g in groups.OrderBy(x => x.First))
                sb.Append(g.First).Append('\t').Append(g.Last).Append('\t').Append(g.Collapsed ? 1 : 0).Append('\t').Append(g.Name).Append('\n');
            return sb.ToString();
        }

        public static Dictionary<int, string> ReadCharacter(ChaFile file)
        {
            var result = new Dictionary<int, string>();
            var data = ExtendedSave.GetExtendedDataById(file, GUID);
            if (data?.data == null) return result;
            foreach (var pair in data.data)
            {
                if (pair.Key.StartsWith(OutfitKeyPrefix) && int.TryParse(pair.Key.Substring(OutfitKeyPrefix.Length), out var outfit) && pair.Value is string text)
                    result[outfit] = text;
            }
            return result;
        }

        public static string ReadCoordinate(ChaFileCoordinate coordinate)
        {
            var data = ExtendedSave.GetExtendedDataById(coordinate, GUID);
            return data?.data != null && data.data.TryGetValue(CoordinateKey, out var value) ? value as string : null;
        }

        public static string Get(ChaControl chaCtrl, int outfit) => Available ? _get.Invoke(null, new object[] { chaCtrl, outfit }) as string : null;

        public static void Reserve(List<bool> occupied, string groups)
        {
            foreach (var group in Parse(groups))
                for (var i = group.First; i <= group.Last; i++)
                {
                    while (occupied.Count <= i) occupied.Add(false);
                    occupied[i] = true;
                }
        }

        public static Dictionary<int, string> Snapshot(ChaControl chaCtrl, int outfitCount)
        {
            var result = new Dictionary<int, string>();
            if (!Available) return result;
            for (var i = 0; i < outfitCount; i++) result[i] = Get(chaCtrl, i);
            return result;
        }

        public static void Set(ChaControl chaCtrl, int outfit, string groups)
        {
            if (Available) _set.Invoke(null, new object[] { chaCtrl, outfit, groups });
        }

        public static List<IList<int>> Blocks(IEnumerable<AccessoryGroup> cardGroups, LoadOptions options, IEnumerable<int> sources)
        {
            var list = sources.ToList();
            var blocks = new List<IList<int>>();
            if (cardGroups == null) return blocks;
            foreach (var group in cardGroups)
            {
                if (options.Accessories == AccessoryMode.Selected && !options.SelectedGroups.Contains(group.First)) continue;
                var members = list.Where(group.Contains).OrderBy(x => x).ToList();
                if (members.Count > 1) blocks.Add(members);
            }
            return blocks;
        }

        public static string Combine(string before, string card, IDictionary<int, int> targetToSource, bool replacesAll, Func<int, bool> isEmpty)
        {
            if (replacesAll) return card;
            targetToSource = targetToSource ?? new Dictionary<int, int>();

            var result = Parse(before).Where(g => Enumerable.Range(g.First, g.Last - g.First + 1).Any(s => !targetToSource.ContainsKey(s))).ToList();
            var sourceToTarget = targetToSource.ToDictionary(x => x.Value, x => x.Key);

            foreach (var group in Parse(card))
            {
                var targets = Enumerable.Range(group.First, group.Last - group.First + 1)
                    .Where(sourceToTarget.ContainsKey).Select(s => sourceToTarget[s]).OrderBy(x => x).ToList();
                var part = 0;
                var i = 0;
                while (i < targets.Count)
                {
                    var j = i;
                    while (j + 1 < targets.Count && Enumerable.Range(targets[j] + 1, targets[j + 1] - targets[j] - 1).All(isEmpty)) j++;
                    var moved = new AccessoryGroup
                    {
                        First = targets[i],
                        Last = targets[j],
                        Collapsed = group.Collapsed,
                        Name = part == 0 ? group.Name : $"{group.Name} {part + 1}"
                    };
                    part++;
                    if (!result.Any(x => x.Overlaps(moved))) result.Add(moved);
                    i = j + 1;
                }
            }
            return Format(result);
        }
    }
}
