﻿using System.Windows;
using UnchainedLauncher.GUI.ViewModels.Installer;
using Wpf.Ui.Controls;

namespace UnchainedLauncher.GUI.Views.Installer {
    public partial class InstallerWindow : FluentWindow {
        public InstallerWindow(InstallerWindowViewModel installerWindowViewModel) {
            DataContext = installerWindowViewModel;
            InitializeComponent();

            installerWindowViewModel.PropertyChanged += (sender, args) => {
                if (args.PropertyName == nameof(installerWindowViewModel.Finished)) {
                    if (installerWindowViewModel.Finished) {
                        Close();
                    }
                }
                else if (args.PropertyName == nameof(installerWindowViewModel.WindowVisibility)) {
                    // This is a janky hack because my visibility binding isn't working
                    if (installerWindowViewModel.WindowVisibility == Visibility.Hidden) {
                        Hide();
                    }
                }
            };
        }
    }
}