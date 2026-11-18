using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes an NPC walk alongside the player. It matches the player's speed, keeps to one side,
/// catches up when it falls behind, stops when the player stops - and always yields to the player:
/// it never walks into them, never pushes them, and walks around them when it has to switch sides.
/// NPCDialogue starts it when a conversation ends. Add this to the NPC.
///
/// Optional: add a CharacterController to the NPC so it collides with walls and houses
/// (without one it only follows the ground height and can walk through objects).
/// </summary>
public class NPCCompanion : MonoBehaviour
{
    [Header("Where to walk (relative to the player)")]
    [Tooltip("Sideways distance from the player. + = on the player's right, - = on the left.")]
    [SerializeField] private float sideOffset = 2.5f;
    [Tooltip("How far behind the player's position to walk. Negative = slightly ahead.")]
    [SerializeField] private float backOffset = 0.5f;

    [Header("Keeping out of the player's way")]
    [Tooltip("The NPC never gets closer than this to the player's center. It is raised automatically to fit the player's size.")]
    [SerializeField] private float minPlayerDistance = 1.5f;
    [Tooltip("Stops the NPC's and the player's colliders from pushing each other.")]
    [SerializeField] private bool ignorePlayerCollision = true;

    [Header("Movement")]
    [Tooltip("Top speed while catching up. Should be above the player's sprint speed.")]
    [SerializeField] private float maxSpeed = 9f;
    [Tooltip("How eagerly it closes the gap when behind. Higher = catches up faster.")]
    [SerializeField] private float catchUpGain = 2.5f;
    [Tooltip("Stands still when this close to its spot and the player isn't moving.")]
    [SerializeField] private float stopDistance = 0.4f;
    [SerializeField] private float acceleration = 12f;
    [Tooltip("If the player gets farther than this, the NPC appears next to them.")]
    [SerializeField] private float teleportDistance = 30f;

    [Header("Resting (when the player stops)")]
    [Tooltip("Seconds the player must stand still before the NPC settles down and stops moving around.")]
    [SerializeField] private float restDelay = 0.4f;
    [Tooltip("While resting, the NPC stays exactly where it is as long as it is closer than this to the player. Keep it below your Interact Distance.")]
    [SerializeField] private float restMaxDistance = 3.5f;
    [Tooltip("Player speed (units/second) above which the player counts as moving.")]
    [SerializeField] private float movingThreshold = 0.5f;

    [Header("Guiding (leading the player to a place)")]
    [Tooltip("Walking speed while leading. Keep it a little below the player's run speed so they can keep up.")]
    [SerializeField] private float guideSpeed = 4.5f;
    [Tooltip("The guide stops and waits when the player is farther away than this.")]
    [SerializeField] private float guideWaitDistance = 9f;
    [Tooltip("...and continues once the player is closer than this.")]
    [SerializeField] private float guideResumeDistance = 6f;
    [Tooltip("If the player gets farther than this, the guide gives up and goes back to following.")]
    [SerializeField] private float guideGiveUpDistance = 45f;
    [Tooltip("Use a baked NavMesh for the route if the scene has one. Without a NavMesh the guide walks in a straight line.")]
    [SerializeField] private bool useNavMesh = true;
    [SerializeField] private float navMeshSnapDistance = 6f;

    [Header("Rotation")]
    [SerializeField] private float turnSpeed = 8f;

