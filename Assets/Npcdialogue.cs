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

    [Header("Interaction")]
    [Tooltip("Leave empty to find the object with GTAPlayerMovement automatically.")]
    [SerializeField] private Transform player;
    [SerializeField] private Key interactKey = Key.E;
    [SerializeField] private float interactDistance = 4f;
    [Tooltip("Talk range while the NPC is following you. It is larger so you can always talk to your companion.")]
    [SerializeField] private float followingInteractDistance = 12f;
    [SerializeField] private bool facePlayerWhileTalking = true;
    [SerializeField] private float turnSpeed = 6f;

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
    private GUIStyle boxStyle, promptStyle, nameStyle, npcLineStyle, playerLineStyle, thinkStyle,
                     statusStyle, inputStyle, buttonStyle, bigButtonStyle;
    private Texture2D panelTex;

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
        if (panelTex != null) Destroy(panelTex);
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
        if (text.Length == 0 || waiting || selectingLanguage || offering) return;
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
            string notes = knowledge.BuildContextFor(visible ? text : "", language, sendBriefing);
            if (notes.Length > 0) query += notes + "\n\n";
        }
        if (visible) query += NavigationNote(text);
        sendBriefing = false;
        query += text + "\n\n" + LanguageReminder();

        _ = AskAsync(query);
    }

    // ------------------------------------------------------------- navigation

    // If the player asks where something is, tell the model what it knows. A known place becomes an offer
    // (the Yes / No buttons appear after the NPC's reply); an unknown place is answered honestly.
    // The place a message is about: named in it, or (for "there", "tamo", "again") the one asked about before
    private NPCDestination PlaceFor(string text)
    {
        NPCDestination place = NPCDestination.FindInMessage(text);
        if (place == null && lastPlace != null && NPCDestination.RefersToPreviousPlace(text)) place = lastPlace;
        return place;
    }

    // A direct order to be led somewhere. Replies with a fixed line, then starts guiding a moment later.
    private bool TryDirectGuide(string text)
    {
        if (!offerNavigation || companion == null || NPCDestination.All.Count == 0) return false;
        if (!NPCDestination.LooksLikeDirectCommand(text)) return false;

        NPCDestination place = PlaceFor(text);
        if (place == null) return false;   // no known place named: let the model answer normally

        lastPlace = place;
        pendingOffer = null;
        offering = false;
        activeOffer = place;
        lastPlayerLine = null;
        lastPlayerText = null;

        AddLine(true, text);
        string name = place.NameFor(language);
        AddLine(false, T("Of course! Follow me to " + name + ".", "Naravno! Slijedi me do: " + name + "."));
        startGuideAt = Time.unscaledTime + 1.2f;   // long enough to read the answer
        return true;
    }

    private string NavigationNote(string text)
    {
        pendingOffer = null;
        if (!offerNavigation || companion == null || NPCDestination.All.Count == 0) return "";
        if (!NPCDestination.LooksLikeDirectionsRequest(text) && !NPCDestination.LooksLikeDirectCommand(text)) return "";

        bool hr = language == DialogueLanguage.Croatian;
        NPCDestination place = PlaceFor(text);

        if (place != null)
        {
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

    // "Yes": close the chat and start leading the way
    private void AcceptOffer()
    {
        NPCDestination place = activeOffer;
        offering = false;
        activeOffer = null;
        if (place == null || companion == null) return;

        bool followAfter = followAfterConversation && !stayPut;
        guidingTo = place;

        EndTalking();   // gives the player control back
        companion.StartGuiding(player, place.transform.position, place.ArriveDistance, followAfter, OnGuideArrived);
    }

    // "No": the NPC stays where it is and the conversation goes on
    private void DeclineOffer()
    {
        offering = false;
        activeOffer = null;
        focusRequested = true;

        AddLine(true, T("No, thanks.", "Ne, hvala."));
        Say(T("(The player declined your offer to lead them there. Reply in one short, friendly sentence and say you are right here if they need anything.)",
              "(Igrač je odbio tvoju ponudu da ga odvedeš. Odgovori jednom kratkom prijateljskom rečenicom i reci da si tu ako mu išta zatreba.)"),
            visible: false);
    }

    private void OnGuideArrived()
    {
        if (this == null) return;
        arrivedName = guidingTo != null ? guidingTo.NameFor(language) : "";
        arrivedUntil = Time.time + 5f;
        guidingTo = null;
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
            offering = true;
            if (!reply.Contains("?"))
                reply += T(" Shall I take you there?", " Želiš li da te odvedem tamo?");
        }

        AddLine(false, reply);
    }

    private void OnError(string error)
    {
        if (this == null) return;
        waiting = false;
        pendingOffer = null;
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

        float s = Screen.height / 720f;
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

            var r = new Rect(vw / 2f - 220f, vh - 90f, 440f, 44f);
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
    }

    // A line at the top of the screen while the NPC is leading the way, and for a moment after arriving
    private void DrawGuideBanner()
    {
        string message = null;
        if (guidingTo != null) message = T("On the way to: ", "Na putu do: ") + guidingTo.NameFor(language);
        else if (Time.time < arrivedUntil && arrivedName.Length > 0) message = T("You have arrived: ", "Stigli ste: ") + arrivedName;
        if (message == null) return;

        EnsureStyles();
        float s = Screen.height / 720f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float vw = Screen.width / s;

        GUI.Box(new Rect(vw / 2f - 260f, 44f, 520f, 44f), message, promptStyle);
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
        float w = Mathf.Min(520f, vw - 40f);
        float h = NeutralName.Length > 0 ? 150f : 110f;
        var panel = new Rect((vw - w) / 2f, vh - h - 24f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 18f, panel.y + 12f, panel.width - 36f, panel.height - 24f));

        if (NeutralName.Length > 0)
        {
            GUILayout.Label(NeutralName, nameStyle);
            GUILayout.Space(6f);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("English   [1]", bigButtonStyle, GUILayout.Height(52f))) pendingLanguage = 0;
        GUILayout.Space(10f);
        if (GUILayout.Button("Hrvatski   [2]", bigButtonStyle, GUILayout.Height(52f))) pendingLanguage = 1;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawDialogue(float vw, float vh)
    {
        Event e = Event.current;

        float w = Mathf.Min(820f, vw - 40f);
        float h = 310f;
        var panel = new Rect((vw - w) / 2f, vh - h - 24f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 18f, panel.y + 12f, panel.width - 36f, panel.height - 24f));

        GUILayout.Label(DisplayName, nameStyle);

        // conversation log
        if (scrollFrames > 0)
        {
            scroll.y = float.MaxValue;
            if (e.type == EventType.Repaint) scrollFrames--;
        }
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (Line l in log)
        {
            string who = l.fromPlayer ? T("You: ", "Ti: ") : DisplayName + ": ";
            GUILayout.Label(who + l.text, l.fromPlayer ? playerLineStyle : npcLineStyle);
        }
        if (waiting) GUILayout.Label(DisplayName + T(" is thinking...", " razmišlja..."), thinkStyle);
        GUILayout.EndScrollView();

        if (status.Length > 0) GUILayout.Label(status, statusStyle);

        // input row (or the Yes / No question after the NPC offered to lead the way)
        if (offering)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Yes, lead the way   [1]", "Da, vodi me   [1]"), buttonStyle, GUILayout.Height(46f)))
                pendingOfferChoice = 0;
            GUILayout.Space(10f);
            if (GUILayout.Button(T("No, thanks   [2]", "Ne, hvala   [2]"), buttonStyle, GUILayout.Height(46f)))
                pendingOfferChoice = 1;
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.BeginHorizontal();

            bool enter = e.type == EventType.KeyDown
                         && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                         && GUI.GetNameOfFocusedControl() == "ChatInput";
            if (enter) { pendingSend = true; e.Use(); }

            // ignore the key that opened the dialogue so it isn't typed into the box
            if (Time.unscaledTime < openedTime + 0.2f) inputText = "";

            GUI.SetNextControlName("ChatInput");
            inputText = GUILayout.TextField(inputText, 300, inputStyle, GUILayout.Height(36f));

            if (GUILayout.Button(T("Send", "Pošalji"), buttonStyle, GUILayout.Width(96f), GUILayout.Height(36f))) pendingSend = true;
            if (GUILayout.Button(T("Language", "Jezik"), buttonStyle, GUILayout.Width(96f), GUILayout.Height(36f))) pendingChooseLanguage = true;
            if (companion != null && followAfterConversation)
            {
                string stayLabel = stayPut ? T("Follow me", "Prati me") : T("Stay here", "Ostani ovdje");
                if (GUILayout.Button(stayLabel, buttonStyle, GUILayout.Width(130f), GUILayout.Height(36f)))
                    pendingToggleStay = true;
            }
            if (GUILayout.Button(T("Leave", "Zatvori"), buttonStyle, GUILayout.Width(96f), GUILayout.Height(36f))) pendingLeave = true;

            GUILayout.EndHorizontal();
        }
        GUILayout.EndArea();

        if (focusRequested && e.type == EventType.Repaint)
        {
            GUI.FocusControl("ChatInput");
            focusRequested = false;
        }
    }

    private void EnsureStyles()
    {
        if (boxStyle != null) return;

        panelTex = new Texture2D(1, 1);
        panelTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.09f, 0.9f));
        panelTex.Apply();

        boxStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTex } };

        promptStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 19,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = panelTex, textColor = Color.white }
        };

        nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.5f) }
        };

        npcLineStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            wordWrap = true,
            normal = { textColor = Color.white }
        };

        playerLineStyle = new GUIStyle(npcLineStyle) { normal = { textColor = new Color(0.65f, 0.85f, 1f) } };

        thinkStyle = new GUIStyle(npcLineStyle)
        {
            fontStyle = FontStyle.Italic,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
        };

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            wordWrap = true,
            normal = { textColor = new Color(1f, 0.55f, 0.55f) }
        };

        inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 17 };
        bigButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
    }
}