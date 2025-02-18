using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.Views;

namespace UnchainedLauncher.GUI.Services {
    public class MessageBoxSpawner : IUserDialogueSpawner {
        public void DisplayMessage(string message) => MessageBox.Show(message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption) =>
            MessageBox.Show(message, caption, MessageBoxButton.YesNo).ToMessageBoxResult();

        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText, string noButtonText,
            string? cancelButtonText, IEnumerable<DependencyUpdate> updates) {
            var result = UpdatesWindow.Show(titleText, messageText, yesButtonText, noButtonText, cancelButtonText,
                from update in updates
                select new DependencyUpdateViewModel(update)
            );

            return result?.ToMessageBoxResult();
        }

        public Action DisplayProgress(MemoryProgress progress) {
            //this code feels super evil
            // set a default "do nothing" action to satisfy compiler's assignment requirements
            Action closeWindowAction = () => { };
            // set a reset MRE (this is like a semaphore holding 0)
            ManualResetEventSlim naughty = new ManualResetEventSlim(false);
            // on the UI-safe thread...
            Application.Current.Dispatcher.Invoke((Action)delegate {
                var window = new ProgressWindow(progress);
                window.Show();
                // set the action via closure that, when invoked, closes the window on the UI-safe thread
                closeWindowAction = () => Application.Current.Dispatcher.Invoke((Action)delegate { window.Close(); });
                // set the MRE to signal the initially calling thread
                // that closeWindowAction is assigned, and it can return
                // (this is like releasing a semaphore)
                naughty.Set();
            });
            // wait for the action on the UI-safe thread to complete
            naughty.Wait();

            return closeWindowAction;
        }
    }

    internal static class MessageBoxResultExtensions {
        public static UserDialogueChoice ToMessageBoxResult(this MessageBoxResult result) =>
            result switch {
                MessageBoxResult.Yes or MessageBoxResult.OK => UserDialogueChoice.Yes,
                MessageBoxResult.Cancel or MessageBoxResult.None => UserDialogueChoice.Cancel,
                MessageBoxResult.No => UserDialogueChoice.No,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
            };
    }
}