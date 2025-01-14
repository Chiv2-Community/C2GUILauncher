using LanguageExt;
using LanguageExt.Common;
using System.Diagnostics;
using UnchainedLauncher.Core.Processes;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public interface IUnchainedChivalry2Launcher {
        /// <summary>
        /// Launches a modded instance of the game with additional parameters, supporting server hosting and potentially
        /// DLL Injection.  
        /// </summary>
        /// <param name="launchOptions"></param>
        /// <param name="updateUnchainedDependencies"></param> If updateUnchainedDependencies is true, the implementation is expected to download updates for required launch files.
        /// <param name="args"></param>
        /// <returns>
        /// Left if the game failed to launch.
        /// Right if the game was launched successfully.
        /// </returns>
        public Task<Either<UnchainedLaunchFailure, Process>> Launch(ModdedLaunchOptions launchOptions, bool updateUnchainedDependencies, string args);
    }

    public abstract record UnchainedLaunchFailure(string Message, int Code, Option<Error> Underlying) : Expected(Message, Code, Underlying) {

        public record InjectionFailedError() : UnchainedLaunchFailure("Failed to inject process with unchained code.", 0, None);
        public record LaunchFailedError(LaunchFailed Failure) : UnchainedLaunchFailure(Failure.Message, Failure.Code, Failure.Underlying);
        public record DependencyDownloadFailedError(IEnumerable<string> DependencyNames)
            : UnchainedLaunchFailure($"Failed to download dependencies '{string.Join("', '", DependencyNames)}'", 0, None);
        public record LaunchCancelledError() : UnchainedLaunchFailure($"Launch Cancelled", 0, None);


        public static UnchainedLaunchFailure InjectionFailed() =>
            new InjectionFailedError();
        public static UnchainedLaunchFailure LaunchFailed(LaunchFailed failure) => new LaunchFailedError(failure);
        public static UnchainedLaunchFailure DependencyDownloadFailed(IEnumerable<string> dependencyNames) =>
            new DependencyDownloadFailedError(dependencyNames);

        public static UnchainedLaunchFailure LaunchCancelled() => new LaunchCancelledError();
    }

    public record ModdedLaunchOptions(
        string ServerBrowserBackend,
        bool CheckForDependencyUpdates,
        Option<string> SavedDirSuffix,
        Option<ServerLaunchOptions> ServerLaunchOptions
    ) {
        public IEnumerable<string> ToCLIArgs() {
            var args = new List<string> {
                $"--server-browser-backend {ServerBrowserBackend}"
            };
            ServerLaunchOptions.IfSome(opts => args.AddRange(opts.ToCLIArgs()));

            var suffix = SavedDirSuffix.IfNone("Unchained");
            args.Add($"--saved-dir-suffix {suffix}");

            return args;
        }
    };

    public record ServerLaunchOptions(
        bool Headless,
        string Name,
        string Description,
        Option<string> Password,
        string Map,
        int GamePort,
        int BeaconPort,
        int QueryPort,
        int RconPort
    ) {
        public IEnumerable<String> ToCLIArgs() {
            var args = new List<string>();
            if (Headless) {
                args.Add("-nullrhi");
                args.Add("-unattended");
                args.Add("-nosound");
            }

            Password.IfSome(password => args.Add($"ServerPassword={password.Trim()}"));
            args.Add($"--next-map-name {Map}");
            args.Add($"Port={GamePort}");
            args.Add($"GameServerPingPort={BeaconPort}");
            args.Add($"GameServerQueryPort={QueryPort}");
            args.Add($"--rcon {RconPort}");

            return args;
        }
    };


}