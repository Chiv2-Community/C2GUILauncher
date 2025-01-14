using LanguageExt;
using log4net;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.GUI.ViewModels;
using UnchainedLauncher.GUI.ViewModels.Installer;
using UnchainedLauncher.GUI.Views;
using UnchainedLauncher.GUI.Views.Installer;

namespace UnchainedLauncher.GUI {
    using static LanguageExt.Prelude;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private readonly ILog _log = LogManager.GetLogger(typeof(App));

        public App() : base() { }
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            var assembly = Assembly.GetExecutingAssembly();
            if (File.Exists("log4net.config")) {
                log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
            }
            else if (File.Exists("Resources/log4net.config")) {
                // for running in Visual Studio
                log4net.Config.XmlConfigurator.Configure(new FileInfo("Resources/log4net.config"));
            }
            else {
                using Stream? configStream = assembly.GetManifestResourceStream("UnchainedLauncher.GUI.Resources.log4net.config");
                if (configStream != null) {
                    log4net.Config.XmlConfigurator.Configure(configStream);
                }
            }


            // Init common dependencies
            var githubClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("UnchainedLauncher"));
            var unchainedLauncherReleaseLocator = new GithubReleaseLocator(githubClient, "Chiv2-Community", "UnchainedLauncher");
            var pluginReleaseLocator = new GithubReleaseLocator(githubClient, "Chiv2-Community", "UnchainedPlugin");


            var installationFinder = new Chivalry2InstallationFinder();
            var installer = new UnchainedLauncherInstaller(Environment.Exit);

            // figure out if we need to install by checking our current working directory
            var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            var needsInstallation = !installationFinder.IsValidInstallation(currentDirectory);

            var forceSkipInstallation = Environment.GetCommandLineArgs().ToList().Contains("--no-install");

            if (forceSkipInstallation && needsInstallation)
                _log.Info("Skipping installation");

            var createWindowTask =
                needsInstallation && !forceSkipInstallation
                    ? InitializeInstallerWindow(installationFinder, installer, unchainedLauncherReleaseLocator)
                    : InitializeMainWindow(installationFinder, installer, unchainedLauncherReleaseLocator, pluginReleaseLocator);

            createWindowTask.Wait();
            createWindowTask.Result?.Show();
        }

        public async Task<Window?> InitializeInstallerWindow(Chivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator) {
            var installationSelectionVM = new InstallationSelectionPageViewModel(installationFinder);
            var versionSelectionVM = new VersionSelectionPageViewModel(launcherReleaseLocator);
            var installationLogVM = new InstallerLogPageViewModel(
                installer,
                () =>
                      from chiv2Installations in installationSelectionVM.Installations
                      where chiv2Installations.IsSelected
                      select chiv2Installations.Path
                ,
                () => versionSelectionVM.SelectedVersion!
            );

            var installerPageViewModels = new ObservableCollection<IInstallerPageViewModel> {
                installationSelectionVM,
                versionSelectionVM,
                installationLogVM
            };

            var installerWindowVM = new InstallerWindowViewModel(installerPageViewModels, installationSelectionVM.Installations);
            return new InstallerWindow(installerWindowVM);
        }

        public async Task<Window?> InitializeMainWindow(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer, IReleaseLocator launcherReleaseLocator, IReleaseLocator pluginReleaseLocator) {
            var userDialogueSpawner = new MessageBoxSpawner();

            var settingsViewModel = SettingsViewModel.LoadSettings(installationFinder, installer, launcherReleaseLocator, userDialogueSpawner, Environment.Exit);

            var modManager = ModManager.ForRegistries(
                new GithubModRegistry("Chiv2-Community", "C2ModRegistry", HttpPakDownloader.GithubPakDownloader)
            );

            var officialProcessLauncher = new ProcessLauncher(Path.Combine(Directory.GetCurrentDirectory(), FilePaths.OriginalLauncherPath));
            var unchainedProcessLauncher = new ProcessLauncher(Path.Combine(Directory.GetCurrentDirectory(), FilePaths.GameBinPath));

            var noSigLaunchPreparer = NoSigPreparer.Create(userDialogueSpawner);
            var sigLaunchPreparer = SigPreparer.Create(userDialogueSpawner);

            var unchainedContentPreparer = UnchainedDependencyPreparer.Create(
                modManager,
                pluginReleaseLocator,
                new FileInfoVersionExtractor(),
                userDialogueSpawner);

            var vanillaLauncher = new OfficialChivalry2Launcher(
                noSigLaunchPreparer,
                officialProcessLauncher,
                Directory.GetCurrentDirectory()
            );

            var clientsideModdedLauncher = new OfficialChivalry2Launcher(
                sigLaunchPreparer,
                officialProcessLauncher,
                Directory.GetCurrentDirectory()
            );

            var unchainedLauncher = new UnchainedChivalry2Launcher(
                unchainedContentPreparer.Sub(sigLaunchPreparer, _ => Unit.Default),
                unchainedProcessLauncher,
                Directory.GetCurrentDirectory(),
                new DllInjector(Path.Combine(Directory.GetCurrentDirectory(), FilePaths.PluginDir))
            );

            var serversViewModel = new ServersViewModel(settingsViewModel, null);
            var launcherViewModel = new LauncherViewModel(settingsViewModel, vanillaLauncher, clientsideModdedLauncher, unchainedLauncher, userDialogueSpawner);
            var serverLauncherViewModel = ServerLauncherViewModel.LoadSettings(settingsViewModel, serversViewModel, unchainedLauncher, modManager, userDialogueSpawner);
            var modListViewModel = new ModListViewModel(modManager, userDialogueSpawner);

            modListViewModel.RefreshModListCommand.Execute(null);

            var envArgs = Environment.GetCommandLineArgs().ToList();

            // TODO: Replace this if/else chain with a real CLI
            if (envArgs.Contains("--startvanilla")) {
                await launcherViewModel.LaunchVanilla(false);
                return null;
            }
            else if (envArgs.Contains("--startmodded")) {
                await launcherViewModel.LaunchVanilla(true);
                return null;
            }
            else if (envArgs.Contains("--startunchained")) {
                await launcherViewModel.LaunchUnchained();
                return null;
            }

            var mainWindowViewModel = new MainWindowViewModel(
                launcherViewModel,
                modListViewModel,
                settingsViewModel,
                serverLauncherViewModel,
                serversViewModel
            );

            return new MainWindow(mainWindowViewModel);
        }
    }
}