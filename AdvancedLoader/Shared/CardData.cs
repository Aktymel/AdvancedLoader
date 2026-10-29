using System;
using System.Collections.Generic;
using System.Linq;
using ExtensibleSaveFormat;
#if HS2 || AI
using AIChara;
#elif KKS || KK
#endif

namespace AdvancedLoader
{
    internal sealed class CardData
    {
        public string Path;
        public LoadMode Mode;

        public List<ChaFileAccessory.PartsInfo> AccessorySlots;

        public readonly List<KeyValuePair<int, string>> SlotNames = new List<KeyValuePair<int, string>>();

        // object: no Material Editor types here
        public object Materials;

        // object: no ABMX types here
        public object Bonemod;

        public readonly HashSet<string> PluginDataIds = new HashSet<string>();

#if KKS || KK
        public List<CardOutfit> CardOutfits = new List<CardOutfit>();

        public void ShowOutfit(int index)
        {
            if (index < 0 || index >= CardOutfits.Count) return;
            AccessorySlots = CardOutfits[index].Accessories;
            SlotNames.Clear();
            SlotNames.AddRange(CardOutfits[index].SlotNames);
        }
#endif

        public static CardData Read(string path, LoadMode mode, byte sex)
        {
            var card = new CardData { Path = path, Mode = mode };
            var readMaterials = MaterialEditorPresence.Available;
            var readBonemod = mode == LoadMode.Character && AbmxPresence.Available;

            void Inspect(ChaFileControl temp)
            {
                if (mode == LoadMode.Character) ReadPluginDataIds(card, temp);
#if KKS || KK
                if (mode == LoadMode.Character)
                {
                    try
                    {
                        card.CardOutfits = Outfits.ReadCard(temp);
                    }
                    catch (Exception ex)
                    {
                        AdvancedLoaderPlugin.Logger.LogWarning($"Could not read outfits from {path}: {ex.Message}");
                    }
                }
#endif
                if (readBonemod)
                {
                    try
                    {
                        card.Bonemod = AbmxBridge.ReadFromCard(temp);
                    }
                    catch (Exception ex)
                    {
                        AdvancedLoaderPlugin.Logger.LogWarning($"Could not read bonemod data from {path}: {ex.Message}");
                    }
                }

                if (!readMaterials) return;
                try
                {
                    card.Materials = mode == LoadMode.Character
                        ? MaterialEditorBridge.ReadFromCharacterFile(temp)
                        : MaterialEditorBridge.ReadFromCoordinate(AccessoryData.CardCoordinate(temp));
                }
                catch (Exception ex)
                {
                    AdvancedLoaderPlugin.Logger.LogWarning($"Could not read Material Editor data from {path}: {ex.Message}");
                }
            }

            try
            {
                card.AccessorySlots = mode == LoadMode.Character
                    ? AccessoryData.ReadCard(path, sex, Inspect)
                    : AccessoryData.ReadCoordinateCard(path, Inspect);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogError($"Failed to read {path}: {ex}");
            }

            if (card.AccessorySlots != null)
            {
                for (var i = 0; i < card.AccessorySlots.Count; i++)
                {
                    if (!AccessoryData.IsEmpty(card.AccessorySlots[i]))
                        card.SlotNames.Add(new KeyValuePair<int, string>(i, $"{i + 1:00}.  {AccessoryData.GetName(card.AccessorySlots[i])}"));
                }
            }
            return card;
        }

        private static void ReadPluginDataIds(CardData card, ChaFileControl temp)
        {
            try
            {
                card.PluginDataIds.UnionWith(ExtendedSave.GetAllExtendedData(temp).Where(x => x.Value != null).Select(x => x.Key));
#if KKS || KK
                // Koikatsu Sunshine keeps clothes related plugin data in each outfit
                foreach (var coordinate in temp.coordinate)
                    card.PluginDataIds.UnionWith(ExtendedSave.GetAllExtendedData(coordinate).Where(x => x.Value != null).Select(x => x.Key));
#else
                card.PluginDataIds.UnionWith(ExtendedSave.GetAllExtendedData(temp.coordinate).Where(x => x.Value != null).Select(x => x.Key));
#endif
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not read plugin data from {card.Path}: {ex.Message}");
            }
        }
    }
}
