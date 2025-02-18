using LanguageExt;
using System.Text.RegularExpressions;

namespace UnchainedLauncher.Core.Utilities {
    public static class Successors {
        public static (int, Set<int>) ReserveRestrictedSuccessor(int number, Set<int> excluded) {
            var next = RestrictedSuccessor(number, excluded);
            return (next, excluded.Add(next));
        }

        /// <summary>
        /// gets a number's successor, excluding any number in excluded
        /// </summary>
        /// <param name="number">the number to get the successor of</param>
        /// <param name="excluded">numbers that should not be returned</param>
        /// <returns>number's next successor not in excluded</returns>
        public static int RestrictedSuccessor(int number, Set<int> excluded) {
            while (excluded.Contains(++number)) ;
            return number;
        }

        /// <summary>
        /// Return a string which is the textual successor. 
        /// This means incrementing any counting numbers in the string.
        /// Ignores numbers not in parentheses, and preserves intervening whitespace.
        /// Examples:
        ///     "Test 1 string (1)" => "Test 1 string (2)"
        ///     "Test 1 string" => "Test 1 string (2)"
        /// </summary>
        /// <param name="text">The text to get a successor for</param>
        /// <returns>The successor text</returns>
        public static string TextualSuccessor(string text) {
            if (Regex.IsMatch(text, "\\((\\s*)(\\d+)(\\s*)\\)")) {
                return Regex.Replace(
                    text,
                    "\\((\\s*)(\\d+)(\\s*)\\)",
                    (Match match) =>
                        $"({match.Groups[1].Value}{int.Parse(match.Groups[2].Value) + 1}{match.Groups[3].Value})"
                );
            }
            return $"{text} (1)";
        }

    }
}