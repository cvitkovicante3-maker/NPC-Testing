using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Gives an NPC a knowledge base made of JSON files (for example medjugorje_knowledge.json).
/// Entries marked "always" go into the NPC's permanent background. For every player message,
/// the entries whose keywords match the message are added in front of it, so the NPC can
/// answer with real facts without a huge prompt. Add this to the NPC and drag the JSON file in.
///
/// JSON format:
/// { "topic": "...", "entries": [ { "id": "...", "title": "...", "always": false,
///                                  "keywords": ["word", "another word"], "text": "..." } ] }
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

    [Header("Knowledge files (drag .json files here)")]
    [SerializeField] private TextAsset[] knowledgeFiles;

    [Header("Retrieval")]
    [Tooltip("How many entries are added to each player message. More = better answers but a longer prompt (small models have limited memory).")]
    [SerializeField] private int maxFactsPerMessage = 2;
    [Tooltip("A short follow-up that matches nothing (like 'tell me more') reuses the previous entries.")]
    [SerializeField] private bool reusePreviousForFollowUps = true;
    [Tooltip("Writes the chosen entries to the Console, handy for tuning keywords.")]
    [SerializeField] private bool logMatches = false;

    private readonly List<Item> items = new List<Item>();
    private readonly List<Item> lastUsed = new List<Item>();

    private void Awake()
    {
        Load();
    }

    private void Load()
    {
        items.Clear();
        if (knowledgeFiles == null) return;

        foreach (TextAsset file in knowledgeFiles)
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
                items.Add(new Item { entry = entry, keys = keys.ToArray() });
            }
        }

        Debug.Log($"NPCKnowledge on '{name}': loaded {items.Count} entries.", this);
    }

    /// <summary>Entries marked "always": background the NPC knows in every conversation.</summary>
    public string GetAlwaysText()
    {
        var sb = new StringBuilder();
        foreach (Item item in items)
            if (item.entry.always) sb.Append("- ").AppendLine(item.entry.text.Trim());
        return sb.ToString().Trim();
    }

    /// <summary>The notes matching this player message, ready to put in front of it ("" if none).</summary>
    public string BuildContextFor(string playerMessage)
    {
        if (items.Count == 0 || string.IsNullOrWhiteSpace(playerMessage)) return "";

        string message = Normalize(playerMessage);
        string padded = " " + message + " ";

        var scored = new List<KeyValuePair<Item, int>>();
        foreach (Item item in items)
        {
            if (item.entry.always) continue;   // already in the permanent background

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

        var chosen = new List<Item>();
        for (int i = 0; i < scored.Count && chosen.Count < maxFactsPerMessage; i++)
            chosen.Add(scored[i].Key);

        if (chosen.Count == 0 && reusePreviousForFollowUps && lastUsed.Count > 0 && WordCount(message) <= 6)
            chosen.AddRange(lastUsed);

        if (chosen.Count == 0) return "";

        lastUsed.Clear();
        lastUsed.AddRange(chosen);

        var sb = new StringBuilder("[Notes you may use if they help (don't read them out or mention them):");
        foreach (Item item in chosen)
            sb.Append("\n- ").Append(item.entry.title).Append(": ").Append(item.entry.text.Trim());
        sb.Append("]");

        if (logMatches)
        {
            var titles = new List<string>();
            foreach (Item item in chosen) titles.Add(item.entry.title);
            Debug.Log("NPCKnowledge used: " + string.Join(", ", titles), this);
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