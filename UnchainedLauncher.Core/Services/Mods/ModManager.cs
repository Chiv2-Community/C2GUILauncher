﻿using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {

    // TODO: Alot of these paths are old/duplicated elsewhere.
    //       Remove paths not in use, and figure out which paths we'll need again soon.
    internal static class CoreMods {
        public const string GithubBaseURL = "https://github.com";

        public const string EnabledModsCacheDir = $"{FilePaths.ModCachePath}\\enabled_mods";
        public const string ModsCachePackageDBDir = $"{FilePaths.ModCachePath}\\package_db";
        public const string ModsCachePackageDBPackagesDir = $"{ModsCachePackageDBDir}\\packages";
        public const string ModsCachePackageDBListPath = $"{ModsCachePackageDBDir}\\mod_list_index.txt";

        public const string AssetLoaderPluginPath = $"{FilePaths.PluginDir}\\C2AssetLoaderPlugin.dll";
        public const string ServerPluginPath = $"{FilePaths.PluginDir}\\C2ServerPlugin.dll";
        public const string BrowserPluginPath = $"{FilePaths.PluginDir}\\C2BrowserPlugin.dll";

        //public const string AssetLoaderPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2AssetLoaderPlugin/releases/latest/download/C2AssetLoaderPlugin.dll";
        //public const string ServerPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2ServerPlugin/releases/latest/download/C2ServerPlugin.dll";
        //public const string BrowserPluginURL = $"{GithubBaseURL}/Chiv2-Community/C2BrowserPlugin/releases/latest/download/C2BrowserPlugin.dll";

        public const string UnchainedPluginPath = $"{FilePaths.PluginDir}\\UnchainedPlugin.dll";
        public const string UnchainedPluginURL = $"{GithubBaseURL}/Chiv2-Community/UnchainedPlugin/releases/latest/download/UnchainedPlugin.dll";

        public const string PackageDBBaseUrl = "https://raw.githubusercontent.com/Chiv2-Community/C2ModRegistry/db/package_db";
        public const string PackageDBPackageListUrl = $"{PackageDBBaseUrl}/mod_list_index.txt";
    }

    public class ModManager : IModManager {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ModManager));

        public event ModEnabledHandler ModEnabled;
        public event ModDisabledHandler ModDisabled;

        public IEnumerable<ReleaseCoordinates> EnabledModReleases => _enabledModReleases;
        private readonly List<ReleaseCoordinates> _enabledModReleases;

        public IEnumerable<Mod> Mods => _mods;
        private readonly List<Mod> _mods;

        private IModRegistry _registry { get; }

        public ModManager(
            IModRegistry registry,
            IEnumerable<ReleaseCoordinates> enabledMods) {
            _registry = registry;
            _enabledModReleases = enabledMods.ToList();
            _mods = new List<Mod>();
        }

        public bool DisableModRelease(ReleaseCoordinates release) {
            var releaseExists = ReleaseExists(release);

            if (releaseExists && _enabledModReleases.Contains(release)) {
                _enabledModReleases.Remove(release);
                return true;
            }

            return true;
        }

        public bool EnableModRelease(ReleaseCoordinates coordinates) {
            var maybeRelease = this.GetRelease(coordinates);
            if (maybeRelease.IsNone || _enabledModReleases.Contains(coordinates)) return false;

            var existing = this.GetCurrentlyEnabledReleaseForMod(coordinates);
            existing.IfSome(r => _enabledModReleases.Remove(ReleaseCoordinates.FromRelease(r)));
            var existingVersion = existing.Map(x => x.Tag).FirstOrDefault();

            _enabledModReleases.Add(coordinates);
            ModEnabled.Invoke(maybeRelease.ValueUnsafe(), existingVersion);

            return true;
        }

        public bool EnableMod(ModIdentifier modId) {
            return
                this.GetLatestRelease(modId)
                    .Match(
                      Some: release => {
                          var releaseCoords = ReleaseCoordinates.FromRelease(release);
                          if (_enabledModReleases.Contains(releaseCoords)) return false;

                          var otherVersion = this.GetCurrentlyEnabledReleaseForMod(modId);
                          otherVersion.IfSome(other => _enabledModReleases.Remove(ReleaseCoordinates.FromRelease(other)));

                          _enabledModReleases.Add(releaseCoords);

                          ModEnabled.Invoke(release, otherVersion.Map(x => x.Tag).SingleOrDefault());
                          return true;
                      },
                      None: () => false
                    );
        }

        public bool DisableMod(ModIdentifier modId) {
            var maybeReleaseToDisable = this.GetCurrentlyEnabledReleaseForMod(modId);
            if (maybeReleaseToDisable.IsNone) return false;

            var releaseToDisable = maybeReleaseToDisable.ValueUnsafe();
            var releaseToDisableCoords = ReleaseCoordinates.FromRelease(releaseToDisable);

            _enabledModReleases.Remove(releaseToDisableCoords);

            ModDisabled.Invoke(releaseToDisable);
            return true;
        }

        public async Task<GetAllModsResult> UpdateModsList() {
            logger.Info("Updating mods list...");
            var result = await _registry.GetAllMods();
            logger.Info($"Got a total of {result.Mods.Count()} mods from {_registry.Name}");

            if (result.HasErrors) {
                var errorCount = result.Errors.Count();
                logger.Error($"Encountered {errorCount} errors while fetching mod list from registry");
                result.Errors
                    .Zip(Enumerable.Range(1, errorCount))
                    .ToList()
                    .ForEach(tuple => logger.Error($"Error {tuple.Item2}: ", tuple.Item1));
            }

            _mods.Clear();
            _mods.AddRange(result.Mods);

            return result;
        }


        private bool ReleaseExists(ReleaseCoordinates release) {
            var releaseExists =
                _mods
                    .Find(release.Matches)?// Find the mod that matches these coords
                    .Releases
                    .Exists(release.Matches) // Find the release that matches these coords 
                ?? false; // default to false if nothing was found

            return releaseExists;
        }
    }
}