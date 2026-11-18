using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player walk up to this NPC and chat with it, in English or Croatian.
/// Walk close and a prompt appears; press E, pick a language, and talk. Replies come from a free,
/// local model (the "LLM for Unity" package), so nothing is pre-written and nothing needs the internet.
///
/// Needs on this object: an "LLM Agent" component. Somewhere in the scene: an "LLM" component
/// with a downloaded model. The UI is drawn with Unity's built-in IMGUI (no Canvas needed).
/// </summary>
public class NPCDialogue : MonoBehaviour
{
    private class Line { public bool fromPlayer; public string text; }

    [Header("Character in English")]
    [SerializeField] private string npcName = "Mouse";
    [TextArea(5, 12)]
    [SerializeField]
    private string persona =
        "You are a small, curious village mouse with a big heart. You are friendly, a little shy at first, and quickly " +
        "become chatty and playful once you like someone. You are always hungry for crumbs, you see the world from very " +
        "low down, and you notice small things people miss. You are loyal: once you have met the player, you gladly tag " +
        "along beside them as a travelling companion. You are respectful and warm, never rude, and you never make fun " +
        "of anyone's beliefs.";
    [TextArea(3, 8)]
    [SerializeField]
    private string worldNotes =
        "You live in the village of Međugorje in Herzegovina, in a valley between two hills: Podbrdo (Apparition Hill) " +
        "and Križevac (Cross Mountain). You have lived here all your life, scurrying around the church of St. James, the " +
        "guesthouses, the vineyards and the paths up the hills. You have met pilgrims and visitors from all over the world, " +
        "and you love telling newcomers about the village, its walks, its food and its history. You are not a priest or an " +
        "official guide. When asked about faith, you share only what you have heard from pilgrims and what the Church has " +
        "said, and you never push an opinion. The player is a visitor exploring the village. You know nothing about the " +
        "world beyond what travellers have told you.";

    [Header("Character in Croatian")]
    [Tooltip("The name shown when talking in Croatian. Leave empty to use the same name as in English.")]
    [SerializeField] private string npcNameCroatian = "Miš";
    [TextArea(5, 12)]
    [SerializeField]
    private string personaCroatian =
        "Ti si mali, znatiželjni seoski miš s velikim srcem. Prijateljski si raspoložen, isprva malo sramežljiv, ali brzo " +
        "postaješ pričljiv i razigran kad nekoga zavoliš. Uvijek si gladan mrvica, svijet vidiš s vrlo niske visine i " +
        "primjećuješ sitnice koje ljudi propuste. Odan si: kad upoznaš igrača, rado ga pratiš kao suputnik. Ljubazan si i pun " +
        "poštovanja, nikad grub, i nikad se ne rugaš ničijim uvjerenjima.";
    [TextArea(3, 8)]
    [SerializeField]
    private string worldNotesCroatian =
        "Živiš u selu Međugorju u Hercegovini, u dolini između dva brda: Podbrda (Brdo ukazanja) i Križevca. Cijeli život " +
        "provodiš ovdje, šmugajući oko crkve svetog Jakova, pansiona, vinograda i staza uz brda. Upoznao si hodočasnike i " +
        "posjetitelje iz cijeloga svijeta i voliš pridošlicama pričati o selu, njegovim šetnjama, hrani i povijesti. Nisi " +
        "svećenik ni službeni vodič. Kad te pitaju o vjeri, dijeliš samo ono što si čuo od hodočasnika i što je rekla Crkva " +
        "te nikada ne guraš vlastito mišljenje. Igrač je posjetitelj koji istražuje selo. Ne znaš ništa o svijetu izvan onoga " +
        "što su ti putnici ispričali.";

    [Tooltip("The NPC greets the player first when the conversation starts.")]
    [SerializeField] private bool speaksFirst = true;

    [Header("Language")]
    [Tooltip("Ask for English or Croatian every time a conversation starts. If off, the first choice is remembered (the 'Language' button in the chat can still change it).")]
    [SerializeField] private bool askLanguageEveryTime = false;

    [Header("AI")]
    [Tooltip("The 'LLM Agent' on this object. Found automatically if it's on the same object.")]
    [SerializeField] private LLMUnity.LLMAgent agent;

    [Header("Knowledge")]
    [Tooltip("Optional. Add an NPCKnowledge component (with JSON files) to give the NPC facts to draw on. Found automatically if it's on the same object.")]
    [SerializeField] private NPCKnowledge knowledge;

    [Header("Companion")]
    [Tooltip("Add an NPCCompanion to the NPC. Found automatically if it's on the same object.")]
    [SerializeField] private NPCCompanion companion;
    [Tooltip("After the conversation ends, the NPC walks alongside the player.")]
    [SerializeField] private bool followAfterConversation = true;

    [Header("Navigation (leading the player to places)")]
    [Tooltip("When the player asks where a marked place is (see NPCDestination), the NPC offers to lead the way. Needs an NPCCompanion.")]
    [SerializeField] private bool offerNavigation = true;
    [Tooltip("When the NPC has led the player to a place, it stops, opens the chat and asks whether the player wants to hear facts about it.")]
    [SerializeField] private bool offerFactsOnArrival = true;

    [Header("Interaction")]
    [Tooltip("Leave empty to find the object with GTAPlayerMovement automatically.")]
    [SerializeField] private Transform player;
    [SerializeField] private Key interactKey = Key.E;
    [SerializeField] private float interactDistance = 4f;
    [Tooltip("Talk range while the NPC is following you. It is larger so you can always talk to your companion.")]
    [SerializeField] private float followingInteractDistance = 12f;
    [SerializeField] private bool facePlayerWhileTalking = true;
    [SerializeField] private float turnSpeed = 6f;

    [Header("Chat look")]
    [Tooltip("Optional: drag a .ttf / .otf font here. If empty, the script looks for Assets/Resources/Fonts/AtkinsonHyperlegible_400Regular, and then falls back to the computer's Segoe UI / Arial.")]
    [SerializeField] private Font uiFont;
    [Tooltip("Optional bold version of the font (used for headings and buttons). If empty, the regular font is made bold automatically.")]
    [SerializeField] private Font uiFontBold;
    [Tooltip("Makes the whole chat bigger or smaller.")]
    [Range(0.7f, 2f)][SerializeField] private float uiScale = 1f;
    [Tooltip("Makes only the TEXT bigger or smaller (the panel and buttons stay the same size). Changes apply immediately, also while the game is running.")]
    [Range(0.7f, 2f)][SerializeField] private float textSize = 1.15f;

    [Header("Troubleshooting")]
    [Tooltip("Shows a yellow status line at the top-left of the Game view. Turn off once everything works.")]
    [SerializeField] private bool showDebugInfo = false;

    // --- conversation state ---
    private readonly List<Line> log = new List<Line>();
    private Line lastPlayerLine;
    private string lastPlayerText;
    private bool talking, waiting;
    private string inputText = "";
    private string status = "";
    private bool stayPut, pendingToggleStay;

