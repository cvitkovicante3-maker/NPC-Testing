using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Old-school GTA chase camera. No mouse input at all.
/// The camera stays a FIXED distance behind and above the target, and slowly swings
/// around to stay behind it when the target turns. Drag your Player into "Target".
///
/// IMPORTANT: Put this on the Camera, but do NOT make the Camera a child of the Player.
/// (If it is, this script un-parents it automatically.)
/// </summary>
public class GTAFollowCamera : MonoBehaviour
{
    [Header("Target (drag & drop)")]
    [SerializeField] private Transform target;

    [Header("Fixed position relative to the player")]
    [Tooltip("Distance behind the player.")]
    [SerializeField] private float distance = 5.5f;
    [Tooltip("Camera height above the player's pivot.")]
    [SerializeField] private float height = 2.4f;
    [Tooltip("Height above the player's pivot that the camera looks at.")]
    [SerializeField] private float lookHeight = 1.4f;
    [Tooltip("Sideways shift of the camera AND its aim. + moves the camera right (player appears further left on screen), - moves it left. 0 = player centered.")]
    [SerializeField] private float shoulderOffset = 0f;

    [Header("Smoothness")]
    [Tooltip("How long the camera takes to swing around behind the player. Higher = lazier.")]
    [SerializeField] private float swingSmoothTime = 0.45f;
    [Tooltip("Fastest the camera may orbit around the player, in degrees per second. Keep this well below the player's turn speed so the camera never outruns the player.")]
    [SerializeField] private float maxSwingSpeed = 60f;
    [Tooltip("Only swing behind the player while it is actually moving (like GTA). Turning on the spot leaves the camera where it is.")]
    [SerializeField] private bool swingOnlyWhileMoving = true;
    [Tooltip("Speed (units/second) above which the player counts as moving.")]
    [SerializeField] private float movingThreshold = 0.3f;
    [Tooltip("Smooths tiny up/down bumps (steps, ground contact) so the picture doesn't shake.")]
    [SerializeField] private float verticalSmoothing = 8f;

    [Header("Collision (turn off for a strictly fixed distance)")]
    [SerializeField] private bool avoidWalls = true;
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float wallPadding = 0.25f;
    [Tooltip("How long the camera takes to move back out after a wall stops blocking it.")]
    [SerializeField] private float collisionRecoverTime = 0.25f;

    [Header("Free look (used while the player auto-follows a guide)")]
    [Tooltip("After the last look / zoom input the camera stays where you put it for this many seconds, then swings back behind the player.")]
    [SerializeField] private float lookHoldSeconds = 4f;
    [SerializeField] private float minPitch = -8f;
    [SerializeField] private float maxPitch = 75f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float maxZoom = 3f;

    [Header("Arrow-key look (always on)")]
    [Tooltip("Arrow keys look around any time: left / right turn the view, up tilts it towards the sky, down tilts it back (and then looks down from above).")]
    [SerializeField] private bool arrowKeyLook = true;
    [SerializeField] private float arrowTurnSpeed = 90f;
    [SerializeField] private float arrowTiltSpeed = 70f;
    [Tooltip("How far the view can tilt up towards the sky.")]
    [SerializeField] private float maxSkyTilt = 85f;
    [Tooltip("Press this key to go straight back to the normal camera.")]
    [SerializeField] private Key resetLookKey = Key.Home;
    [Tooltip("Seconds after you let go of the arrow keys before the camera swings back behind the character.")]
    [SerializeField] private float recenterDelay = 0.8f;
    [Tooltip("How fast the camera swings back (degrees per second).")]
    [SerializeField] private float recenterSpeed = 60f;
    [Tooltip("How fast the sky tilt comes back down (degrees per second).")]
    [SerializeField] private float recenterTiltSpeed = 40f;
    [Tooltip("Higher = the return is more gradual (it eases in and out over about this many seconds).")]
    [SerializeField] private float recenterSmoothTime = 0.9f;

    /// <summary>Turned off while a chat box is open, so typing doesn't move the camera.</summary>
    public bool LookInputEnabled { get; set; } = true;

    /// <summary>The object this camera follows (used by SetupDiagnostics).</summary>
    public Transform Target => target;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private float yaw;
    private float yawVel;
    private Vector3 lastTargetPos;
    private float smoothedY;
    private float currentLen;
    private float lenVel;

