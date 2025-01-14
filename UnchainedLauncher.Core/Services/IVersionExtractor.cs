using Semver;

namespace UnchainedLauncher.Core.Utilities {
    public interface IVersionExtractor {
        /// <summary>
        /// Provided some identifier (like a file path), output the SemVersion of the associated object.
        /// </summary>
        /// <param name="input">Some identifier used to locate a versioned resource</param>
        /// <returns>The version of the target</returns>
        SemVersion? GetVersion(string input);
    }
}