    // --- navigation state ---
    private NPCDestination pendingOffer;        // the place the player asked about (waiting for the NPC's reply)
    private NPCDestination activeOffer;         // the place being offered right now
    private bool offering;                      // showing the Yes / No buttons
    private int pendingOfferChoice = -1;        // 0 = yes, 1 = no (set by buttons / keys)
    private enum OfferKind { Guide, Facts }
    private OfferKind offerKind = OfferKind.Guide;   // what the Yes / No buttons are about
    private string factsTopic;                  // set while asking for facts about a place (used to look them up)
    private List<NPCDestination> pendingChoices;     // several places match: waiting for the NPC's reply
    private List<NPCDestination> choices;            // the list the player picks from
    private bool choosing;                      // showing the numbered list of places
    private int pendingChoicePick = -1;         // index picked, or -2 for "none"
    private NPCDestination guidingTo;           // where the NPC is leading the player
    private NPCDestination lastPlace;           // the last place the player asked about ("take me there")
    private float startGuideAt = -1f;           // a direct "take me there" order: the guide starts at this time
    private string arrivedName = "";
    private float arrivedUntil;

    // --- language state ---
    private DialogueLanguage language = DialogueLanguage.English;
    private bool languageChosen;
    private bool selectingLanguage;
    private bool sendBriefing = true;           // send the character description + background facts with the next message
    private int pendingLanguage = -1;           // set by buttons / keys, applied in Update
    private bool pendingChooseLanguage;

    // --- UI state ---
    private bool pendingSend, pendingLeave, focusRequested;
    private float openedTime;
    private Vector2 scroll;
    private int scrollFrames;
    private GUIStyle boxStyle, promptStyle, nameStyle, npcBubbleStyle, playerBubbleStyle, thinkStyle, statusStyle,
                     inputStyle, hintStyle, buttonStyle, smallButtonStyle, accentButtonStyle, yesButtonStyle,
                     choiceButtonStyle, bigButtonStyle, scrollbarStyle;
    private GUISkin chatSkin;
    private float builtTextSize;
    private Font chatFont, chatFontBold;
    private readonly List<Texture2D> uiTextures = new List<Texture2D>();

    // --- things we pause while talking ---
    private GTAPlayerMovement playerMovement;
    private NPCSplineFollower follower;
    private bool followerWasEnabled;

    private void Start()
    {
        if (player == null)
        {
            GTAPlayerMovement pm = FindFirstObjectByType<GTAPlayerMovement>();
            if (pm != null) player = pm.transform;
        }
        if (player == null)
            Debug.LogWarning($"{name}: NPCDialogue couldn't find the player. Drag it into the Player slot.", this);
        else
            playerMovement = player.GetComponent<GTAPlayerMovement>();

        if (agent == null) agent = GetComponent<LLMUnity.LLMAgent>();
        if (agent == null)
            Debug.LogWarning($"{name}: NPCDialogue needs an 'LLM Agent' component on this object.", this);

        follower = GetComponent<NPCSplineFollower>();
        if (companion == null) companion = GetComponent<NPCCompanion>();
        if (companion == null) companion = GetComponentInChildren<NPCCompanion>();
        if (companion == null) companion = GetComponentInParent<NPCCompanion>();

        if (knowledge == null) knowledge = GetComponent<NPCKnowledge>();
        if (knowledge == null) knowledge = GetComponentInChildren<NPCKnowledge>();
        if (knowledge == null) knowledge = GetComponentInParent<NPCKnowledge>();

        Debug.Log($"NPCDialogue ready on '{name}'. Player: {(player != null ? "'" + player.name + "'" : "NOT FOUND")}, " +
                  $"LLM Agent: {(agent != null ? "found" : "MISSING")}.", this);
    }

    private void OnDisable()
    {
        if (talking) EndTalking();
    }

    private void OnDestroy()
    {
        foreach (Texture2D t in uiTextures) if (t != null) Destroy(t);
        uiTextures.Clear();
        if (chatSkin != null) Destroy(chatSkin);
    }

    // Picks the English or Croatian text, depending on the language the player chose
    private string T(string english, string croatian)
    {
        return language == DialogueLanguage.Croatian ? croatian : english;
    }

    // The NPC's name in the language being spoken
    private string DisplayName =>
        (language == DialogueLanguage.Croatian && !string.IsNullOrWhiteSpace(npcNameCroatian)) ? npcNameCroatian : npcName;

    // A name that is the same in both languages (or "" if the names differ), safe to show before a language is chosen
    private string NeutralName =>
        (string.IsNullOrWhiteSpace(npcNameCroatian) || npcNameCroatian == npcName) ? npcName : "";

    // ---------------------------------------------------------------- logic

