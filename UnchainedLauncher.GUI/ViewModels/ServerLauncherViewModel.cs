using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.GUI.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels {
    using static LanguageExt.Prelude;

    public partial class ServerLauncherViewModel : IDisposable, INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ServerLauncherViewModel));
        private static readonly string SettingsFilePath = $"{FilePaths.ModCachePath}\\unchained_launcher_server_settings.json";

        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public string ServerPassword { get; set; }
        public int GamePort { get; set; }
        public int RconPort { get; set; }
        public int A2sPort { get; set; }
        public int PingPort { get; set; }
        public string SelectedMap { get; set; }
        public bool ShowInServerBrowser { get; set; }
        public ObservableCollection<string> MapsList { get; set; }
        private IUnchainedChivalry2Launcher Launcher { get; }
        private SettingsViewModel SettingsViewModel { get; }
        private ServersViewModel ServersViewModel { get; }
        public string ButtonToolTip { get; set; }
        public ICommand LaunchServerCommand { get; }
        public ICommand LaunchServerHeadlessCommand { get; }
        public IUserDialogueSpawner UserDialogueSpawner { get; }


        public FileBackedSettings<ServerSettings> SettingsFile { get; }

        private IModManager ModManager { get; }
        //may want to add a mods list here as well,
        //in the hopes of having multiple independent servers running one machine
        //whose settings can be stored/loaded from files

        public ServerLauncherViewModel(SettingsViewModel settingsViewModel, ServersViewModel serversViewModel, IUnchainedChivalry2Launcher launcher, IModManager modManager, IUserDialogueSpawner dialogueSpawner, string serverName, string serverDescription, string serverPassword, string selectedMap, int gamePort, int rconPort, int a2sPort, int pingPort, bool showInServerBrowser, FileBackedSettings<ServerSettings> settingsFile) {

            ServerName = serverName;
            ServerDescription = serverDescription;
            ServerPassword = serverPassword;

            SelectedMap = selectedMap;
            GamePort = gamePort;
            RconPort = rconPort;
            A2sPort = a2sPort;
            PingPort = pingPort;
            ShowInServerBrowser = showInServerBrowser;
            SettingsViewModel = settingsViewModel;

            SettingsFile = settingsFile;

            Launcher = launcher;

            ServersViewModel = serversViewModel;
            ModManager = modManager;
            UserDialogueSpawner = dialogueSpawner;

            ButtonToolTip = "";

            LaunchServerCommand = new AsyncRelayCommand(() => RunServerLaunch(false));
            LaunchServerHeadlessCommand = new AsyncRelayCommand(() => RunServerLaunch(true));

            MapsList = new ObservableCollection<string>();

            using (var defaultMapsListStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnchainedLauncher.GUI.Resources.DefaultMaps.txt")) {
                if (defaultMapsListStream != null) {
                    using var reader = new StreamReader(defaultMapsListStream);

                    var defaultMapsString = reader.ReadToEnd();
                    defaultMapsString
                        .Split("\n")
                        .Select(x => x.Trim())
                        .ToList()
                        .ForEach(MapsList.Add);
                }
            }

            ModManager.EnabledModReleases.SelectMany(x => x.Manifest.Maps).ToList().ForEach(MapsList.Add);
            ModManager.EnabledModReleases.CollectionChanged += ProcessEnabledModsChanged;
        }

        private void ProcessEnabledModsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (e.OldItems != null) {
                foreach (Release modRelease in e.OldItems) {
                    modRelease.Manifest.Maps.ForEach(x => MapsList.Remove(x));
                }
            }

            if (e.NewItems != null) {
                foreach (Release modRelease in e.NewItems) {
                    modRelease.Manifest.Maps.ForEach(MapsList.Add);
                }
            }
        }

        public static ServerLauncherViewModel LoadSettings(SettingsViewModel settingsViewModel, ServersViewModel serversViewModel, IUnchainedChivalry2Launcher chivalry2Launcher, IModManager modManager, IUserDialogueSpawner dialogueSpawner) {
            var fileBackedSettings = new FileBackedSettings<ServerSettings>(SettingsFilePath);
            var loadedSettings = fileBackedSettings.LoadSettings();


            return new ServerLauncherViewModel(
                settingsViewModel,
                serversViewModel,
                chivalry2Launcher,
                modManager,
                dialogueSpawner,
                loadedSettings?.ServerName ?? "Chivalry 2 server",
                loadedSettings?.ServerDescription ?? "",
                loadedSettings?.ServerPassword ?? "",
                loadedSettings?.SelectedMap ?? "FFA_Courtyard",
                loadedSettings?.GamePort ?? 7777,
                loadedSettings?.RconPort ?? 9001,
                loadedSettings?.A2sPort ?? 7071,
                loadedSettings?.PingPort ?? 3075,
                loadedSettings?.ShowInServerBrowser ?? true,
                fileBackedSettings
            );
        }

        public void SaveSettings() {
            SettingsFile.SaveSettings(
                new ServerSettings(
                    ServerName,
                    ServerDescription,
                    ServerPassword,
                    SelectedMap,
                    GamePort,
                    RconPort,
                    A2sPort,
                    PingPort,
                    ShowInServerBrowser
                )
            );
        }

        private async Task RunServerLaunch(bool headless) {
            if (!SettingsViewModel.IsLauncherReusable())
                SettingsViewModel.CanClick = false;

            try {
                var serverLaunchOptions = new ServerLaunchOptions(
                    headless,
                    ServerName,
                    ServerDescription,
                    Some(ServerPassword).Map(pw => pw.Trim()).Filter(pw => pw != ""),
                    SelectedMap,
                    GamePort,
                    PingPort,
                    A2sPort,
                    RconPort
                );

                var options = new ModdedLaunchOptions(
                    SettingsViewModel.ServerBrowserBackend,
                    SettingsViewModel.EnablePluginAutomaticUpdates,
                    None,
                    Some(serverLaunchOptions)
                );

                var launchResult = await Launcher.Launch(options, SettingsViewModel.EnablePluginAutomaticUpdates, SettingsViewModel.CLIArgs);

                launchResult.Match(
                    Left: _ => {
                        UserDialogueSpawner.DisplayMessage($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                        SettingsViewModel.CanClick = true;
                        return None;
                    },
                    Right: process => {
                        CreateChivalryProcessWatcher(process);

                        var ports = new PublicPorts(
                            serverLaunchOptions.GamePort,
                            serverLaunchOptions.BeaconPort,
                            serverLaunchOptions.QueryPort
                        );
                        var serverInfo = new C2ServerInfo() {
                            Ports = ports,
                            Name = serverLaunchOptions.Name,
                            Description = serverLaunchOptions.Description,
                            PasswordProtected = serverLaunchOptions.Password.IsSome,
                            Mods = ModManager.EnabledModReleases.Select(release =>
                                new ServerBrowserMod(
                                    release.Manifest.Name,
                                    release.Version.ToString(),
                                    release.Manifest.Organization
                                )
                            ).ToArray()
                        };

                        ServersViewModel.RegisterServer("127.0.0.1", serverLaunchOptions.RconPort, serverInfo, process);

                        return Some(process);
                    }
                );
            }
            catch (Exception ex) {
                UserDialogueSpawner.DisplayMessage("Failed to launch. Check the logs for details.");
                logger.Error("Failed to launch.", ex);
            }
        }

        public void Dispose() {
            SaveSettings();
            GC.SuppressFinalize(this);
        }

        private Thread CreateChivalryProcessWatcher(Process process) {
            var thread = new Thread(async void () => {
                await process.WaitForExitAsync();
                if (SettingsViewModel.IsLauncherReusable()) SettingsViewModel.CanClick = true;

                if (process.ExitCode == 0) return;

                logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                UserDialogueSpawner.DisplayMessage($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
            });

            thread.Start();

            return thread;
        }
    }
}