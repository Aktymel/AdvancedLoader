using System;
using AIChara;

namespace AdvancedLoader
{
    internal static partial class LoadRunner
    {
        private static bool RestoreInMaker(ChaControl chaCtrl, string path, out Action restoreToggles) => PressMakerLoad(LoadMode.Character, path, out restoreToggles);
    }
}