    private void Update()
    {
        if (player == null) return;

        // UI buttons only set flags; the real work happens here (IMGUI dislikes layout changes mid-frame)
        if (pendingSend) { pendingSend = false; SendPlayerMessage(); }
        if (pendingLeave) { pendingLeave = false; if (talking) EndTalking(); }
        if (pendingToggleStay) { pendingToggleStay = false; ToggleStay(); }
        if (pendingOfferChoice >= 0)
        {
            int choice = pendingOfferChoice;
            pendingOfferChoice = -1;
            if (talking && offering)
            {
                if (choice == 0) AcceptOffer();
                else DeclineOffer();
            }
        }
        if (pendingChoicePick != -1)
        {
            int pick = pendingChoicePick;
            pendingChoicePick = -1;
            if (talking && choosing) PickChoice(pick);
        }
        if (startGuideAt >= 0f && Time.unscaledTime >= startGuideAt)
        {
            startGuideAt = -1f;
            if (talking && activeOffer != null) AcceptOffer();
        }
        if (guidingTo != null && (companion == null || !companion.IsGuiding)) guidingTo = null;   // guiding ended
        if (pendingChooseLanguage) { pendingChooseLanguage = false; if (talking) selectingLanguage = true; }
        if (pendingLanguage >= 0)
        {
            int chosen = pendingLanguage;
            pendingLanguage = -1;
            if (talking) ApplyLanguage(chosen == 1 ? DialogueLanguage.Croatian : DialogueLanguage.English);
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (!talking)
        {
            if (InRange() && kb[interactKey].wasPressedThisFrame) BeginTalking();
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame) { EndTalking(); return; }

        if (selectingLanguage)
        {
            if (kb.digit1Key.wasPressedThisFrame) pendingLanguage = 0;
            else if (kb.digit2Key.wasPressedThisFrame) pendingLanguage = 1;
        }

        if (offering)
        {
            if (kb.digit1Key.wasPressedThisFrame) pendingOfferChoice = 0;
            else if (kb.digit2Key.wasPressedThisFrame) pendingOfferChoice = 1;
        }

        if (choosing && choices != null)
        {
            for (int i = 0; i < choices.Count && i < 9; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) pendingChoicePick = i;
            if (kb.digit0Key.wasPressedThisFrame) pendingChoicePick = -2;
        }

        FacePlayer();
    }

    // The object that physically walks around. Distance and facing are measured from here,
    // so the prompt follows the mouse even if NPCDialogue sits on a different object.
    private Transform Anchor => companion != null ? companion.transform : transform;

    // How close the player must be to talk (larger while the NPC is following)
    private float Range => (companion != null && companion.IsFollowing) ? followingInteractDistance : interactDistance;

    private bool InRange()
    {
        Vector3 d = player.position - Anchor.position;
        d.y = 0f;
        return d.sqrMagnitude <= Range * Range;
    }

    private void BeginTalking()
    {
        talking = true;
        status = "";
        openedTime = Time.unscaledTime;
        focusRequested = true;
        scrollFrames = 3;

        if (playerMovement != null) playerMovement.enabled = false;
        if (follower != null) { followerWasEnabled = follower.enabled; follower.enabled = false; }
        if (companion != null) companion.SetPaused(true);

        // First ask which language to talk in (unless it was chosen before and we should remember it)
        if (askLanguageEveryTime || !languageChosen)
            selectingLanguage = true;
        else
            StartConversation();
    }

    private void EndTalking()
    {
        talking = false;
        selectingLanguage = false;
        offering = false;
        choosing = false;
        pendingChoices = null;
        choices = null;
        pendingOffer = null;
        activeOffer = null;
        startGuideAt = -1f;
        if (playerMovement != null) playerMovement.enabled = true;
        if (follower != null && followerWasEnabled) follower.enabled = true;

        if (companion != null)
        {
            companion.SetPaused(false);
            if (followAfterConversation && !stayPut && player != null) companion.StartFollowing(player);
        }
    }

    // The player picked a language. Switching language starts a fresh conversation, so that nothing
    // from the other language stays on screen or in the model's memory.
    private void ApplyLanguage(DialogueLanguage chosen)
    {
        bool changed = languageChosen && chosen != language;

        language = chosen;
        languageChosen = true;
        selectingLanguage = false;
        status = "";
        openedTime = Time.unscaledTime;
        focusRequested = true;
        scrollFrames = 3;

        if (changed)
        {
            offering = false;
            choosing = false;
            pendingChoices = null;
            choices = null;
            startGuideAt = -1f;
            pendingOffer = null;
            activeOffer = null;
            log.Clear();
            lastPlayerLine = null;
            lastPlayerText = null;
            ResetAgentHistory();
            sendBriefing = true;
        }

        StartConversation();
    }

    private void StartConversation()
    {
        if (speaksFirst && log.Count == 0 && !waiting)
            Say(T("(The player has just walked up to you. Greet them in character.)",
                  "(Igrač ti se upravo približio. Pozdravi ga u svojoj ulozi.)"), visible: false);
    }

    private void FacePlayer()
    {
        if (!facePlayerWhileTalking) return;
        Transform t = Anchor;
        Vector3 dir = player.position - t.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    private void ToggleStay()
    {
        stayPut = !stayPut;
        if (stayPut && companion != null) companion.StopFollowing();
        // if the player chose "Follow me", following starts when the conversation ends
    }

    private void SendPlayerMessage()
    {
        string text = inputText.Trim();
        if (text.Length == 0 || waiting || selectingLanguage || offering || choosing) return;
        inputText = "";
        focusRequested = true;
        Say(text, visible: true);
    }

    private void Say(string text, bool visible)
    {
        if (agent == null)
        {
            status = T("No 'LLM Agent' component found on this object. Add one to the mouse.",
                       "Na ovom objektu nema komponente 'LLM Agent'. Dodaj je mišu.");
            if (visible) inputText = text;
            return;
        }

        // "Take me to the church": an order. No need to ask the model; the mouse answers and leads the way.
        if (visible && TryDirectGuide(text)) return;

        lastPlayerLine = null;
        lastPlayerText = visible ? text : null;
        if (visible) lastPlayerLine = AddLine(true, text);

        waiting = true;
        status = "";

        // What the model receives (the player only sees their own words):
        //   language tag, then (first message only) the character description, then the matching facts,
        //   then the player's message, then a reminder of the language to answer in.
        string query = LanguageTag() + "\n";
        if (sendBriefing) query += BuildBriefing() + "\n\n";
        if (knowledge != null)
        {
            string notes = knowledge.BuildContextFor(visible ? text : (factsTopic ?? ""), language, sendBriefing);
            if (notes.Length > 0) query += notes + "\n\n";
        }
        factsTopic = null;
        if (visible) query += NavigationNote(text);
        sendBriefing = false;
        query += text + "\n\n" + LanguageReminder();

        _ = AskAsync(query);
    }

    // ------------------------------------------------------------- navigation

    // The places a message is about: the ones it names (all of them if it only names a kind of place,
    // like "a hotel"), or, for "there" / "tamo" / "again", the one asked about before.
    private List<NPCDestination> PlacesFor(string text)
    {
        List<NPCDestination> list = NPCDestination.FindAllInMessage(text);
        if (list.Count == 0 && lastPlace != null && NPCDestination.RefersToPreviousPlace(text)) list.Add(lastPlace);
        return list;
    }

    // Nearest to the player first
    private void SortByDistance(List<NPCDestination> list)
    {
        if (player == null) return;
        Vector3 p = player.position;
        list.Sort((x, y) => (x.transform.position - p).sqrMagnitude.CompareTo((y.transform.position - p).sqrMagnitude));
    }

    // A direct order to be led somewhere. Replies with a fixed line, then starts guiding a moment later.
    private bool TryDirectGuide(string text)
    {
        if (!offerNavigation || companion == null || NPCDestination.All.Count == 0) return false;
        if (!NPCDestination.LooksLikeDirectCommand(text)) return false;

        List<NPCDestination> places = PlacesFor(text);
        if (places.Count != 1) return false;   // nothing named (the model answers), or several (the player picks)
        NPCDestination place = places[0];

        lastPlace = place;
        pendingOffer = null;
        offering = false;
        offerKind = OfferKind.Guide;
        activeOffer = place;
        lastPlayerLine = null;
        lastPlayerText = null;

        AddLine(true, text);
        string name = place.NameFor(language);
        AddLine(false, T("Of course! Follow me to " + name + ".", "Naravno! Slijedi me do: " + name + "."));
        startGuideAt = Time.unscaledTime + 1.2f;   // long enough to read the answer
        return true;
    }

    // If the player asks where something is, tell the model what it knows. One known place becomes an offer
    // (the Yes / No buttons appear after the NPC's reply); several places become a list to pick from;
    // an unknown place is answered honestly.
    private string NavigationNote(string text)
    {
        pendingOffer = null;
        pendingChoices = null;
        if (!offerNavigation || companion == null || NPCDestination.All.Count == 0) return "";

        bool hr = language == DialogueLanguage.Croatian;
        List<NPCDestination> places = PlacesFor(text);

        // "where is...", "take me to...", or (when it names a place we know) "are there any hotels here?"
        bool intent = NPCDestination.LooksLikeDirectionsRequest(text)
                      || NPCDestination.LooksLikeDirectCommand(text)
                      || (places.Count > 0 && NPCDestination.LooksLikeExistenceQuestion(text));
        if (!intent) return "";

        if (places.Count > 1)
        {
            SortByDistance(places);
            if (places.Count > 6) places.RemoveRange(6, places.Count - 6);
            pendingChoices = places;

            var names = new System.Text.StringBuilder();
            for (int i = 0; i < places.Count; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(places[i].NameFor(language));
            }
            return hr
                ? "[Igrač pita za vrstu mjesta, a ti znaš više takvih mjesta u blizini: " + names + ". U jednoj do dvije kratke rečenice reci da znaš više njih, nabroji ih po imenu i pitaj koje želi posjetiti.]\n\n"
                : "[The player asked about a kind of place, and you know several of them nearby: " + names + ". In one or two short sentences, say you know several, list them by name, and ask which one they would like to visit.]\n\n";
        }

        if (places.Count == 1)
        {
            NPCDestination place = places[0];
            lastPlace = place;
            pendingOffer = place;
            string name = place.NameFor(language);
            return hr
                ? "[Igrač pita gdje je: " + name + ". Točno znaš gdje je. U jednoj do dvije kratke rečenice reci da znaš gdje je i ponudi da ga odvedeš tamo, pa pitaj želi li da ga povedeš. Ne daj upute za put.]\n\n"
                : "[The player asked where this place is: " + name + ". You know exactly where it is. In one or two short sentences, say that you know where it is and offer to take them there, then ask whether they would like you to lead the way. Do not give directions.]\n\n";
        }

        string known = NPCDestination.NamesFor(language);
        return hr
            ? "[Igrač pita gdje je neko mjesto, ali ne znaš mjesto s tim imenom. Reci da ne znaš gdje je to. Mjesta do kojih možeš odvesti igrača: " + known + ".]\n\n"
            : "[The player asked where a place is, but you don't know a place by that name. Say you don't know where that is. Places you can take the player to: " + known + ".]\n\n";
    }

    // The player picked a place from the list (or -2 = none of them)
    private void PickChoice(int pick)
    {
        List<NPCDestination> list = choices;
        choosing = false;
        choices = null;
        focusRequested = true;

        if (pick < 0 || list == null || pick >= list.Count)
        {
            AddLine(true, T("None of them, thanks.", "Nijedno, hvala."));
            Say(T("(The player doesn't want to go to any of those places. Reply in one short, friendly sentence and say they can ask you about anything else.)",
                  "(Igrač ne želi ići ni na jedno od tih mjesta. Odgovori jednom kratkom prijateljskom rečenicom i reci da te može pitati bilo što drugo.)"),
                visible: false);
            return;
        }

        NPCDestination place = list[pick];
        lastPlace = place;
        offerKind = OfferKind.Guide;
        activeOffer = place;

        string name = place.NameFor(language);
        AddLine(true, name);
        AddLine(false, T("Good choice! Follow me to " + name + ".", "Dobar izbor! Slijedi me do: " + name + "."));
        startGuideAt = Time.unscaledTime + 1.2f;
    }

    // "Yes": lead the way (close the chat), or tell facts about the place we are standing at
    private void AcceptOffer()
    {
        if (offerKind == OfferKind.Facts) { AcceptFacts(); return; }

        NPCDestination place = activeOffer;
        offering = false;
        activeOffer = null;
        if (place == null || companion == null) return;

        bool followAfter = followAfterConversation && !stayPut;
        guidingTo = place;

        EndTalking();   // gives the player control back
        companion.StartGuiding(player, place.transform.position, place.ArriveDistance, followAfter, OnGuideArrived);
    }

    private void AcceptFacts()
    {
        NPCDestination place = activeOffer;
        offering = false;
        activeOffer = null;
        focusRequested = true;
        if (place == null) return;

        string name = place.NameFor(language);
        AddLine(true, T("Yes, please.", "Da, molim."));
        factsTopic = place.FactsQuery();   // makes the matching knowledge entries available to the model
        Say(T("(The player is standing at " + name + " and wants to hear facts about it. Using the notes you were given, tell them two or three interesting facts in a friendly way, in at most four short sentences.)",
              "(Igrač stoji kod mjesta: " + name + " i želi čuti činjenice o njemu. Koristeći dane bilješke, prijateljski mu ispričaj dvije do tri zanimljive činjenice, u najviše četiri kratke rečenice.)"),
            visible: false);
    }

    // "No": the NPC stays where it is and the conversation goes on
    private void DeclineOffer()
    {
        bool facts = offerKind == OfferKind.Facts;
        offering = false;
        activeOffer = null;
        focusRequested = true;

        AddLine(true, T("No, thanks.", "Ne, hvala."));
        if (facts)
            Say(T("(The player doesn't want to hear facts right now. Reply in one short, friendly sentence and say they can ask you anything about this place.)",
                  "(Igrač trenutno ne želi čuti činjenice. Odgovori jednom kratkom prijateljskom rečenicom i reci da te može pitati bilo što o ovom mjestu.)"),
                visible: false);
        else
            Say(T("(The player declined your offer to lead them there. Reply in one short, friendly sentence and say you are right here if they need anything.)",
                  "(Igrač je odbio tvoju ponudu da ga odvedeš. Odgovori jednom kratkom prijateljskom rečenicom i reci da si tu ako mu išta zatreba.)"),
                visible: false);
    }

    // The mouse has led the player to the place: it stops, opens the chat and asks about facts
    private void OnGuideArrived()
    {
        if (this == null) return;
        NPCDestination place = guidingTo;
        arrivedName = place != null ? place.NameFor(language) : "";
        arrivedUntil = Time.time + 5f;
        guidingTo = null;

        if (!offerFactsOnArrival || place == null || talking || player == null) return;

        BeginTalking();                 // opens the chat and keeps the mouse standing here
        selectingLanguage = false;      // the language is already known
        pendingOffer = null;
        pendingChoices = null;

        string name = place.NameFor(language);
        AddLine(false, T("We're here: " + name + ". Would you like to hear some facts about it?",
                         "Stigli smo: " + name + ". Želiš li čuti nekoliko činjenica o tome?"));
        activeOffer = place;
        offerKind = OfferKind.Facts;
        offering = true;
    }

    // Short tag at the start of every message, written in the language to answer in
    private string LanguageTag()
    {
        return language == DialogueLanguage.Croatian
            ? "[Odgovaraj samo na hrvatskom jeziku.]"
            : "[Reply only in English.]";
    }

    // Reminder at the END of every message: small models pay most attention to the last lines
    private string LanguageReminder()
    {
        return language == DialogueLanguage.Croatian
            ? "(Odgovori samo na hrvatskom jeziku. Ne piši engleski i ne prevodi.)"
            : "(Reply only in English. Do not add a translation.)";
    }

    // Used for a second attempt when the model answered in the wrong language
    private string StrongReminder()
    {
        return language == DialogueLanguage.Croatian
            ? "[VAŽNO: odgovori ISKLJUČIVO na hrvatskom jeziku. Tvoj prethodni odgovor nije bio na hrvatskom.]"
            : "[IMPORTANT: reply ONLY in English. Your previous reply was not in English.]";
    }

    private async Task AskAsync(string query)
    {
        try
        {
            string reply = await agent.Chat(query);
            reply = LanguageGuard.Clean(reply, language, out bool wrongLanguage);

            // Wrong language: forget that exchange and try once more with a firmer instruction
            if (wrongLanguage)
            {
                DropLastExchange();
                string second = await agent.Chat(StrongReminder() + "\n" + query);
                second = LanguageGuard.Clean(second, language, out _);
                if (second.Length > 0) reply = second;
            }

            if (string.IsNullOrWhiteSpace(reply)) OnError(T("The local model returned an empty reply.",
                                                            "Lokalni model je vratio prazan odgovor."));
            else OnReply(reply.Trim());
        }
        catch (Exception e)
        {
            OnError(T("The local model isn't ready or failed (it may still be loading): ",
                      "Lokalni model nije spreman ili je zakazao (možda se još učitava): ") + e.Message);
        }
    }

    private void OnReply(string reply)
    {
        if (this == null) return;
        waiting = false;

        // The player asked where a known place is: after the NPC's offer, show the Yes / No buttons
        if (pendingOffer != null)
        {
            activeOffer = pendingOffer;
            pendingOffer = null;
            offerKind = OfferKind.Guide;
            offering = true;
            if (!reply.Contains("?"))
                reply += T(" Shall I take you there?", " Želiš li da te odvedem tamo?");
        }
        else if (pendingChoices != null)
        {
            // Several places match: show them as a numbered list after the NPC's reply
            choices = pendingChoices;
            pendingChoices = null;
            choosing = true;
            if (!reply.Contains("?"))
                reply += T(" Which one would you like to visit?", " Koje želiš posjetiti?");
        }

        AddLine(false, reply);
    }

    private void OnError(string error)
    {
        if (this == null) return;
        waiting = false;
        pendingOffer = null;
        pendingChoices = null;
        status = error;

        // Undo the failed message so the player can simply try again
        if (lastPlayerLine != null) log.Remove(lastPlayerLine);
        if (lastPlayerText != null && inputText.Length == 0) inputText = lastPlayerText;
        lastPlayerLine = null;
        lastPlayerText = null;
        sendBriefing = true;   // the character description may not have arrived, send it again
    }

    private Line AddLine(bool fromPlayer, string text)
    {
        var line = new Line { fromPlayer = fromPlayer, text = text };
        log.Add(line);
        scrollFrames = 3;
        return line;
    }

    // Removes the last player message and the last reply from the model's memory
    private void DropLastExchange()
    {
        try
        {
            var chat = agent.chat;
            if (chat != null && chat.Count >= 2) chat.RemoveRange(chat.Count - 2, 2);
        }
        catch (Exception) { }
    }

    // Gives the model a clean memory (used when the player switches language)
    private void ResetAgentHistory()
    {
        if (agent == null) return;
        try
        {
            var old = agent.chat;
            var fresh = new List<LLMUnity.ChatMessage>();
            // keep the system message, if this version of the package stores it in the chat list
            if (old != null && old.Count > 0 && old[0] != null && old[0].role == "system") fresh.Add(old[0]);
            agent.chat = fresh;
        }
        catch (Exception e)
        {
            Debug.LogWarning("NPCDialogue: couldn't reset the chat history: " + e.Message, this);
        }
    }

    // The character description, written in the language being spoken. It goes in front of the first
    // message of every conversation (instead of relying on the LLM Agent's System Prompt field).
    private string BuildBriefing()
    {
        bool hr = language == DialogueLanguage.Croatian;
        bool tagAlong = companion != null && followAfterConversation;

        if (hr)
        {
            return "Igraš lik u videoigri. Cijelo vrijeme ostani u liku.\n\n" +
                   personaCroatian.Trim() +
                   "\n\nTvoje ime je " + DisplayName + "." +
                   "\n\nŠto znaš o svom svijetu:\n" + worldNotesCroatian.Trim() +
                   (knowledge != null
                       ? "\n\nPonekad poruka igrača sadrži bilješke u uglatim zagradama. Koristi ih prirodno za odgovor; nikada ih ne čitaj naglas i ne spominji da si ih dobio. Ako ni tvoje znanje ni bilješke ne pokrivaju pitanje, reci da nisi siguran umjesto da izmišljaš činjenice."
                       : "") +
                   "\n\nPravila: razgovaraj s igračem. Odgovaraj kratko (jedna do tri rečenice) i prirodno, kao u govoru. " +
                   "Bez opisa radnji, bez zvjezdica, bez emotikona. " +
                   "Govoriš ISKLJUČIVO hrvatski (standardni hrvatski jezik). Nikada ne odgovaraj na engleskom i nikada ne prevodi svoj odgovor na engleski." +
                   (tagAlong ? "\nKad razgovor završi, rado pratiš igrača kao suputnik." : "");
        }

        return "You are playing a character in a video game. Stay in character at all times.\n\n" +
               persona.Trim() +
               "\n\nYour name is " + DisplayName + "." +
               "\n\nWhat you know about your world:\n" + worldNotes.Trim() +
               (knowledge != null
                   ? "\n\nSometimes the player's message contains some notes in square brackets. Use them naturally to answer; never read them out and never mention that you were given notes. If neither your knowledge nor the notes cover a question, say you're not sure instead of inventing facts."
                   : "") +
               "\n\nRules: talk with the player. Keep replies short (one to three sentences) and natural, like speech. " +
               "No stage directions, no asterisks, no emojis. " +
               "You speak only English. Never answer in Croatian and never add a translation." +
               (tagAlong ? "\nWhen the conversation ends, you happily tag along beside the player as a travelling companion." : "");
    }

    // ------------------------------------------------------- language check

    // Catches replies in the wrong language, and removes translations that the model adds next to its answer
    // (for example "Zdravo! (Hello!)" or an English line under a Croatian one).
    private static class LanguageGuard
    {
        private static readonly HashSet<string> EnglishWords = new HashSet<string>
        {
            "the", "and", "you", "your", "yours", "is", "are", "was", "were", "this", "that", "with", "for", "have", "has",
            "not", "but", "what", "how", "who", "where", "when", "why", "can", "will", "would", "could", "should", "hello",
            "welcome", "here", "there", "from", "about", "they", "them", "we", "it's", "i'm", "don't", "just", "very", "my",
            "of", "in", "it", "be", "am", "if", "or", "at", "as", "an", "by", "all", "so", "some", "there's", "that's",
            "what's", "let's", "you're", "i'd", "i'll", "i've", "love", "like", "know", "tell", "visit", "village", "little",
            "great", "good", "nice", "yes", "please", "thank", "thanks"
        };

        private static readonly HashSet<string> CroatianWords = new HashSet<string>
        {
            "je", "sam", "si", "smo", "ste", "su", "se", "ne", "da", "na", "za", "što", "sto", "ti", "ja", "vi", "ovo",
            "kako", "gdje", "kad", "kada", "koji", "koja", "koje", "ali", "od", "po", "pa", "ovdje", "tamo", "dobro",
            "hvala", "bok", "zdravo", "molim", "jako", "samo", "ima", "imam", "imaš", "možeš", "mogu", "volim", "kod",
            "ili", "jer", "nije", "nisam", "bih", "bi", "će", "ću", "ćeš", "već", "još", "vrlo", "svi", "sve", "moj",
            "moja", "moje", "tvoj", "tvoja", "naš", "naša", "ovaj", "ova", "taj", "ta", "tko", "zašto", "jesi", "jesam",
            "bio", "bila", "bilo", "ako", "kao", "preko", "iz", "među", "nešto", "ništa", "uvijek", "nikad", "sada",
            "danas", "malo", "puno", "mnogo", "hej", "dragi", "selo", "brdo", "crkva", "mi", "nas", "vas", "njih",
            "sa", "uz", "oko", "dan", "dobar", "dobrodošao", "dobrodošli", "u", "došli", "došao", "došla"
        };

        public static string Clean(string reply, DialogueLanguage language, out bool stillWrong)
        {
            stillWrong = false;
            if (string.IsNullOrWhiteSpace(reply)) return "";

            // *stage directions*
            string text = Regex.Replace(reply, @"\*[^*\n]*\*", " ");

            // translations in parentheses
            text = Regex.Replace(text, @"\(([^)]*)\)", m => IsWrong(m.Groups[1].Value, language, 1) ? "" : m.Value);

            // whole sentences / lines in the other language
            string[] parts = Regex.Split(text, @"(?<=[.!?])\s+|\n+");
            var kept = new List<string>();
            foreach (string part in parts)
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                if (!IsWrong(p, language, 2)) kept.Add(p);
            }

            if (kept.Count == 0)
            {
                stillWrong = true;          // everything was in the wrong language
                return text.Trim();
            }

            return string.Join(" ", kept).Trim();
        }

        private static bool IsWrong(string segment, DialogueLanguage language, int minHits)
        {
            int english = 0, croatian = 0, total = 0;
            foreach (Match m in Regex.Matches(segment.ToLowerInvariant(), @"[\p{L}']+"))
            {
                string w = m.Value;
                total++;
                if (EnglishWords.Contains(w)) english++;
                if (CroatianWords.Contains(w)) croatian++;
            }

            // very short segments ("Hello!") only need one telling word
            int needed = total <= 3 ? 1 : minHits;

            return language == DialogueLanguage.Croatian
                ? english >= needed && english > croatian
                : croatian >= needed && croatian > english;
        }
    }


    // ------------------------------------------------------------------ UI

    private void OnGUI()
    {
        if (showDebugInfo) DrawDebugInfo();
        if (player == null) return;
        DrawGuideBanner();
        if (!talking && !InRange()) return;

        EnsureStyles();
        GUISkin previousSkin = GUI.skin;
        GUI.skin = chatSkin;

        float s = Screen.height / 720f * uiScale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        if (!talking)
        {
            // Before a language has been chosen the prompt is just the key (and the name, if it is the same
            // in both languages); afterwards it is written only in the chosen language.
            string prompt = languageChosen
                ? T("Press " + interactKey + " to talk", "Pritisni " + interactKey + " za razgovor")
                : ("[" + interactKey + "]   " + NeutralName).Trim();

            var r = new Rect(vw / 2f - 260f, vh - 96f, 520f, 54f);
            GUI.Box(r, prompt, promptStyle);
        }
        else if (selectingLanguage)
        {
            DrawLanguageChoice(vw, vh);
        }
        else
        {
            DrawDialogue(vw, vh);
        }

        GUI.matrix = Matrix4x4.identity;
        GUI.skin = previousSkin;
    }

    // A line at the top of the screen while the NPC is leading the way, and for a moment after arriving
    private void DrawGuideBanner()
    {
        string message = null;
        if (guidingTo != null) message = T("On the way to: ", "Na putu do: ") + guidingTo.NameFor(language);
        else if (Time.time < arrivedUntil && arrivedName.Length > 0) message = T("You have arrived: ", "Stigli ste: ") + arrivedName;
        if (message == null) return;

        EnsureStyles();
        float s = Screen.height / 720f * uiScale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float vw = Screen.width / s;

        GUI.Box(new Rect(vw / 2f - 300f, 44f, 600f, 54f), message, promptStyle);
        GUI.matrix = Matrix4x4.identity;
    }

    private void DrawDebugInfo()
    {
        string agentInfo = agent != null ? "found" : "MISSING";
        string languageInfo = languageChosen ? language.ToString() : "not chosen yet";
        string info;
        if (player == null)
        {
            info = "NPCDialogue on '" + name + "': PLAYER NOT FOUND (drag it into the Player slot) | LLM Agent: " + agentInfo;
        }
        else
        {
            Vector3 d = player.position - Anchor.position;
            d.y = 0f;
            info = "NPCDialogue on '" + name + "' | player '" + player.name + "' | distance " +
                   d.magnitude.ToString("F1") + " (talk range " + Range.ToString("F1") + ") | key " +
                   interactKey + " | LLM Agent: " + agentInfo + " | language: " + languageInfo +
                   " | companion: " + (companion == null ? "none" : companion.IsFollowing ? "following" : "idle");
        }

        Color old = GUI.color;
        GUI.color = Color.yellow;
        GUI.Label(new Rect(10f, 8f, 1100f, 26f), info);
        GUI.color = old;
    }

    private void DrawLanguageChoice(float vw, float vh)
    {
        const float pad = 22f;
        float w = Mathf.Min(580f, vw - 40f);
        float h = NeutralName.Length > 0 ? 190f : 135f;
        var panel = new Rect((vw - w) / 2f, vh - h - 24f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + pad, panel.y + 16f, panel.width - pad * 2f, panel.height - 32f));

        if (NeutralName.Length > 0)
        {
            GUILayout.Label(NeutralName, nameStyle);
            GUILayout.Space(6f);
        }

        float half = (panel.width - pad * 2f - 12f) / 2f;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("English   [1]", bigButtonStyle, GUILayout.Width(half), GUILayout.Height(64f))) pendingLanguage = 0;
        GUILayout.Space(12f);
        if (GUILayout.Button("Hrvatski   [2]", bigButtonStyle, GUILayout.Width(half), GUILayout.Height(64f))) pendingLanguage = 1;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawDialogue(float vw, float vh)
    {
        Event e = Event.current;

        const float pad = 22f;
        float w = Mathf.Min(860f, vw - 40f);
        float h = Mathf.Min(500f, vh - 60f);
        var panel = new Rect((vw - w) / 2f, vh - h - 24f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        float tz = Mathf.Max(1f, textSize);   // bigger text gets proportionally bigger buttons
        float inner = w - pad * 2f;   // every control below has a fixed width, so nothing moves while typing
        GUILayout.BeginArea(new Rect(panel.x + pad, panel.y + 14f, inner, h - 28f));

        // ---- header: name on the left, small buttons on the right (they never move)
        GUILayout.BeginHorizontal(GUILayout.Height(42f * tz));
        GUILayout.Label(DisplayName, nameStyle, GUILayout.ExpandWidth(true), GUILayout.Height(42f * tz));
        if (GUILayout.Button(T("Language", "Jezik"), smallButtonStyle, GUILayout.Width(118f * tz), GUILayout.Height(38f * tz)))
            pendingChooseLanguage = true;
        if (companion != null && followAfterConversation)
        {
            string stayLabel = stayPut ? T("Follow me", "Prati me") : T("Stay here", "Ostani ovdje");
            if (GUILayout.Button(stayLabel, smallButtonStyle, GUILayout.Width(158f * tz), GUILayout.Height(38f * tz)))
                pendingToggleStay = true;
        }
        if (GUILayout.Button(T("Leave", "Zatvori"), smallButtonStyle, GUILayout.Width(110f * tz), GUILayout.Height(38f * tz)))
            pendingLeave = true;
        GUILayout.EndHorizontal();

        // thin divider
        GUILayout.Space(6f);
        Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        Color prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        GUI.DrawTexture(line, Texture2D.whiteTexture);
        GUI.color = prev;
        GUILayout.Space(8f);

        // ---- conversation: speech bubbles (NPC on the left, player on the right)
        if (scrollFrames > 0)
        {
            scroll.y = float.MaxValue;
            if (e.type == EventType.Repaint) scrollFrames--;
        }
        scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, scrollbarStyle, GUILayout.ExpandHeight(true));
        float maxBubble = inner * 0.78f;
        foreach (Line l in log)
        {
            GUILayout.BeginHorizontal();
            if (l.fromPlayer)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(l.text, playerBubbleStyle, GUILayout.MaxWidth(maxBubble));
            }
            else
            {
                GUILayout.Label(l.text, npcBubbleStyle, GUILayout.MaxWidth(maxBubble));
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
        }
        if (waiting)
        {
            string dots = new string('.', 1 + (int)(Time.unscaledTime * 2.5f) % 3);
            GUILayout.BeginHorizontal();
            GUILayout.Label(DisplayName + T(" is thinking", " razmišlja") + dots, thinkStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        if (status.Length > 0) GUILayout.Label(status, statusStyle);
        GUILayout.Space(8f);

        // ---- bottom: a list to pick from, the Yes / No question, or the text box
        if (choosing && choices != null)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                string label = (i + 1) + ".   " + choices[i].NameFor(language) + "      [" + (i + 1) + "]";
                if (GUILayout.Button(label, choiceButtonStyle, GUILayout.Height(46f))) pendingChoicePick = i;
            }
            if (GUILayout.Button(T("None of them   [0]", "Nijedno   [0]"), choiceButtonStyle, GUILayout.Height(42f)))
                pendingChoicePick = -2;
        }
        else if (offering)
        {
            bool facts = offerKind == OfferKind.Facts;
            float half = (inner - 12f) / 2f;
            GUILayout.BeginHorizontal();
            string yesLabel = facts ? T("Yes, tell me   [1]", "Da, ispričaj   [1]") : T("Yes, lead the way   [1]", "Da, vodi me   [1]");
            if (GUILayout.Button(yesLabel, yesButtonStyle, GUILayout.Width(half), GUILayout.Height(58f)))
                pendingOfferChoice = 0;
            GUILayout.Space(12f);
            if (GUILayout.Button(T("No, thanks   [2]", "Ne, hvala   [2]"), buttonStyle, GUILayout.Width(half), GUILayout.Height(58f)))
                pendingOfferChoice = 1;
            GUILayout.EndHorizontal();
        }
        else
        {
            bool enter = e.type == EventType.KeyDown
                         && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                         && GUI.GetNameOfFocusedControl() == "ChatInput";
            if (enter) { pendingSend = true; e.Use(); }

            // ignore the key that opened the dialogue so it isn't typed into the box
            if (Time.unscaledTime < openedTime + 0.2f) inputText = "";

            float sendWidth = 130f * tz;
            float fieldWidth = inner - sendWidth - 10f;   // FIXED width: long text scrolls inside the box, buttons stay put

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("ChatInput");
            inputText = GUILayout.TextField(inputText, 300, inputStyle, GUILayout.Width(fieldWidth), GUILayout.Height(52f * tz));

            if (e.type == EventType.Repaint && inputText.Length == 0)
            {
                Rect fr = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(fr.x + 20f, fr.y, fr.width - 28f, fr.height),
                          T("Type a message and press Enter...", "Napiši poruku i pritisni Enter..."), hintStyle);
            }

            GUILayout.Space(10f);
            if (GUILayout.Button(T("Send", "Pošalji"), accentButtonStyle, GUILayout.Width(sendWidth), GUILayout.Height(52f * tz)))
                pendingSend = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndArea();

        if (focusRequested && e.type == EventType.Repaint)
        {
            GUI.FocusControl("ChatInput");
            focusRequested = false;
        }
    }

