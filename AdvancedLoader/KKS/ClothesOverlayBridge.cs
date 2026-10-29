using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KoiClothesOverlayX;
using MessagePack;

namespace AdvancedLoader
{
    // Only use when Dependencies.ClothesOverlays is OK
    internal static class ClothesOverlayBridge
    {
        private const string OverlaysField = "_allOverlayTextures";

        public static object TakeSnapshot(ChaControl chaCtrl)
        {
            var all = GetAll(chaCtrl);
            return all == null ? null : Copy(all);
        }

        private static Dictionary<ChaFileDefine.CoordinateType, Dictionary<string, ClothesTexData>> GetAll(ChaControl chaCtrl)
        {
            var controller = chaCtrl.GetComponent<KoiClothesOverlayController>();
            return controller == null ? null : Traverse.Create(controller).Field(OverlaysField).GetValue<Dictionary<ChaFileDefine.CoordinateType, Dictionary<string, ClothesTexData>>>();
        }

        private static Dictionary<ChaFileDefine.CoordinateType, Dictionary<string, ClothesTexData>> Copy(Dictionary<ChaFileDefine.CoordinateType, Dictionary<string, ClothesTexData>> all)
        {
            return all.ToDictionary(x => x.Key, x => x.Value == null
                ? new Dictionary<string, ClothesTexData>()
                : MessagePackSerializer.Deserialize<Dictionary<string, ClothesTexData>>(MessagePackSerializer.Serialize(x.Value)));
        }

        public static void Restore(ChaControl chaCtrl, object snapshotObject, IEnumerable<int> slots)
        {
            var current = (ChaFileDefine.CoordinateType)chaCtrl.fileStatus.coordinateType;
            RebuildOutfits(chaCtrl, snapshotObject, new Dictionary<int, int> { { (int)current, (int)current } }, slots, -1);
        }

        public static void RebuildOutfits(ChaControl chaCtrl, object snapshotObject, IDictionary<int, int> targetToSource, IEnumerable<int> untickedSlots, int outfitCount)
        {
            var before = snapshotObject as Dictionary<ChaFileDefine.CoordinateType, Dictionary<string, ClothesTexData>>;
            var controller = chaCtrl.GetComponent<KoiClothesOverlayController>();
            var all = GetAll(chaCtrl);
            if (before == null || controller == null || all == null) return;

            var loaded = Copy(all);
            var kept = new HashSet<int>(untickedSlots);
            Dictionary<string, ClothesTexData> Of(Dictionary<ChaFileDefine.CoordinateType, Dictionary<string, ClothesTexData>> source, int outfit) =>
                source.TryGetValue((ChaFileDefine.CoordinateType)outfit, out var value) && value != null ? value : new Dictionary<string, ClothesTexData>();

            var outfits = outfitCount >= 0 ? Enumerable.Range(0, outfitCount) : targetToSource.Keys;
            foreach (var t in outfits.ToList())
            {
                Dictionary<string, ClothesTexData> result;
                if (targetToSource.TryGetValue(t, out var s))
                {
                    result = Of(loaded, s).Where(x => !kept.Contains(SlotOf(x.Key))).ToDictionary(x => x.Key, x => x.Value);
                    foreach (var pair in Of(before, t).Where(x => kept.Contains(SlotOf(x.Key)))) result[pair.Key] = pair.Value;
                }
                else
                {
                    result = Of(before, t);
                }
                all[(ChaFileDefine.CoordinateType)t] = result;
            }

            // Outfits that no longer exist
            if (outfitCount >= 0)
                foreach (var key in all.Keys.Where(x => (int)x >= outfitCount).ToList()) all.Remove(key);

            controller.RefreshAllTextures();
        }

        private static int SlotOf(string key)
        {
            var id = KoiClothesOverlayController.GetRealId(key);
            if (KoiClothesOverlayMgr.SubClothesNames.Contains(id)) return 0;
            for (var kind = 0; kind < ClothesParts.Count; kind++)
                if (KoiClothesOverlayController.GetClothesIdFromKind(true, kind) == id) return kind;
            return -1;
        }
    }
}
