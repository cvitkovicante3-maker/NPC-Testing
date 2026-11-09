using UnityEngine;

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

    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];
    private float yaw;
    private float yawVel;
    private Vector3 lastTargetPos;
    private float smoothedY;
    private float currentLen;
    private float lenVel;

    private void Awake()
    {
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

    private void LateUpdate()
    {
        if (target == null) return;
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
            float wantedYaw = (!swingOnlyWhileMoving || moving) ? TargetYaw() : yaw;
            yaw = Mathf.SmoothDampAngle(yaw, wantedYaw, ref yawVel, swingSmoothTime, maxSwingSpeed, dt);

            smoothedY = Mathf.Lerp(smoothedY, anchorY, 1f - Mathf.Exp(-verticalSmoothing * dt));
        }

        // The player's head (smoothed vertically) and the point the camera looks at
        Vector3 anchor = target.position;
        anchor.y = smoothedY;

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 aim = anchor + yawRot * Vector3.right * shoulderOffset;

        // The camera's ideal spot: always the same offset from the player
        Vector3 ideal = aim + yawRot * new Vector3(0f, height - lookHeight, -distance);
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

        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, Vector3.up));
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