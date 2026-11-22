using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// "Auto-follow" mode of the guide: instead of walking to the destination yourself, your character
/// follows the mouse on its own (along the mouse's own path, so it never cuts through houses the mouse
/// walked around). While it does:
///   - ARROW KEYS look around (free camera), R / F (or the mouse wheel) zoom in / out.
///     A few seconds after you let go, the camera swings back behind the character.
///   - A speed gauge changes the speed of the mouse AND your character together (also keys  -  and  + ).
///     At 0% neither of them moves.
///   - Press W / A / S / D (or the stick) at any time to take control yourself again.
///
/// NPCDialogue adds this component automatically and draws the speed gauge. You don't need to set it up.
/// </summary>
public class PlayerAutoFollow : MonoBehaviour
{
    [Header("Following the mouse")]
    [Tooltip("How far behind the mouse (along its path) your character walks.")]
    [SerializeField] private float followDistance = 3f;
    [Tooltip("How eagerly your character closes the gap when it falls behind.")]
    [SerializeField] private float catchUpGain = 1.5f;
    [Tooltip("Your character may walk up to this much faster than the mouse (times the speed gauge) to catch up.")]
    [SerializeField] private float catchUpSpeedFactor = 1.5f;
    [SerializeField] private float pathSpacing = 0.5f;

    [Header("Speed gauge")]
    [Tooltip("0 = both stand still.")]
    [SerializeField] private float minSpeedScale = 0f;
    [SerializeField] private float maxSpeedScale = 1.25f;
    [Tooltip("How much the - and + keys change the speed.")]
    [SerializeField] private float speedKeyStep = 0.1f;

    [Header("Free look")]
    [Tooltip("How fast the arrow keys turn the camera, in degrees per second.")]
    [SerializeField] private float lookKeySpeed = 110f;
    [Tooltip("How fast R / F zoom the camera (1 = the full distance per second).")]
    [SerializeField] private float zoomKeySpeed = 1.2f;
    [Tooltip("Zoom per mouse wheel step.")]
    [SerializeField] private float zoomWheelSensitivity = 0.0015f;

    /// <summary>True while the character is auto-following.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True while the player is chatting with the mouse (the trip is on hold: no keys are read, nobody moves).</summary>
    public bool Suspended { get; private set; }

    /// <summary>Puts the trip on hold while the chat is open, and takes it up again afterwards.</summary>
    public void SetSuspended(bool value)
    {
        if (!IsActive || Suspended == value) return;
        Suspended = value;
        if (value)
        {
            if (player != null) player.StopAutopilot();
        }
        else
        {
            trail.Clear();
            if (mouse != null) lastCrumb = mouse.transform.position;
            armedTime = Time.time + 0.5f;
        }
    }

    /// <summary>Overall speed: 1 = normal. Applies to the mouse and the character together.</summary>
    public float SpeedScale
    {
        get => speedScale;
        set
        {
            value = Mathf.Clamp(value, minSpeedScale, maxSpeedScale);
            speedScale = value < 0.02f ? 0f : value;   // snaps to a true 0%
        }
    }
    public float MinSpeedScale => minSpeedScale;
    public float MaxSpeedScale => maxSpeedScale;

    private GTAPlayerMovement player;
    private NPCCompanion mouse;
    private GTAFollowCamera cam;

    private float speedScale = 1f;
    private float armedTime;                       // keyboard takeover is ignored for a moment after starting
    private readonly List<Vector3> trail = new List<Vector3>();
    private Vector3 lastCrumb;


    public void Begin(GTAPlayerMovement playerMovement, NPCCompanion guide, GTAFollowCamera camera)
    {
        if (playerMovement == null || guide == null) return;
        End();

        player = playerMovement;
        mouse = guide;
        cam = camera;

        trail.Clear();
        lastCrumb = mouse.transform.position;
        speedScale = 1f;
        armedTime = Time.time + 0.5f;


        IsActive = true;
    }

    /// <summary>Stops auto-following and gives control (and the cursor) back.</summary>
    public void End()
    {
        if (!IsActive) return;
        IsActive = false;
        Suspended = false;

        if (player != null) player.StopAutopilot();
        if (mouse != null) mouse.GuideSpeedScale = 1f;
        trail.Clear();
    }

    private void OnDisable()
    {
        End();
    }

    private void Update()
    {
        if (!IsActive) return;

        // Over: the guide arrived, gave up, or something was removed
        if (player == null || mouse == null || !mouse.IsGuiding) { End(); return; }

        if (Suspended) return;   // chatting with the mouse

        // The player grabs the controls
        if (Time.time >= armedTime && player.HasManualInput) { End(); return; }

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) SpeedScale -= speedKeyStep;
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) SpeedScale += speedKeyStep;
        }

        UpdateLook();

        mouse.GuideSpeedScale = speedScale;   // the mouse walks at the chosen speed...
        Follow();                             // ...and so does the character
    }

    // Arrow keys look around, R / F (or the wheel) zoom. The mouse cursor stays free for the speed gauge.
    private void UpdateLook()
    {
        if (cam == null) return;
        float dt = Time.deltaTime;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            float x = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
            float y = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);   // up = look up
            if (x != 0f || y != 0f) cam.FreeLook(x * lookKeySpeed * dt, y * lookKeySpeed * dt);

            float zoom = (kb.rKey.isPressed ? 1f : 0f) - (kb.fKey.isPressed ? 1f : 0f);              // R = in, F = out
            if (zoom != 0f) cam.Zoom(zoom * zoomKeySpeed * dt);
        }

        Mouse m = Mouse.current;
        if (m != null)
        {
            float scroll = m.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) cam.Zoom(scroll * zoomWheelSensitivity);
        }
    }

    // Walk along the mouse's own path, a few steps behind it, at the mouse's speed
    private void Follow()
    {
        Vector3 p = player.transform.position;
        Vector3 m = mouse.transform.position;

        // leave breadcrumbs where the mouse has been
        if (Flat(m - lastCrumb).magnitude >= pathSpacing)
        {
            trail.Add(m);
            lastCrumb = m;
            if (trail.Count > 400) trail.RemoveAt(0);
        }

        // crumbs we have already reached are done
        while (trail.Count > 0 && Flat(trail[0] - p).magnitude < 0.8f) trail.RemoveAt(0);

        Vector3 target = trail.Count > 0 ? trail[0] : m;

        // how far behind the mouse are we, measured along its path
        float behind = Flat(target - p).magnitude;
        for (int i = 0; i < trail.Count - 1; i++) behind += Flat(trail[i + 1] - trail[i]).magnitude;
        if (trail.Count > 0) behind += Flat(m - trail[trail.Count - 1]).magnitude;

        float mouseSpeed = mouse.CurrentSpeed;
        float maxSpeed = mouse.GuideSpeed * speedScale * catchUpSpeedFactor;

        float speed = mouseSpeed + (behind - followDistance) * catchUpGain;
        speed = Mathf.Clamp(speed, 0f, maxSpeed);
        if (behind < followDistance * 0.6f || speed < 0.3f) speed = 0f;

        Vector3 dir = Flat(target - p);
        player.SetAutopilot(dir.sqrMagnitude > 0.0001f ? dir.normalized : player.transform.forward, speed);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}