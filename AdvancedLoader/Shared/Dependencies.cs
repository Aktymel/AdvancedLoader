using System;
using BepInEx.Bootstrap;

namespace AdvancedLoader
{
    internal enum DependencyState
    {
        Ok,
        Missing,
        TooOld
    }

    // Older versions differ inside, so only the tested version or newer
    internal sealed class Dependency
    {
        private bool _checked;
        private DependencyState _state;

        public Dependency(string name, string guid, string minimumVersion)
        {
            Name = name;
            GUID = guid;
            MinimumVersion = new Version(minimumVersion);
        }

        public string Name { get; }
        public string GUID { get; }
        public Version MinimumVersion { get; }
        public Version InstalledVersion { get; private set; }

        public DependencyState State
        {
            get
            {
                if (_checked) return _state;
                _checked = true;
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out var info))
                {
                    _state = DependencyState.Missing;
                }
                else
                {
                    InstalledVersion = info.Metadata.Version;
                    _state = InstalledVersion < MinimumVersion ? DependencyState.TooOld : DependencyState.Ok;
                }
                return _state;
            }
        }

        public bool IsOk => State == DependencyState.Ok;

        public string Warning(string consequence)
        {
            switch (State)
            {
                case DependencyState.Missing:
                    return $"{Name} is not installed: {consequence}";
                case DependencyState.TooOld:
                    return $"{Name} {InstalledVersion} is older than {MinimumVersion}: {consequence} Update {Name} to use this.";
                default:
                    return null;
            }
        }
    }

    internal static class Dependencies
    {
        // Minimum versions are the ones each game's build was made and tested with
#if HS2 || AI
        public static readonly Dependency MoreAccessories = new Dependency("MoreAccessories", "com.joan6694.illusionplugins.moreaccessories", "1.2.2");
        public static readonly Dependency MaterialEditor = new Dependency("Material Editor", "com.deathweasel.bepinex.materialeditor", "4.0.3.1");
        public static readonly Dependency Abmx = new Dependency("ABMX (bonemod)", "KKABMX.Core", "5.2.2");
        public static readonly Dependency ClothesOverlays = new Dependency("Clothes Overlay Mod", "KCOX", "6.3");
#elif KKS || KK
        public static readonly Dependency MoreAccessories = new Dependency("MoreAccessories", "com.joan6694.illusionplugins.moreaccessories", "2.0.21");
        public static readonly Dependency MaterialEditor = new Dependency("Material Editor", "com.deathweasel.bepinex.materialeditor", "4.0.3");
        public static readonly Dependency Abmx = new Dependency("ABMX (bonemod)", "KKABMX.Core", "5.4");
        public static readonly Dependency ClothesOverlays = new Dependency("Clothes Overlay Mod", "KCOX", "6.2");
        public static readonly Dependency MoreOutfits = new Dependency("More Outfit Slots", "com.deathweasel.bepinex.moreoutfits", "1.1.2");
#endif

        public static void LogProblems()
        {
            foreach (var dependency in new[] { MoreAccessories, MaterialEditor, Abmx, ClothesOverlays })
            {
                if (dependency.State == DependencyState.TooOld)
                    AdvancedLoaderPlugin.Logger.LogWarning($"{dependency.Name} {dependency.InstalledVersion} is older than {dependency.MinimumVersion}, its features in Advanced load are turned off");
                else if (dependency.State == DependencyState.Missing)
                    AdvancedLoaderPlugin.Logger.LogInfo($"{dependency.Name} is not installed, its features in Advanced load are turned off");
            }
        }
    }
}
