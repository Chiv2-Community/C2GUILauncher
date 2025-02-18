using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.Views.DesignInstances {
    public static class MemoryProgressDesignInstances {
        public static MemoryProgress DEFAULT_PROGRESS => new MemoryProgress("test progress name");
        public static ProgressWindow DEFAULT_WINDOW => new ProgressWindow(DEFAULT_PROGRESS);
    }
}