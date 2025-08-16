// VehicleBehaviour.cs (Unity 6)
// Lane-following car with simple stop flag (traffic lights / peds) and gap-keeping.
// Works with or without a Rigidbody. If present, it's used as kinematic.

using System.Collections.Generic;
using UnityEngine;

public class VehicleBehaviour : MonoBehaviour
{
    // ===== Global registry (Crosswalks use this) =====
    public static readonly List<VehicleBehaviour> All = new List<VehicleBehaviour>();

    [Header("Path")]
    public LanePath path;                     // assigned by spawner or auto-picked at Start()
    [Tooltip("Meters within which a waypoint counts as reached.")]
    public float waypointReachDist = 1.0f;

    [Header("Motion")]
    public float speed = 8f;
    public float turnSpeed = 8f;

    [Header("Car Spacing")]
    [Tooltip("Desired following gap to the car ahead (meters).")]
    public float safeGap = 6f;

    [Header("Start on Lane (hand-placed cars)")]
    public bool autoPickStartNode = true;
    [Range(-1f, 1f)] public float minForwardDot = 0.2f;
    public bool  snapToCenterline = true;
    public float snapMaxDistance  = 6f;

    [Header("Physics (optional)")]
    [Tooltip("If a Rigidbody exists, use it (as kinematic). Otherwise move via transform.")]
    public bool useRigidbodyIfPresent = true;

    [Header("Stop (simple)")]
    [Tooltip("Start braking within this distance of the stop point.")]
    public float stopApproachDistance = 8f;
    [Tooltip("Within this distance from stop point = fully stopped.")]
    public float stopPointTolerance   = 0.5f;

    // ===== runtime =====
    Rigidbody rb;                       // may be null (transform-only mode)
    int wpIndex;
    public float DistanceAlongPath { get; private set; }
    public int PathId { get; set; } = -1;      // set by spawner for spawned cars
    public List<VehicleBehaviour> SharedPathCars; // injected by spawner; else falls back to All

    // Simple stop state (set by VehicleStopZone)
    [HideInInspector] public bool   shouldStop = false;
    [HideInInspector] public Vector3 stopTarget;

