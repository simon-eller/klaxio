using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Klaxio
{
    /// <summary>
    /// Central localisation helper.
    ///
    /// Console/server strings are looked up by their plain resource key.
    /// Every resource whose key starts with <see cref="UiPrefix"/> is
    /// additionally shipped to the browser as one flat dictionary, so the
    /// frontend never keeps its own copy of any translation.
    /// </summary>
    static class L
    {
        const string UiPrefix = "ui.";

        static readonly ResourceManager _rm = new ResourceManager(
            "Klaxio.Strings", Assembly.GetExecutingAssembly());

        public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture; // = English

        public static string Lang => Culture.TwoLetterISOLanguageName == "de" ? "de" : "en";

        public static void SetLanguage(string lang)
        {
            Culture = lang == "de"
                ? new CultureInfo("de")
                : CultureInfo.InvariantCulture;
        }

        public static string Get(string key) =>
            _rm.GetString(key, Culture) ?? $"[{key}]";

        public static string Get(string key, params object[] args) =>
            string.Format(Get(key), args);

        /// <summary>All "ui.*" resources for the active culture, keys stripped of the prefix.</summary>
        public static Dictionary<string, string> UiStrings()
        {
            var strings = new Dictionary<string, string>();

            // Enumerate the invariant set for the key list, then resolve each
            // value through GetString so culture fallback still applies.
            var set = _rm.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            if (set == null) return strings;

            foreach (DictionaryEntry entry in set)
            {
                var key = entry.Key as string;
                if (key == null || !key.StartsWith(UiPrefix, StringComparison.Ordinal)) continue;
                strings[key.Substring(UiPrefix.Length)] = Get(key);
            }
            return strings;
        }
    }
}
