using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services {


    public interface IUserDialogueSpawner {

        public void DisplayMessage(string message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption);

        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates);

        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, DependencyUpdate firstUpdate, params DependencyUpdate[] updates) {
            var allUpdates = new List<DependencyUpdate>();
            allUpdates.Add(firstUpdate);
            allUpdates.AddRange(updates);

            return DisplayUpdateMessage(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, allUpdates);
        }

        /// <summary>
        /// Displays a progress popup window for the user. Returns an action that, when called, will close the window
        /// regardless of whether the progress is completed
        /// </summary>
        /// <param name="progress"></param>
        /// <returns></returns>
        public Action DisplayProgress(MemoryProgress progress);
    }

    public enum UserDialogueChoice {
        Yes,
        No,
        Cancel
    }

    public record DependencyUpdate(string Name, string? CurrentVersion, string LatestVersion, string ReleaseUrl, string Reason) {
        public static DependencyUpdate FromUpdateCandidate(UpdateCandidate modUpdate) {
            return new DependencyUpdate(modUpdate.CurrentlyEnabled.Manifest.Name, modUpdate.CurrentlyEnabled.Tag, modUpdate.AvailableUpdate.Tag, modUpdate.AvailableUpdate.ReleaseUrl, "");
        }
    };
}