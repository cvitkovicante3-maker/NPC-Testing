using UnityEngine;
using UnityEngine.InputSystem; // Uses the modern input system

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 7f;

    [Header("Camera Settings")]
    public Camera playerCamera;
    public Vector3 cameraOffset = new Vector3(0f, 2f, -4f);

    void Start()
    {
        // Set up the camera placement on game start if assigned
        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(this.transform);
            playerCamera.transform.localPosition = cameraOffset;
            playerCamera.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        }
        else
        {
            Debug.LogWarning("Please drag and drop your Main Camera into the PlayerController component!");
        }
    }

    void Update()
    {
        // Directly check the keyboard state using the New Input System
        var keyboard = Keyboard.current;
        if (keyboard == null) return; // No keyboard connected

        float moveX = 0f;
        float moveZ = 0f;

        // Read W / S or Up / Down Arrow
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) moveZ = 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) moveZ = -1f;

        // Read A / D or Left / Right Arrow
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) moveX = -1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) moveX = 1f;

        // Calculate direction based on player orientation
        Vector3 movement = (transform.forward * moveZ) + (transform.right * moveX);

        // Move the player forward, backward, or sideways
        transform.Translate(movement.normalized * moveSpeed * Time.deltaTime, Space.World);
    }
}