    [Header("Ground (used when there is no CharacterController)")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float gravity = -20f;

    /// <summary>True while the NPC is following the player.</summary>
    public bool IsFollowing => target != null;

    /// <summary>True while the NPC is leading the player to a destination.</summary>
    public bool IsGuiding => guiding;

    /// <summary>Current speed. Handy for driving an Animator.</summary>
    public float CurrentSpeed { get; private set; }

    private Transform target;
    private bool paused;
    private Vector3 velocity;
    private Vector3 lastTargetPos;
    private Vector3 targetVelocity;
    private float verticalVelocity;
    private float stillTimer;
    private float feetOffset;
    private float npcRadius = 0.3f;
    private float space;                       // personal space around the player, in world units

    // guiding
    private bool guiding;
    private bool guideWaiting;
    private bool followAfterGuide;
    private float guideAvoidSide = 1f;   // which way to walk around a player who blocks the route (+1 / -1)
    private Vector3 guideDestination;
    private float guideArriveDistance;
    private Action guideArrived;
    private readonly List<Vector3> guidePath = new List<Vector3>();
    private int guideIndex;

    private CharacterController controller;
    private NPCSplineFollower splineFollower;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private Collider[] myColliders;
    private Collider[] playerColliders;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        splineFollower = GetComponent<NPCSplineFollower>();
        myColliders = GetComponentsInChildren<Collider>();

        // This script moves the NPC, so physics must not move it too.
        if (TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
        {
            Debug.LogWarning($"{name}: non-kinematic Rigidbody found on the NPC. Setting it to kinematic.", this);
            rb.isKinematic = true;
        }

        CalculateBodySize();
    }

    private void OnDisable()
    {
        SetIgnorePlayerCollision(false);
    }

    public void StartFollowing(Transform player)
    {
        if (player == null) return;

        SetIgnorePlayerCollision(false);       // clear any old pairing first
        target = player;
        lastTargetPos = player.position;
        targetVelocity = Vector3.zero;
        stillTimer = 0f;

        // Personal space: big enough for the player's body plus ours
        space = minPlayerDistance;
        CharacterController playerController = player.GetComponent<CharacterController>();
        if (playerController != null)
        {
            Vector3 s = player.lossyScale;
            float playerRadius = playerController.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            space = Mathf.Max(space, playerRadius + npcRadius + 0.3f);
        }

        playerColliders = player.GetComponentsInChildren<Collider>();
        if (ignorePlayerCollision) SetIgnorePlayerCollision(true);

        if (splineFollower != null) splineFollower.enabled = false;   // the companion drives the NPC now
    }

    public void StopFollowing()
    {
        SetIgnorePlayerCollision(false);
        target = null;
        guiding = false;
        guideArrived = null;
        velocity = Vector3.zero;
        CurrentSpeed = 0f;
    }

    /// <summary>
    /// Starts leading the player to a place. The NPC walks there (around obstacles if the scene has a
    /// baked NavMesh), waits when the player falls behind, and calls onArrived when it gets there.
    /// </summary>
    public void StartGuiding(Transform player, Vector3 destination, float arriveDistance, bool followAfterwards, Action onArrived)
    {
        if (player == null) return;
        if (target == null) StartFollowing(player);

        guiding = true;
        guideWaiting = false;
        guideDestination = destination;
        guideArriveDistance = Mathf.Max(0.5f, arriveDistance);
        followAfterGuide = followAfterwards;
        guideArrived = onArrived;
        BuildGuidePath();
    }

    /// <summary>Stops leading (the NPC goes back to following the player).</summary>
    public void CancelGuiding()
    {
        guiding = false;
        guideArrived = null;
        guidePath.Clear();
    }

    /// <summary>
    /// Makes the NPC settle down and stay put (used while it is talking). It only moves if it is
    /// too far from the player or too close to them.
    /// </summary>
    public void SetPaused(bool value)
    {
        paused = value;
    }

    private void Update()
    {
        if (target == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // The player's horizontal velocity, estimated from how it actually moved
        Vector3 delta = target.position - lastTargetPos;
        delta.y = 0f;
        lastTargetPos = target.position;
        Vector3 rawVelocity = delta / dt;
        if (rawVelocity.magnitude > 40f) rawVelocity = Vector3.zero;   // ignore teleports
        targetVelocity = Vector3.Lerp(targetVelocity, rawVelocity, 1f - Mathf.Exp(-10f * dt));

        if (guiding)
        {
            UpdateGuide(dt);
            return;
        }

        // The player counts as "still" after standing quietly for a moment
        stillTimer = targetVelocity.magnitude > movingThreshold ? 0f : stillTimer + dt;
        bool resting = paused || stillTimer >= restDelay;

        // The spot beside the player where the NPC wants to be
        Vector3 right = Vector3.ProjectOnPlane(target.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        Vector3 goal = target.position + right * sideOffset - forward * backOffset;

        // ...which must itself stay outside the player's personal space
        Vector3 goalOffset = goal - target.position;
        goalOffset.y = 0f;
        if (goalOffset.magnitude < space)
            goal = target.position + (goalOffset.sqrMagnitude > 0.0001f ? goalOffset.normalized : right) * space;

        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;
        float distance = toGoal.magnitude;

        if (distance > teleportDistance)
        {
            Teleport(goal);
            return;
        }

        Vector3 desired;
        if (resting)
        {
            // Player is standing still: don't wander around, just stay where we are
            desired = RestingVelocity();
        }
        else
        {
            // Match the player's speed, plus a push toward the spot to close any gap
            desired = Vector3.zero;
            if (!(distance < stopDistance && targetVelocity.magnitude < 0.3f))
                desired = targetVelocity + toGoal * catchUpGain;
        }

        // Never head INTO the player: turn that speed into walking around them
        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0f;
        float playerDistance = toPlayer.magnitude;
        if (playerDistance > 0.001f && playerDistance < space + 1f)
        {
            Vector3 dirToPlayer = toPlayer / playerDistance;
            float into = Vector3.Dot(desired, dirToPlayer);
            if (into > 0f)
            {
                Vector3 tangent = Vector3.Cross(Vector3.up, dirToPlayer);
                if (Vector3.Dot(tangent, toGoal) < 0f) tangent = -tangent;
                desired += tangent * into - dirToPlayer * into;
            }
            if (playerDistance < space)
                desired -= dirToPlayer * (space - playerDistance) * 4f;   // step back out
        }
        desired = Vector3.ClampMagnitude(desired, maxSpeed);

        velocity = Vector3.MoveTowards(velocity, desired, acceleration * dt);

        Vector3 step = velocity * dt;
        KeepOutOfPlayer(ref step, dt);
        CurrentSpeed = velocity.magnitude;

        Move(step, dt);
        Rotate(dt);
    }

    // ------------------------------------------------------------------ guiding

    // The route: NavMesh corners if the scene has a NavMesh, otherwise straight to the destination
    private void BuildGuidePath()
    {
        guidePath.Clear();
        guideIndex = 0;

        bool found = false;
        if (useNavMesh &&
            NavMesh.SamplePosition(transform.position, out NavMeshHit from, navMeshSnapDistance, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(guideDestination, out NavMeshHit to, navMeshSnapDistance, NavMesh.AllAreas))
        {
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) &&
                path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 1)
            {
                guidePath.AddRange(path.corners);
                guideIndex = 1;                                                  // corner 0 is where we start
                if (path.status == NavMeshPathStatus.PathPartial) guidePath.Add(guideDestination);
                found = true;
            }
        }

        if (!found) guidePath.Add(guideDestination);
    }

    private void UpdateGuide(float dt)
    {
        Vector3 pos = transform.position;

        Vector3 toPlayer = target.position - pos;
        toPlayer.y = 0f;
        float playerDistance = toPlayer.magnitude;

        // The player walked far away: stop leading and go back to following (which brings us to them)
        if (playerDistance > guideGiveUpDistance)
        {
            CancelGuiding();
            return;
        }

        // Arrived?
        Vector3 toDestination = guideDestination - pos;
        toDestination.y = 0f;
        if (toDestination.magnitude <= guideArriveDistance)
        {
            FinishGuiding();
            return;
        }

        // Wait for the player to catch up (two distances, so it doesn't flicker)
        if (guideWaiting) { if (playerDistance < guideResumeDistance) guideWaiting = false; }
        else if (playerDistance > guideWaitDistance) guideWaiting = true;
        if (paused) guideWaiting = true;                                          // talking

        Vector3 desired = Vector3.zero;
        if (!guideWaiting && guidePath.Count > 0)
        {
            // move on to the next corner when we are close to the current one
            while (guideIndex < guidePath.Count - 1)
            {
                Vector3 toCorner = guidePath[guideIndex] - pos;
                toCorner.y = 0f;
                if (toCorner.magnitude < 0.8f) guideIndex++;
                else break;
            }

            Vector3 corner = guidePath[Mathf.Min(guideIndex, guidePath.Count - 1)] - pos;
            corner.y = 0f;
            if (corner.sqrMagnitude > 0.0001f)
            {
                // go a bit slower when the player is lagging behind
                float closeness = Mathf.InverseLerp(guideWaitDistance, guideResumeDistance, playerDistance);
                desired = corner.normalized * guideSpeed * Mathf.Lerp(0.4f, 1f, closeness);
            }
        }

        // The player is standing in the way: walk AROUND them instead of into them (and never stall)
        if (desired.sqrMagnitude > 0.0001f && playerDistance > 0.001f && playerDistance < space + 1.5f)
        {
            Vector3 dirToPlayer = toPlayer / playerDistance;
            float into = Vector3.Dot(desired, dirToPlayer);
            if (into > 0f)
            {
                Vector3 tangent = Vector3.Cross(Vector3.up, dirToPlayer);
                float side = Vector3.Dot(tangent, desired);
                if (Mathf.Abs(side) < 0.25f * desired.magnitude)
                    side = guideAvoidSide;                        // head-on: keep the side we already chose
                else
                    guideAvoidSide = Mathf.Sign(side);            // remember it so we never flip back and forth
                if (side < 0f) tangent = -tangent;

                // sidestep at full speed, plus a small push away from the player
                desired = tangent * desired.magnitude - dirToPlayer * (into * 0.3f);
            }
        }

        velocity = Vector3.MoveTowards(velocity, desired, acceleration * dt);

        Vector3 step = velocity * dt;
        KeepOutOfPlayer(ref step, dt);
        CurrentSpeed = velocity.magnitude;

        Move(step, dt);
        Rotate(dt);          // faces where it is going, or the player while it waits
    }

    private void FinishGuiding()
    {
        guiding = false;
        guidePath.Clear();
        velocity = Vector3.zero;
        CurrentSpeed = 0f;

        Action callback = guideArrived;
        guideArrived = null;

        if (!followAfterGuide) StopFollowing();      // stay at the destination
        callback?.Invoke();
    }

    // While the player stands still: do nothing, unless the NPC is too far away or too close.
    private Vector3 RestingVelocity()
    {
        Vector3 toPlayer = target.position - transform.position;
        toPlayer.y = 0f;
        float d = toPlayer.magnitude;
        if (d < 0.001f) return Vector3.zero;

        float minD = space + 0.6f;
        float maxD = Mathf.Max(restMaxDistance, minD + 1f);
        Vector3 dirToPlayer = toPlayer / d;

        float wantedDistance;
        if (d > maxD) wantedDistance = maxD - 0.7f;          // too far: come a bit closer
        else if (d < minD) wantedDistance = minD + 0.4f;     // too close: step back
        else return Vector3.zero;                            // comfortable: stay put

        Vector3 spot = target.position - dirToPlayer * wantedDistance;
        Vector3 toSpot = spot - transform.position;
        toSpot.y = 0f;
        return toSpot * catchUpGain;
    }

    // Final safety net: the NPC may never end a frame inside the player's personal space.
    private void KeepOutOfPlayer(ref Vector3 step, float dt)
    {
        Vector3 next = transform.position + step;
        Vector3 offset = next - target.position;
        offset.y = 0f;
        float d = offset.magnitude;
        if (d >= space) return;

        Vector3 dir = d > 0.001f ? offset / d : Vector3.ProjectOnPlane(target.right, Vector3.up).normalized;
        Vector3 corrected = target.position + dir * space;
        step.x = corrected.x - transform.position.x;
        step.z = corrected.z - transform.position.z;

        // keep the stored velocity consistent with what really happened
        velocity = Vector3.ClampMagnitude(new Vector3(step.x, 0f, step.z) / dt, maxSpeed);
    }

    private void Move(Vector3 delta, float dt)
    {
        if (controller != null && controller.enabled)
        {
            verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * dt;
            controller.Move(delta + Vector3.up * verticalVelocity * dt);
        }
        else
        {
            Vector3 p = transform.position + delta;
            if (TryGetGroundY(p, out float groundY)) p.y = groundY + feetOffset;
            transform.position = p;
        }
    }

    private void Rotate(float dt)
    {
        Vector3 flat = velocity;
        flat.y = 0f;

        Quaternion wanted;
        if (flat.magnitude > 0.2f)
        {
            wanted = Quaternion.LookRotation(flat);
        }
        else
        {
            // standing still: look at the player
            Vector3 toPlayer = target.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) return;
            wanted = Quaternion.LookRotation(toPlayer);
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-turnSpeed * dt));
    }

