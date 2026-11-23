using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Maker;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal static class UndoSnapshot
    {
        internal sealed class Entry
        {
            public ChaControl Target;
            public string Path;
        }

        private static readonly string Folder = System.IO.Path.Combine(Paths.CachePath, "AdvancedLoader");
        private static readonly Dictionary<ChaControl, Entry> Saved = new Dictionary<ChaControl, Entry>();
        private static Entry _pending;
        private static bool _initialized;

        public static bool Has(ChaControl target) => target != null && Saved.ContainsKey(target);

        public static Entry Get(ChaControl target)
        {
            if (target == null || !Saved.TryGetValue(target, out var entry)) return null;
            if (File.Exists(entry.Path)) return entry;
            Forget(target);
            return null;
        }

        public static void Take(ChaControl target)
        {
            Init();
            Drop();
            if (target == null || (!MakerAPI.InsideAndLoaded && KoikatuAPI.GetCurrentGameMode() != GameMode.Studio)) return;
            var entry = new Entry { Target = target, Path = FilePath(target, true) };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(Folder);
                SaveCharacter(target, entry.Path);
                _pending = entry;
                Diag.Log($"Undo snapshot of {target.fileParam.fullname}: {new FileInfo(entry.Path).Length / 1024} KB in {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not save the character for Undo: {ex}");
                Delete(entry.Path);
            }
        }

        public static void Drop()
        {
            if (_pending == null) return;
            Delete(_pending.Path);
            _pending = null;
        }

        public static void Commit(ChaControl target)
        {
            if (_pending == null || _pending.Target != target) return;
            var entry = _pending;
            _pending = null;
            var path = FilePath(target, false);
            try
            {
                Forget(target);
                File.Move(entry.Path, path);
                entry.Path = path;
                Saved[target] = entry;
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not keep the character for Undo: {ex.Message}");
                Delete(entry.Path);
            }
        }

        public static void Forget(ChaControl target)
        {
            if (!Saved.TryGetValue(target, out var entry)) return;
            Saved.Remove(target);
            Delete(entry.Path);
        }

        private static void ForgetAll()
        {
            foreach (var entry in Saved.Values) Delete(entry.Path);
            Saved.Clear();
        }

        private static string FilePath(ChaControl target, bool pending) =>
            System.IO.Path.Combine(Folder, $"undo_{target.GetInstanceID()}{(pending ? "_new" : "")}.png");

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                if (Directory.Exists(Folder))
                    foreach (var file in Directory.GetFiles(Folder, "undo_*")) Delete(file);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not clear old Undo files: {ex.Message}");
            }
            CharacterApi.CharacterReloaded += (sender, e) => OnChanged(e.ReloadedCharacter);
            CharacterApi.CoordinateLoaded += (sender, e) => OnChanged(e.Character);
            MakerAPI.MakerExiting += (sender, e) => ForgetAll();
        }

        private static void OnChanged(ChaControl chaCtrl)
        {
            if (LoadRunner.Busy) return;
            if (chaCtrl == null)
            {
                ForgetAll();
                return;
            }
            if (Saved.ContainsKey(chaCtrl)) Diag.Log($"Undo cleared, {chaCtrl.fileParam.fullname} was loaded by something else");
            Forget(chaCtrl);
            foreach (var gone in Saved.Keys.Where(x => x == null).ToList()) Forget(gone);
        }

        private static void SaveCharacter(ChaControl target, string path)
        {
            var chaFile = target.chaFile;
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
                chaFile.SaveCharaFile(writer, chaFile.pngData != null);
        }

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                AdvancedLoaderPlugin.Logger.LogWarning($"Could not delete {path}: {ex.Message}");
            }
        }
    }
}
