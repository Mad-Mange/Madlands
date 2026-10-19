using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The interface text in the player's language (ADR 0013): flat string tables in
    /// Resources/Localization/&lt;code&gt;.json ("key": "text", {0} for arguments), English as the
    /// fallback. Code asks <see cref="T(string)"/>; UXML writes "#key" as a text or label and
    /// <see cref="Apply"/> fills it in. Game content (quests, items, NPCs) comes from the server and is
    /// not translated here yet.
    /// </summary>
    public static class Loc
    {
        public const string Fallback = "en";

        private static Dictionary<string, string> _table = new Dictionary<string, string>();
        private static Dictionary<string, string> _fallback;
        private static readonly HashSet<string> Missing = new HashSet<string>();

        public static string Language { get; private set; }

        /// <summary>Languages with a table, as (code, name in that language), from Localization/languages.json.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Languages
        {
            get
            {
                var list = new List<KeyValuePair<string, string>>();
                var asset = Resources.Load<TextAsset>("Localization/languages");
                if (asset != null)
                    foreach (var pair in Parse(asset.text)) list.Add(pair);
                if (list.Count == 0) list.Add(new KeyValuePair<string, string>(Fallback, "English"));
                return list;
            }
        }

        /// <summary>Raised after the language changed: open screens refresh their text.</summary>
        public static event Action Changed;

        static Loc()
        {
            Load(Preferences.Language);
        }

        public static void Load(string code)
        {
            _fallback ??= Read(Fallback) ?? new Dictionary<string, string>();
            _table = code == Fallback ? _fallback : Read(code) ?? _fallback;
            Language = _table == _fallback ? Fallback : code;
            Changed?.Invoke();
        }

        private static Dictionary<string, string> Read(string code)
        {
            var asset = Resources.Load<TextAsset>("Localization/" + code);
            return asset != null ? Parse(asset.text) : null;
        }

        public static string T(string key)
        {
            if (_table.TryGetValue(key, out var text) || _fallback != null && _fallback.TryGetValue(key, out text)) return text;
            if (Missing.Add(key)) Debug.LogWarning("[Loc] No text for " + key);
            return key;
        }

        public static string T(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

        /// <summary>Fills in every "#key" text and field label under <paramref name="root"/>.</summary>
        public static void Apply(VisualElement root)
        {
            if (root is TextElement text && text.text != null && text.text.StartsWith("#", StringComparison.Ordinal))
                text.text = T(text.text.Substring(1));
            if (root is BaseField<string> field && field.label != null && field.label.StartsWith("#", StringComparison.Ordinal))
                field.label = T(field.label.Substring(1));
            for (int i = 0; i < root.childCount; i++) Apply(root[i]);
        }

        /// <summary>A flat JSON object of strings (all the tables need; JsonUtility cannot read dictionaries).</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            int i = 0;
            Skip(json, ref i);
            Expect(json, ref i, '{');
            while (true)
            {
                Skip(json, ref i);
                if (i < json.Length && json[i] == '}') break;
                string key = ReadString(json, ref i);
                Skip(json, ref i);
                Expect(json, ref i, ':');
                Skip(json, ref i);
                result[key] = ReadString(json, ref i);
                Skip(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
            }
            return result;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException($"Expected '{c}' at {i}");
            i++;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            Expect(s, ref i, '"');
            return sb.ToString();
        }
    }
}
