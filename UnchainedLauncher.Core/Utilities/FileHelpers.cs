using LanguageExt;
using LanguageExt.Common;
using log4net;

namespace UnchainedLauncher.Core.Utilities {
    using static LanguageExt.Prelude;
    public static class FileHelpers {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(FileHelpers));

        public const string UserLockSuffix = ".USER_LOCK";

        public static bool IsFileLocked(string filePath) {
            return File.Exists(Path.Combine(Path.GetDirectoryName(filePath)!, UserLockSuffix)) ||
                   File.Exists(filePath + UserLockSuffix);
        }

        public static bool DeleteFiles(IEnumerable<string> filePaths) {
            Logger.Info("Deleting files: " + string.Join("\n    ", filePaths));
            // Delete all files in a transaction.
            // If any of them are locked, don't delete any of them.
            if (filePaths.Any(IsFileLocked)) {
                var lockedFiles = filePaths.Where(IsFileLocked);
                Logger.Warn($"Not deleting files because some of them are locked:\n" + string.Join("\n    ", lockedFiles));
                return false;
            }

            foreach (var filePath in filePaths) {
                File.Delete(filePath);
            }

            return true;
        }

        public static bool DeleteFile(string filePath) {
            if (IsFileLocked(filePath)) {
                Logger.Warn($"Not deleting file because it is locked: {filePath}");
                return false;
            }

            if (File.Exists(filePath)) {
                Logger.Info("Deleting file: " + filePath);
                File.Delete(filePath);
            }

            return true;
        }

        public static bool DeleteDirectory(string filePath) {
            Logger.Info("Deleting directory: " + filePath);
            if (!Directory.Exists(filePath)) {
                Logger.Warn($"Not deleting directory because it doesn't exist: {filePath}");
                return false;
            }

            var files = Directory.GetFiles(filePath, "*", SearchOption.AllDirectories);
            var result = DeleteFiles(files);

            if (result)
                Directory.Delete(filePath, true);

            return result;
        }

        // Currently unused, but maybe someday.
        public static void LockFile(string filePath) {
            File.Create(Path.Combine(Path.GetDirectoryName(filePath)!, UserLockSuffix)).Close();
        }

        // Currently unused, but maybe someday.
        public static void UnlockFile(string filePath) {
            File.Delete(Path.Combine(Path.GetDirectoryName(filePath)!, UserLockSuffix));
        }

        public static Either<HashFailure, string> Sha512(string filePath) {
            return Prelude.Try(() => {
                using var sha512 = System.Security.Cryptography.SHA512.Create();
                var bytes = File.ReadAllBytes(filePath);
                return BitConverter.ToString(sha512.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }).ToEither()
              .MapLeft(error => new HashFailure(filePath, error));
        }


        /// <summary>
        /// Asynchronously computes the SHA-512 hash of a file.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>An Either representing a failure on the left, or a hash on the right.  If Right(None), then the file does not exist.</returns>
        public static EitherAsync<HashFailure, Option<string>> Sha512Async(string filePath) {

            if (!File.Exists(filePath))
                return Prelude.RightAsync<HashFailure, Option<string>>(Prelude.None);

            return
                Prelude
                    .TryAsync(() => File.ReadAllBytesAsync(filePath))
                    .ToEither()
                    .Map(bytes => {
                        using var sha512 = System.Security.Cryptography.SHA512.Create();
                        return Prelude.Some(BitConverter.ToString(sha512.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
                    })
                    .MapLeft(error => new HashFailure(filePath, error));
        }
    }

    public record HashFailure(string FilePath, Error Error) : Expected($"Failed to hash file at {FilePath}. Reason: {Error.Message}", Error.Code, Some(Error));


    public class FileWriter {
        private static readonly ILog logger = LogManager.GetLogger(nameof(FileWriter));

        public string FilePath { get; }
        public Stream InputStream { get; }
        public long Size { get; }

        public FileWriter(string filePath, Stream inputStream, long size) {
            FilePath = filePath;
            InputStream = inputStream;
            Size = size;
        }

        public EitherAsync<Error, Unit> WriteAsync(Option<IProgress<double>> progress, CancellationToken cancellationToken) {
            return Prelude.TryAsync(
                async () => {
                    // Ensure the input stream is readable
                    if (!InputStream.CanRead) {
                        logger.Error($"The input stream for file '{FilePath}' is not readable.");
                        throw new ArgumentException("The input stream is not readable.", nameof(InputStream));
                    }

                    // Create or overwrite the target file
                    using (var outputStream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true)) {
                        logger.Debug($"Writing file '{FilePath}' from input stream.");
                        var totalBytesWritten = 0L; // Total bytes written to the file
                        var buffer = new byte[8 * 1024 * 1024]; // Buffer to hold data from input stream
                        int bytesRead;

                        // While there's data to read from the input stream, read and write asynchronously
                        var memory = new Memory<byte>(buffer);
                        while ((bytesRead = await InputStream.ReadAsync(memory, cancellationToken)) > 0) {
                            // Write only the portion of the buffer that was read
                            await outputStream.WriteAsync(memory[..bytesRead], cancellationToken);

                            totalBytesWritten += bytesRead;
                            var percentage = (double)totalBytesWritten / Size * 100;

                            progress.IfSome(p => p.Report(percentage));
                            //logger.Debug($"Wrote {totalBytesWritten} bytes to file '{FilePath}'. Progress: {percentage}%");
                        }
                    }

                    return default(Unit);
                })
                .ToEither();
        }



    }
}