using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCBehaviour : MonoBehaviour
{
    public static class ViolationSystem
    {
        public static event Action<Transform, Vector3, string> OnViolation;

        /// Report a jaywalk or illegal cross. offender may be Player or NPC.
        public static void Report(Transform offender, Vector3 position, string reason)
        {
            OnViolation?.Invoke(offender, position, reason);
        }
    }

    // ============================ NPC role & state ============================
    public enum MoveState { Wander, Crossing, Chase }

    [Header("Identity (auto from Tag)")]
    [Tooltip("If true (tag == NPC_Abnormal), this NPC may start an illegal crossing (jaywalk).")]
    public bool isAbnormal;
    [Tooltip("If true (tag == NPC_Enforcer), this NPC can chase offenders.")]
    public bool isEnforcer;

    [Header("State (debug)")]
    public MoveState state = MoveState.Wander;

    // ----------------------------- Wander config -----------------------------
    [Header("Wander")]
    [Tooltip("Max radius for random roam picks.")]
    public float roamRadius = 15f;
    [Tooltip("Seconds between path refreshes while roaming.")]
    public float repathInterval = 3f;
    [Tooltip("Arrival threshold for roam destinations.")]
    public float arriveThreshold = 0.6f;

    // ---------------------------- Crossing config ----------------------------
    [Header("Crossing")]
    [Tooltip("How far beyond the crosswalk center to target (toward far curb).")]
    public float crossBeyond = 3.5f;
    [Tooltip("Tolerance for considering the far curb reached.")]
    public float crossArriveThreshold = 0.6f;

    [Header("Crossing Lockout")]
    [Tooltip("Max crossings allowed inside window before lockout triggers.")]
    public int crossLimit = 2;            // “cannot cross three roads in a row”
    [Tooltip("Rolling time window (seconds) for counting crossings.")]
    public float crossWindowSec = 30f;    // “…within 30 seconds”
    [Tooltip("Lockout duration (seconds) after exceeding limit.")]
    public float lockoutSec = 40f;        // “…locked out for 40 seconds”

    // ------------------------------ Chase config -----------------------------
    [Header("Chase (Enforcers only)")]
    [Tooltip("Max chase distance to consider targets.")]
    public float viewDistance = 25f;
    [Range(1f, 179f)]
    [Tooltip("Chase only if offender is within this FOV cone (degrees).")]
    public float viewAngle = 90f;
    [Tooltip("Layers that block line-of-sight (0 = ignore).")]
    public LayerMask losObstacles;
    [Tooltip("Give up if target not seen for this long (seconds).")]
    public float chaseForgetTime = 5f;
    [Tooltip("Close enough to ‘catch’ target (no arrest logic here; just stop).")]
    public float arrestDistance = 2.0f;
    [Tooltip("Speed multiplier while chasing.")]
    public float chaseSpeedMult = 1.25f;

    // ------------------------------ Animation --------------------------------
    [Header("Animation (optional)")]
    public Animator animator;
    public string isWalkingBool = "isWalking";

    // =============================== Runtime ================================
    NavMeshAgent agent;
    Vector3 currentWanderTarget;
    float repathTimer;

    CrosswalkZone zoneInside;     // Trigger zone we’re currently in (if any)
    CrosswalkZone activeCrossing; // Zone we are actively traversing
    Vector3 crossingTarget;       // Far curb target position

    readonly List<float> recentCrossTimes = new List<float>();
    float crossingLockUntil = 0f;

    // Chase state
    Transform chaseTarget;
    float lastSeenTime = -999f;
    Vector3 lastSeenPos;

    // =============================== Lifecycle ===============================
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();

        // Auto role from tags
        isAbnormal = CompareTag("NPC_Abnormal");
        isEnforcer = CompareTag("NPC_Enforcer");

        // Enforcers subscribe to violations
        if (isEnforcer)
            ViolationSystem.OnViolation += OnViolationHeard;
    }

    void OnDestroy()
    {
        if (isEnforcer)
            ViolationSystem.OnViolation -= OnViolationHeard;

        // If we vanish mid-cross, release the crossing for vehicles
        if (activeCrossing) activeCrossing.NotifyPedestrianEnd();
    }

    void OnEnable()
    {
        PickNewWanderTarget(true);
        UpdateAnim();
    }

    void Update()
    {
        switch (state)
        {
            case MoveState.Wander:   TickWander();   break;
            case MoveState.Crossing: TickCrossing(); break;
            case MoveState.Chase:    TickChase();    break;
        }
        UpdateAnim();
    }

    // =============================== State: Wander ===============================
    void TickWander()
    {
        // If we’re at a crosswalk and NOT locked, decide to cross (or wait).
        if (zoneInside && !IsCrossingLocked())
        {
            bool allowed = zoneInside.CanPedestrianStartCrossing();

            // Abnormal NPCs may jaywalk (illegal start)
            if (!allowed && isAbnormal)
            {
                BeginCross(illegal: true);
                return;
            }

            // Normal NPC: wait until legal
            if (allowed)
            {
                BeginCross(illegal: false);
                return;
            }

            // Waiting at curb
            agent.isStopped = true;
            return;
        }

        // Not at a crossing (or locked) → roam
        agent.isStopped = false;

        // Basic repath cadence
        repathTimer -= Time.deltaTime;
        if (!agent.hasPath || agent.remainingDistance <= arriveThreshold || repathTimer <= 0f)
        {
            if (zoneInside && IsCrossingLocked())
                PickNewWanderTargetAwayFrom(zoneInside.transform.position, 8f);
            else
                PickNewWanderTarget(false);
        }

        // Passive detection for enforcers: if someone illegal is reported AND in FOV, OnViolationHeard will switch state.
    }

    // ============================== State: Crossing ==============================
    void TickCrossing()
    {
        agent.isStopped = false;
        agent.SetDestination(crossingTarget);

        // Arrived at far curb?
        if (!agent.pathPending &&
            (agent.remainingDistance <= crossArriveThreshold ||
             (transform.position - crossingTarget).sqrMagnitude <= crossArriveThreshold * crossArriveThreshold))
        {
            // Clear crossing for vehicles
            activeCrossing?.NotifyPedestrianEnd();

            // Count this crossing (may lock future ones)
            RegisterCrossing();

            // Reset & resume wandering
            activeCrossing = null;
            zoneInside = null;
            state = MoveState.Wander;
            PickNewWanderTarget(true);
        }
    }

    // ================================ State: Chase ===============================
    void TickChase()
    {
        if (!isEnforcer)
        {
            // Safety: non-enforcers shouldn’t be here
            state = MoveState.Wander;
            return;
        }

        if (!chaseTarget)
        {
            // Lost target completely
            state = MoveState.Wander;
            agent.speed = Mathf.Max(0.1f, agent.speed / chaseSpeedMult);
            PickNewWanderTarget(true);
            return;
        }

        // Check FOV + LOS
        if (CanSee(chaseTarget, out var seenPos))
        {
            lastSeenPos = seenPos;
            lastSeenTime = Time.time;
        }

        // Move toward last seen pos (or current target pos if visible)
        Vector3 goal = (Time.time - lastSeenTime <= chaseForgetTime) ? lastSeenPos : chaseTarget.position;
        agent.isStopped = false;
        agent.SetDestination(goal);

        // "Catch"
        float dist = Vector3.Distance(transform.position, chaseTarget.position);
        if (dist <= arrestDistance)
        {
            // End chase (you can add penalty/effects here)
            state = MoveState.Wander;
            agent.speed = Mathf.Max(0.1f, agent.speed / chaseSpeedMult);
            chaseTarget = null;
            PickNewWanderTarget(true);
            return;
        }

        // Give up if unseen for too long and far from lastSeenPos
        if (Time.time - lastSeenTime > chaseForgetTime &&
            Vector3.Distance(transform.position, goal) < 1.0f) // reached last-known
        {
            state = MoveState.Wander;
            agent.speed = Mathf.Max(0.1f, agent.speed / chaseSpeedMult);
            chaseTarget = null;
            PickNewWanderTarget(true);
        }
    }

    // =============================== Crossing flow ===============================
    void BeginCross(bool illegal)
    {
        // Compute a far curb target (based on crosswalk center)
        crossingTarget = ComputeFarCurbTarget(zoneInside);
        if (!SampleNav(ref crossingTarget))
        {
            // Couldn’t find a valid nav point; just keep roaming
            agent.isStopped = false;
            PickNewWanderTarget(false);
            return;
        }

        // Tell cars to yield while we cross
        zoneInside.NotifyPedestrianStart();
        activeCrossing = zoneInside;

        // Report violation if this was an illegal start (Abnormal jaywalk or player will be reported elsewhere)
        if (illegal)
            ViolationSystem.Report(transform, transform.position, "Jaywalk");

        // Start moving
        agent.isStopped = false;
        state = MoveState.Crossing;
        agent.SetDestination(crossingTarget);
    }

    Vector3 ComputeFarCurbTarget(CrosswalkZone cz)
    {
        Vector3 center = cz.transform.position;
        Vector3 toCenter = center - transform.position; toCenter.y = 0f;
        Vector3 dir = toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : transform.forward;
        var tgt = center + dir * crossBeyond;
        tgt.y = transform.position.y;
        return tgt;
    }

    void RegisterCrossing()
    {
        float now = Time.time;

        // prune old
        for (int i = recentCrossTimes.Count - 1; i >= 0; i--)
            if (now - recentCrossTimes[i] > crossWindowSec) recentCrossTimes.RemoveAt(i);

        recentCrossTimes.Add(now);

        if (recentCrossTimes.Count >= crossLimit)
            crossingLockUntil = Mathf.Max(crossingLockUntil, now + lockoutSec);
    }

    bool IsCrossingLocked() => Time.time < crossingLockUntil;

    // ============================== Enforcer hooks ==============================
    void OnViolationHeard(Transform offender, Vector3 pos, string reason)
    {
        if (!isEnforcer || offender == null || offender == transform) return;

        // Check if offender is within FOV + LOS
        if (!CanSee(offender, out var seenPos)) return;

        // Begin/refresh chase
        chaseTarget = offender;
        lastSeenPos = seenPos;
        lastSeenTime = Time.time;

        // Boost speed
        agent.speed *= chaseSpeedMult;
        state = MoveState.Chase;
        agent.isStopped = false;
    }

    bool CanSee(Transform target, out Vector3 seenPos)
    {
        seenPos = target.position;
        Vector3 to = target.position - transform.position; to.y = 0f;
        float dist = to.magnitude;
        if (dist > viewDistance) return false;

        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (Vector3.Angle(fwd, to) > viewAngle * 0.5f) return false;

        if (losObstacles.value != 0)
        {
            var origin = transform.position + Vector3.up * 1.6f;
            var dest   = target.position   + Vector3.up * 1.2f;
            if (Physics.Linecast(origin, dest, losObstacles, QueryTriggerInteraction.Ignore))
                return false;
        }

        seenPos = target.position;
        return true;
    }

    // ================================ Roaming =================================
    void PickNewWanderTarget(bool immediate)
    {
        repathTimer = repathInterval;

        if (RandomNavPoint(transform.position, roamRadius, out var candidate))
        {
            currentWanderTarget = candidate;
            agent.SetDestination(currentWanderTarget);
        }
        else
        {
            currentWanderTarget = transform.position + transform.forward * 2f;
            agent.SetDestination(currentWanderTarget);
        }

        if (immediate) agent.isStopped = false;
    }

    void PickNewWanderTargetAwayFrom(Vector3 avoid, float minAway)
    {
        repathTimer = repathInterval;

        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = (transform.position - avoid); dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = UnityEngine.Random.insideUnitSphere;
            dir.Normalize();

            Vector3 probe = transform.position + dir * UnityEngine.Random.Range(minAway * 0.5f, Mathf.Max(minAway, roamRadius));
            if (RandomNavPoint(probe, 3f, out var candidate))
            {
                currentWanderTarget = candidate;
                agent.isStopped = false;
                agent.SetDestination(currentWanderTarget);
                return;
            }
        }

        PickNewWanderTarget(false);
    }

    bool RandomNavPoint(Vector3 center, float radius, out Vector3 result)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 random = center + UnityEngine.Random.insideUnitSphere * radius;
            if (NavMesh.SamplePosition(random, out var hit, 2.0f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }
        result = center;
        return false;
    }

    bool SampleNav(ref Vector3 pos)
    {
        if (NavMesh.SamplePosition(pos, out var hit, 2.0f, NavMesh.AllAreas))
        {
            pos = hit.position;
            return true;
        }
        return false;
    }

    // ============================== Animation sync ============================
    void UpdateAnim()
    {
        if (!animator || string.IsNullOrEmpty(isWalkingBool)) return;

        bool walking =
            !agent.isStopped &&
            agent.hasPath &&
            agent.remainingDistance > 0.1f &&
            agent.velocity.sqrMagnitude > 0.01f;

        animator.SetBool(isWalkingBool, walking);
    }

    // ================================ Triggers =================================
    void OnTriggerEnter(Collider other)
    {
        var cz = other.GetComponent<CrosswalkZone>();
        if (cz != null)
        {
            zoneInside = cz;

            // If currently locked, keep moving and step away soon
            if (IsCrossingLocked()) agent.isStopped = false;
        }
    }

    void OnTriggerExit(Collider other)
    {
        var cz = other.GetComponent<CrosswalkZone>();
        if (cz != null && cz == zoneInside)
        {
            zoneInside = null;
            if (state == MoveState.Wander) agent.isStopped = false;
        }
    }
}
