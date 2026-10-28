using System;
using System.Collections.Generic;
#if HS2
using AIChara;
#elif KKS
#endif

namespace AdvancedLoader
{
    /// <summary>
    /// Everything the Advanced Load window needs from one card, read once when the card is selected.
    /// </summary>
    internal sealed class CardData
    {
        public string Path;
        public LoadMode Mode;

        /// <summary>All accessory slots (vanilla and MoreAccessories), or null if the card could not be read.</summary>
        public List<ChaFileAccessory.PartsInfo> AccessorySlots;

        /// <summary>Non-empty accessory slots as (slot index, display text).</summary>
        public readonly List<KeyValuePair<int, string>> SlotNames = new List<KeyValuePair<int, string>>();

        /// <summary>MaterialEditorData, kept as object so this class never loads Material Editor types.</summary>
        public object Materials;

        /// <summary>Body bonemod (ABMX) of a character card, kept as object for the same reason.</summary>
        public object Bonemod;

#if KKS
        /// <summary>All outfits of a character card (empty for a clothes card).</summary>
        public List<CardOutfit> CardOutfits = new List<CardOutfit>();

        /// <summary>Show the accessories of one outfit of a character card in <see cref="AccessorySlots"/> and <see cref="SlotNames"/>.</summary>
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
#if KKS
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
    }
}
