using System.Text;
using UnityEngine;

/// <summary>
/// Temporary helper. Add it to any object (e.g. the Main Camera), press Play, and read the Console.
/// It reports how the player, camera and NPCs are set up, then logs distances and speeds once a second.
/// Delete it when everything works.
/// </summary>
public class SetupDiagnostics : MonoBehaviour
{
    [SerializeField] private float reportInterval = 1f;

    private Transform cam;
    private Transform player;
    private Vector3 lastCamPos;
    private Vector3 lastPlayerPos;
    private float timer;

    private void Start()
    {
        var sb = new StringBuilder("[Diagnostics] Setup report\n");

        Camera main = Camera.main;
        if (main == null)
        {
            Debug.LogWarning("[Diagnostics] No camera tagged 'MainCamera' found.");
            return;
        }
        cam = main.transform;

        sb.AppendLine($"Camera object: '{cam.name}', parent: {(cam.parent ? "'" + cam.parent.name + "'" : "none")}");
        sb.Append("Components on camera: ");
        foreach (Component c in cam.GetComponents<Component>())
            if (c != null) sb.Append(c.GetType().Name).Append(", ");
        sb.AppendLine();

        sb.AppendLine($"Cameras in scene: {Camera.allCamerasCount}, AudioListeners: {FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}");

        // Every movement / camera / NPC script in the scene, and the object it sits on
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            string n = mb.GetType().Name;
            if (n == "GTAPlayerMovement" || n == "PlayerMovement" || n == "GTAFollowCamera" ||
                n == "ThirdPersonCamera" || n == "NPCSplineFollower")
                sb.AppendLine($"  {n} on '{mb.name}'");
        }

        GTAFollowCamera follow = cam.GetComponent<GTAFollowCamera>();
        if (follow == null)
            sb.AppendLine("WARNING: the Main Camera has no GTAFollowCamera.");
        else
            sb.AppendLine($"GTAFollowCamera target: {(follow.Target ? "'" + follow.Target.name + "'" : "NOT ASSIGNED")}");

        GTAPlayerMovement pm = FindFirstObjectByType<GTAPlayerMovement>();
        if (pm != null)
        {
            player = pm.transform;
            sb.AppendLine($"Player (has GTAPlayerMovement): '{player.name}', scale {player.lossyScale}, Rigidbody: {player.GetComponent<Rigidbody>() != null}");
            if (pm.gameObject == cam.gameObject)
                sb.AppendLine("WARNING: GTAPlayerMovement is on the CAMERA object.");
            if (follow != null && follow.Target != null && follow.Target != player)
                sb.AppendLine("WARNING: the camera's Target is NOT the object with GTAPlayerMovement.");
        }
        else
        {
            sb.AppendLine("WARNING: no GTAPlayerMovement found in the scene.");
        }

        if (cam.GetComponent<CharacterController>() != null)
            sb.AppendLine("WARNING: the camera has a CharacterController.");

        Debug.Log(sb.ToString());

        lastCamPos = cam.position;
        if (player != null) lastPlayerPos = player.position;
    }

    private void Update()
    {
        if (cam == null) return;

        timer += Time.unscaledDeltaTime;
        if (timer < reportInterval) return;
        float dt = timer;
        timer = 0f;

        Vector3 camDelta = cam.position - lastCamPos;
        lastCamPos = cam.position;
        string msg = $"camera speed {camDelta.magnitude / dt:F1} u/s";

        if (player != null)
        {
            Vector3 pDelta = player.position - lastPlayerPos;
            lastPlayerPos = player.position;
            msg += $" | player speed {pDelta.magnitude / dt:F1} u/s | camera-to-player distance {Vector3.Distance(cam.position, player.position):F1}";
        }

        Debug.Log("[Diagnostics] " + msg);
    }
}