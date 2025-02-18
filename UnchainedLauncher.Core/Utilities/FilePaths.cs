namespace UnchainedLauncher.Core.Utilities {
    public static class FilePaths {
        public const string BinDir = "TBL\\Binaries\\Win64";
        public const string GameBinPath = BinDir + "\\Chivalry2-Win64-Shipping.exe";
        public const string LauncherPath = "Chivalry2Launcher.exe";
        public const string OriginalLauncherPath = "Chivalry2Launcher-ORIGINAL.exe";
        public const string PluginDir = BinDir + "\\Plugins";
        public const string UnchainedPluginPath = PluginDir + "\\UnchainedPlugin.dll";
        public const string PakDir = "TBL\\Content\\Paks";
        public const string SteamAppIdPath = BinDir + "\\steam_appid.txt";
        public const string ModCachePath = ".mod_cache";
        public const string LauncherSettingsFilePath = $"{ModCachePath}\\unchained_launcher_settings.json";
        public const string ServerTemplatesFilePath = $"{ModCachePath}\\server_templates.json";
        public const string RegistryConfigPath = $"{ModCachePath}\\registry_config.json";
        public const string ModManagerConfigPath = $"{ModCachePath}\\mod_manager_config.json";

    }
}