    // free look
    private float freeUntil;
    private float freeBlend;
    private float freePitch;
    private float freeZoom = 1f;

    private bool arrowsUsed;      // the arrow keys were used and the camera has not centered yet
    private bool recentering;
    private float lastArrowTime;
    private float skyTilt;        // how far the view is tilted up at the sky (degrees)
    private float skyTiltShown;

    private bool FreeActive => Time.time < freeUntil || freeBlend > 0.01f;

    private void BeginFreeIfNeeded()
    {
        if (!FreeActive) freePitch = Mathf.Atan2(height - lookHeight, distance) * Mathf.Rad2Deg;
        freeUntil = Time.time + lookHoldSeconds;
        recentering = false;
    }

    /// <summary>Orbit around the player (degrees). Call every frame while the player is looking around.</summary>
    public void FreeLook(float yawDelta, float pitchDelta)
    {
        BeginFreeIfNeeded();
        yaw += yawDelta;
        yawVel = 0f;
        freePitch = Mathf.Clamp(freePitch - pitchDelta, minPitch, maxPitch);
    }

    /// <summary>Zoom in / out while free-looking (positive = in).</summary>
    public void Zoom(float amount)
    {
        BeginFreeIfNeeded();
        freeZoom = Mathf.Clamp(freeZoom - amount, minZoom, maxZoom);
    }

    /// <summary>Goes back to the normal chase camera right away.</summary>
    public void ResetLook()
    {
        freeUntil = 0f;
        arrowsUsed = false;
        recentering = true;
    }

    private void Awake()
    {
        // If the target isn't the object that actually moves, follow the child that does.
        if (target != null && target.GetComponent<GTAPlayerMovement>() == null)
        {
            GTAPlayerMovement mover = target.GetComponentInChildren<GTAPlayerMovement>();
            if (mover != null)
            {
                Debug.LogWarning($"GTAFollowCamera: '{target.name}' has no GTAPlayerMovement but its child '{mover.name}' does. Following '{mover.name}' instead.", this);
                target = mover.transform;
            }
        }

        // A camera parented to the thing it follows causes runaway movement/jitter.
        if (target != null && transform.IsChildOf(target))
        {
            Debug.LogWarning("GTAFollowCamera: camera was a child of the target. Un-parenting it.", this);
            transform.SetParent(null, true);
        }
    }

    private void Start()
    {
        if (target == null)
        {
            Debug.LogWarning("GTAFollowCamera: no target assigned.", this);
            enabled = false;
            return;
        }

        UpdateCamera(0f, snap: true);
    }