    private void Teleport(Vector3 position)
    {
        velocity = Vector3.zero;

        if (controller != null && controller.enabled)
        {
            position.y = target.position.y;
            controller.enabled = false;     // a CharacterController must be off to be moved directly
            transform.position = position;
            controller.enabled = true;
        }
        else
        {
            if (TryGetGroundY(position, out float groundY)) position.y = groundY + feetOffset;
            transform.position = position;
        }
    }

    // Makes the NPC's and the player's colliders ignore each other, so neither can push the other
    private void SetIgnorePlayerCollision(bool ignore)
    {
        if (myColliders == null || playerColliders == null) return;

        foreach (Collider a in myColliders)
        {
            if (a == null) continue;
            foreach (Collider b in playerColliders)
            {
                if (b == null || a == b) continue;
                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    // Height of the ground below a point, ignoring this NPC and the player
    private bool TryGetGroundY(Vector3 point, out float groundY)
    {
        groundY = 0f;
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 3f, Vector3.down, hits, 20f, groundMask,
            QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Transform t = hits[i].collider.transform;
            if (t.IsChildOf(transform)) continue;
            if (target != null && t.IsChildOf(target)) continue;
            if (hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                groundY = hits[i].point.y;
                found = true;
            }
        }
        return found;
    }

    // Distance from the NPC's pivot down to the bottom of its model, and its rough radius
    private void CalculateBodySize()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        feetOffset = transform.position.y - b.min.y;
        npcRadius = Mathf.Max(b.extents.x, b.extents.z);
    }
}