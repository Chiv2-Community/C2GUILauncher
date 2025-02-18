using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using LanguageExt.Pipes;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.API;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab {
    using static LanguageExt.Prelude;
    using static Successors;
    [AddINotifyPropertyChangedInterface]
    public partial class ServersTabVM : IDisposable, INotifyPropertyChanged {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ServersTabVM));
        public SettingsVM Settings { get; }
        public IModRegistry ModRegistry { get; }
        public readonly IChivalry2Launcher Launcher;
        public Func<IModManager> ModManagerCreator;
        public IUserDialogueSpawner DialogueSpawner;
        public FileBackedSettings<IEnumerable<SavedServerTemplate>>? SaveLocation;
        public ObservableCollection<ServerTemplateVM> ServerTemplates { get; }
        public ObservableCollection<(ServerTemplateVM template, ServerVM live)> RunningTemplates { get; } = new();

        private void OnTemplateFormChanged(object? o, PropertyChangedEventArgs e) {
            // this saves all templates, but since we're not using a very complex
            // file format, there's no real way to save just one without just saving
            // all of them. Partial updates don't give value since the data size
            // is so small.
            Save();
        }

        private void OnTemplateModManagerModDisabled(Release r) {
            Save();
        }

        private void OnTemplateModManagerModEnabled(Release r, string? prev) {
            Save();
        }

        private ServerTemplateVM? _selectedTemplate;
        public ServerTemplateVM? SelectedTemplate {
            get => _selectedTemplate;
            set {
                if (_selectedTemplate != null) {
                    _selectedTemplate.Form.PropertyChanged -= OnTemplateFormChanged;
                    _selectedTemplate.ModList._modManager.ModEnabled -= OnTemplateModManagerModEnabled;
                    _selectedTemplate.ModList._modManager.ModDisabled -= OnTemplateModManagerModDisabled;
                }

                if (value != null) {
                    value.Form.PropertyChanged += OnTemplateFormChanged;
                    value.ModList._modManager.ModEnabled += OnTemplateModManagerModEnabled;
                    value.ModList._modManager.ModDisabled += OnTemplateModManagerModDisabled;
                }

                _selectedTemplate = value;
                UpdateVisibility();
            }
        }
        public ServerVM? SelectedLive { get; private set; }

        public Visibility TemplateEditorVisibility { get; private set; }
        public Visibility LiveServerVisibility { get; private set; }

        public ServersTabVM(SettingsVM settings,
                            Func<IModManager> modManagerCreator,
                            IUserDialogueSpawner dialogueSpawner,
                            IChivalry2Launcher launcher,
                            IModRegistry modRegistry,
                            FileBackedSettings<IEnumerable<SavedServerTemplate>>? saveLocation = null) {
            ServerTemplates = new ObservableCollection<ServerTemplateVM>();
            ServerTemplates.CollectionChanged += (_, _) => {
                UpdateVisibility();
                Save();
            };
            RunningTemplates.CollectionChanged += (_, _) => UpdateVisibility();
            Settings = settings;
            ModRegistry = modRegistry;
            Launcher = launcher;
            DialogueSpawner = dialogueSpawner;
            ModManagerCreator = modManagerCreator;
            SaveLocation = saveLocation;
            Load();
            SelectedTemplate = ServerTemplates.FirstOrDefault();
            UpdateVisibility();
        }

        [RelayCommand]
        public async Task LaunchHeadless() => await LaunchSelected(true);

        [RelayCommand]
        public async Task LaunchServer() => await LaunchSelected(false);

        [RelayCommand]
        public Task ShutdownServer() => Task.Run(() => SelectedLive?.Dispose());

        [RelayCommand]
        public async Task AddTemplate() {
            var newModManager = new ModManager(
                ModRegistry,
                new List<ReleaseCoordinates> { });
            var newTemplate = new ServerTemplateVM(
                new ModListVM(newModManager, DialogueSpawner)
                );
            newTemplate.ModList.RefreshModListCommand.Execute(null);
            var occupiedPorts = ServerTemplates.Select(
                (e) => new Set<int>(new List<int> {
                    e.Form.A2SPort,
                    e.Form.RconPort,
                    e.Form.PingPort,
                    e.Form.GamePort
                })
            ).Aggregate(Set<int>(), (s1, s2) => s1.AddRange(s2));

            // try to make the new template nice
            if (SelectedTemplate != null) {
                // increment ports so that added server is not incompatible with other templates
                var oldForm = SelectedTemplate.Form;
                var newForm = newTemplate.Form;
                (newForm.GamePort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.GamePort, occupiedPorts);
                (newForm.PingPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.PingPort, occupiedPorts);
                (newForm.A2SPort, occupiedPorts) = ReserveRestrictedSuccessor(oldForm.A2SPort, occupiedPorts);
                (newForm.RconPort, _) = ReserveRestrictedSuccessor(oldForm.RconPort, occupiedPorts);

                // increment name in a similar way, so the user doesn't get things confused
                newForm.Name = TextualSuccessor(oldForm.Name);
            }

            ServerTemplates.Add(newTemplate);
            SelectedTemplate = newTemplate;
        }

        [RelayCommand]
        public void RemoveTemplate() {
            if (SelectedTemplate != null) {
                ServerTemplates.Remove(SelectedTemplate);
            }
            SelectedTemplate = ServerTemplates.FirstOrDefault();
        }

        public async Task LaunchSelected(bool headless = false) {
            if (SelectedTemplate == null) return;

            var formData = SelectedTemplate.Form.Data;
            var enabledMods =
                SelectedTemplate.ModList._modManager.GetEnabledModReleases();
            var maybeProcess = await LaunchProcessForSelected(formData, headless);
            maybeProcess.IfSome(process => {
                var server = new Chivalry2Server(
                    process,
                    RegisterWithBackend(formData, enabledMods),
                    new RCON(new IPEndPoint(IPAddress.Loopback, formData.RconPort))
                    );
                var serverVm = new ServerVM(server);
                var runningTuple = (SelectedTemplate, serverVm);
                process.Exited += (_, _) => {
                    RunningTemplates.Remove(runningTuple);
                    runningTuple.serverVm.Dispose();
                };
                RunningTemplates.Add(runningTuple);
            });
        }

        [RelayCommand]
        public void Save() {
            if (SaveLocation == null) {
                Logger.Warn("Tried to save server templates, but no file is selected.");
                return;
            }
            Logger.Info("Saving server templates...");
            SaveLocation.SaveSettings(ServerTemplates.Select(template => template.Saved()));
            Logger.Info($"Saved {ServerTemplates.Count} server templates.");
        }

        public void Load() {
            if (SaveLocation == null) {
                Logger.Warn("Tried to load server templates, but no file is selected.");
                return;
            }

            var loaded = SaveLocation.LoadSettings();
            if (loaded == null) {
                Logger.Warn("Failed to load server templates. Error unavailable, but likely invalid JSON.");
                return;
            }

            foreach (var template in loaded) {
                var newTemplate = new ServerTemplateVM(template, ModRegistry, DialogueSpawner);
                newTemplate.ModList.RefreshModListCommand.Execute(null);
                ServerTemplates.Add(newTemplate);
            }
            Logger.Info($"Loaded {ServerTemplates.Count} server templates.");
        }

        public void UpdateVisibility() {
            SelectedLive = RunningTemplates.Choose(
                (e) => e.template == SelectedTemplate ? e.live : Option<ServerVM>.None
            ).FirstOrDefault();
            var isSelectedRunning = SelectedLive != null;

            TemplateEditorVisibility = isSelectedRunning || ServerTemplates.Length() == 0 ? Visibility.Hidden : Visibility.Visible;
            LiveServerVisibility = !isSelectedRunning ? Visibility.Hidden : Visibility.Visible;
        }

        // TODO: this should really be a part of Chivalry2Server
        private async Task<Option<Process>> LaunchProcessForSelected(ServerInfoFormData formData, bool headless) {
            if (!Settings.IsLauncherReusable()) {
                Settings.CanClick = false;
            }

            if (SelectedTemplate == null) return None;

            var serverLaunchOptions = formData.ToServerLaunchOptions(headless);
            var options = new LaunchOptions(
                SelectedTemplate.ModList._modManager.GetEnabledAndDependencies(),
                Settings.ServerBrowserBackend,
                Settings.CLIArgs,
                Settings.EnablePluginAutomaticUpdates,
                None,
                Some(serverLaunchOptions)
            );

            var launchResult = await Launcher.Launch(options);
            return launchResult.Match(
                Left: _ => {
                    DialogueSpawner.DisplayMessage($"Failed to launch Chivalry 2 Unchained. Check the logs for details.");
                    Settings.CanClick = true;
                    return None;
                },
                Right: process => {
                    process.EnableRaisingEvents = true;
                    process.Exited += (sender, e) => {
                        if (process.ExitCode == 0) return;
                        Logger.Error($"Chivalry 2 Unchained exited with code {process.ExitCode}.");
                        DialogueSpawner.DisplayMessage($"Chivalry 2 Unchained exited with code {process.ExitCode}. Check the logs for details.");
                    };
                    return Some(process);
                }
            );
        }

        // TODO: this should really be a part of Chivalry2Server
        public A2SBoundRegistration RegisterWithBackend(ServerInfoFormData formData, IEnumerable<Release> enabledMods) {
            var ports = formData.ToPublicPorts();
            var serverInfo = new C2ServerInfo {
                Ports = ports,
                Name = formData.Name,
                Description = formData.Description,
                PasswordProtected = formData.Password.Length != 0,
                Mods = enabledMods.Select(release =>
                    new ServerBrowserMod(
                        release.Manifest.Name,
                        release.Manifest.Organization,
                        release.Tag.ToString()
                    )
                ).ToArray()
            };

            return new A2SBoundRegistration(
                new ServerBrowser(new Uri(Settings.ServerBrowserBackend + "/api/v1")),
                new A2S(new IPEndPoint(IPAddress.Loopback, ports.A2S)),
                serverInfo,
                formData.LocalIp);
        }

        public void Dispose() {
            SelectedLive?.Dispose();
            foreach (var runningTemplate in RunningTemplates) {
                runningTemplate.live.Dispose();
            }

            Save(); // save templates to file
            GC.SuppressFinalize(this);
        }
    }
}