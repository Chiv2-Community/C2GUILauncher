using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    using static Prelude;

    public class UnchainedChivalry2Launcher : IUnchainedChivalry2Launcher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(UnchainedLauncher));

        private IProcessLauncher Launcher { get; }
        private IChivalry2LaunchPreparer<ModdedLaunchOptions> LaunchPreparer { get; }
        private string InstallationRootDir { get; }
        private IProcessInjector ProcessInjector { get; }

        public UnchainedChivalry2Launcher(
            IChivalry2LaunchPreparer<ModdedLaunchOptions> preparer,
            IProcessLauncher processLauncher,
            string installationRootDir,
            IProcessInjector processInjector) {

            InstallationRootDir = installationRootDir;
            ProcessInjector = processInjector;

            LaunchPreparer = preparer;
            Launcher = processLauncher;
        }

        public async Task<Either<UnchainedLaunchFailure, Process>> Launch(ModdedLaunchOptions launchOptions, bool updateUnchainedDependencies, string args) {
            logger.Info("Attempting to launch modded game.");

            var updatedLaunchOpts = await LaunchPreparer.PrepareLaunch(launchOptions);
            if (updatedLaunchOpts.IsNone) {
                return Left(UnchainedLaunchFailure.LaunchCancelled());
            }

            var moddedLaunchArgs = args;
            var tblLoc = moddedLaunchArgs.IndexOf("TBL", StringComparison.Ordinal);
            var offsetIndex = tblLoc == -1 ? 0 : tblLoc + 3;

            var launchOpts = updatedLaunchOpts.Map(x => x.ToCLIArgs()).ValueUnsafe();

            moddedLaunchArgs.Insert(offsetIndex, " " + launchOpts);

            logger.Info($"Launch args: {moddedLaunchArgs}");

            var launchResult = Launcher.Launch(Path.Combine(InstallationRootDir, FilePaths.BinDir), moddedLaunchArgs);

            return launchResult.Match(
                Left: error => {
                    logger.Error(error);
                    return Left(UnchainedLaunchFailure.LaunchFailed(error));
                },
                Right: proc => {
                    logger.Info("Successfully launched Chivalry 2 Unchained");
                    return InjectDLLs(proc);
                }
            );
        }

        private Either<UnchainedLaunchFailure, Process> InjectDLLs(Process process) {
            if (ProcessInjector.Inject(process)) return Right(process);

            process.Kill();
            return Left(UnchainedLaunchFailure.InjectionFailed());
        }
    }
}