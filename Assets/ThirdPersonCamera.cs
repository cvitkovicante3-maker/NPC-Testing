using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple third-person orbit camera using the New Input System.
/// Put this on your Camera and drag your Player (or its CameraTarget child) into "Target".
/// Mouse / right stick to look. Esc unlocks the cursor, click to lock again.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target (drag & drop)")]
    [SerializeField] private Transform target;
    [Tooltip("Height above the target's pivot that the camera looks at.")]
    [SerializeField] private float heightOffset = 1.6f;

    [Header("Distance")]
    [SerializeField] private float distance = 4f;
    [SerializeField] private bool avoidWalls = true;
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float wallPadding = 0.2f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float stickSensitivity = 140f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;
    [SerializeField] private bool lockCursor = true;

    [Header("Smoothing")]
    [SerializeField] private float followSmoothing = 15f;

    private InputAction mouseLook;
    private InputAction stickLook;
    private float yaw;
    private float pitch = 15f;
    private Vector3 smoothedPivot;

    private void Awake()
    {
        mouseLook = new InputAction("MouseLook", InputActionType.Value, "<Mouse>/delta");
        stickLook = new InputAction("StickLook", InputActionType.Value, "<Gamepad>/rightStick");
    }

    private void OnEnable()
    {
        mouseLook.Enable();
        stickLook.Enable();
        if (lockCursor) SetCursorLocked(true);
    }

    private void OnDisable()
    {
        mouseLook.Disable();
        stickLook.Disable();
        if (lockCursor) SetCursorLocked(false);
    }

    private void Start()
    {
        yaw = transform.eulerAngles.y;
        if (target != null) smoothedPivot = target.position + Vector3.up * heightOffset;
    }

    private void Update()
    {
        if (!lockCursor) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                 && Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Only read mouse look while the cursor is locked
        if (!lockCursor || Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 m = mouseLook.ReadValue<Vector2>() * mouseSensitivity;
            Vector2 s = stickLook.ReadValue<Vector2>() * stickSensitivity * Time.deltaTime;
            yaw += m.x + s.x;
            pitch -= m.y + s.y;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        Vector3 pivotTarget = target.position + Vector3.up * heightOffset;
        smoothedPivot = Vector3.Lerp(smoothedPivot, pivotTarget, 1f - Mathf.Exp(-followSmoothing * Time.deltaTime));

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desired = smoothedPivot - rot * Vector3.forward * distance;

        if (avoidWalls && Physics.Linecast(smoothedPivot, desired, out RaycastHit hit, collisionMask,
                QueryTriggerInteraction.Ignore))
        {
            desired = hit.point + hit.normal * wallPadding;
        }

        transform.SetPositionAndRotation(desired, rot);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}