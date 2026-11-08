using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Basic third-person movement using the New Input System.
/// Input actions are created in code, so no Input Actions asset is needed.
/// Movement is relative to the camera. Drag your camera into "Camera Transform".
///
/// To use your OWN movement script instead: disable or remove this component
/// and add yours. The CharacterController, camera and CameraTarget keep working.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Camera (drag & drop)")]
    [Tooltip("Drag your camera here. Falls back to Camera.main if empty.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Speed")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float acceleration = 12f;

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Move: WASD, arrow keys, gamepad left stick
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

        // Jump: Space / gamepad south button
        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        // Sprint: Left Shift / left stick press
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
        Vector2 input = moveAction.ReadValue<Vector2>();
        input = Vector2.ClampMagnitude(input, 1f);

        // Camera-relative directions (flattened)
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = cameraTransform.forward; forward.y = 0f; forward.Normalize();
            right = cameraTransform.right; right.y = 0f; right.Normalize();
        }

        Vector3 moveDir = forward * input.y + right * input.x;
        float targetSpeed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
        Vector3 targetVelocity = moveDir * targetSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

        // Face movement direction
        if (moveDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        // Gravity & jump
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (controller.isGrounded && jumpAction.WasPressedThisFrame())
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
}