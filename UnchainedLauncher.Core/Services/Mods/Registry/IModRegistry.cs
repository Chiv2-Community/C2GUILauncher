using LanguageExt;
using LanguageExt.Common;
using log4net;
using Semver;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods.Registry {

    public record ModIdentifier(string Org, string ModuleName) : IComparable<ModIdentifier> {
        public static ModIdentifier FromMod(Mod mod) =>
            new ModIdentifier(mod.LatestManifest.Organization, mod.LatestManifest.RepoName);

        public static ModIdentifier FromDependency(Dependency dependency) => new ModIdentifier(dependency.Organization, dependency.RepoName);
        public static ModIdentifier FromRelease(Release release) => new ModIdentifier(release.Manifest.Organization, release.Manifest.RepoName);

        public bool Matches(Mod mod) => Org == mod.LatestManifest.Organization && ModuleName == mod.LatestManifest.RepoName;

        // Intentionally not using equality of this == modId here, because the other ModIdentifier may be a ReleaseCoordinate.
        public bool Matches(ModIdentifier modId) => Org == modId.Org && ModuleName == modId.ModuleName;
        public int CompareTo(ModIdentifier? other) =>
            other == null
                ? -1
                : (this.Org, this.ModuleName).CompareTo((other.Org, other.ModuleName));

        public override string ToString() {
            return $"('{Org}','{ModuleName}')";
        }
    }

    public record ReleaseCoordinates(string Org, string ModuleName, string Version) : ModIdentifier(Org, ModuleName), IComparable<ReleaseCoordinates> {
        public static new ReleaseCoordinates FromRelease(Release release) =>
            new ReleaseCoordinates(release.Manifest.Organization, release.Manifest.RepoName, release.Tag);

        public bool Matches(Release release) => Org == release.Manifest.Organization && ModuleName == release.Manifest.RepoName && Version == release.Tag;
        public int CompareTo(ReleaseCoordinates? other) {
            if (other == null) return -1;

            SemVersion.TryParse(Version, SemVersionStyles.Any, out var thisVersion);
            SemVersion.TryParse(other.Version, SemVersionStyles.Any, out var otherVersion);

            return (thisVersion, otherVersion) switch {
                (null, null) =>
                    (this.Org, this.ModuleName, this.Version).CompareTo((other.Org, other.ModuleName, other.Version)),
                (null, _) =>
                    (this.Org, this.ModuleName, new SemVersion(0, 0, 0)).CompareTo((other.Org, other.ModuleName,
                        otherVersion)),
                (_, _) =>
                    (this.Org, this.ModuleName, thisVersion).CompareTo((other.Org, other.ModuleName, otherVersion!)),
            };
        }

        public override string ToString() {
            return $"('{Org}','{ModuleName}', '{Version}')";
        }
    }

    public record GetAllModsResult(IEnumerable<RegistryMetadataException> Errors, IEnumerable<Mod> Mods) {
        public bool HasErrors => Errors.Any();

        public GetAllModsResult AddError(RegistryMetadataException error) {
            return this with { Errors = Errors.Append(error) };
        }

        public GetAllModsResult AddMod(Mod mod) {
            return this with { Mods = Mods.Append(mod) };
        }

        public static GetAllModsResult operator +(GetAllModsResult a, GetAllModsResult b) {
            return new GetAllModsResult(a.Errors.Concat(b.Errors), a.Mods.Concat(b.Mods));
        }
    };

    /// <summary>
    /// A ModRegistry contains all relevant metadata for a repository of mods
    /// A ModRegistry does not necessarily know how to download the mods, however
    /// it will provide the coordinates which can be used to download the mods via
    /// ModRegistryPakFetcher
    /// </summary>
    public interface IModRegistry {
        protected static readonly ILog Logger = LogManager.GetLogger(nameof(IModRegistry));

        public string Name { get; }

        /// <summary>
        /// Get all mod metadata from this registry
        /// </summary>
        /// <returns></returns>
        public Task<GetAllModsResult> GetAllMods();

        /// <summary>
        /// Fetches a mod object from the registry
        /// </summary>
        /// <param name="modId"></param>
        /// <returns></returns>
        public EitherAsync<RegistryMetadataException, Mod> GetMod(ModIdentifier modId);

        /// <summary>
        /// Fetches a specific release from the registry
        /// </summary>
        /// <param name="coords"></param>
        /// <returns></returns>
        public EitherAsync<RegistryMetadataException, Release> GetModRelease(ReleaseCoordinates coords);

        /// <summary>
        /// Download pak
        /// </summary>
        /// <param name="coordinates"></param>
        /// <param name="outputLocation"></param>
        /// <returns></returns>
        public EitherAsync<ModPakStreamAcquisitionFailure, FileWriter> DownloadPak(ReleaseCoordinates coordinates, string outputLocation);
    }

    public record SizedStream(Stream Stream, long Size);
    public record ModPakStreamAcquisitionFailure(ReleaseCoordinates Target, Error Error) : Expected($"Failed to acquire download stream for mod pak '{Target.Org} / {Target.ModuleName} / {Target.Version}", 4000, Some(Error));

    public abstract record RegistryMetadataException(string Message, int Code, Option<Error> Underlying) : Expected(Message, Code, Underlying) {
        public record ParseException(string Message, Option<Error> Underlying)
            : RegistryMetadataException(Message, 0, Underlying);

        public record NotFoundException(ModIdentifier ModId, Option<string> Version, Option<Error> Underlying)
            : RegistryMetadataException(FormatMessage(ModId, Version), 0, Underlying) {
            private static string FormatMessage(ModIdentifier modId, Option<string> version) =>
                $"Failed to get mod metadata for '{modId.Org}/{modId.ModuleName}" +
                version.Map(v => $" / {v}'").IfNone("'");
        };
        public record PackageListRetrievalException(string Message, Option<Error> Underlying) : RegistryMetadataException(Message, 0, Underlying);

        public static RegistryMetadataException Parse(string message, Option<Error> underlying) => new ParseException(message, underlying);
        public static RegistryMetadataException NotFound(ModIdentifier modId, Option<Error> underlying) => new NotFoundException(modId, None, underlying);
        public static RegistryMetadataException NotFound(ReleaseCoordinates coords, Option<Error> underlying) =>
            new NotFoundException(coords, Some(coords.Version), underlying);
        public static RegistryMetadataException PackageListRetrieval(string message, Option<Error> underlying) => new PackageListRetrievalException(message, underlying);

        public T Match<T>(
            Func<ParseException, T> parseExceptionFunc,
            Func<NotFoundException, T> notFoundFunc,
            Func<PackageListRetrievalException, T> packageListRetrievalFunc
        ) => this switch {
            NotFoundException notFound => notFoundFunc(notFound),
            PackageListRetrievalException packageListRetrievalException => packageListRetrievalFunc(packageListRetrievalException),
            ParseException parseError => parseExceptionFunc(parseError),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}