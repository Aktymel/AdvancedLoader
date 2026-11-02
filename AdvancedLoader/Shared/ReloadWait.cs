using System;
using KKAPI.Chara;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal sealed class ReloadWait
    {
        private const int MinFrames = 6;
        private const int FramesAfterReload = 6;
        private const int MaxFrames = 120;

        private readonly ChaControl _target;
        private bool _reloaded;
        private int _reloadedAt = -1;
        private bool _stopped;

        private ReloadWait(ChaControl target)
        {
            _target = target;
            CharacterApi.CharacterReloaded += OnCharacterReloaded;
            CharacterApi.CoordinateLoaded += OnCoordinateLoaded;
        }

        public static ReloadWait Start(ChaControl target) => new ReloadWait(target);

        public static bool Ready(ReloadWait wait, int frame) => wait?.Ready(frame) ?? frame >= MinFrames;

        private void OnCharacterReloaded(object sender, CharaReloadEventArgs e)
        {
            if (e.ReloadedCharacter == _target) _reloaded = true;
        }

        private void OnCoordinateLoaded(object sender, CoordinateEventArgs e)
        {
            if (e.Character == _target) _reloaded = true;
        }

        private bool Ready(int frame)
        {
            if (_reloaded && _reloadedAt < 0) _reloadedAt = frame;
            var ready = frame >= MaxFrames || (_reloadedAt >= 0 && frame >= Math.Max(MinFrames, _reloadedAt + FramesAfterReload));
            if (ready)
            {
                Diag.Log(_reloadedAt >= 0 ? $"Plugins reloaded at frame {_reloadedAt}, continuing at {frame}" : $"No reload event after {frame} frames");
                Stop();
            }
            return ready;
        }

        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            CharacterApi.CharacterReloaded -= OnCharacterReloaded;
            CharacterApi.CoordinateLoaded -= OnCoordinateLoaded;
        }
    }
}
