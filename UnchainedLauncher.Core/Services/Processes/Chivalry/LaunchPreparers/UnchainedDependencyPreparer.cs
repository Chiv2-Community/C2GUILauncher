using LanguageExt;
using log4net;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    // TODO: Break up this class further. There are several components in here that should be reusable
    //       - Aggregating a list of pending updates
    //       - Asking user if the new things should be downloaded/updated
    //       - Downloading mod manager files
    //       - Downloading dlls/non-mod manager files
    //       
    //       Ideally what would happen is there would be some pipeline, where we could keep on adding mods and other 
    //       things which require acquisition, append them all in to ModdedLaunchOpts (or some intermediate structure),
    //       display the notification, then download.
    //
    //       Doing that would make this all a lot easier to test while also being much more flexible.
    //
    //       See the 'kleisli-stuff' branch for some ideas I had while working toward this.
    public class UnchainedDependencyPreparer : IChivalry2LaunchPreparer<ModdedLaunchOptions> {
        private readonly ILog logger = LogManager.GetLogger(typeof(UnchainedDependencyPreparer));
        private readonly string pluginPath;

        public static IChivalry2LaunchPreparer<ModdedLaunchOptions> Create(
            IModManager modManager,
            IReleaseLocator pluginReleaseLocator,
            IVersionExtractor fileVersionExtractor,
            IUserDialogueSpawner userDialogueSpawner
        ) => new UnchainedDependencyPreparer(modManager, pluginReleaseLocator, fileVersionExtractor, userDialogueSpawner);

        private UnchainedDependencyPreparer(
            IModManager modManager,
            IReleaseLocator pluginReleaseLocator,
            IVersionExtractor fileVersionExtractor,
            IUserDialogueSpawner userDialogueSpawner) {
            ModManager = modManager;
            PluginReleaseLocator = pluginReleaseLocator;
            FileVersionExtractor = fileVersionExtractor;
            UserDialogueSpawner = userDialogueSpawner;
            pluginPath = Path.Combine(Directory.GetCurrentDirectory(), FilePaths.UnchainedPluginPath);
        }

        private IModManager ModManager { get; }
        private IReleaseLocator PluginReleaseLocator { get; }
        private IVersionExtractor FileVersionExtractor { get; }
        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public async Task<Option<ModdedLaunchOptions>> PrepareLaunch(ModdedLaunchOptions options) {
            if (!ShouldCheckForUpdate(options))
                return Some(options);

            var updates = await GetRequiredUpdates();
            if (!updates.Any())
                return Some(options);

            var userChoice = ShowUpdateDialog(updates);

            return userChoice switch {
                UserDialogueChoice.Yes => await PerformUpdates(updates) ? Some(options) : None,
                UserDialogueChoice.No => Some(options), // Don't install anything, continue launch anyway
                UserDialogueChoice.Cancel => None, // Cancel launch
            };
        }

        private bool ShouldCheckForUpdate(ModdedLaunchOptions options) {
            var pluginExists = File.Exists(pluginPath);
            var isUnchainedModsEnabled = ModManager.EnabledModReleases.Exists(IsUnchainedMods);
            return options.CheckForDependencyUpdates || !pluginExists || !isUnchainedModsEnabled;
        }

        private async Task<IEnumerable<DependencyUpdate>> GetRequiredUpdates() {
            var updates = new List<DependencyUpdate?>();

            updates.Add(await GetPluginUpdate());
            updates.Add(GetModsUpdate());

            return updates.Filter(x => x != null)!;
        }

        private async Task<DependencyUpdate?> GetPluginUpdate() {
            var latestPlugin = await PluginReleaseLocator.GetLatestRelease();
            if (latestPlugin == null) {
                logger.Warn("Could not find latest plugin");
                return null;
            }

            var currentVersion = FileVersionExtractor.GetVersion(pluginPath);

            if (currentVersion?.ComparePrecedenceTo(latestPlugin.Version) >= 0)
                return null;

            return new DependencyUpdate(
                "UnchainedPlugin.dll",
                currentVersion?.ToString(),
                latestPlugin.Version.ToString(),
                latestPlugin.PageUrl,
                "Used for hosting and connecting to player owned servers. Required to run Chivalry 2 Unchained."
            );
        }

        private DependencyUpdate? GetModsUpdate() {
            var isUnchainedModsEnabled = ModManager.EnabledModReleases.Exists(IsUnchainedMods);
            var latestUnchainedMods = ModManager.Mods
                .SelectMany(x => x.LatestRelease)
                .Find(IsUnchainedMods)
                .FirstOrDefault();

            if (latestUnchainedMods == null) {
                logger.Warn("Could not find any unchained mods release.");
                return null;
            }

            if (!isUnchainedModsEnabled) {
                return new DependencyUpdate(
                    latestUnchainedMods.Manifest.Name,
                    null,
                    latestUnchainedMods.Version.ToString(),
                    latestUnchainedMods.ReleaseUrl,
                    "Adds necessary Unchained content to Chivalry 2"
                );
            }

            var updateCandidate = ModManager.GetUpdateCandidates()
                .Find(x => IsUnchainedMods(x.AvailableUpdate))
                .FirstOrDefault();

            return updateCandidate != null
                ? DependencyUpdate.FromUpdateCandidate(updateCandidate)
                : null;

        }

        private UserDialogueChoice? ShowUpdateDialog(IEnumerable<DependencyUpdate> updates) {
            var pluginExists = File.Exists(pluginPath);
            var title = pluginExists ? "Update Required Unchained Dependencies" : "Install Required Unchained Dependencies";
            var message = pluginExists ? "Updates for the Unchained Dependencies are available." : "The Unchained Dependencies are not installed.";

            return UserDialogueSpawner.DisplayUpdateMessage(
                title,
                message,
                "Yes",
                "No",
                "Cancel",
                updates
            );
        }

        private async Task<bool> PerformUpdates(IEnumerable<DependencyUpdate> updates) {
            foreach (var update in updates) {
                if (update.Name == "UnchainedPlugin.dll") {
                    var latestPlugin = await PluginReleaseLocator.GetLatestRelease();
                    if (!await UpdatePlugin(latestPlugin!))
                        return false;
                }
                else {
                    var latestMods = ModManager.Mods
                        .SelectMany(x => x.LatestRelease)
                        .Find(IsUnchainedMods)
                        .FirstOrDefault();

                    if (latestMods != null && !await UpdateMods(latestMods))
                        return false;
                }
            }
            return true;
        }

        private async Task<bool> UpdatePlugin(ReleaseTarget latestPlugin) {
            var downloadResult = await HttpHelpers.DownloadReleaseTarget(
                latestPlugin,
                asset => (asset.Name == "UnchainedPlugin.dll") ? pluginPath : null
            );

            if (!downloadResult) {
                UserDialogueSpawner.DisplayMessage(
                    "Failed to download Unchained Plugin. Aborting launch. Check the logs for more details.");
                return false;
            }
            return true;
        }

        private async Task<bool> UpdateMods(Release latestMods) {
            var result = await ModManager.EnableModRelease(latestMods, None, CancellationToken.None);
            if (result.IsLeft) {
                var error = result.LeftToSeq().FirstOrDefault()!;
                logger.Error("Failed to download latest Unchained-Mods", error);
                UserDialogueSpawner.DisplayMessage(
                    "Failed to download latest Unchained-Mods. Aborting launch. Check the logs for more details.");
                return false;
            }
            return true;
        }

        private static bool IsUnchainedMods(Release release) =>
            release.Manifest.RepoName == "Unchained-Mods" &&
            release.Manifest.Organization == "Chiv2-Community";
    }
}