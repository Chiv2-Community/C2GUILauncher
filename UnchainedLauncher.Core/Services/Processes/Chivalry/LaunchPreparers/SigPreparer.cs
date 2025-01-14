using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    /// <summary>
    /// Removes all non-vanilla sig files.
    /// Ignores the input parameters, returning them unmodified.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SigPreparer : IChivalry2LaunchPreparer<Unit> {
        private readonly ILog logger = LogManager.GetLogger(nameof(SigPreparer));

        private IUserDialogueSpawner UserDialogueSpawner;

        public static IChivalry2LaunchPreparer<Unit> Create(IUserDialogueSpawner userDialogueSpawner) =>
            new SigPreparer(userDialogueSpawner);

        private SigPreparer(IUserDialogueSpawner userDialogueSpawner) {
            UserDialogueSpawner = userDialogueSpawner;
        }

        public async Task<Option<Unit>> PrepareLaunch(Unit input) {
            logger.Info("Ensuring sigs are set up for all pak files.");
            var result = await Task.Run(() =>
                SigFileHelper.CheckAndCopySigFiles() && SigFileHelper.DeleteOrphanedSigFiles()
            );

            if (result) return Some(input);

            UserDialogueSpawner.DisplayMessage("Failed to prepare sig files. Check the logs for more information.");
            return None;
        }
    }
}