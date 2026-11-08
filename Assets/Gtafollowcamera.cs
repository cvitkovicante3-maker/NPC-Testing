using UnityEngine;

/// <summary>
/// Old-school GTA chase camera. No mouse input at all.
/// The camera trails behind the target and slowly swings around to stay behind it
/// when the target turns. Drag your Player into "Target".
///
/// IMPORTANT: Put this on the Camera, but do NOT make the Camera a child of the Player.
/// (If it is, this script un-parents it automatically.)
/// </summary>
public class GTAFollowCamera : MonoBehaviour
{
    [Header("Target (drag & drop)")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField] private float distance = 5.5f;
    [SerializeField] private float height = 2.4f;
    [Tooltip("Height above the target's pivot that the camera looks at.")]
    [SerializeField] private float lookHeight = 1.4f;
    [Tooltip("Sideways shift of the camera AND its aim. + moves the camera right (player appears further left on screen), - moves it left. 0 = player centered.")]
    [SerializeField] private float shoulderOffset = 0f;

    [Header("Smoothness")]
    [Tooltip("How quickly the camera swings behind the target. Lower = lazier, more GTA-like.")]
    [SerializeField] private float swingSpeed = 2.5f;
    [Tooltip("Position lag in seconds. Higher = floatier.")]
    [SerializeField] private float positionSmoothTime = 0.12f;
    [SerializeField] private float rotationSmoothing = 10f;

    [Header("Collision")]
    [SerializeField] private bool avoidWalls = true;
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float wallPadding = 0.25f;

    private float yaw;
    private Vector3 velocity;

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

        yaw = TargetYaw();
        SnapToDesired();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        yaw = Mathf.LerpAngle(yaw, TargetYaw(), 1f - Mathf.Exp(-swingSpeed * Time.deltaTime));

        GetPivots(out Vector3 anchor, out Vector3 aim);
        Vector3 desired = DesiredPosition(aim);

        Vector3 pos = Vector3.SmoothDamp(transform.position, desired, ref velocity, positionSmoothTime);

        // Pull the camera in front of walls (checked from the player's head, not the shoulder-shifted aim)
        if (avoidWalls && Physics.Linecast(anchor, pos, out RaycastHit hit, collisionMask,
                QueryTriggerInteraction.Ignore))
        {
            pos = hit.point + hit.normal * wallPadding;
        }

        transform.position = pos;

        Quaternion look = Quaternion.LookRotation(aim - pos, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime));
    }

    // Heading from the flattened forward vector, so a tilted target can't throw the camera off.
    private float TargetYaw()
    {
        Vector3 f = target.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) return yaw;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    private void GetPivots(out Vector3 anchor, out Vector3 aim)
    {
        anchor = target.position + Vector3.up * lookHeight;
        Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        aim = anchor + right * shoulderOffset;
    }

    private Vector3 DesiredPosition(Vector3 aim)
    {
        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height - lookHeight, -distance);
        return aim + offset;
    }

    private void SnapToDesired()
    {
        GetPivots(out _, out Vector3 aim);
        transform.position = DesiredPosition(aim);
        transform.rotation = Quaternion.LookRotation(aim - transform.position, Vector3.up);
        velocity = Vector3.zero;
    }
}