    // ---------- lifecycle ----------
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb && useRigidbodyIfPresent)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.constraints = RigidbodyConstraints.FreezeRotationX |
                             RigidbodyConstraints.FreezeRotationZ |
                             RigidbodyConstraints.FreezePositionY;
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
        }
    }

    void Start()
    {
        // If path not assigned (hand-placed), try to find a reasonable one.
        if (path == null)
        {
            LanePath[] all = Object.FindObjectsByType<LanePath>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

            float best = float.PositiveInfinity;
            LanePath bestLane = null;
            for (int i = 0; i < all.Length; i++)
            {
                var lp = all[i];
                if (lp == null || lp.Count < 2) continue;
                float d = Vector3.Distance(transform.position, lp[0].position);
                if (d < best) { best = d; bestLane = lp; }
            }
            path = bestLane;
        }

        if (path == null || path.Count < 2)
        {
            Debug.LogError($"[{name}] Missing path or not enough waypoints.");
            enabled = false;
            return;
        }

        if (SharedPathCars == null) SharedPathCars = VehicleBehaviour.All;

        if (autoPickStartNode) InitWaypointIndexFromCurrentPosition();
        else wpIndex = Mathf.Clamp(wpIndex, 1, path.Count - 1);
    }

    void Update()
    {
        if (!path) return;

        Vector3 target = path[wpIndex].position;
        Vector3 toTarget = target - transform.position;
        Vector3 dir = toTarget.normalized;

        // rotate toward next node
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir, Vector3.up);
            if (rb && useRigidbodyIfPresent)
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, lookRot, turnSpeed * Time.deltaTime));
            else
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, turnSpeed * Time.deltaTime);
        }

        // base speed
        float targetSpeed = speed;

        // gap keeping
        var ahead = GetCarAhead();
        if (ahead != null)
        {
            float gap = Vector3.Distance(ahead.transform.position, transform.position);
            float slowFactor = Mathf.Clamp01((gap - safeGap) / safeGap);
            targetSpeed *= Mathf.Clamp(slowFactor, 0f, 1f);
        }

        // stop for lights / peds (simple)
        if (shouldStop)
        {
            float d = Vector3.Distance(transform.position, stopTarget);
            if (d <= stopPointTolerance)
                targetSpeed = 0f;
            else
                targetSpeed = Mathf.Min(targetSpeed, speed * Mathf.Clamp01(d / Mathf.Max(0.01f, stopApproachDistance)));
        }

        // move forward
        Vector3 step = transform.forward * (targetSpeed * Time.deltaTime);
        if (rb && useRigidbodyIfPresent) rb.MovePosition(rb.position + step);
        else                             transform.position += step;

        // waypoint progress
        if (toTarget.magnitude <= waypointReachDist)
        {
            wpIndex++;
            if (wpIndex >= path.Count)
            {
                Destroy(gameObject);
                return;
            }
        }

        DistanceAlongPath += targetSpeed * Time.deltaTime;
    }

    // ---------- helpers ----------
    void InitWaypointIndexFromCurrentPosition()
    {
        Vector3 pos = transform.position;
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

        int bestSeg = -1;
        float bestDist = float.PositiveInfinity;
        float bestT = 0f;
        float cumBefore = 0f;

        float cumulative = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i].position;
            Vector3 b = path[i + 1].position;

            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.01f) { cumulative += len; continue; }

            Vector3 dir = (new Vector3(ab.x, 0f, ab.z)).normalized;
            float align = Vector3.Dot(dir, fwd);
            if (align < minForwardDot) { cumulative += len; continue; }

            float t = Mathf.Clamp01(Vector3.Dot(pos - a, (b - a).normalized) / len);
            Vector3 p = Vector3.Lerp(a, b, t);
            float d = Vector3.Distance(pos, p);

            if (d < bestDist)
            {
                bestDist = d;
                bestSeg = i;
                bestT = t;
                cumBefore = cumulative;
            }
            cumulative += len;
        }

        if (bestSeg >= 0)
        {
            wpIndex = Mathf.Clamp(bestSeg + 1, 1, path.Count - 1);

            if (snapToCenterline && bestDist <= snapMaxDistance)
            {
                Vector3 a = path[bestSeg].position;
                Vector3 b = path[bestSeg + 1].position;
                Vector3 ab = b - a;
                float len = ab.magnitude;
                if (len > 0.01f)
                {
                    Vector3 dir = ab / len;
                    float tUnclamped = Vector3.Dot(pos - a, dir);
                    float tClamped = Mathf.Clamp(tUnclamped, 0f, len);
                    Vector3 p = a + dir * tClamped;

                    transform.position = new Vector3(p.x, transform.position.y, p.z);

                    Vector3 flatDir = new Vector3(dir.x, 0f, dir.z).normalized;
                    if (flatDir.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.LookRotation(flatDir, Vector3.up);
                }
            }

            float segLen = Vector3.Distance(path[bestSeg].position, path[bestSeg + 1].position);
            DistanceAlongPath = cumBefore + segLen * bestT;
        }
        else
        {
            int bestIdx = -1;
            float bestSqr = float.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 to = path[i].position - pos; to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Dot(to.normalized, fwd) <= 0f) continue;
                float s = to.sqrMagnitude;
                if (s < bestSqr) { bestSqr = s; bestIdx = i; }
            }
            wpIndex = (bestIdx != -1) ? bestIdx : 1;
            DistanceAlongPath = 0f;
        }
    }

    VehicleBehaviour GetCarAhead()
    {
        if (SharedPathCars == null || SharedPathCars.Count == 0) return null;

        VehicleBehaviour ahead = null;
        float myZ = DistanceAlongPath;
        float best = float.PositiveInfinity;

        bool haveId = PathId >= 0;

        for (int i = 0; i < SharedPathCars.Count; i++)
        {
            var other = SharedPathCars[i];
            if (other == null || other == this) continue;

            if (haveId) { if (other.PathId != PathId) continue; }
            else        { if (other.path != this.path) continue; }

            float dz = other.DistanceAlongPath - myZ;
            if (dz > 0f && dz < best) { best = dz; ahead = other; }
        }
        return ahead;
    }
}