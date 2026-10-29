using System.Collections.Generic;
using System.Linq;
using AIChara;
using HarmonyLib;
using KoiClothesOverlayX;
using MessagePack;

namespace AdvancedLoader
{
    // Only use when Dependencies.ClothesOverlays is OK
    internal static class ClothesOverlayBridge
    {
        private sealed class Snapshot
        {
            public Dictionary<string, ClothesTexData> Overlays;
            public Dictionary<string, int> SizeOverrides;
        }

        public static object TakeSnapshot(ChaControl chaCtrl)
        {
            var controller = chaCtrl.GetComponent<KoiClothesOverlayController>();
            if (controller == null) return null;
            var traverse = Traverse.Create(controller);
            var overlays = traverse.Property("CurrentOverlayTextures").GetValue<Dictionary<string, ClothesTexData>>();
            var sizes = traverse.Property("CurrentTextureSizeOverrides").GetValue<Dictionary<string, int>>();
            return new Snapshot
            {
                Overlays = overlays == null ? new Dictionary<string, ClothesTexData>() : MessagePackSerializer.Deserialize<Dictionary<string, ClothesTexData>>(MessagePackSerializer.Serialize(overlays)),
                SizeOverrides = sizes == null ? new Dictionary<string, int>() : new Dictionary<string, int>(sizes)
            };
        }

        public static void Restore(ChaControl chaCtrl, object snapshotObject, IEnumerable<int> slots)
        {
            var snapshot = snapshotObject as Snapshot;
            var controller = chaCtrl.GetComponent<KoiClothesOverlayController>();
            if (snapshot == null || controller == null) return;

            var traverse = Traverse.Create(controller);
            var overlays = traverse.Property("CurrentOverlayTextures").GetValue<Dictionary<string, ClothesTexData>>();
            var sizes = traverse.Property("CurrentTextureSizeOverrides").GetValue<Dictionary<string, int>>();
            if (overlays == null) return;

            var slotSet = new HashSet<int>(slots);
            if (slotSet.Count == 0) return;

            bool InSlots(string key) => slotSet.Contains(SlotOf(key));

            foreach (var key in overlays.Keys.Where(InSlots).ToList())
            {
                overlays[key]?.Dispose();
                overlays.Remove(key);
            }
            foreach (var pair in snapshot.Overlays.Where(x => InSlots(x.Key)))
                overlays[pair.Key] = pair.Value;

            if (sizes != null)
            {
                foreach (var key in sizes.Keys.Where(InSlots).ToList()) sizes.Remove(key);
                foreach (var pair in snapshot.SizeOverrides.Where(x => InSlots(x.Key))) sizes[pair.Key] = pair.Value;
            }

            controller.RefreshAllTextures();
        }

        private static int SlotOf(string key)
        {
            var id = KoiClothesOverlayController.GetRealId(key);
            if (id.StartsWith("ct_top_parts_")) return 0;
            for (var kind = 0; kind < ClothesParts.Count; kind++)
                if (KoiClothesOverlayController.GetClothesIdFromKind(true, kind) == id) return kind;
            return -1;
        }
    }
}
