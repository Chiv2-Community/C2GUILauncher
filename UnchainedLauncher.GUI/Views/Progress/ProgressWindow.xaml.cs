using System.Windows;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Views {
    public partial class ProgressWindow : Window {
        public MemoryProgress Progress { get; private set; }
        public ProgressWindow(MemoryProgress progress) {
            DataContext = Progress = progress;
            InitializeComponent();
        }
    }
}