﻿using C2GUILauncher.JsonModels;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace C2GUILauncher.Mods {

    class CoreMods {
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

    public class ModManager {
        // Doesn't work or something.
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The registry org is the github organization that the mod registry is hosted under.
        /// </summary>
        public string RegistryOrg { get; }

        /// <summary>
        /// The registry repo name is the github repository that the mod registry is hosted under.
        /// </summary>
        public string RegistryRepoName { get; }

        /// <summary>
        /// The list of mods found in the registry
        /// </summary>
        public ObservableCollection<Mod> Mods { get; }

        /// <summary>
        /// The list of enabled mods loaded from the enabled mods cache dir, updated when the user enables or disables mods.
        /// </summary>
        public ObservableCollection<Release> EnabledModReleases { get; }

        /// <summary>
        /// The list of pending downloads, updated when the user enables mods or changes the version of a mod.
        /// </summary>
        public ObservableCollection<ModReleaseDownloadTask> PendingDownloads { get; }

        /// <summary>
        /// The list of failed downloads, updated when a download fails.
        /// </summary>
        public ObservableCollection<ModReleaseDownloadTask> FailedDownloads { get; }

        /// <summary>
        /// The path to the directory where the pak files are stored.
        /// </summary>
        private string PakDir { get; }

        public ModManager(
            string registryOrg,
            string registryRepoName,
            string pakDir,
            ObservableCollection<Mod> baseModList,
            ObservableCollection<Release> enabledMods,
            ObservableCollection<ModReleaseDownloadTask> pendingDownloads,
            ObservableCollection<ModReleaseDownloadTask> failedDownloads) {
            RegistryOrg = registryOrg;
            RegistryRepoName = registryRepoName;
            PakDir = pakDir;
            Mods = baseModList;
            EnabledModReleases = enabledMods;
            PendingDownloads = pendingDownloads;
            FailedDownloads = failedDownloads;
        }

        public static ModManager ForRegistry(string registryOrg, string registryRepoName, string pakDir) {
            // Create mod cache dirs if they don't exist
            Directory.CreateDirectory(CoreMods.EnabledModsCacheDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBDir);
            Directory.CreateDirectory(CoreMods.ModsCachePackageDBPackagesDir);

            // List everything in the EnabledModsCacheDir and its direct subdirs, then deserialize and filter out any failures (null)
            var enabledModReleases =
                Directory.GetDirectories(CoreMods.EnabledModsCacheDir)
                    .SelectMany(x => Directory.GetFiles(x))
                    .Select(x => JsonConvert.DeserializeObject<Release>(File.ReadAllText(x)))
                    .Where(x => x != null)
                    .Select(x => x!);

            return new ModManager(
                registryOrg,
                registryRepoName,
                pakDir,
                new ObservableCollection<Mod>(),
                new ObservableCollection<Release>(enabledModReleases!),
                new ObservableCollection<ModReleaseDownloadTask>(),
                new ObservableCollection<ModReleaseDownloadTask>()
            );
        }

        /// <summary>
        /// Searches the enabled releases to see if this mod has any releases enabled.
        /// </summary>
        /// <param name="mod">The mod to find an associated enabled release for</param>
        /// <returns></returns>
        public Release? GetCurrentlyEnabledReleaseForMod(Mod mod) {
            return EnabledModReleases.FirstOrDefault(x => x.Manifest.RepoUrl == mod.LatestManifest.RepoUrl);
        }

        /// <summary>
        /// Disables a mod release, optionally forcing it to disable even if it has dependents, and optionally cascading the disable to its dependents.
        /// </summary>
        /// <param name="release">The release to disable</param>
        /// <param name="force">Whether or not to disable despite enabled dependents</param>
        /// <param name="cascade">Whether or not to disable the dependents</param>
        /// <returns></returns>
        public ModDisableResult DisableModRelease(Release release, bool force, bool cascade) {
            logger.Info("Disabling mod release: " + release.Manifest.Name + " @" + release.Tag);

            var dependents = GetDependents(release);
            ModDisableResult result = ModDisableResult.Success;

            if (dependents.Any()) {
                if (!force) {
                    // if we're not forcing, we need to make the result fail and add the conflicts
                    result += ModDisableResult.Conflicts(dependents);
                }

                if (cascade) {
                    // if we're cascading, we need to disable all the dependents
                    foreach (var dependent in dependents) {
                        result += DisableModRelease(dependent, force, cascade);
                    }
                }
            }

            if (result.Successful) {
                var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);

                var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
                var metadataFilePath = orgPath + "\\" + urlParts.Last() + ".json";
                var pakFilePath = PakDir + "\\" + release.PakFileName;

                var fileList = new List<string> { pakFilePath, metadataFilePath };

                var deleteResult = FileHelpers.DeleteFiles(fileList);

                EnabledModReleases.Remove(release);
              
                if(!deleteResult) {
                    result += ModDisableResult.Locked(release);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a list of releases that depend on the target release.
        /// </summary>
        /// <param name="targetRelease"></param>
        /// <returns></returns>
        private List<Release> GetDependents(Release targetRelease) {
              return EnabledModReleases
                .Where(otherRelease => otherRelease.Manifest.RepoUrl != targetRelease.Manifest.RepoUrl) // Filter out the target release
                .Where(otherRelease => otherRelease.Manifest.Dependencies.Any(dep => dep.RepoUrl == targetRelease.Manifest.RepoUrl)) // Get anything depending on the target release
                .ToList();
        }


        /// <summary>
        /// Enables a mod release and downloads any dependencies that are not already enabled.
        /// </summary>
        /// <param name="release">the release to enable</param>
        /// <returns></returns>
        public ModEnableResult EnableModRelease(Release release) {
            logger.Debug("Enabling mod release: " + release.Manifest.Name + " @" + release.Tag);
            var associatedMod = this.Mods.First(Mods => Mods.Releases.Contains(release));

            // If there is no mod associated with this release, return a failure
            if (associatedMod == null)
                return ModEnableResult.Fail("Selected release not found in mod list: " + release.Manifest.Name + " @" + release.Tag);

            var result = ModEnableResult.Success;

            // Check if this mod is already enabled
            var enabledModRelease = GetCurrentlyEnabledReleaseForMod(associatedMod);

            // If it is, check if its the same version
            if (enabledModRelease != null) {
                // If its already enabled with the same version, and the download was successful, just return success
                if (enabledModRelease == release && !FailedDownloads.Any(x => x.Release == release))
                    return ModEnableResult.Success;

                result += ModEnableResult.Warn("Mod already enabled with different version: " + enabledModRelease.Manifest.Name + " @" + enabledModRelease.Tag);
            }

            foreach (var dependency in release.Manifest.Dependencies) {
                // Get the release for the given dependency
                var dependencyRelease =
                    this.Mods
                        .FirstOrDefault(mod => mod.LatestManifest.RepoUrl == dependency.RepoUrl)?.Releases
                        .FirstOrDefault(release => release.Tag == dependency.Version);

                // If a dependency release was not found, return a failure, otherwise enable it.
                if (dependencyRelease == null)
                    result += ModEnableResult.Fail("Dependency not found: " + dependency.RepoUrl + " @" + dependency.Version);
                else
                    result += EnableModRelease(dependencyRelease);
            }

            var downloadResult = DownloadModRelease(release);

            if (FileHelpers.IsFileLocked(downloadResult.DownloadTask.Target.OutputPath!)) {
                // We'll still pretend like we downloaded and changed things, but we'll return a warning about the lock too
                result += ModEnableResult.Locked(release);
            }
          
            if (result.Successful) {
                EnabledModReleases.Add(release);
                PendingDownloads.Add(downloadResult);
            }

            return result;
        }
        
        /// <summary>
        /// Downloads a mod release and adds it to the pending downloads list.
        /// </summary>
        /// <param name="release">the release to download</param>
        /// <returns></returns>
        private ModReleaseDownloadTask DownloadModRelease(Release release) {
            // Cleanup the previously failed download if it exists
            if (FailedDownloads.Any(x => x.Release == release))
                FailedDownloads.Remove(FailedDownloads.First(x => x.Release == release));

            var downloadUrl = release.Manifest.RepoUrl + "/releases/download/" + release.Tag + "/" + release.PakFileName;
            var outputPath = PakDir + "\\" + release.PakFileName;

            logger.Info("Beginning download of " + downloadUrl + " to " + outputPath);
            var downloadTask = new ModReleaseDownloadTask(release, HttpHelpers.DownloadFileAsync(downloadUrl, outputPath));
            PendingDownloads.Add(downloadTask);

            downloadTask.DownloadTask.Task.ContinueWith(task => {
                if (task.IsFaulted) {
                    logger.Error("Download failed: " + task.Exception?.Message);
                    FailedDownloads.Add(downloadTask);
                } else {
                    logger.Info("Download complete: " + outputPath);
                    var enabledModJson = JsonConvert.SerializeObject(release);
                    var urlParts = release.Manifest.RepoUrl.Split("/").TakeLast(2);

                    var orgPath = CoreMods.EnabledModsCacheDir + "\\" + urlParts.First();
                    var filePath = orgPath + "\\" + urlParts.Last() + ".json";

                    Directory.CreateDirectory(orgPath);
                    File.WriteAllText(filePath, enabledModJson);

                    PendingDownloads.Remove(downloadTask);
                }
            });

            return downloadTask;
        }

        /// <summary>
        /// Updates the list of mods from the package db.
        /// </summary>
        /// <returns>A Task that completes when the update completes</returns>
        public async Task UpdateModsList() {
            Mods.Clear();

            await HttpHelpers.DownloadFileAsync(CoreMods.PackageDBPackageListUrl, CoreMods.ModsCachePackageDBListPath).Task;

            var packageListString = File.ReadAllText(CoreMods.ModsCachePackageDBListPath);
            var packageNameToMetadataPath = (String s) => $"{CoreMods.PackageDBBaseUrl}/packages/{s}.json";
            var packageNameToFilePath = (String s) => $"{CoreMods.ModsCachePackageDBPackagesDir}\\{s}.json";

            var packages = packageListString.Split("\n").Where(s => s.Length > 0);

            var downloadTasks = packages
                .Select(packageName => HttpHelpers.DownloadFileAsync(packageNameToMetadataPath(packageName), packageNameToFilePath(packageName)))
                .Select(async downloadTask => {
                    await downloadTask.Task;
                    var fileLocation = downloadTask.Target.OutputPath!;
                    var mod = JsonConvert.DeserializeObject<Mod>(await File.ReadAllTextAsync(fileLocation));
                    if (mod != null)
                        Mods.Add(mod);
                    return mod;
                })
                .ToList();

            await Task.WhenAll(downloadTasks);
        }

        // TODO: Somehow move this around. this function downloads the plugins, not the mods
        public IEnumerable<DownloadTask> DownloadModFiles(bool debug) {
            // Create plugins dir. This method does nothing if the directory already exists.
            Directory.CreateDirectory(FilePaths.PluginDir);

            // All Chiv2-Community dll releases have an optional _dbg suffix for debug builds.
            var downloadFileSuffix = debug ? "_dbg.dll" : ".dll";

            // These are the core mods necessary for asset loading, server hosting, server browser usage, and the injector itself.
            // Please forgive the jank debug dll implementation. It'll be less jank after we aren't using hardcoded paths
            var coreMods = new List<DownloadTarget>() {
                new DownloadTarget(CoreMods.UnchainedPluginURL.Replace(".dll", downloadFileSuffix), CoreMods.UnchainedPluginPath)
            };

            var DeprecatedLibs = new List<String>()
            {
                CoreMods.AssetLoaderPluginPath,
                CoreMods.ServerPluginPath,
                CoreMods.BrowserPluginPath
            };

            foreach (var depr in DeprecatedLibs)
                FileHelpers.DeleteFile(depr);

            return HttpHelpers.DownloadAllFiles(coreMods);
        }
    }


    public record ModEnableResult(bool Successful, List<string> Failures, List<string> Warnings) {
        public static ModEnableResult Success => new ModEnableResult(true, new List<string>(), new List<string>());

        public static ModEnableResult Fail(string failure) => new ModEnableResult(false, new List<string>() { failure }, new List<string>());
        public static ModEnableResult Fails(List<string> failures) => new ModEnableResult(false, failures, new List<string>());

        public static ModEnableResult Warn(string warning) => Warns(new List<string>() { warning });
        public static ModEnableResult Warns(List<string> warnings) => new ModEnableResult(true, new List<string>(), warnings);

        public static ModEnableResult Locked(Release lockedRelease) => Warn($"Mod {lockedRelease.Manifest.Name} is locked and will remain unchanged.");

        public static ModEnableResult operator +(ModEnableResult a, ModEnableResult b) {
            return new ModEnableResult(
                a.Successful && b.Successful,
                a.Failures.Concat(b.Failures).ToList(),
                a.Warnings.Concat(b.Warnings).ToList()
            );
        }
    }

    public record ModDisableResult(bool Successful, List<Release> DependencyConflicts, List<Release> Locks) {
        public static ModDisableResult Success => new ModDisableResult(true, new List<Release>(), new List<Release>());

        public static ModDisableResult Conflict(Release conflict) => Conflicts(new List<Release>() { conflict });
        public static ModDisableResult Conflicts(List<Release> conflicts) => new ModDisableResult(false, conflicts, new List<Release>());

        public static ModDisableResult Locked(Release lockedRelease) => Lockeds(new List<Release>() { lockedRelease });

        // Locks happen when a user has manually imposed a lock on a release. This is not a conflict, but it does prevent the mod from being disabled.
        // As such, success is still "true" but the locks are returned so that the user can be informed of them.
        public static ModDisableResult Lockeds(List<Release> lockedReleases) => new ModDisableResult(true, new List<Release>(), lockedReleases);

        public static ModDisableResult operator +(ModDisableResult a, ModDisableResult b) {
            return new ModDisableResult(
                a.Successful && b.Successful,
                a.DependencyConflicts.Concat(b.DependencyConflicts).ToList(),
                a.Locks.Concat(b.Locks).ToList()
            );
        }
    }

    public record ModReleaseDownloadTask(Release Release, DownloadTask DownloadTask);
}
