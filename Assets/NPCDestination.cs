using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Marks a place the mouse knows and can lead the player to (a church, a hill, a house...).
/// Put this on an empty GameObject standing on a spot the mouse can walk to, and fill in the names and
/// the words players use for it. When the player asks "where is the church?" the mouse offers to take them there.
/// </summary>
public class NPCDestination : MonoBehaviour
{
    /// <summary>All destinations in the scene.</summary>
    public static readonly List<NPCDestination> All = new List<NPCDestination>();

    [Header("Name shown to the player")]
    [SerializeField] private string nameEnglish = "St. James Church";
    [SerializeField] private string nameCroatian = "Crkva sv. Jakova";

    [Header("What the mouse knows about this place (optional)")]
    [Tooltip("A few facts about this place, used when the mouse tells the player about it on arrival. Works even if the knowledge files have no entry for it.")]
    [TextArea(2, 6)][SerializeField] private string descriptionEnglish = "";
    [TextArea(2, 6)][SerializeField] private string descriptionCroatian = "";

    [Header("Words the player may use for this place")]
    [Tooltip("English and Croatian words, no need for accents. Short words (4 letters or less) must match a whole word, longer ones also match inside words. Add the endings people use: crkva, crkvu, crkve.")]
    [SerializeField] private string[] keywords = { "church", "parish", "st james", "saint james", "crkva", "crkvu", "crkve", "sveti jakov", "sv jakov" };

    [Header("Arrival")]
    [Tooltip("The guide counts as arrived when it gets this close to the marker (world units).")]
    [SerializeField] private float arriveDistance = 4f;

    public float ArriveDistance => arriveDistance;