    private void ReadArrowKeys(float dt)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb[resetLookKey].wasPressedThisFrame) { ResetLook(); return; }

        float x = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
        float y = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);

        if (x != 0f || y != 0f)
        {
            arrowsUsed = true;
            recentering = false;
            lastArrowTime = Time.time;

            if (x != 0f) FreeLook(x * arrowTurnSpeed * dt, 0f);

            if (y > 0f)
            {
                float baseline = Mathf.Atan2(height - lookHeight, distance) * Mathf.Rad2Deg;
                if (FreeActive && freePitch > baseline + 0.5f) FreeLook(0f, y * arrowTiltSpeed * dt);   // camera comes back down first
                else skyTilt = Mathf.Min(maxSkyTilt, skyTilt + y * arrowTiltSpeed * dt);                 // then the view tilts to the sky
            }
            else
            {
                if (skyTilt > 0f) skyTilt = Mathf.Max(0f, skyTilt + y * arrowTiltSpeed * dt);
                else FreeLook(0f, y * arrowTiltSpeed * dt);                                              // view from above
            }
            freeUntil = Time.time + 0.2f;   // stays free only while the keys are down (plus a moment)
        }
        else if (arrowsUsed && Time.time - lastArrowTime >= recenterDelay)
        {
            // keys released: swing back behind the character
            arrowsUsed = false;
            recentering = true;
            freeUntil = 0f;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;
        if (arrowKeyLook && LookInputEnabled) ReadArrowKeys(Time.deltaTime);
        UpdateCamera(Time.deltaTime, snap: false);
    }

    private void UpdateCamera(float dt, bool snap)
    {
        float anchorY = target.position.y + lookHeight;

        if (snap)
        {
            yaw = TargetYaw();
            yawVel = 0f;
            smoothedY = anchorY;
            freeBlend = 0f;
            lastTargetPos = target.position;
        }
        else
        {
            // Is the player actually travelling? (measured from its real movement)
            Vector3 delta = target.position - lastTargetPos;
            delta.y = 0f;
            bool moving = dt > 0f && delta.magnitude / dt > movingThreshold;
            lastTargetPos = target.position;

            // Orbit speed is capped, and eased in and out, so the camera never whips around.
            bool free = Time.time < freeUntil;
            freeBlend = Mathf.MoveTowards(freeBlend, free ? 1f : 0f, dt * (recentering ? 1.2f : 2.5f));

            if (recentering)
            {
                skyTilt = Mathf.MoveTowards(skyTilt, 0f, (recenterTiltSpeed + skyTilt * 0.6f) * dt);   // fast when high, gentle at the end
                if (skyTilt <= 0.01f && freeBlend <= 0.01f && Mathf.Abs(Mathf.DeltaAngle(yaw, TargetYaw())) < 1f) recentering = false;
            }

            // while recentering the camera swings back even if the character is standing still
            float wantedYaw = (!free && (recentering || !swingOnlyWhileMoving || moving)) ? TargetYaw() : yaw;
            yaw = Mathf.SmoothDampAngle(yaw, wantedYaw, ref yawVel, recentering ? recenterSmoothTime : swingSmoothTime,
                                        recentering ? recenterSpeed : maxSwingSpeed, dt);

            smoothedY = Mathf.Lerp(smoothedY, anchorY, 1f - Mathf.Exp(-verticalSmoothing * dt));
        }

        // The player's head (smoothed vertically) and the point the camera looks at
        Vector3 anchor = target.position;
        anchor.y = smoothedY;

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 aim = anchor + yawRot * Vector3.right * shoulderOffset;

        // The camera's ideal spot: always the same offset from the player
        Vector3 ideal = aim + yawRot * new Vector3(0f, height - lookHeight, -distance);
        if (freeBlend > 0.001f)
        {
            Vector3 idealFree = aim + Quaternion.Euler(freePitch, yaw, 0f) * new Vector3(0f, 0f, -distance * freeZoom);
            ideal = Vector3.Lerp(ideal, idealFree, freeBlend);
        }
        Vector3 toIdeal = ideal - anchor;
        float fullLen = toIdeal.magnitude;

        // Walls: shorten the camera arm instantly, extend it back out smoothly (no flicker)
        float allowedLen = fullLen;
        if (avoidWalls && fullLen > 0.001f && TryGetWallHit(anchor, ideal, out RaycastHit wall))
            allowedLen = Mathf.Clamp(wall.distance - wallPadding, 0.3f, fullLen);

        if (snap || !avoidWalls || allowedLen < currentLen)
        {
            currentLen = allowedLen;
            lenVel = 0f;
        }
        else
        {
            currentLen = Mathf.SmoothDamp(currentLen, allowedLen, ref lenVel, collisionRecoverTime);
        }
        currentLen = Mathf.Min(currentLen, fullLen);

        Vector3 pos = fullLen > 0.001f ? anchor + toIdeal / fullLen * currentLen : anchor;
        Vector3 forward = aim - pos;
        if (forward.sqrMagnitude < 0.0001f) forward = yawRot * Vector3.forward;

        skyTiltShown = snap ? skyTilt : Mathf.Lerp(skyTiltShown, skyTilt, 1f - Mathf.Exp(-12f * dt));
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(-skyTiltShown, 0f, 0f);
        transform.SetPositionAndRotation(pos, rotation);
    }

    // Nearest solid hit between 'from' and 'to', ignoring the target itself and NPCs.
    private bool TryGetWallHit(Vector3 from, Vector3 to, out RaycastHit result)
    {
        result = default;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.001f) return false;

        int count = Physics.RaycastNonAlloc(from, dir / dist, hitBuffer, dist, collisionMask,
            QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(target)) continue;
            if (h.collider.GetComponentInParent<NPCSplineFollower>() != null) continue;
            if (h.distance < nearest)
            {
                nearest = h.distance;
                result = h;
                found = true;
            }
        }
        return found;
    }

    // Heading from the flattened forward vector, so a tilted target can't throw the camera off.
    private float TargetYaw()
    {
        Vector3 f = target.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) return yaw;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }
}