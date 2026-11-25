using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Classic GTA (Vice City / San Andreas style) on-foot movement, New Input System.
///   W / S      = walk forward / backward (relative to the character, NOT the camera)
///   A / D      = turn left / right
///   Left Shift = sprint
///   Space      = jump
/// The mouse is not used at all. Gamepad left stick works too.
///
/// Because movement does not depend on the camera, the camera can simply trail
/// behind the player without any feedback loop.
///
/// To use your OWN movement script later: remove this component and add yours.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class GTAPlayerMovement : MonoBehaviour
{
    [Header("Speed (units per second)")]
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [Range(0.1f, 1f)][SerializeField] private float backwardSpeedMultiplier = 0.5f;

    [Header("Smoothness")]
    [SerializeField] private float acceleration = 14f;
    [SerializeField] private float deceleration = 22f;

    [Header("Turning (degrees per second)")]
    [SerializeField] private float turnSpeed = 150f;
    [Tooltip("How quickly turning ramps up/down. Higher = snappier.")]
    [SerializeField] private float turnSmoothing = 12f;
    [Tooltip("Turn slower while sprinting, for a heavier feel (1 = no change).")]
    [Range(0.3f, 1f)][SerializeField] private float sprintTurnMultiplier = 0.7f;

    [Header("Autopilot (while a guide leads the player)")]
    [Tooltip("How fast the character turns toward where it is being led, in degrees per second.")]
    [SerializeField] private float autopilotTurnSpeed = 300f;

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = -22f;

    /// <summary>Forward speed (negative when walking backwards). Handy for an Animator.</summary>
    public float CurrentSpeed => currentSpeed;
    public bool IsGrounded => controller.isGrounded;

    /// <summary>True while a script is steering the character (see SetAutopilot).</summary>
    public bool AutopilotActive { get; private set; }

    /// <summary>
    /// True while the player is pressing W / A / S / D or pushing the stick (works during autopilot too).
    /// The arrow keys are NOT included: during auto-follow they look around.
    /// </summary>
    public bool HasManualInput
    {
        get
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.25f;
        }
    }

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    private float currentSpeed;
    private float smoothedTurn;
    private float verticalVelocity;
    private Vector3 autoDirection;
    private float autoSpeed;

    /// <summary>Steers the character: walk toward 'worldDirection' at 'speed'. Call every frame; the keyboard is ignored meanwhile.</summary>
    public void SetAutopilot(Vector3 worldDirection, float speed)
    {
        AutopilotActive = true;
        autoDirection = worldDirection;
        autoSpeed = speed;
    }

    /// <summary>Gives the keyboard back to the player.</summary>
    public void StopAutopilot()
    {
        AutopilotActive = false;
        autoSpeed = 0f;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Two movement scripts in one parent/child chain both move the character (double movement, wrong camera target).
        foreach (GTAPlayerMovement other in FindObjectsByType<GTAPlayerMovement>(FindObjectsSortMode.None))
        {
            if (other != this && (other.transform.IsChildOf(transform) || transform.IsChildOf(other.transform)))
                Debug.LogError($"GTAPlayerMovement on '{name}' and on '{other.name}' are in the same parent/child chain. Keep only ONE of them.", this);
        }

        // A non-kinematic Rigidbody fights with the CharacterController (tilting, flying off).
        if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
        {
            Debug.LogWarning("GTAPlayerMovement: non-kinematic Rigidbody found on the player. Setting it to kinematic; remove it if you don't need it.", this);
            rb.isKinematic = true;
        }

        // The CharacterController is the player's collider. A second collider on the same
        // object (e.g. the CapsuleCollider that comes with a default Capsule) causes pushing and launching.
        foreach (Collider extra in GetComponents<Collider>())
        {
            if (extra is CharacterController || !extra.enabled) continue;
            Debug.LogWarning($"GTAPlayerMovement: disabled extra {extra.GetType().Name} on the player. Remove it; the CharacterController is the collider.", this);
            extra.enabled = false;
        }

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        // (the arrow keys only look around, see GTAFollowCamera; they never move the character)
        moveAction.AddBinding("<Gamepad>/leftStick");

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        sprintAction = new InputAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
    }

    private void OnDisable()
    {
        AutopilotActive = false;
        moveAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        KeepUpright();
        Vector2 input = AutopilotActive ? Vector2.zero : moveAction.ReadValue<Vector2>();
        bool sprinting = sprintAction.IsPressed() && input.y > 0.1f;

        float targetSpeed;
        if (AutopilotActive)
        {
            // --- Autopilot: turn toward the wanted direction and walk there ---
            smoothedTurn = 0f;
            targetSpeed = 0f;
            Vector3 wanted = autoDirection;
            wanted.y = 0f;
            if (wanted.sqrMagnitude > 0.0001f && autoSpeed > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(wanted.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, autopilotTurnSpeed * dt);
                float angle = Quaternion.Angle(transform.rotation, look);
                targetSpeed = autoSpeed * Mathf.Clamp01(1f - angle / 80f);   // turn first, then walk
            }
        }
        else
        {
            // --- Turning (A / D) ---
            float turnMult = sprinting ? sprintTurnMultiplier : 1f;
            smoothedTurn = Mathf.Lerp(smoothedTurn, input.x, 1f - Mathf.Exp(-turnSmoothing * dt));
            transform.Rotate(0f, smoothedTurn * turnSpeed * turnMult * dt, 0f);

            // --- Forward / backward (W / S) ---
            targetSpeed = input.y * (sprinting ? sprintSpeed : runSpeed);
            if (input.y < 0f) targetSpeed *= backwardSpeedMultiplier;
        }

        float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);

        // --- Gravity & jump ---
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (!AutopilotActive && controller.isGrounded && jumpAction.WasPressedThisFrame())
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * dt;

        Vector3 velocity = transform.forward * currentSpeed + Vector3.up * verticalVelocity;
        controller.Move(velocity * dt);
    }

    // The player only ever turns around the Y axis; undo any tilt from outside influences.
    private void KeepUpright()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) return;

        Quaternion upright = Quaternion.LookRotation(f.normalized, Vector3.up);
        if (Quaternion.Angle(transform.rotation, upright) > 0.01f)
            transform.rotation = upright;
    }
}