using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCBehaviour : MonoBehaviour
{
    // ===================== Public Violation Bus (static) =====================
    public static class ViolationSystem
    {
        public static event Action<Transform, Vector3, string> OnViolation;
        public static void Report(Transform offender, Vector3 position, string reason)
        {
            OnViolation?.Invoke(offender, position, reason);
        }
    }

    // ============================ NPC role & state ============================
    public enum MoveState { Wander, Crossing, Chase }

    [Header("Identity (auto from Tag)")]
    public bool isAbnormal; // tag == NPC_Abnormal (may jaywalk)
    public bool isEnforcer; // tag == NPC_Enforcer (chases offenders)

    [Header("State (debug)")]
    public MoveState state = MoveState.Wander;

    // ----------------------------- Wander config -----------------------------
    [Header("Wander")]
    public float roamRadius = 15f;
    public float repathInterval = 3f;
    public float arriveThreshold = 0.6f;

    // ---------------------------- Crossing config ----------------------------
    [Header("Crossing")]
    [Tooltip("How far past the crosswalk center to aim when choosing the far curb.")]
    public float crossBeyond = 3.5f;
    public float crossArriveThreshold = 0.6f;

    [Header("Crossing Lockout")]
    [Tooltip("After 2 crossings within 30s, lock crossings for 40s.")]
    public int   crossLimit     = 2;      // cannot cross three in a row
    public float crossWindowSec = 30f;    // window
    public float lockoutSec     = 40f;    // lock duration

    // ------------------------------ Chase config -----------------------------
    [Header("Chase (Enforcers only)")]
    public float viewDistance = 25f;
    [Range(1f,179f)] public float viewAngle = 90f;
    public LayerMask losObstacles;
    public float chaseForgetTime = 5f;
    public float arrestDistance = 2.0f;
    public float chaseSpeedMult = 1.25f;

    // ------------------------------ Animation --------------------------------
    [Header("Animation (optional)")]
    public Animator animator;
    public string isWalkingBool = "isWalking";

    [Header("Debug")]
    public bool debugCrossing = false;

    // =============================== Runtime ================================
    NavMeshAgent agent;
    Vector3 currentWanderTarget;
    float repathTimer;

    CrosswalkZone zoneInside;    // zone we are standing in (curb)
    CrosswalkZone activeCrossing; // zone we actually started to cross
    Vector3 crossingTarget;      // far curb point

    readonly List<float> recentCrossTimes = new List<float>();
    float crossingLockUntil = 0f;

    // Chase state
    Transform chaseTarget;
    float lastSeenTime = -999f;
    Vector3 lastSeenPos;

    // --- Crosswalk area mask toggle (so we don't wander across when we shouldn't) ---
    int crosswalkArea = -1;
    int crosswalkMaskBit = 0;
    int sidewalkMask = ~0; // agent's mask with Crosswalk bit removed

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();

        // Auto role from tags
        isAbnormal = CompareTag("NPC_Abnormal");
        isEnforcer = CompareTag("NPC_Enforcer");

        // Enforcers listen for violations
        if (isEnforcer) ViolationSystem.OnViolation += OnViolationHeard;

        // Cache Crosswalk area & masks
        crosswalkArea = NavMesh.GetAreaFromName("Crosswalk");
        if (crosswalkArea >= 0)
        {
            crosswalkMaskBit = 1 << crosswalkArea;
            sidewalkMask = agent.areaMask & ~crosswalkMaskBit; // forbid Crosswalk by default
        }
        else
        {
            Debug.LogWarning("NavMesh area 'Crosswalk' not found. NPCs may wander across when they shouldn't.");
            sidewalkMask = agent.areaMask;
        }
    }

    void OnDestroy()
    {
        if (isEnforcer) ViolationSystem.OnViolation -= OnViolationHeard;
        if (activeCrossing) activeCrossing.NotifyPedestrianEnd();
    }

    void OnEnable()
    {
        // Forbid crosswalk area while wandering
        if (crosswalkArea >= 0) agent.areaMask = sidewalkMask;

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
        if (zoneInside && !IsCrossingLocked())
        {
            bool allowed = zoneInside.CanPedestrianStartCrossing();
            if (debugCrossing)
                Debug.Log($"{name}: at {zoneInside.name} allowed={allowed} type={zoneInside.type}");

            if (!allowed && isAbnormal)
            {
                BeginCross(illegal: true);
                return;
            }
            if (allowed)
            {
                BeginCross(illegal: false);
                return;
            }

            // waiting at curb
            agent.isStopped = true;
            return;
        }

        // roam
        agent.isStopped = false;
        repathTimer -= Time.deltaTime;
        if (!agent.hasPath || agent.remainingDistance <= arriveThreshold || repathTimer <= 0f)
        {
            if (zoneInside && IsCrossingLocked())
                PickNewWanderTargetAwayFrom(zoneInside.transform.position, 8f);
            else
                PickNewWanderTarget(false);
        }
    }

    // ============================== State: Crossing ==============================
    void TickCrossing()
    {
        agent.isStopped = false;
        agent.SetDestination(crossingTarget);

        if (!agent.pathPending &&
            (agent.remainingDistance <= crossArriveThreshold ||
             (transform.position - crossingTarget).sqrMagnitude <= crossArriveThreshold * crossArriveThreshold))
        {
            activeCrossing?.NotifyPedestrianEnd();
            RegisterCrossing();

            activeCrossing = null;
            zoneInside = null;

            // Back to wander: forbid Crosswalk area again
            if (crosswalkArea >= 0) agent.areaMask = sidewalkMask;

            state = MoveState.Wander;
            PickNewWanderTarget(true);
        }
    }

    // ================================ State: Chase ===============================
    void TickChase()
    {
        if (!isEnforcer)
        {
            state = MoveState.Wander;
            return;
        }

        if (!chaseTarget)
        {
            state = MoveState.Wander;
            agent.speed = Mathf.Max(0.1f, agent.speed / chaseSpeedMult);
            PickNewWanderTarget(true);
            return;
        }

        if (CanSee(chaseTarget, out var seenPos))
        {
            lastSeenPos = seenPos;
            lastSeenTime = Time.time;
        }

        Vector3 goal = (Time.time - lastSeenTime <= chaseForgetTime) ? lastSeenPos : chaseTarget.position;
        agent.isStopped = false;
        agent.SetDestination(goal);

        float dist = Vector3.Distance(transform.position, chaseTarget.position);
        if (dist <= arrestDistance)
        {
            state = MoveState.Wander;
            agent.speed = Mathf.Max(0.1f, agent.speed / chaseSpeedMult);
            chaseTarget = null;
            PickNewWanderTarget(true);
            return;
        }

        if (Time.time - lastSeenTime > chaseForgetTime &&
            Vector3.Distance(transform.position, goal) < 1.0f)
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
        // Allow Crosswalk area while crossing
        if (crosswalkArea >= 0) agent.areaMask = sidewalkMask | crosswalkMaskBit;

        // Try both sides of the strip; then snap to SIDEWALK (exclude Crosswalk) for the final target
        Vector3 center = zoneInside.transform.position;
        Vector3 dir = center - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
        dir.Normalize();

        Vector3 cand1 = center + dir * crossBeyond;
        Vector3 cand2 = center - dir * crossBeyond;

        if (!TryFindSidewalk(cand1, out crossingTarget) && !TryFindSidewalk(cand2, out crossingTarget))
        {
            if (debugCrossing) Debug.LogWarning($"{name}: crossing target sample failed at {zoneInside.name}");
            // Revert mask and keep roaming
            if (crosswalkArea >= 0) agent.areaMask = sidewalkMask;
            agent.isStopped = false;
            PickNewWanderTarget(false);
            return;
        }

        zoneInside.NotifyPedestrianStart();
        activeCrossing = zoneInside;

        if (illegal)
            ViolationSystem.Report(transform, transform.position, "Jaywalk");

        agent.isStopped = false;
        state = MoveState.Crossing;
        agent.SetDestination(crossingTarget);

        if (debugCrossing) Debug.Log($"{name}: crossing {zoneInside.name} -> {crossingTarget}");
    }

    // Sample ONLY sidewalk (exclude Crosswalk area) for the end point
    bool TryFindSidewalk(Vector3 probe, out Vector3 result)
    {
        result = probe;
        int maskNoCrosswalk = (crosswalkArea >= 0) ? (sidewalkMask) : agent.areaMask;
        if (NavMesh.SamplePosition(probe, out var hit, 2.5f, maskNoCrosswalk))
        {
            result = hit.position;
            return true;
        }
        return false;
    }

    void RegisterCrossing()
    {
        float now = Time.time;
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
        if (!CanSee(offender, out var seenPos)) return;

        chaseTarget = offender;
        lastSeenPos = seenPos;
        lastSeenTime = Time.time;

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
            if (debugCrossing) Debug.Log($"{name}: entered crosswalk {cz.name}");
        }
    }

    void OnTriggerExit(Collider other)
    {
        var cz = other.GetComponent<CrosswalkZone>();
        if (cz != null && cz == zoneInside)
        {
            if (debugCrossing) Debug.Log($"{name}: left crosswalk {cz.name}");
            zoneInside = null;
            if (state == MoveState.Wander) agent.isStopped = false;
        }
    }
}