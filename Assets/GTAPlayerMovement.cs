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

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = -22f;

    /// <summary>Forward speed (negative when walking backwards). Handy for an Animator.</summary>
    public float CurrentSpeed => currentSpeed;
    public bool IsGrounded => controller.isGrounded;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    private float currentSpeed;
    private float smoothedTurn;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // A non-kinematic Rigidbody fights with the CharacterController (tilting, flying off).
        if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
        {
            Debug.LogWarning("GTAPlayerMovement: non-kinematic Rigidbody found on the player. Setting it to kinematic; remove it if you don't need it.", this);
            rb.isKinematic = true;
        }

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
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
        moveAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        Vector2 input = moveAction.ReadValue<Vector2>();
        bool sprinting = sprintAction.IsPressed() && input.y > 0.1f;

        // --- Turning (A / D) ---
        float turnMult = sprinting ? sprintTurnMultiplier : 1f;
        smoothedTurn = Mathf.Lerp(smoothedTurn, input.x, 1f - Mathf.Exp(-turnSmoothing * dt));
        transform.Rotate(0f, smoothedTurn * turnSpeed * turnMult * dt, 0f);

        // --- Forward / backward (W / S) ---
        float targetSpeed = input.y * (sprinting ? sprintSpeed : runSpeed);
        if (input.y < 0f) targetSpeed *= backwardSpeedMultiplier;

        float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * dt);

        // --- Gravity & jump ---
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (controller.isGrounded && jumpAction.WasPressedThisFrame())
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * dt;

        Vector3 velocity = transform.forward * currentSpeed + Vector3.up * verticalVelocity;
        controller.Move(velocity * dt);
    }
}