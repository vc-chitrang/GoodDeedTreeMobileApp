using System.Text.RegularExpressions;

namespace GoodDeedTreeMobileApp {
    /// <summary>
    /// Joins a visitor's name and promise into one sentence that reads as a deed:
    /// "Chitrang" + "I will call my parents" -> "Chitrang chose to call their parents."
    /// Keep in sync with GoodDeed.DeedSentence in the wall project.
    /// </summary>
    public static class DeedSentence {
        private static readonly Regex LeadingSelf = new Regex(
            @"^(i\s+will|i'll|i\s+am\s+going\s+to|i'm\s+going\s+to|i\s+shall|i\s+want\s+to|i\s+promise\s+to|i)\s+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Myself = new Regex(@"\bmyself\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex My = new Regex(@"\bmy\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Me = new Regex(@"\bme\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Plain sentence, e.g. "Chitrang chose to help a stranger."</summary>
        public static string Plain(string name, string promise) {
            return Build(name, promise, false);
        }

        /// <summary>Same sentence with the name in bold rich text, for labels that support it.</summary>
        public static string Rich(string name, string promise) {
            return Build(name, promise, true);
        }

        private static string Build(string name, string promise, bool boldName) {
            string who = (name ?? string.Empty).Trim();
            string deed = Deed(promise);
            if (who.Length == 0) {
                return deed.Length == 0 ? string.Empty : Capitalise(deed) + ".";
            }
            string shownName = boldName ? "<b>" + who + "</b>" : who;
            return deed.Length == 0 ? shownName : shownName + " chose to " + deed + ".";
        }

        private static string Deed(string promise) {
            string deed = (promise ?? string.Empty).Trim().TrimEnd('.', '!', '?', ' ');
            deed = LeadingSelf.Replace(deed, string.Empty);
            if (deed.StartsWith("to ", System.StringComparison.OrdinalIgnoreCase)) {
                deed = deed.Substring(3);
            }
            deed = Myself.Replace(deed, "themselves");
            deed = My.Replace(deed, "their");
            deed = Me.Replace(deed, "them");
            // Lower-case the first letter so it continues the sentence ("Help" -> "help").
            if (deed.Length > 1 && char.IsUpper(deed[0]) && !char.IsUpper(deed[1])) {
                deed = char.ToLowerInvariant(deed[0]) + deed.Substring(1);
            }
            return deed;
        }

        private static string Capitalise(string text) {
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
