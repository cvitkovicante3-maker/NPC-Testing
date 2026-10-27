using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;                 // Your character
    public Vector3 targetOffset = new Vector3(0f, 1.6f, 0f); // Look at roughly head height

    [Header("Orbit")]
    public float distance = 4f;
    public float mouseSensitivity = 3f;
    public float minPitch = -30f;
    public float maxPitch = 70f;
    public float followSmoothing = 15f;

    [Header("Collision")]
    public bool avoidWalls = true;
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = ~0;     // Everything by default

    private float yaw;
    private float pitch = 15f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        yaw = transform.eulerAngles.y;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Mouse input
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position + targetOffset;
        Vector3 desiredPos = focusPoint - rotation * Vector3.forward * distance;

        // Pull the camera in if something is between it and the character
        if (avoidWalls)
        {
            Vector3 dir = desiredPos - focusPoint;
            if (Physics.SphereCast(focusPoint, collisionRadius, dir.normalized, out RaycastHit hit,
                                   distance, collisionMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(target))
            {
                desiredPos = focusPoint + dir.normalized * hit.distance;
            }
        }

        transform.position = Vector3.Lerp(transform.position, desiredPos, followSmoothing * Time.deltaTime);
        transform.rotation = rotation;

        // Unlock cursor with Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}