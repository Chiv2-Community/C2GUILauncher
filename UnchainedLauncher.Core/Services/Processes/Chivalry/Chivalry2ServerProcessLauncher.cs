using LanguageExt;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {
    public class Chivalry2ServerProcessLauncher {
        public IUnchainedChivalry2Launcher Launcher { get; set; }
        public ModdedLaunchOptions ModdedLaunchOptions { get; set; }
        public Option<ServerLaunchOptions> ServerLaunchOptions { get; set; }
        string ExtraArgs { get; set; }
        public Chivalry2ServerProcessLauncher(IUnchainedChivalry2Launcher launcher,
                                           ModdedLaunchOptions options,
                                           string extraArgs) {
            Launcher = launcher;
            ModdedLaunchOptions = options;
            ExtraArgs = extraArgs;
        }

        public async Task<Option<Either<UnchainedLaunchFailure, Process>>> Launch() {
            var launch = await Launcher.Launch(ModdedLaunchOptions, true, ExtraArgs);
            return launch;
        }
    }
}