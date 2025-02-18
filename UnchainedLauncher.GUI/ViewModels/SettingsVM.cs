using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Installer;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {
    [AddINotifyPropertyChangedInterface]
    public partial class SettingsVM : IDisposable {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(SettingsVM));
        private static readonly Version Version = Assembly.GetExecutingAssembly().GetName().Version!;

        public InstallationType InstallationType { get; set; }
        public bool EnablePluginAutomaticUpdates { get; set; }
        public string AdditionalModActors { get; set; }
        public string ServerBrowserBackend { get; set; }

        private string _cliArgs;
        public string CLIArgs {
            get => _cliArgs;
            set {
                if (value != _cliArgs) {
                    CLIArgsModified = true;
                }
                _cliArgs = value;
            }
        }
        public bool CLIArgsModified { get; set; }
        public string CurrentVersion {
            get => "v" + Version.ToString(3);
        }

        public bool IsLauncherReusable() => InstallationType == InstallationType.Steam;

        public IPakDir PakDir { get; private set; }

        private IUserDialogueSpawner UserDialogueSpawner { get; }

        public static IEnumerable<InstallationType> AllInstallationTypes {
            get { return Enum.GetValues(typeof(InstallationType)).Cast<InstallationType>(); }
        }

        public FileBackedSettings<LauncherSettings> LauncherSettings { get; set; }
        public IUnchainedLauncherInstaller Installer { get; }
        public IReleaseLocator UnchainedReleaseLocator { get; set; }
        public readonly Action<int> ExitProgram;
        public bool CanClick { get; set; }

        public SettingsVM(IUnchainedLauncherInstaller installer, IReleaseLocator unchainedReleaseLocator, IPakDir pakDir, IUserDialogueSpawner dialogueSpawner, InstallationType installationType, bool enablePluginAutomaticUpdates, string additionalModActors, string serverBrowserBackend, FileBackedSettings<LauncherSettings> launcherSettings, string cliArgs, Action<int> exitProgram) {
            Installer = installer;
            UnchainedReleaseLocator = unchainedReleaseLocator;
            PakDir = pakDir;
            UserDialogueSpawner = dialogueSpawner;
            InstallationType = installationType;
            EnablePluginAutomaticUpdates = enablePluginAutomaticUpdates;
            AdditionalModActors = additionalModActors;
            LauncherSettings = launcherSettings;
            ServerBrowserBackend = serverBrowserBackend;
            ExitProgram = exitProgram;
            CanClick = true;

            _cliArgs = cliArgs;
            CLIArgsModified = false;
        }


        public static SettingsVM LoadSettings(IChivalry2InstallationFinder installationFinder, IUnchainedLauncherInstaller installer, IReleaseLocator unchainedReleaseLocator, IPakDir pakdir, IUserDialogueSpawner userDialogueSpawner, Action<int> exitProgram) {
            var cliArgsList = Environment.GetCommandLineArgs();
            var cliArgs = cliArgsList.Length > 1 ? Environment.GetCommandLineArgs().Skip(1).Aggregate((x, y) => $"{x} {y}") : "";

            var fileBackedSettings = new FileBackedSettings<LauncherSettings>(FilePaths.LauncherSettingsFilePath);
            var loadedSettings = fileBackedSettings.LoadSettings();

            return new SettingsVM(
                installer,
                unchainedReleaseLocator,
                pakdir,
                userDialogueSpawner,
                loadedSettings?.InstallationType ?? DetectInstallationType(installationFinder),
                loadedSettings?.EnablePluginAutomaticUpdates ?? true,
                loadedSettings?.AdditionalModActors ?? "",
                loadedSettings?.ServerBrowserBackend ?? "https://servers.polehammer.net",
                fileBackedSettings,
                cliArgs,
                exitProgram
            );
        }

        public void SaveSettings() {
            LauncherSettings.SaveSettings(
                new LauncherSettings(InstallationType, EnablePluginAutomaticUpdates, AdditionalModActors, ServerBrowserBackend)
            );
        }

        [RelayCommand]
        private void UninstallLauncher() {
            const string originalPath = FilePaths.OriginalLauncherPath;
            const string launcherPath = FilePaths.LauncherPath;

            var originalExists = File.Exists(originalPath);
            var message = new List<string>() {
                "Are you sure? This will disable all mods, reset all settings to their defaults, and uninstall the launcher. This will delete the following:",
                "* All files in .mod_cache",
                "* All files in TBL\\Binaries\\Win64\\Plugins",
                "* All non-vanilla paks in TBL\\Content\\Paks.",
                "",
                originalExists
                ? "IMPORTANT: Wait at least 1 second and then launch normally."
                : "IMPORTANT: The original launcher is not present. You will need to verify game files after this so it can be re-downloaded."
            }.Aggregate((accumulator, next) => accumulator + "\n" + next);

            var choice = UserDialogueSpawner.DisplayYesNoMessage(message, "Really uninstall?");
            Logger.Info($"Are you sure? User selects: {choice}");

            if (choice == UserDialogueChoice.No) return;

            CleanUpInstallation_actions();
            const string replaceCommand = $@" 
                while (Test-Path '{originalPath}' -PathType Leaf) {{
                    try {{
                        Move-Item -Path '{originalPath}' -Destination '{launcherPath}' -Force;
                        break;
                    }} catch {{
                        Start-Sleep -Milliseconds 200;
                        Write-Error $_.Exception.Message;
                    }}
                }};";

            const string deleteCommand = $@" 
                while (Test-Path '{launcherPath}' -PathType Leaf) {{
                    try {{
                        Remove-Item -Path '{launcherPath}' -Force;
                        break;
                    }} catch {{
                        Start-Sleep -Milliseconds 200;
                        Write-Error $_.Exception.Message;
                    }}
                }};";

            // flatten the script so newlines don't cause problems
            var flatReplaceCommand = Regex.Replace(
                replaceCommand,
                @"[\n\r]+\s*",
                " ",
                RegexOptions.Singleline
                );
            // flatten the script so newlines don't cause problems
            var flatDeleteCommand = Regex.Replace(
                deleteCommand,
                @"[\n\r]+\s*",
                " ",
                RegexOptions.Singleline
            );

            // if there's no original, then there's nothing we can do but delete ourselves
            if (originalExists) {
                PowerShell.Run(new List<string>() {
                    $"Wait-Process -Id {Process.GetCurrentProcess().Id}",
                    flatReplaceCommand
                }, false).Dispose();
            }
            else {
                PowerShell.Run(new List<string>() {
                    $"Wait-Process -Id {Process.GetCurrentProcess().Id}",
                    flatDeleteCommand
                }, false).Dispose();
            }

            ExitProgram(0);
        }

        private void CleanUpInstallation_actions() {
            FileHelpers.DeleteDirectory(FilePaths.ModCachePath);
            FileHelpers.DeleteDirectory(FilePaths.PluginDir);

            PakDir.CleanUpDir();
        }

        [RelayCommand]
        private void CleanUpInstallation() {
            Logger.Info("CleanUpInstallation button clicked.");
            var message = new List<string>() {
                "Are you sure? This will disable all mods and reset all settings to their defaults. This will delete the following:",
                "* All files in .mod_cache",
                "* All files in TBL\\Binaries\\Win64\\Plugins",
                "* All non-vanilla paks in TBL\\Content\\Paks.",
                "",
                "After deleting, the launcher will restart itself."
            }.Aggregate((accumulator, next) => accumulator + "\n" + next);

            var choice = UserDialogueSpawner.DisplayYesNoMessage(message, "Really clean up installation?");
            Logger.Info($"Are you sure? User selects: {choice}");

            if (choice == UserDialogueChoice.No) return;
            CleanUpInstallation_actions();
            RestartLauncher();
        }

        private void RestartLauncher() {
            Logger.Info("Restarting launcher...");

            var currentExecutableName = Process.GetCurrentProcess().ProcessName;

            var commandLinePass = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            var powershellCommands = new List<string>() {
                $"Wait-Process -Id {Environment.ProcessId} -ErrorAction 'Ignore'",
                $"Start-Sleep -Milliseconds 500",
                $".\\{currentExecutableName} {commandLinePass}"
            };

            PowerShell.Run(powershellCommands);
            UserDialogueSpawner.DisplayMessage("The launcher will now restart. No further action must be taken.");

            Logger.Info("Closing");
            ExitProgram(0);
        }

        [RelayCommand]
        public async Task CheckForUpdate() {
            Logger.Info("Checking for updates...");

            var latestRelease = await UnchainedReleaseLocator.GetLatestRelease();
            if (latestRelease == null) {
                UserDialogueSpawner.DisplayMessage("Failed to check for updates. Check the logs for more details.");
                return;
            }

            if (latestRelease.Version.ComparePrecedenceTo(new Semver.SemVersion(Version.Major, Version.Minor, Version.Build)) > 0) {
                Logger.Info($"Latest version: {latestRelease.Version}, Current version: {CurrentVersion}");
                await ChangeVersion(latestRelease);
            }
            else {
                UserDialogueSpawner.DisplayMessage("You are currently running the latest version.");
            }
        }

        private async Task ChangeVersion(ReleaseTarget release) {
            var dialogResult =
                UserDialogueSpawner.DisplayUpdateMessage(
                    "Chivalry 2 Unchained Launcher Update",
                    "Update the Unchained Launcher?",
                    "Yes",
                    "No",
                    null,
                    new DependencyUpdate("Launcher", CurrentVersion, release.Version.ToString(), release.PageUrl, "")
                );

            if (dialogResult == UserDialogueChoice.No) {
                Logger.Info("User chose not to update.");
                return;
            }

            if (dialogResult == UserDialogueChoice.Yes) {
                Logger.Info("User chose to update.");
                await Installer.Install(new DirectoryInfo(Environment.CurrentDirectory), release, true, (_) => { });
            }
        }

        public void Dispose() {
            SaveSettings();
            GC.SuppressFinalize(this);
        }

        private static InstallationType DetectInstallationType(IChivalry2InstallationFinder finder) {
            var curDir = new DirectoryInfo(Directory.GetCurrentDirectory());

            if (finder.IsEGSDir(curDir)) return InstallationType.EpicGamesStore;

            if (finder.IsSteamDir(curDir)) return InstallationType.Steam;

            Logger.Warn("Could not detect installation type.");
            return InstallationType.NotSet;
        }
    }

}