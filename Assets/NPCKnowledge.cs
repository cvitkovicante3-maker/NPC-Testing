using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>The languages an NPC can talk in. Used by NPCDialogue and NPCKnowledge.</summary>
public enum DialogueLanguage { English, Croatian }

/// <summary>
/// Gives an NPC a knowledge base made of JSON files (for example medjugorje_knowledge.json).
/// There is one set of files per language. Entries marked "always" are sent at the start of a
/// conversation. For every player message, the entries whose keywords match the message are added
/// in front of it, so the NPC can answer with real facts without a huge prompt.
/// Add this to the NPC and drag the JSON files in.
///
/// JSON format:
/// { "topic": "...", "entries": [ { "id": "...", "title": "...", "always": false,
///                                  "keywords": ["word", "another word"], "text": "..." } ] }
/// Keep the same ids and keywords in the English and Croatian files; only title and text change.
/// </summary>
public class NPCKnowledge : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string title;
        public bool always;
        public string[] keywords;
        public string text;
    }

    [Serializable]
    private class KnowledgeFile
    {
        public string topic;
        public Entry[] entries;
    }

    private class Item
    {
        public Entry entry;
        public string[] keys;     // normalized keywords
    }

    [Header("English facts (drag .json files here)")]
    [SerializeField] private TextAsset[] knowledgeFiles;

    [Header("Croatian facts (the same entries, translated)")]
    [Tooltip("If this is empty, the English facts are also used when the player talks in Croatian.")]
    [SerializeField] private TextAsset[] croatianKnowledgeFiles;

    [Header("Retrieval")]
    [Tooltip("How many entries are added to each player message. More = better answers but a longer prompt (small models have limited memory).")]
    [SerializeField] private int maxFactsPerMessage = 2;
    [Tooltip("A short follow-up that matches nothing (like 'tell me more') reuses the previous entries.")]
    [SerializeField] private bool reusePreviousForFollowUps = true;
    [Tooltip("Writes the chosen entries to the Console, handy for tuning keywords.")]
    [SerializeField] private bool logMatches = false;

    private readonly List<Item> englishItems = new List<Item>();
    private readonly List<Item> croatianItems = new List<Item>();
    private readonly List<string> lastUsedIds = new List<string>();

    private void Awake()
    {
        Load(knowledgeFiles, englishItems);
        Load(croatianKnowledgeFiles, croatianItems);
        Debug.Log($"NPCKnowledge on '{name}': loaded {englishItems.Count} English and {croatianItems.Count} Croatian entries.", this);
    }

    private void Load(TextAsset[] files, List<Item> target)
    {
        target.Clear();
        if (files == null) return;

        foreach (TextAsset file in files)
        {
            if (file == null) continue;

            KnowledgeFile data = null;
            try { data = JsonUtility.FromJson<KnowledgeFile>(file.text); }
            catch (Exception ex)
            {
                Debug.LogError($"NPCKnowledge: couldn't read '{file.name}' - check the JSON for typos. {ex.Message}", this);
                continue;
            }

            if (data == null || data.entries == null)
            {
                Debug.LogWarning($"NPCKnowledge: '{file.name}' has no \"entries\" list.", this);
                continue;
            }

            foreach (Entry entry in data.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.text)) continue;

                var keys = new List<string>();
                if (entry.keywords != null)
                {
                    foreach (string k in entry.keywords)
                    {
                        string normalized = Normalize(k);
                        if (normalized.Length >= 3) keys.Add(normalized);
                    }
                }
                target.Add(new Item { entry = entry, keys = keys.ToArray() });
            }
        }
    }

    // Croatian facts if there are any, otherwise the English ones
    private List<Item> ItemsFor(DialogueLanguage language)
    {
        return (language == DialogueLanguage.Croatian && croatianItems.Count > 0) ? croatianItems : englishItems;
    }

    /// <summary>Entries marked "always": background the NPC should know in every conversation.</summary>
    public string GetAlwaysText(DialogueLanguage language)
    {
        var sb = new StringBuilder();
        foreach (Item item in ItemsFor(language))
            if (item.entry.always) sb.Append("- ").AppendLine(item.entry.text.Trim());
        return sb.ToString().Trim();
    }

    /// <summary>
    /// The notes to put in front of a player message ("" if there are none): the entries matching
    /// the message, plus the "always" entries when includeCore is true.
    /// </summary>
    public string BuildContextFor(string playerMessage, DialogueLanguage language, bool includeCore)
    {
        List<Item> items = ItemsFor(language);
        if (items.Count == 0) return "";

        var chosen = new List<Item>();

        if (!string.IsNullOrWhiteSpace(playerMessage))
        {
            string message = Normalize(playerMessage);
            string padded = " " + message + " ";

            var scored = new List<KeyValuePair<Item, int>>();
            foreach (Item item in items)
            {
                if (item.entry.always) continue;   // sent separately as the core notes

                int score = 0;
                foreach (string key in item.keys)
                    if (Matches(padded, key)) score += WordCount(key);   // multi-word keywords count more

                if (score > 0) scored.Add(new KeyValuePair<Item, int>(item, score));
            }

            scored.Sort((a, b) =>
            {
                int byScore = b.Value.CompareTo(a.Value);
                return byScore != 0 ? byScore : items.IndexOf(a.Key).CompareTo(items.IndexOf(b.Key));
            });

            for (int i = 0; i < scored.Count && chosen.Count < maxFactsPerMessage; i++)
                chosen.Add(scored[i].Key);

            // "tell me more" and similar short follow-ups: reuse what we used last time
            if (chosen.Count == 0 && reusePreviousForFollowUps && lastUsedIds.Count > 0 && WordCount(message) <= 6)
            {
                foreach (Item item in items)
                    if (lastUsedIds.Contains(item.entry.id)) chosen.Add(item);
            }

            if (chosen.Count > 0)
            {
                lastUsedIds.Clear();
                foreach (Item item in chosen) lastUsedIds.Add(item.entry.id);
            }
        }

        string core = includeCore ? GetAlwaysText(language) : "";
        if (chosen.Count == 0 && core.Length == 0) return "";

        var sb = new StringBuilder(language == DialogueLanguage.Croatian
            ? "[Bilješke koje možeš koristiti ako pomažu (ne čitaj ih naglas i ne spominji ih):"
            : "[Notes you may use if they help (don't read them out or mention them):");

        if (core.Length > 0) sb.Append("\n").Append(core);
        foreach (Item item in chosen)
            sb.Append("\n- ").Append(item.entry.title).Append(": ").Append(item.entry.text.Trim());
        sb.Append("]");

        if (logMatches)
        {
            var titles = new List<string>();
            foreach (Item item in chosen) titles.Add(item.entry.title);
            Debug.Log("NPCKnowledge used (" + language + "): " + (titles.Count > 0 ? string.Join(", ", titles) : "core only"), this);
        }

        return sb.ToString();
    }

    // Short keywords must match whole words ("eat" must not match "weather");
    // longer ones also match word beginnings ("visionar" matches "visionaries").
    private static bool Matches(string paddedMessage, string key)
    {
        if (key.Length <= 4) return paddedMessage.Contains(" " + key + " ");
        return paddedMessage.Contains(key);
    }

    // Lower-case, no accents (č -> c, š -> s, đ/dj -> d), only letters and digits separated by
    // single spaces. "Međugorje" and "Medjugorje" both become "medugorje".
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        s = s.ToLowerInvariant().Replace('đ', 'd');
        string decomposed = s.Normalize(NormalizationForm.FormD);

        var plain = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);

        string text = plain.ToString().Replace("dj", "d");

        var result = new StringBuilder(text.Length);
        bool lastWasSpace = true;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) { result.Append(c); lastWasSpace = false; }
            else if (!lastWasSpace) { result.Append(' '); lastWasSpace = true; }
        }
        return result.ToString().Trim();
    }

    private static int WordCount(string s)
    {
        return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}