    private string[] normalizedKeys;
    private string normalizedNames = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }

    private void OnEnable()
    {
        Prepare();
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>The facts typed in for this place, in the given language (falls back to English; "" if none).</summary>
    public string DescriptionFor(DialogueLanguage language)
    {
        string d = (language == DialogueLanguage.Croatian && !string.IsNullOrWhiteSpace(descriptionCroatian)) ? descriptionCroatian : descriptionEnglish;
        return string.IsNullOrWhiteSpace(d) ? "" : d.Trim();
    }

    public string NameFor(DialogueLanguage language)
    {
        return (language == DialogueLanguage.Croatian && !string.IsNullOrWhiteSpace(nameCroatian)) ? nameCroatian : nameEnglish;
    }

    private void Prepare()
    {
        var keys = new List<string>();
        AddKey(keys, nameEnglish);
        AddKey(keys, nameCroatian);
        if (keywords != null) foreach (string k in keywords) AddKey(keys, k);
        normalizedKeys = keys.ToArray();
        normalizedNames = " " + NPCKnowledge.Normalize(nameEnglish) + " | " + NPCKnowledge.Normalize(nameCroatian) + " ";
    }

    private static void AddKey(List<string> keys, string raw)
    {
        string n = NPCKnowledge.Normalize(raw);
        if (n.Length >= 3 && !keys.Contains(n)) keys.Add(n);
    }

    private int Score(string paddedMessage)
    {
        if (normalizedKeys == null) Prepare();

        int score = 0;
        foreach (string key in normalizedKeys)
        {
            if (!Has(paddedMessage, key)) continue;
            score += key.Split(' ').Length;                 // multi-word keys count more

            // A word that is part of the place's own NAME counts much more than a loose keyword. So "church"
            // finds St. James Church even if another place still has "church" left over in its keywords.
            if (normalizedNames.Contains(key)) score += 2;
        }
        return score;
    }

    // Short words must match whole words; longer ones also match inside words (endings like "crkvu")
    private static bool Has(string paddedMessage, string key)
    {
        if (key.Length < 3) return false;
        return key.Length <= 4 ? paddedMessage.Contains(" " + key + " ") : paddedMessage.Contains(key);
    }

    // ------------------------------------------------------------- asking for directions

    private static readonly string[] IntentWords =
    {
        // English
        "where", "how do i get", "how to get", "how can i get", "how do we get", "take me", "show me", "lead me",
        "guide me", "directions", "which way", "way to", "find", "locate", "navigate", "how far",
        // Croatian
        "gdje", "kako doći", "kako do", "kako da dođem", "kako da dođemo", "odvedi", "povedi", "vodi me", "pokaži",
        "put do", "put prema", "nađi", "pronađi", "kojim putem", "u kojem smjeru", "kako se dolazi", "dovedi", "upute"
    };

    // Direct orders: the player isn't asking where something is, they are telling the mouse to lead them
    private static readonly string[] CommandWords =
    {
        // English
        "take me", "take us", "lead me", "lead us", "guide me", "guide us", "bring me", "bring us", "walk me",
        "show me the way", "let's go", "lets go", "i want to go", "i wanna go", "we want to go", "go to", "head to",
        "can you take", "could you take", "please take",
        // Croatian
        "odvedi", "odvedi me", "odvedi nas", "povedi", "vodi me", "vodi nas", "dovedi", "idemo", "hajdemo", "hajde",
        "zelim ici", "želim ići", "hocu ici", "hoću ići", "želimo ići", "zelimo ici", "pokazi mi put", "pokaži mi put",
        "mozes li me", "možeš li me"
    };

    private static readonly string[] PreviousPlaceWords =
    {
        " there ", " it ", " that place ", " the same place ", " again ", " tamo ", " onamo ", " opet ", " ponovno ", " to mjesto "
    };

    // "Are there any hotels here?", "ima li hotela?": asking whether a kind of place exists nearby
    private static readonly string[] ExistenceWords =
    {
        "are there", "is there", "any", "do you know", "know of", "nearby", "near here", "around here", "in the area",
        "close by", "recommend", "suggest", "list", "options", "which",
        "ima li", "ima", "postoji", "postoje", "ikakv", "u blizini", "blizu", "ovdje", "preporuci", "preporuc",
        "predlozi", "koji", "koje", "koja", "znas li"
    };

    /// <summary>Only meaningful together with a matched place: "are there any hotels here?"</summary>
    public static bool LooksLikeExistenceQuestion(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";
        foreach (string word in ExistenceWords)
            if (Has(padded, NPCKnowledge.Normalize(word))) return true;
        return false;
    }

    /// <summary>"Take me to the church","odvedi me do crkve", "let's go to...": an order, not a question.</summary>
    public static bool LooksLikeDirectCommand(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";
        foreach (string word in CommandWords)
            if (Has(padded, NPCKnowledge.Normalize(word))) return true;
        return false;
    }

    /// <summary>Does the message point back at a place from earlier ("take me there", "odvedi me opet")?</summary>
    public static bool RefersToPreviousPlace(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";
        foreach (string word in PreviousPlaceWords)
            if (padded.Contains(" " + NPCKnowledge.Normalize(word) + " ")) return true;
        return false;
    }

    /// <summary>Does the message sound like "where is...", "how do I get to...", "take me to..."?</summary>
    public static bool LooksLikeDirectionsRequest(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";
        foreach (string word in IntentWords)
            if (Has(padded, NPCKnowledge.Normalize(word))) return true;
        return false;
    }

    /// <summary>The destination the message talks about (the best match), or null.</summary>
    public static NPCDestination FindInMessage(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";

        NPCDestination best = null;
        int bestScore = 0;
        foreach (NPCDestination d in All)
        {
            if (d == null) continue;
            int score = d.Score(padded);
            if (score > bestScore) { best = d; bestScore = score; }
        }
        return best;
    }

    // ------------------------------------------------------------- which place does the player MEAN?

    // Splits a message into clauses: sentences, commas, and words like "but" / "and" / "ali" / "pa"
    private static readonly Regex ClauseSplit =
        new Regex(@"[.!?;,\n\r]+|\s(?:but|and|ali|pa|then)\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The place(s) the player wants to GO TO or asks about, not every place they happen to mention.
    /// "I had a good stay in the hotel. Can you take me to the Church?" gives the church only: only the part
    /// of the message that asks for directions is searched, "from the hotel to the church" looks at what
    /// comes after "to", and if several places are still left the one mentioned first wins
    /// ("the church near the hotel"). Asking for a kind of place ("a hotel") still gives all of them.
    /// </summary>
    public static List<NPCDestination> FindTargets(string message)
    {
        string target = TargetText(message);
        string used = target;
        List<NPCDestination> list = FindAllInMessage(target);

        if (list.Count == 0 && target != NPCKnowledge.Normalize(message))
        {
            used = message;
            list = FindAllInMessage(message);   // the asking part named no place ("take me there"): use the whole message
        }

        if (list.Count > 1) KeepFirstMentioned(list, used);
        return list;
    }

    // Only the clauses that ask for directions, and (for "from X to Y") only what follows "to"
    private static string TargetText(string message)
    {
        var sb = new StringBuilder();
        foreach (string clause in ClauseSplit.Split(message))
        {
            if (string.IsNullOrWhiteSpace(clause)) continue;
            if (LooksLikeDirectionsRequest(clause) || LooksLikeDirectCommand(clause) || LooksLikeExistenceQuestion(clause))
                sb.Append(clause).Append(" . ");
        }

        string text = " " + NPCKnowledge.Normalize(sb.Length > 0 ? sb.ToString() : message) + " ";

        int from = IndexOfAny(text, " from ", " iz ", " od ");
        if (from >= 0)
        {
            int to = Math.Max(Math.Max(text.LastIndexOf(" to ", StringComparison.Ordinal),
                                       text.LastIndexOf(" do ", StringComparison.Ordinal)),
                              text.LastIndexOf(" prema ", StringComparison.Ordinal));
            if (to > from)
            {
                int cut = text.IndexOf(' ', to + 1);
                if (cut > 0) text = text.Substring(cut);
            }
        }
        return text.Trim();
    }

    private static int IndexOfAny(string text, params string[] needles)
    {
        int best = -1;
        foreach (string n in needles)
        {
            int i = text.IndexOf(n, StringComparison.Ordinal);
            if (i >= 0 && (best < 0 || i < best)) best = i;
        }
        return best;
    }

    // Of several candidates, keep the ones named first in the text
    private static void KeepFirstMentioned(List<NPCDestination> list, string text)
    {
        string padded = " " + NPCKnowledge.Normalize(text) + " ";
        var positions = new int[list.Count];
        int anchor = int.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            positions[i] = list[i].FirstMention(padded);
            if (positions[i] < anchor) anchor = positions[i];
        }

        var kept = new List<NPCDestination>();
        for (int i = 0; i < list.Count; i++)
            if (positions[i] <= anchor + 2) kept.Add(list[i]);   // same word, give or take an ending
        list.Clear();
        list.AddRange(kept);
    }

    private int FirstMention(string padded)
    {
        if (normalizedKeys == null) Prepare();
        int best = int.MaxValue;
        foreach (string key in normalizedKeys)
        {
            if (!Has(padded, key)) continue;
            int i = padded.IndexOf(key, StringComparison.Ordinal);
            if (i >= 0 && i < best) best = i;
        }
        return best;
    }

    // "Where am I?", "gdje sam?": about the player's own position, not a request to go somewhere
    private static readonly string[] SelfLocationWords =
    {
        "where am i", "where are we", "where exactly am i", "where is this", "what is this place", "what place is this",
        "what is this area", "which place is this", "where am i right now", "do you know where i am",
        "gdje sam", "gdje smo", "gdje se nalazim", "gdje se nalazimo", "koje je ovo mjesto", "sto je ovo za mjesto",
        "koje je ovo podrucje", "u kojem sam mjestu", "znas li gdje sam"
    };

    public static bool LooksLikeSelfLocation(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";
        foreach (string word in SelfLocationWords)
            if (padded.Contains(" " + NPCKnowledge.Normalize(word) + " ")) return true;
        return false;
    }


    // Broad wishes that are about places even without naming one: "I'm tired", "where can I eat", "show me something"
    private static readonly string[] PlaceSeekingWords =
    {
        "where", "take me", "bring me", "show me", "lead me", "guide", "go to", "walk to", "visit", "see", "find", "near", "nearby", "close",
        "sleep", "stay", "room", "eat", "food", "hungry", "drink", "pray", "church", "hotel", "directions", "way to", "how do i get",
        "gdje", "odvedi", "vodi", "povedi", "pokazi", "pokaz", "ici", "idem", "posjeti", "vidjeti", "naci", "nadi", "blizu", "blizini",
        "spavati", "prenociti", "soba", "jesti", "gladan", "gladna", "piti", "moliti", "crkva", "hotel", "put do", "kako do", "kako doci"
    };

    /// <summary>True if the message is probably about going somewhere or finding a kind of place.</summary>
    public static bool LooksLikePlaceSeeking(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";
        foreach (string word in PlaceSeekingWords)
        {
            string w = NPCKnowledge.Normalize(word);
            if (w.Length <= 4 ? padded.Contains(" " + w + " ") : padded.Contains(" " + w)) return true;
        }
        return false;
    }

    /// <summary>
    /// The place(s) a NAME refers to (as written by the language model in a tag): an exact name gives one place,
    /// anything looser ("hotel") gives every place that fits equally well.
    /// </summary>
    public static List<NPCDestination> ResolveName(string name)
    {
        var result = new List<NPCDestination>();
        string n = NPCKnowledge.Normalize(name);
        if (n.Length == 0) return result;

        foreach (NPCDestination d in All)
        {
            if (d == null) continue;
            if (n == NPCKnowledge.Normalize(d.nameEnglish) || n == NPCKnowledge.Normalize(d.nameCroatian))
            {
                result.Add(d);
                return result;
            }
        }
        return FindAllInMessage(name);
    }

    /// <summary>
    /// Every destination the message talks about equally well. "a hotel" returns all hotels;
    /// "Hotel Santa Maria" returns just that one. Empty if nothing matches.
    /// </summary>
    public static List<NPCDestination> FindAllInMessage(string message)
    {
        string padded = " " + NPCKnowledge.Normalize(message) + " ";

        var result = new List<NPCDestination>();
        int bestScore = 0;
        foreach (NPCDestination d in All)
        {
            if (d == null) continue;
            int score = d.Score(padded);
            if (score <= 0) continue;
            if (score > bestScore) { bestScore = score; result.Clear(); }
            if (score == bestScore) result.Add(d);
        }
        return result;
    }

    /// <summary>Words used to look up facts about this place in the knowledge base.</summary>
    public string FactsQuery()
    {
        var sb = new StringBuilder();
        sb.Append(nameEnglish).Append(' ').Append(nameCroatian);
        if (keywords != null) foreach (string k in keywords) sb.Append(' ').Append(k);
        return sb.ToString();
    }

    /// <summary>The names of all destinations, for telling the player what the mouse can show them.</summary>
    public static string NamesFor(DialogueLanguage language)
    {
        var sb = new StringBuilder();
        int count = 0;
        foreach (NPCDestination d in All)
        {
            if (d == null || count >= 8) continue;
            if (count > 0) sb.Append(", ");
            sb.Append(d.NameFor(language));
            count++;
        }
        return sb.ToString();
    }

    // Shows the marker in the Scene view so it is easy to place
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, arriveDistance);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 3f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3.2f, nameEnglish);
#endif
    }
}