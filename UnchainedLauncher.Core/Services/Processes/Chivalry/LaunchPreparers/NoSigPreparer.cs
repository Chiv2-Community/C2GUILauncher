using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.PakDir;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    /// <summary>
    /// Removes all non-vanilla sig files.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class NoSigPreparer : IChivalry2LaunchPreparer<Unit> {
        private readonly ILog _logger = LogManager.GetLogger(nameof(NoSigPreparer));

        public IPakDir PakDir { get; private set; }

        private readonly IUserDialogueSpawner _userDialogueSpawner;

        public static IChivalry2LaunchPreparer<Unit> Create(IPakDir pakDir, IUserDialogueSpawner userDialogueSpawner) =>
            new NoSigPreparer(pakDir, userDialogueSpawner);

        private NoSigPreparer(IPakDir pakDir, IUserDialogueSpawner userDialogueSpawner) {
            PakDir = pakDir;
            _userDialogueSpawner = userDialogueSpawner;
        }

        public Task<Option<Unit>> PrepareLaunch(Unit input) {
            _logger.Info("Ensuring sigs are removed for all modded paks.");
            var result = PakDir.UnSignUnmanaged()
                .Bind(_ =>
                    PakDir.GetInstalledReleases()
                        .Map(PakDir.Unsign)
                        .BindLefts()
                )
                .Match(
                    _ => Some(input),
                    errors => {
                        errors.ToList().ForEach(e => _logger.Error($"Failed to unsign release: {e.Message}"));

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