    // ------------------------------------------------------------------ look (fonts, colours, rounded boxes)

    // A rounded rectangle texture with an optional outline, stretched with 9-slice (see GUIStyle.border)
    private Texture2D MakeRounded(Color fill, Color outline, int radius, float outlineWidth)
    {
        int size = radius * 2 + 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // signed distance to the rounded box (negative inside)
                float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float d = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;

                float outer = Mathf.Clamp01(0.5f - d);                      // inside the shape
                float inside = Mathf.Clamp01(0.5f - (d + outlineWidth));    // inside the outline
                Color c = Color.Lerp(outline, fill, inside);
                c.a *= outer;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        uiTextures.Add(tex);
        return tex;
    }

    // Applies the chat font to a style (the bold font for bold styles, if there is one)
    private void SetFont(GUIStyle style)
    {
        style.fontSize = Mathf.RoundToInt(style.fontSize * textSize);
        if (chatFont == null) return;
        if (style.fontStyle == FontStyle.Bold && chatFontBold != null)
        {
            style.font = chatFontBold;
            style.fontStyle = FontStyle.Normal;
        }
        else style.font = chatFont;
    }

    private void Apply(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, Color text, int radius)
    {
        style.normal.background = normal;
        style.hover.background = hover != null ? hover : normal;
        style.active.background = active != null ? active : normal;
        style.focused.background = style.normal.background;
        style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = text;
        style.border = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1);
        SetFont(style);
    }

    private GUIStyle MakeButton(Color normal, Color hover, Color active, int fontSize, FontStyle fontStyle,
                                TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var style = new GUIStyle(GUI.skin.button)
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            alignment = anchor,
            padding = new RectOffset(16, 16, 6, 6),
            margin = new RectOffset(4, 4, 4, 4)
        };
        Apply(style, MakeRounded(normal, new Color(1f, 1f, 1f, 0.08f), 10, 1f),
                     MakeRounded(hover, new Color(1f, 1f, 1f, 0.18f), 10, 1f),
                     MakeRounded(active, new Color(1f, 1f, 1f, 0.05f), 10, 1f),
                     Color.white, 10);
        return style;
    }

    private GUIStyle MakeBubble(Color fill, Color text)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 23,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            richText = false,
            padding = new RectOffset(18, 18, 11, 13),
            margin = new RectOffset(2, 2, 4, 4)
        };
        Apply(style, MakeRounded(fill, Color.clear, 16, 0f), null, null, text, 16);
        return style;
    }

    private void EnsureStyles()
    {
        if (boxStyle != null)
        {
            if (Mathf.Approximately(builtTextSize, textSize)) return;

            // the text size changed: rebuild everything
            foreach (Texture2D t in uiTextures) if (t != null) Destroy(t);
            uiTextures.Clear();
            if (chatSkin != null) Destroy(chatSkin);
        }
        builtTextSize = textSize;

        // Font: the one you assigned, or Atkinson Hyperlegible from Resources/Fonts, or the computer's own
        chatFont = uiFont != null ? uiFont : Resources.Load<Font>("Fonts/AtkinsonHyperlegible_400Regular");
        chatFontBold = uiFontBold != null ? uiFontBold : Resources.Load<Font>("Fonts/AtkinsonHyperlegible_700Bold");
        if (chatFont == null)
        {
            try
            {
                chatFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Segoe UI", "Calibri", "Arial", "Helvetica Neue", "Helvetica", "Liberation Sans" }, 22);
            }
            catch (Exception) { chatFont = null; }
        }

        // our own copy of the skin, so scrollbars and the text cursor look right without touching anything else
        chatSkin = Instantiate(GUI.skin);
        chatSkin.settings.cursorColor = Color.white;
        chatSkin.settings.selectionColor = new Color(0.25f, 0.5f, 0.95f, 0.5f);
        if (chatFont != null) chatSkin.font = chatFont;

        Color white = Color.white;

        // panel + pills
        Texture2D panelTex = MakeRounded(new Color(0.07f, 0.08f, 0.12f, 0.95f), new Color(1f, 1f, 1f, 0.10f), 20, 1.5f);
        boxStyle = new GUIStyle(GUI.skin.box) { border = new RectOffset(21, 21, 21, 21), padding = new RectOffset(0, 0, 0, 0) };
        boxStyle.normal.background = panelTex;
        SetFont(boxStyle);

        Texture2D pillTex = MakeRounded(new Color(0.07f, 0.08f, 0.12f, 0.93f), new Color(1f, 1f, 1f, 0.12f), 22, 1.5f);
        promptStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 23,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(23, 23, 23, 23)
        };
        promptStyle.normal.background = pillTex;
        promptStyle.normal.textColor = white;
        SetFont(promptStyle);

        nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        nameStyle.normal.textColor = new Color(1f, 0.86f, 0.55f);
        SetFont(nameStyle);

        // bubbles
        npcBubbleStyle = MakeBubble(new Color(0.16f, 0.19f, 0.27f, 1f), white);
        playerBubbleStyle = MakeBubble(new Color(0.18f, 0.40f, 0.80f, 1f), white);
        thinkStyle = MakeBubble(new Color(0.13f, 0.15f, 0.21f, 1f), new Color(0.72f, 0.76f, 0.85f));
        thinkStyle.fontStyle = FontStyle.Italic;

        statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true };
        statusStyle.normal.textColor = new Color(1f, 0.6f, 0.6f);
        SetFont(statusStyle);

        // text box: one line, clipped, fixed width (set where it is drawn)
        inputStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 23,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(18, 18, 8, 8),
            margin = new RectOffset(4, 4, 4, 4)
        };
        Texture2D fieldTex = MakeRounded(new Color(0.11f, 0.13f, 0.19f, 1f), new Color(1f, 1f, 1f, 0.14f), 12, 1.5f);
        Texture2D fieldFocus = MakeRounded(new Color(0.11f, 0.13f, 0.19f, 1f), new Color(0.40f, 0.65f, 1f, 0.95f), 12, 1.5f);
        inputStyle.normal.background = fieldTex;
        inputStyle.hover.background = fieldTex;
        inputStyle.focused.background = fieldFocus;
        inputStyle.active.background = fieldFocus;
        inputStyle.normal.textColor = inputStyle.hover.textColor = inputStyle.focused.textColor = inputStyle.active.textColor = white;
        inputStyle.border = new RectOffset(13, 13, 13, 13);
        SetFont(inputStyle);

        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 23, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.38f);
        SetFont(hintStyle);

        // buttons
        Color neutral = new Color(0.20f, 0.23f, 0.31f, 1f);
        buttonStyle = MakeButton(neutral, new Color(0.28f, 0.32f, 0.43f, 1f), new Color(0.15f, 0.17f, 0.24f, 1f), 22, FontStyle.Bold);
        smallButtonStyle = MakeButton(neutral, new Color(0.28f, 0.32f, 0.43f, 1f), new Color(0.15f, 0.17f, 0.24f, 1f), 19, FontStyle.Normal);
        smallButtonStyle.padding = new RectOffset(8, 8, 4, 4);
        accentButtonStyle = MakeButton(new Color(0.20f, 0.44f, 0.86f, 1f), new Color(0.27f, 0.52f, 0.95f, 1f), new Color(0.15f, 0.34f, 0.70f, 1f), 22, FontStyle.Bold);
        yesButtonStyle = MakeButton(new Color(0.17f, 0.55f, 0.36f, 1f), new Color(0.22f, 0.65f, 0.43f, 1f), new Color(0.12f, 0.43f, 0.28f, 1f), 22, FontStyle.Bold);
        choiceButtonStyle = MakeButton(neutral, new Color(0.28f, 0.32f, 0.43f, 1f), new Color(0.15f, 0.17f, 0.24f, 1f), 22, FontStyle.Normal, TextAnchor.MiddleLeft);
        bigButtonStyle = MakeButton(new Color(0.20f, 0.44f, 0.86f, 1f), new Color(0.27f, 0.52f, 0.95f, 1f), new Color(0.15f, 0.34f, 0.70f, 1f), 27, FontStyle.Bold);

        // slim scrollbar
        scrollbarStyle = new GUIStyle(chatSkin.verticalScrollbar) { fixedWidth = 8f, margin = new RectOffset(6, 0, 2, 2) };
        scrollbarStyle.normal.background = MakeRounded(new Color(1f, 1f, 1f, 0.06f), Color.clear, 4, 0f);
        scrollbarStyle.border = new RectOffset(5, 5, 5, 5);
        GUIStyle thumb = chatSkin.verticalScrollbarThumb;
        thumb.fixedWidth = 8f;
        thumb.normal.background = MakeRounded(new Color(1f, 1f, 1f, 0.30f), Color.clear, 4, 0f);
        thumb.hover.background = thumb.active.background = thumb.focused.background = thumb.normal.background;
        thumb.border = new RectOffset(5, 5, 5, 5);
        chatSkin.verticalScrollbarUpButton.fixedHeight = 0f;
        chatSkin.verticalScrollbarDownButton.fixedHeight = 0f;
    }
}