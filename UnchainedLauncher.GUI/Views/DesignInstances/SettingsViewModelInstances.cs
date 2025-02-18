using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.GUI.JsonModels;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class SettingsViewModelInstances {
        public static SettingsVM DEFAULT => new SettingsVM(
            new MockInstaller(),
            null,
            null,
            new MessageBoxSpawner(),
            InstallationType.Steam,
            true,
            "--design-time-only-default-constructor",
            "https://servers.polehammer.net",
            new FileBackedSettings<LauncherSettings>(""), "", (_) => { }
        );
    }
}