using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels {
    public class ProgressWindowVM {
        public MemoryProgress Progress { get; private set; }

        public ProgressWindowVM(MemoryProgress progress) {
            Progress = progress;
        }
    }
}