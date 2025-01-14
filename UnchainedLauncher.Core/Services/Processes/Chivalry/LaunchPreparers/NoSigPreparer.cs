using LanguageExt;
using log4net;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    /// <summary>
    /// Removes all non-vanilla sig files.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class NoSigPreparer : IChivalry2LaunchPreparer<Unit> {
        private readonly ILog logger = LogManager.GetLogger(nameof(NoSigPreparer));

        private IUserDialogueSpawner UserDialogueSpawner;

        public static IChivalry2LaunchPreparer<Unit> Create(IUserDialogueSpawner userDialogueSpawner) =>
            new NoSigPreparer(userDialogueSpawner);

        private NoSigPreparer(IUserDialogueSpawner userDialogueSpawner) {
            UserDialogueSpawner = userDialogueSpawner;
        }

        public async Task<Option<Unit>> PrepareLaunch(Unit input) {
            logger.Info("Ensuring sigs are removed for all modded paks.");
            var result = await Task.Run(SigFileHelper.RemoveAllNonDefaultSigFiles);
            if (result) return Some(input);

            UserDialogueSpawner.DisplayMessage("Failed to remove mod sig files. Check the logs for more details.");
            return None;
        }
    }
}