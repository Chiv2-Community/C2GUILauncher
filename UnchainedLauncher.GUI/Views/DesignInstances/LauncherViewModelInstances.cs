using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class LauncherViewModelInstances {
        public static LauncherViewModel DEFAULT => new LauncherViewModel(
            SettingsViewModelInstances.DEFAULT,
            null, null, null, new MessageBoxSpawner());
    }
}