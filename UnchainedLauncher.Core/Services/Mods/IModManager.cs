using LanguageExt;
using System.Collections.Immutable;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods {

    public record UpdateCandidate(Release CurrentlyEnabled, Release AvailableUpdate) {
        public static Option<UpdateCandidate> CreateIfNewer(Release currentlyEnabled, Release availableUpdate) {
            return (availableUpdate.Version != null && availableUpdate.Version.ComparePrecedenceTo(currentlyEnabled.Version) > 0)
                ? Some(new UpdateCandidate(currentlyEnabled, availableUpdate))
                : None;
        }
    }

    public delegate void ModEnabledHandler(Release enabledRelease, string? previousVersion);
    public delegate void ModDisabledHandler(Release disabledRelease);

    public interface IModManager {
        /// <summary>
        /// Triggered any time the EnabledModReleases collection has an item added
        /// </summary>
        event ModEnabledHandler ModEnabled;

        /// <summary>
        /// Triggered any time the EnabledModReleases collection has an item removed
        /// </summary>
        event ModDisabledHandler ModDisabled;


        /// <summary>
        /// A List of all currently enabled mods
        /// </summary>
        IEnumerable<ReleaseCoordinates> EnabledModReleaseCoordinates { get; }

        /// <summary>
        /// A List of all mods which are available to be enabled.
        /// This list may be empty if UpdateModsList has not been called yet.
        /// </summary>
        IEnumerable<Mod> Mods { get; }

        /// <summary>
        /// Disables the given mod if it is enabled. This may update some local metadata that the mod manager uses to
        /// keep track of what is enabled, but will not actually delete the mod files.
        ///
        /// Disabling an already disabled mod results in a noop
        /// Disabling the wrong version for an enabled mod will result in a noop
        /// </summary>
        /// <param name="release">The release to disable</param>
        /// <returns>bool indicating whether or not the operation was successful</returns>
        bool DisableModRelease(ReleaseCoordinates release);

        /// <summary>
        /// Enables the given release for the given mod. This may update some local metadata that the mod manager uses to
        /// keep track of what is enabled, but will not actually download the mod.
        /// 
        /// Enabling an already enabled mod results in a noop
        /// Enabling a different version for an already enabled mod will disable the currently enabled version
        /// </summary>
        /// <param name="coordinates"></param>
        /// <returns>bool indicating whether or not the operation was successful</returns>
        bool EnableModRelease(ReleaseCoordinates coordinates);

        /// <summary>
        /// Enables the latest version of a given mod if it is disabled. This may update some local metadata that the
        /// mod manager uses to keep track of what is enabled, but will not actually download the mod files.
        /// 
        /// Enabling an already enabled mod results in a noop
        /// </summary>
        /// <param name="identifier"></param>
        /// <returns>bool indicating whether or not the operation was successful</returns>
        bool EnableMod(ModIdentifier identifier);


        /// <summary>
        /// Disables the given mod if it is enabled. This may update some local metadata that the mod manager uses to
        /// keep track of what is enabled, but will not actually delete the mod files.
        /// 
        /// Disabling an already disabled mod results in a noop
        /// </summary>
        /// <param name="identifier"></param>
        /// <returns>bool indicating whether or not the operation was successful</returns>
        bool DisableMod(ModIdentifier identifier);


        /// <summary>
        /// Fetches all mod metadata from all registries, returning a list of successfully fetched
        /// mod metadata, as well as a list of failures containing any potential errors.  If this 
        /// IModManager implementation has a cached internal state, this method may update that 
        /// internal state.
        /// </summary>
        /// <returns>A Task containing a GetAllModsResult which has the aggregate list of errors and mods from all registries this ModManager instance has</returns>
        Task<GetAllModsResult> UpdateModsList();
    }

    public static class ModManagerExtensions {
        public static bool EnableMod(this IModManager modManager, Mod mod) =>
            modManager.EnableMod(ModIdentifier.FromMod(mod));

        public static bool EnableModRelease(this IModManager modManager, Release release) =>
            modManager.EnableModRelease(ReleaseCoordinates.FromRelease(release));

        public static bool DisableMod(this IModManager modManager, Mod mod) =>
            modManager.DisableMod(ModIdentifier.FromMod(mod));

        public static bool DisableModRelease(this IModManager modManager, Release release) =>
            modManager.DisableModRelease(ReleaseCoordinates.FromRelease(release));

        public static IEnumerable<Release> GetEnabledModReleases(this IModManager modManager) =>
            modManager.EnabledModReleaseCoordinates
                .Map(modManager.GetRelease)
                .Bind(x => x.ToList());

        /// <summary>
        /// Finds the currently enabled release for the given mod, if any
        /// </summary>
        /// <returns></returns>
        public static Option<Release> GetCurrentlyEnabledReleaseForMod(this IModManager modManager, ModIdentifier modId) =>
            modManager.EnabledModReleaseCoordinates
                .Find(x => x.Matches(modId))
                .Bind<Release>(releaseCoords =>
                    modManager.Mods.Find(modId.Matches)
                        .Bind<Release>(x => x.Releases.Find(releaseCoords.Matches))
                );

        public static Option<Release> GetCurrentlyEnabledReleaseForMod(this IModManager modManager, Mod mod) =>
            modManager.GetCurrentlyEnabledReleaseForMod(ModIdentifier.FromMod(mod));

        /// <summary>
        /// Returns a list of all mods which have updates available
        /// </summary>
        /// <returns>A List of all available updates</returns>
        public static IEnumerable<UpdateCandidate> GetUpdateCandidates(this IModManager modManager) {
            return modManager.Mods
                .SelectMany(mod =>
                    (modManager.GetCurrentlyEnabledReleaseForMod(ModIdentifier.FromMod(mod)), mod.LatestRelease)
                    .Sequence() // Convert (Option<Release>, Option<Release>) to Option<(Release, Release)>
                    .Bind(tuple => UpdateCandidate.CreateIfNewer(tuple.Item1, tuple.Item2))
                );
        }

        /// <summary>
        /// Gets the latest available release for the provided modId
        /// </summary>
        /// <param name="modManager"></param>
        /// <param name="modId"></param>
        /// <returns></returns>
        public static Option<Release> GetLatestRelease(this IModManager modManager, ModIdentifier modId) =>
            modManager.Mods.Find(m => ModIdentifier.FromMod(m).Matches(modId))
                .Bind(m => m.LatestRelease);

        /// <summary>
        /// Gets the specified release from the cached mods list
        /// </summary>
        /// <param name="modManager"></param>
        /// <param name="coordinates"></param>
        /// <returns></returns>
        public static Option<Release> GetRelease(this IModManager modManager, ReleaseCoordinates coordinates) =>
            modManager.Mods.Filter(coordinates.Matches)
                .Bind(x => x.Releases)
                .Find(coordinates.Matches);

        /// <summary>
        /// Traverses all dependencies of the release associated with the provided coordinates
        /// Returns a list of the found dependencies
        /// TODO: Return a tree structure so you can tell which dependencies are associated with what. Maybe.
        /// </summary>
        /// <param name="modManager"></param>
        /// <param name="coordinates"></param>
        /// <returns></returns>
        public static IEnumerable<Release> GetAllDependenciesForRelease(this IModManager modManager, ReleaseCoordinates coordinates) {
            return modManager.AggregateUniqueDependencies(coordinates, ImmutableHashSet<Release>.Empty);
        }

        public static IEnumerable<Release> GetAllDependenciesForRelease(this IModManager modManager, Release release) =>
            modManager.GetAllDependenciesForRelease(ReleaseCoordinates.FromRelease(release));

        /// <summary>
        /// Traverses all dependencies of the release associated with the provided release coordinates.
        /// Ignores any dependencies contained in the 'existingDependencies' collection as well as their children,
        /// and does not include them in the result.
        /// </summary>
        /// <param name="modManager"></param>
        /// <param name="coordinates"></param>
        /// <param name="existingDependencies"></param>
        /// <returns></returns>
        public static IEnumerable<Release> GetNewDependenciesForRelease(this IModManager modManager, ReleaseCoordinates coordinates, IEnumerable<Release> existingDependencies) {
            return modManager.AggregateUniqueDependencies(
                coordinates,
                existingDependencies.ToImmutableHashSet()
            );
        }

        public static IEnumerable<ReleaseCoordinates> GetEnabledAndDependencies(this IModManager modManager) {
            var enabled = modManager.GetEnabledAndDependencyReleases()
                .Map(ReleaseCoordinates.FromRelease);

            // make sure unchained-mods always comes first
            var unchainedMods = new ModIdentifier("Chiv2-Community", "Unchained-Mods");
            var installedUnchainedMods = enabled
                .Filter(r => r.Matches(unchainedMods))
                .ToOption();

            return installedUnchainedMods.Match(
                r => Lst<ReleaseCoordinates>.Empty.Add(r)
                    .Append(enabled.Filter(release => !release.Matches(unchainedMods))),
                () => enabled
                );
        }

        public static IEnumerable<Release> GetEnabledAndDependencyReleases(this IModManager modManager) {
            var modsSet = modManager
                .GetEnabledModReleases()
                .ToImmutableHashSet();

            return modsSet
                .Fold(
                    modsSet,
                    (s, r) =>
                        s.Union(modManager.GetNewDependenciesForRelease(r, s))
                    );
        }

        public static IEnumerable<Release> GetNewDependenciesForRelease(this IModManager modManager, Release release, IEnumerable<Release> existingDependencies) =>
            modManager.AggregateUniqueDependencies(
                ReleaseCoordinates.FromRelease(release),
                existingDependencies.ToImmutableHashSet()
            );

        private static ImmutableHashSet<Release> AggregateUniqueDependencies(this IModManager modManager, ReleaseCoordinates coordinates, ImmutableHashSet<Release> seenDependencies) {
            var newDependencies =
                modManager.GetLatestRelease(coordinates)
                    .ToList()
                    .Bind(release => release.Manifest.Dependencies)
                    .Map(ModIdentifier.FromDependency)
                    .Bind(id => modManager.GetLatestRelease(id).ToList()) // Get the latest release for each dependency (we're ignoring dependency version range requirements currently)
                    .Filter(dep => !seenDependencies.Contains(dep)) // Filter out any dependency we've already seen
                    .ToList();

            var updatedSeenDependencies = seenDependencies.Append(newDependencies).ToImmutableHashSet();

            return newDependencies
                .Fold(
                    updatedSeenDependencies,
                    (aggregateSeenDependencies, newRelease) =>
                        modManager.AggregateUniqueDependencies(ReleaseCoordinates.FromRelease(newRelease), aggregateSeenDependencies)
                );
        }
    }
}