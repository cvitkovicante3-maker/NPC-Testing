using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Moves an NPC along a Spline (Unity Splines package) at a constant speed.
/// Add to your NPC, drag a GameObject with a SplineContainer into "Path".
/// </summary>
public class NPCSplineFollower : MonoBehaviour
{
    public enum EndBehaviour { Loop, PingPong, Stop }

    [Header("Path")]
    [SerializeField] private SplineContainer path;
    [Tooltip("Which spline inside the container to follow.")]
    [SerializeField] private int splineIndex = 0;
    [Tooltip("Where on the path to start (0 = beginning, 1 = end).")]
    [Range(0f, 1f)][SerializeField] private float startOffset = 0f;

    [Header("Movement")]
    [SerializeField] private float speed = 2f;
    [Tooltip("Closed splines always loop.")]
    [SerializeField] private EndBehaviour endBehaviour = EndBehaviour.Loop;
    [Tooltip("Seconds to wait at the end (PingPong / Stop-then-wait).")]
    [SerializeField] private float waitAtEnd = 0f;
    [Tooltip("Lifts the NPC so the BOTTOM of its model sits on the spline, instead of its center (stops NPCs sinking into the ground).")]
    [SerializeField] private bool standOnSpline = true;
    [Tooltip("Extra offset from the spline point, in world units.")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Header("Rotation")]
    [SerializeField] private bool faceMoveDirection = true;
    [Tooltip("Ignore the path's up/down slope when turning.")]
    [SerializeField] private bool keepUpright = true;
    [SerializeField] private float turnSpeed = 8f;

    /// <summary>Current speed. Handy for driving an Animator.</summary>
    public float CurrentSpeed { get; private set; }

    private float feetOffset;
    private float splineLength;
    private float distance;
    private int direction = 1;
    private float waitTimer;
    private bool finished;

    private void Start()
    {
        if (path == null || path.Splines.Count == 0)
        {
            Debug.LogWarning($"{name}: No SplineContainer assigned.", this);
            enabled = false;
            return;
        }

        // The NPC is moved by this script, so physics must not move it too.
        if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
        {
            Debug.LogWarning($"{name}: non-kinematic Rigidbody found on the NPC. Setting it to kinematic.", this);
            rb.isKinematic = true;
        }

        if (standOnSpline) feetOffset = CalculateFeetOffset();

        splineIndex = Mathf.Clamp(splineIndex, 0, path.Splines.Count - 1);
        splineLength = path.CalculateLength(splineIndex);
        distance = startOffset * splineLength;
        ApplyPosition(Normalized, snapRotation: true);
    }

    // Distance from the NPC's pivot down to the bottom of its visible model.
    private float CalculateFeetOffset()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return transform.position.y - b.min.y;
    }

    private float Normalized => splineLength > 0f ? Mathf.Clamp01(distance / splineLength) : 0f;

    private void Update()
    {
        if (finished) { CurrentSpeed = 0f; return; }

        if (waitTimer > 0f)
        {
            waitTimer -= Time.deltaTime;
            CurrentSpeed = 0f;
            return;
        }

        distance += direction * speed * Time.deltaTime;
        CurrentSpeed = speed;

        bool closed = path.Splines[splineIndex].Closed;

        if (distance >= splineLength)
        {
            if (closed || endBehaviour == EndBehaviour.Loop)
                distance -= splineLength;
            else if (endBehaviour == EndBehaviour.PingPong)
            {
                distance = splineLength;
                direction = -1;
                waitTimer = waitAtEnd;
            }
            else
            {
                distance = splineLength;
                finished = true;
            }
        }
        else if (distance <= 0f)
        {
            if (closed || endBehaviour == EndBehaviour.Loop)
                distance += splineLength;
            else
            {
                distance = 0f;
                direction = 1;
                waitTimer = waitAtEnd;
            }
        }

        ApplyPosition(Normalized, snapRotation: false);
    }

    private void ApplyPosition(float t, bool snapRotation)
    {
        Vector3 pos = path.EvaluatePosition(splineIndex, t);
        transform.position = pos + Vector3.up * feetOffset + positionOffset;

        if (!faceMoveDirection) return;

        Vector3 tangent = path.EvaluateTangent(splineIndex, t);
        tangent *= direction;
        if (keepUpright) tangent.y = 0f;
        if (tangent.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(tangent.normalized, Vector3.up);
        transform.rotation = snapRotation
            ? target
            : Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}