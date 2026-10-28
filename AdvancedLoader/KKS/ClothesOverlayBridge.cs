using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KoiClothesOverlayX;
using MessagePack;

namespace AdvancedLoader
{
    /// <summary>
    /// Keeps clothes overlays (Clothes Overlay Mod, KCOX) of the clothes pieces and outfits that were not loaded.
    /// KCOX stores overlays per outfit and replaces them when clothes are loaded; afterwards the kept pieces and outfits
    /// get their previous overlays back. Only call into this class when <see cref="Dependencies.ClothesOverlays"/> is OK.
    /// </summary>
    internal static class ClothesOverlayBridge
    {
        private const string OverlaysField = "_allOverlayTextures";

        /// <summary>Copy of the overlays of every outfit.</summary>
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

        /// <summary>
        /// Put the overlays of the given clothes slots of the worn outfit back from the snapshot and redraw the clothes.
        /// </summary>
        public static void Restore(ChaControl chaCtrl, object snapshotObject, IEnumerable<int> slots)
        {
            var current = (ChaFileDefine.CoordinateType)chaCtrl.fileStatus.coordinateType;
            RebuildOutfits(chaCtrl, snapshotObject, new Dictionary<int, int> { { (int)current, (int)current } }, slots, -1);
        }

        /// <summary>
        /// Overlays after a character load: outfits taken from the card get the card's overlays for the ticked pieces
        /// (the card outfit's overlays are what KCOX just loaded) and their previous ones for the other pieces;
        /// every other outfit gets its previous overlays back.
        /// </summary>
        /// <param name="targetToSource">Character outfit -> card outfit.</param>
        /// <param name="untickedSlots">Clothes slots that keep their previous overlays.</param>
        /// <param name="outfitCount">Number of outfits after the load, or -1 to only touch the outfits in <paramref name="targetToSource"/>.</param>
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

        /// <summary>
        /// Clothes slot of an overlay key: "ct_clothesBot" is the bottom, "ct_top_parts_A" is part of the top,
        /// "Colormask_x" belongs to x.
        /// </summary>
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
