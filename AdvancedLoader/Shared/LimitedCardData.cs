using System.Collections.Generic;
using System.Linq;
using ExtensibleSaveFormat;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal static class LimitedCardData
    {
        private static ChaFile _target;
        private static ChaControl _character;
        private static ChaFile _card;

        public static void Init()
        {
            ExtendedSave.CardBeingLoaded += file =>
            {
                if (_target != null && _card == null && file != _target) _card = file;
            };
        }

        public static void Begin(ChaFile target, ChaControl character)
        {
            _target = target;
            _character = character;
            _card = null;
        }

        public static void End()
        {
            _target = null;
            _character = null;
            _card = null;
        }

        public static void CopyTo(ChaFile target)
        {
            if (_card == null || target != _target) return;
            var card = _card;
            _card = null;

            var source = ExtendedSave.GetAllExtendedData(card) ?? new Dictionary<string, PluginData>();
            var current = ExtendedSave.GetAllExtendedData(target);
            Diag.Log("Card plugin data copied to the character: " + string.Join(", ", source.Where(x => x.Value != null).Select(x => x.Key).ToArray()));
            if (current != null)
                foreach (var id in current.Keys.Where(x => !source.ContainsKey(x)).ToList()) ExtendedSave.SetExtendedDataById(target, id, null);
            foreach (var pair in source.ToList()) ExtendedSave.SetExtendedDataById(target, pair.Key, pair.Value);

            if (!AbmxPresence.Available) return;
            try
            {
                source.TryGetValue(AbmxBridge.DataId, out var bonemod);
                ExtendedSave.SetExtendedDataById(target, AbmxBridge.DataId, AbmxBridge.KeepBodyBones(_character, bonemod));
            }
            catch (System.Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not keep the current bonemod during the load: {(ex.InnerException ?? ex).Message}");
            }
        }
    }
}
