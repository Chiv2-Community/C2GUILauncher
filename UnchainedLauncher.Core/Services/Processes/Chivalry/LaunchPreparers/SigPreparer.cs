using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.PakDir;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    /// <summary>
    /// Removes all non-vanilla sig files.
    /// Ignores the input parameters, returning them unmodified.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SigPreparer : IChivalry2LaunchPreparer<Unit> {
        public IPakDir PakDir { get; private set; }
        private readonly ILog _logger = LogManager.GetLogger(nameof(SigPreparer));

        private readonly IUserDialogueSpawner _userDialogueSpawner;

        public static IChivalry2LaunchPreparer<Unit> Create(IPakDir pakDir, IUserDialogueSpawner userDialogueSpawner) =>
            new SigPreparer(pakDir, userDialogueSpawner);

        private SigPreparer(IPakDir pakDir, IUserDialogueSpawner userDialogueSpawner) {
            PakDir = pakDir;
            _userDialogueSpawner = userDialogueSpawner;
        }

        public Task<Option<Unit>> PrepareLaunch(Unit input) {
            _logger.Info("Ensuring sigs are set up for all pak files.");
            var result = PakDir.SignUnmanaged()
                .Bind(_ =>
                    PakDir.GetInstalledReleases()
                        .Map(PakDir.Sign)
                        .BindLefts()
                )
                .Bind(_ => PakDir.DeleteOrphanedSigs())
                .Match(
                    _ => Some(input),
                    errors => {
                        errors.ToList().ForEach(e => _logger.Error($"Failed to sign file: {e.Message}"));

                        _userDialogueSpawner.DisplayMessage(
                            $"There were errors while installing releases:\n " +
                            $"{errors.Aggregate((a, b) => $"{a}\n\n{b}")}"
                        );
                        return None;
                    }
                );

            return Task.FromResult(result);
        }
    }
}