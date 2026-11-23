using System;
using System.Collections;
using KKAPI.Maker;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal static partial class LoadRunner
    {
        public static bool CanUndo(ChaControl target) => !Busy && UndoSnapshot.Has(target);

        public static IEnumerator Undo(ChaControl target)
        {
            var entry = Busy ? null : UndoSnapshot.Get(target);
            if (entry == null) yield break;
            Busy = true;
            ReloadWait reload = null;
            Action restoreToggles = null;
            try
            {
                reload = ReloadWait.Start(target);
                if (!RestoreSnapshot(entry, out restoreToggles))
                {
                    AdvancedLoaderPlugin.Logger.LogMessage(MakerAPI.InsideAndLoaded ? "Open \"Load character\" in maker once, then try again" : "Select the character first");
                    yield break;
                }
                for (var frame = 0; !ReloadWait.Ready(reload, frame); frame++) yield return null;
                Diag.Log($"Undo done: {DescribeBody(target)}");
                UndoSnapshot.Forget(target);
                AdvancedLoaderPlugin.Logger.LogMessage("Undone");
            }
            finally
            {
                restoreToggles?.Invoke();
                reload?.Stop();
                OtherPlugins.CharacterChanged();
                Busy = false;
            }
        }

        private static bool RestoreSnapshot(UndoSnapshot.Entry entry, out Action restoreToggles)
        {
            restoreToggles = null;
            var target = entry.Target;
            if (MakerAPI.InsideAndLoaded) return RestoreInMaker(target, entry.Path, out restoreToggles);
            var character = StudioCharaPanel.CharacterOf(target) ?? StudioOutfitPanel.CharacterOf(target);
            if (character == null) return false;
            character.ChangeChara(entry.Path);
            return true;
        }
    }
}
