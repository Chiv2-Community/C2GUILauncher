using log4net;

namespace UnchainedLauncher.Core.Utilities {
    public static class SigFileHelper {
        private static readonly ILog log = LogManager.GetLogger(typeof(SigFileHelper));

        private static string PakDirectory => FilePaths.PakDir;
        private static string DefaultSigFile => Path.Combine(PakDirectory, "pakchunk0-WindowsNoEditor.sig");

        private static Dictionary<string, (bool HasPak, bool HasSig)>? GetPakAndSigFiles() {
            try {
                if (!Directory.Exists(PakDirectory)) {
                    log.Error($"Directory does not exist: {PakDirectory}");
                    return null;
                }

                var pakFiles = Directory.EnumerateFiles(PakDirectory, "*.pak")
                                        .Select(Path.GetFileNameWithoutExtension)
                                        .OfType<string>()
                                        .ToHashSet();

                HashSet<string> sigFiles = Directory.EnumerateFiles(PakDirectory, "*.sig")
                                        .Select(Path.GetFileNameWithoutExtension)
                                        .OfType<string>()
                                        .ToHashSet();

                var allFiles = pakFiles.Union(sigFiles).Distinct();


                return allFiles.ToDictionary(
                    fileName => fileName,
                    fileName => (pakFiles.Contains(fileName), sigFiles.Contains(fileName))
                );
            }
            catch (Exception ex) {
                log.Error("An error occurred while gathering pak and sig files.", ex);
                return null;
            }
        }

        public static bool CheckAndCopySigFiles() {
            try {
                var files = GetPakAndSigFiles();
                if (files == null) return false;

                if (!File.Exists(DefaultSigFile)) {
                    log.Error($"Default .sig file not found: {DefaultSigFile}");
                    return false;
                }

                foreach (var file in files) {
                    if (file.Value.HasPak && !file.Value.HasSig) {
                        var sigFilePath = Path.Combine(PakDirectory, $"{file.Key}.sig");
                        log.Info($"Creating {file.Key}.sig");
                        File.Copy(DefaultSigFile, sigFilePath);
                    }
                }

                log.Info("Sig files copied.");
                return true;
            }
            catch (Exception ex) {
                log.Error("An error occurred during the sig file checking process.", ex);
                return false;
            }
        }

        public static bool DeleteOrphanedSigFiles() {
            try {
                var files = GetPakAndSigFiles();
                if (files == null) return false;

                foreach (var file in files) {
                    if (!file.Value.HasPak && file.Value.HasSig) {
                        var sigFilePath = Path.Combine(PakDirectory, $"{file.Key}.sig");
                        log.Info($"Deleting orphaned sig file: {sigFilePath}");
                        File.Delete(sigFilePath);
                    }
                }

                log.Info("Orphaned sig files deletion completed.");
                return true;
            }
            catch (Exception ex) {
                log.Error("An error occurred during the deletion of orphaned sig files.", ex);
                return false;
            }
        }

        public static bool RemoveAllNonDefaultSigFiles() {
            try {
                var files = GetPakAndSigFiles();
                if (files == null) return false;

                foreach (var file in files) {
                    if (file.Key != "pakchunk0-WindowsNoEditor" && file.Value.HasSig) {
                        var sigFilePath = Path.Combine(PakDirectory, $"{file.Key}.sig");
                        log.Info($"Deleting sig file for non-default pak: {sigFilePath}");
                        File.Delete(sigFilePath);
                    }
                }

                log.Info("Non-default sig files deletion completed.");
                return true;
            }
            catch (Exception ex) {
                log.Error("An error occurred during the deletion of non-default sig files.", ex);
                return false;
            }
        }
    }

}