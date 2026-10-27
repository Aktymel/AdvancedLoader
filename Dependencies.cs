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

    /// <summary>
    /// An optional plugin this one works with. Its feature is only turned on when the plugin is installed in at least
    /// the version this plugin was built and tested against, because internals of older versions differ.
    /// </summary>
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

        /// <summary>Warning shown in the window, or null when the plugin can be used.</summary>
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
        public static readonly Dependency MoreAccessories = new Dependency("MoreAccessories", "com.joan6694.illusionplugins.moreaccessories", "1.2.2");
        public static readonly Dependency MaterialEditor = new Dependency("Material Editor", "com.deathweasel.bepinex.materialeditor", "4.0.3.1");
        public static readonly Dependency Abmx = new Dependency("ABMX (bonemod)", "KKABMX.Core", "5.2.2");
        public static readonly Dependency ClothesOverlays = new Dependency("Clothes Overlay Mod", "KCOX", "6.3");

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
