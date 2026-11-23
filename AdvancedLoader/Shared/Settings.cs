using BepInEx.Configuration;

namespace AdvancedLoader
{
    internal enum AfterLoad
    {
        Keep,
        ClearSelection,
        ResetToDefault
    }

    internal static class Settings
    {
        public static ConfigEntry<AfterLoad> AfterLoadAction;
        public static ConfigEntry<bool> RememberTicks;
        public static ConfigEntry<bool> OpenAutomatically;
        public static ConfigEntry<bool> DockInMaker;
        public static ConfigEntry<bool> DockInStudio;
        public static ConfigEntry<KeyboardShortcut> WindowShortcut;
        private static ConfigEntry<string> _characterTicks;
        private static ConfigEntry<string> _clothesTicks;

        public static void Bind(ConfigFile config)
        {
            AfterLoadAction = config.Bind("General", "After load", AfterLoad.Keep,
                "What happens to the ticks after Load. ClearSelection unticks the selected accessories and outfits, ResetToDefault puts all ticks back to default.");
            RememberTicks = config.Bind("General", "Remember ticks", false, "Keep the ticks after the game is restarted.");
            OpenAutomatically = config.Bind("General", "Open automatically", false, "Open Advanced load together with the game's load list, without clicking the Advanced load button.");
            DockInMaker = config.Bind("General", "Dock in maker", true, "Show Advanced load in place of the game's load options in the maker. Off: a free window.");
            DockInStudio = config.Bind("General", "Dock in Studio", true, "Show Advanced load under Studio's character and outfit lists, in place of their buttons. Off: a free window.");
            WindowShortcut = config.Bind("General", "Open / close shortcut", KeyboardShortcut.Empty, "Opens or closes Advanced load wherever its button is shown.");
            GuiTheme.Bind(config);

            var hidden = new ConfigDescription("", null, new ConfigurationManagerAttributes { Browsable = false });
            _characterTicks = config.Bind("Saved", "Character ticks", "", hidden);
            _clothesTicks = config.Bind("Saved", "Clothes ticks", "", hidden);
        }

        public static string SavedTicks(LoadMode mode) => Ticks(mode).Value;

        public static void SaveTicks(LoadMode mode, string ticks)
        {
            var entry = Ticks(mode);
            if (entry.Value != ticks) entry.Value = ticks;
        }

        private static ConfigEntry<string> Ticks(LoadMode mode) => mode == LoadMode.Character ? _characterTicks : _clothesTicks;

        // Read by ConfigurationManager by name
        private sealed class ConfigurationManagerAttributes
        {
            public bool? Browsable;
        }
    }
}
