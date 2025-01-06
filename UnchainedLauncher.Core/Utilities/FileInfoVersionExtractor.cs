﻿using log4net;
using Semver;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Utilities {

    public interface IVersionExtractor<T> {

        /// <summary>
        /// Provided some target T (like a file path), output the SemVersion of the associated object.
        /// </summary>
        /// <param name="input">Some identifier used to locate a versioned resource</param>
        /// <returns>The version of the target</returns>
        SemVersion? GetVersion(T input);
    }

    /// <summary>
    /// Uses file metadata to extract the SemVersion of the provided file path
    /// </summary>
    public class FileInfoVersionExtractor : IVersionExtractor<string> {
        private readonly ILog logger = LogManager.GetLogger(typeof(FileInfoVersionExtractor));

        public SemVersion? GetVersion(string filePath) {
            if (!File.Exists(filePath)) return null;

            var fileInfo = FileVersionInfo.GetVersionInfo(filePath);

            var versionString = fileInfo.ProductVersion ?? fileInfo.FileVersion ?? "";
            logger.Debug($"Raw file version for '{filePath}': {versionString}");
            var splitVersionString = versionString.Split('.');
            versionString = String.Join('.', splitVersionString.Take(3));
            logger.Debug($"Cleaned file version for '{filePath}': {versionString}");

            try {
                return SemVersion.Parse(versionString, SemVersionStyles.Any);
            }
            catch (Exception e) {
                logger.Error($"Unable to parse version for '{filePath}': {e.Message}");
                return null;
            }
        }
    }

}