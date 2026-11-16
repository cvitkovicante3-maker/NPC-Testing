using System;
using System.Collections.Generic;
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

    [Header("Character (this is all the 'writing' the AI needs)")]
    [SerializeField] private string npcName = "Mouse";
    [TextArea(5, 12)]
    [SerializeField]
    private string persona =
        "You are a small, curious village mouse. You are friendly, a little shy but chatty, and easily " +
        "distracted by cheese and crumbs. You love gossip about the village and are always scurrying about. " +
        "You have a warm, playful sense of humor.";
    [TextArea(3, 8)]
    [SerializeField]
    private string worldNotes =
        "You live in a small village next to a road, in the shadow of an old red-brick house. " +
        "The player is a newcomer exploring the village. You know nothing about the world outside the village.";
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
    private bool talking, waiting, promptApplied;
    private string inputText = "";
    private string status = "";
    private bool stayPut, pendingToggleStay;

    // --- language state ---
    private DialogueLanguage language = DialogueLanguage.English;
    private bool languageChosen;
    private bool selectingLanguage;
    private bool sendCoreNotes;                 // send the NPC's background facts with the next message
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

    // ---------------------------------------------------------------- logic

    private void Update()
    {
        if (player == null) return;

        // UI buttons only set flags; the real work happens here (IMGUI dislikes layout changes mid-frame)
        if (pendingSend) { pendingSend = false; SendPlayerMessage(); }
        if (pendingLeave) { pendingLeave = false; if (talking) EndTalking(); }
        if (pendingToggleStay) { pendingToggleStay = false; ToggleStay(); }
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
        {
            selectingLanguage = true;
        }
        else
        {
            sendCoreNotes = true;
            StartConversation();
        }
    }

    private void EndTalking()
    {
        talking = false;
        selectingLanguage = false;
        if (playerMovement != null) playerMovement.enabled = true;
        if (follower != null && followerWasEnabled) follower.enabled = true;

        if (companion != null)
        {
            companion.SetPaused(false);
            if (followAfterConversation && !stayPut && player != null) companion.StartFollowing(player);
        }
    }

    // The player picked a language: switch to it and (the first time) let the NPC greet them
    private void ApplyLanguage(DialogueLanguage chosen)
    {
        language = chosen;
        languageChosen = true;
        selectingLanguage = false;
        sendCoreNotes = true;
        status = "";
        openedTime = Time.unscaledTime;
        focusRequested = true;
        scrollFrames = 3;

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
        if (text.Length == 0 || waiting || selectingLanguage) return;
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

        lastPlayerLine = null;
        lastPlayerText = visible ? text : null;
        if (visible) lastPlayerLine = AddLine(true, text);

        waiting = true;
        status = "";

        // Every message starts with the language to reply in, then the facts that match the question
        string query = LanguageTag() + "\n";
        if (knowledge != null)
        {
            string notes = knowledge.BuildContextFor(visible ? text : "", language, sendCoreNotes);
            if (notes.Length > 0) query += notes + "\n\n";
        }
        sendCoreNotes = false;
        query += text;

        _ = AskAsync(query);
    }

    private string LanguageTag()
    {
        return language == DialogueLanguage.Croatian
            ? "[Reply in Croatian. Odgovori na hrvatskom jeziku.]"
            : "[Reply in English.]";
    }

    private async Task AskAsync(string text)
    {
        try
        {
            // The LLM Agent keeps its own chat history, so we only send the newest message.
            if (!promptApplied)
            {
                agent.systemPrompt = BuildSystemPrompt();
                promptApplied = true;
            }

            string reply = await agent.Chat(text);
            reply = reply == null ? "" : reply.Trim();

            if (reply.Length == 0) OnError(T("The local model returned an empty reply.",
                                             "Lokalni model je vratio prazan odgovor."));
            else OnReply(reply);
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
        AddLine(false, reply);
    }

    private void OnError(string error)
    {
        if (this == null) return;
        waiting = false;
        status = error;

        // Undo the failed message so the player can simply try again
        if (lastPlayerLine != null) log.Remove(lastPlayerLine);
        if (lastPlayerText != null && inputText.Length == 0) inputText = lastPlayerText;
        lastPlayerLine = null;
        lastPlayerText = null;
        sendCoreNotes = true;   // the background facts may not have arrived, send them again
    }

    private Line AddLine(bool fromPlayer, string text)
    {
        var line = new Line { fromPlayer = fromPlayer, text = text };
        log.Add(line);
        scrollFrames = 3;
        return line;
    }

    private string KnowledgeInstructions()
    {
        if (knowledge == null) return "";

        return "\n\nSometimes the player's message contains some notes in square brackets. Use them naturally " +
               "to answer; never read them out and never mention that you were given notes. " +
               "If neither your knowledge nor the notes cover a question, say you're not sure instead of inventing facts.";
    }

    private string BuildSystemPrompt()
    {
        return persona.Trim() +
               "\n\nYour name is " + npcName + "." +
               "\n\nWhat you know about your world:\n" + worldNotes.Trim() + KnowledgeInstructions() +
               "\n\nYou are a character in a video game, talking with the player. Stay in character. " +
               "Keep replies short (one to three sentences) and natural, like speech. " +
               "No stage directions, no asterisks, no emojis. " +
               "Every player message begins with a note in square brackets saying which language to reply in " +
               "(English or Croatian). Always reply in that language, even if earlier messages used another one. " +
               "When you reply in Croatian, use simple, natural standard Croatian." +
               (companion != null && followAfterConversation
                   ? "\n\nWhen the conversation ends, you happily tag along beside the player as a travelling companion."
                   : "");
    }

    // ------------------------------------------------------------------ UI

    private void OnGUI()
    {
        if (showDebugInfo) DrawDebugInfo();
        if (player == null) return;
        if (!talking && !InRange()) return;

        EnsureStyles();

        float s = Screen.height / 720f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        if (!talking)
        {
            // Before a language has been chosen the prompt is just the key and the name; afterwards it is
            // written only in the chosen language.
            string prompt = languageChosen
                ? T("Press " + interactKey + " to talk", "Pritisni " + interactKey + " za razgovor")
                : "[" + interactKey + "]   " + npcName;

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
        float h = 150f;
        var panel = new Rect((vw - w) / 2f, vh - h - 24f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 18f, panel.y + 12f, panel.width - 36f, panel.height - 24f));

        GUILayout.Label(npcName, nameStyle);
        GUILayout.Space(6f);

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

        GUILayout.Label(npcName, nameStyle);

        // conversation log
        if (scrollFrames > 0)
        {
            scroll.y = float.MaxValue;
            if (e.type == EventType.Repaint) scrollFrames--;
        }
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (Line l in log)
        {
            string who = l.fromPlayer ? T("You: ", "Ti: ") : npcName + ": ";
            GUILayout.Label(who + l.text, l.fromPlayer ? playerLineStyle : npcLineStyle);
        }
        if (waiting) GUILayout.Label(npcName + T(" is thinking...", " razmišlja..."), thinkStyle);
        GUILayout.EndScrollView();

        if (status.Length > 0) GUILayout.Label(status, statusStyle);

        // input row
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