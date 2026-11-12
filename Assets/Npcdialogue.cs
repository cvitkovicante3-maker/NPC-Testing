using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player walk up to this NPC and chat with it. Replies come from a free, local model
/// (the "LLM for Unity" package), so nothing is pre-written and nothing needs the internet.
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

    [Header("AI")]
    [Tooltip("The 'LLM Agent' on this object. Found automatically if it's on the same object.")]
    [SerializeField] private LLMUnity.LLMAgent agent;

    [Header("Interaction")]
    [Tooltip("Leave empty to find the object with GTAPlayerMovement automatically.")]
    [SerializeField] private Transform player;
    [SerializeField] private Key interactKey = Key.E;
    [SerializeField] private float interactDistance = 4f;
    [SerializeField] private bool facePlayerWhileTalking = true;
    [SerializeField] private float turnSpeed = 6f;

    [Header("Troubleshooting")]
    [Tooltip("Shows a yellow status line at the top-left of the Game view. Turn off once everything works.")]
    [SerializeField] private bool showDebugInfo = true;

    // --- conversation state ---
    private readonly List<Line> log = new List<Line>();
    private Line lastPlayerLine;
    private string lastPlayerText;
    private bool talking, waiting, promptApplied;
    private string inputText = "";
    private string status = "";

    // --- UI state ---
    private bool pendingSend, pendingLeave, focusRequested;
    private float openedTime;
    private Vector2 scroll;
    private int scrollFrames;
    private GUIStyle boxStyle, promptStyle, nameStyle, npcLineStyle, playerLineStyle, thinkStyle,
                     statusStyle, inputStyle, buttonStyle;
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

    // ---------------------------------------------------------------- logic

    private void Update()
    {
        if (player == null) return;

        // UI buttons only set flags; the real work happens here (IMGUI dislikes layout changes mid-frame)
        if (pendingSend) { pendingSend = false; SendPlayerMessage(); }
        if (pendingLeave) { pendingLeave = false; if (talking) EndTalking(); }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (!talking)
        {
            if (InRange() && kb[interactKey].wasPressedThisFrame) BeginTalking();
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame) { EndTalking(); return; }
        FacePlayer();
    }

    private bool InRange()
    {
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.sqrMagnitude <= interactDistance * interactDistance;
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

        if (speaksFirst && log.Count == 0 && !waiting)
            Say("(The player has just walked up to you. Greet them in character.)", visible: false);
    }

    private void EndTalking()
    {
        talking = false;
        if (playerMovement != null) playerMovement.enabled = true;
        if (follower != null && followerWasEnabled) follower.enabled = true;
    }

    private void FacePlayer()
    {
        if (!facePlayerWhileTalking) return;
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
    }

    private void SendPlayerMessage()
    {
        string text = inputText.Trim();
        if (text.Length == 0 || waiting) return;
        inputText = "";
        focusRequested = true;
        Say(text, visible: true);
    }

    private void Say(string text, bool visible)
    {
        if (agent == null)
        {
            status = "No 'LLM Agent' component found on this object. Add one to the mouse.";
            if (visible) inputText = text;
            return;
        }

        lastPlayerLine = null;
        lastPlayerText = visible ? text : null;
        if (visible) lastPlayerLine = AddLine(true, text);

        waiting = true;
        status = "";
        _ = AskAsync(text);
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

            if (reply.Length == 0) OnError("The local model returned an empty reply.");
            else OnReply(reply);
        }
        catch (Exception e)
        {
            OnError("The local model isn't ready or failed (it may still be loading): " + e.Message);
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
    }

    private Line AddLine(bool fromPlayer, string text)
    {
        var line = new Line { fromPlayer = fromPlayer, text = text };
        log.Add(line);
        scrollFrames = 3;
        return line;
    }

    private string BuildSystemPrompt()
    {
        return persona.Trim() +
               "\n\nYour name is " + npcName + "." +
               "\n\nWhat you know about your world:\n" + worldNotes.Trim() +
               "\n\nYou are a character in a video game, talking with the player. Stay in character. " +
               "Keep replies short (one to three sentences) and natural, like speech. " +
               "No stage directions, no asterisks, no emojis. Reply in the language the player writes in.";
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
            var r = new Rect(vw / 2f - 190f, vh - 90f, 380f, 44f);
            GUI.Box(r, "Press " + interactKey + " to talk to " + npcName, promptStyle);
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
        string info;
        if (player == null)
        {
            info = "NPCDialogue on '" + name + "': PLAYER NOT FOUND (drag it into the Player slot) | LLM Agent: " + agentInfo;
        }
        else
        {
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            info = "NPCDialogue on '" + name + "' | player '" + player.name + "' | distance " +
                   d.magnitude.ToString("F1") + " (talk range " + interactDistance.ToString("F1") + ") | key " +
                   interactKey + " | LLM Agent: " + agentInfo;
        }

        Color old = GUI.color;
        GUI.color = Color.yellow;
        GUI.Label(new Rect(10f, 8f, 1100f, 26f), info);
        GUI.color = old;
    }

    private void DrawDialogue(float vw, float vh)
    {
        Event e = Event.current;

        float w = Mathf.Min(780f, vw - 40f);
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
            string who = l.fromPlayer ? "You: " : npcName + ": ";
            GUILayout.Label(who + l.text, l.fromPlayer ? playerLineStyle : npcLineStyle);
        }
        if (waiting) GUILayout.Label(npcName + " is thinking...", thinkStyle);
        GUILayout.EndScrollView();

        if (status.Length > 0) GUILayout.Label(status, statusStyle);

        // input row
        GUILayout.BeginHorizontal();

        bool enter = e.type == EventType.KeyDown
                     && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                     && GUI.GetNameOfFocusedControl() == "ChatInput";
        if (enter) { pendingSend = true; e.Use(); }

        // ignore the "E" that opened the dialogue so it isn't typed into the box
        if (Time.unscaledTime < openedTime + 0.2f) inputText = "";

        GUI.SetNextControlName("ChatInput");
        inputText = GUILayout.TextField(inputText, 300, inputStyle, GUILayout.Height(36f));

        if (GUILayout.Button("Send", buttonStyle, GUILayout.Width(84f), GUILayout.Height(36f))) pendingSend = true;
        if (GUILayout.Button("Leave", buttonStyle, GUILayout.Width(84f), GUILayout.Height(36f))) pendingLeave = true;

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
    }
}