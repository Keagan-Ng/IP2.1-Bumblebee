using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class VehicleBehaviour : MonoBehaviour
{
    // -------- Registry --------
    public static readonly List<VehicleBehaviour> All = new List<VehicleBehaviour>();

    // -------- Movement (speed control) --------
    [Header("Basic movement")]
    public float cruiseSpeed   = 6f;   // m/s
    public float accel         = 4f;   // m/s^2
    public float brakingDecel  = 8f;   // m/s^2
    public float stopLookahead = 8f;   // m

    // -------- Car-following --------
    [Header("Car-following")]
    public LayerMask vehicleLayer;         // Vehicle layer
    public float followLookahead = 15f;    // m
    public float minGap          = 4.0f;   // m
    public float timeHeadway     = 1.5f;   // s
    public float leaderMatchGain = 0.8f;   // 0..1

    // -------- Junction grace (don't block the box) --------
    [Header("Junction grace")]
    public float graceAfterGreenSec = 4f;
    float junctionGraceUntil = 0f;

    // -------- Bus (SBS) --------
    [Header("Bus (SBS)")]
    public bool  isSBS = false;
    public float busStopDwellSeconds = 5f;
    public float busStopCooldown     = 10f;
    public float busStopLeftDot      = 0.65f; // must be on left side
    float busDwellUntil   = 0f;
    float busCooldownUntil= 0f;

    // -------- Path following (embedded) --------
    [Header("Lane path (assigned by spawner)")]
    public LanePath path;
    public int   segmentIndex = 0;         // segment i..i+1
    public float distanceOnSeg = 0f;       // metres along segment
    public float turnRateDegPerSec = 240f; // steering responsiveness
    public float lookAhead = 2.5f;         // aim-ahead distance
    public bool  loopIfNoNext = true;      // if no next path

    // cached segment
    Vector3 _a, _b; float _segLen = 1f;

    // -------- Internals --------
    float _currentSpeed;
    Vector3 _lastPos;
    CrosswalkZone _lastNearestSig;
    float _lastNearestSigDot = 1f;

    public float CurrentSpeed => _currentSpeed;

    // ===================== Lifecycle =====================
    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        _currentSpeed = cruiseSpeed * 0.5f;
        _lastPos = transform.position;
        CacheSeg();
        SnapToPathPose();
    }
    void OnDisable() { All.Remove(this); }

    // ====================== Update =======================
    void Update()
    {
        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward;

        // Desired speed baseline
        float targetSpeed = cruiseSpeed;

        // --- 1) Crossing / pedestrians (skip during grace) ---
        bool mustStopForCrossing = false;
        if (Time.time >= junctionGraceUntil)
        {
            var list = CrosswalkZone.All;
            for (int i = 0; i < list.Count; i++)
            {
                var cz = list[i];
                if (!cz) continue;

                // aim to stop slightly before the line (use our forward)
                Vector3 stopPoint = cz.transform.position - fwd * 2.5f; // tweak if needed
                Vector3 to = stopPoint - pos; to.y = 0f;
                float dist = to.magnitude;
                if (dist > stopLookahead) continue;
                if (Vector3.Dot(fwd, to.normalized) < 0.2f) continue; // not ahead

                if (cz.IsRedForVehicles() || cz.someoneIsCrossing)
                {
                    mustStopForCrossing = true;
                    break;
                }
            }
        }

        // --- 2) Car-following (ignore self) ---
        {
            Ray ray = new Ray(pos + Vector3.up * 0.5f + fwd * 0.75f, fwd);
            if (Physics.Raycast(ray, out var hit, followLookahead, vehicleLayer, QueryTriggerInteraction.Ignore))
            {
                var leader = hit.collider.GetComponentInParent<VehicleBehaviour>();
                if (leader && leader != this)
                {
                    float gap = Mathf.Max(0.01f, hit.distance - 1.0f); // bumper fudge
                    float desiredGap = Mathf.Max(minGap, _currentSpeed * timeHeadway);

                    if (gap < desiredGap) targetSpeed = 0f;
                    else targetSpeed = Mathf.Min(targetSpeed, Mathf.Lerp(cruiseSpeed, leader.CurrentSpeed, leaderMatchGain));
                }
                else
                {
                    targetSpeed = Mathf.Min(targetSpeed, cruiseSpeed * 0.6f);
                }
            }
        }

        // --- 3) Bus stop (SBS only) ---
        bool mustStopForBusStop = false;
        if (isSBS && Time.time >= busCooldownUntil)
        {
            var stops = BusStopZone.All;
            Vector3 left = -fwd;

            for (int i = 0; i < stops.Count; i++)
            {
                var s = stops[i];
                if (!s) continue;
                Vector3 to = s.transform.position - pos; to.y = 0f;
                float dist = to.magnitude;
                if (dist > s.triggerRadius) continue;
                if (Vector3.Dot(fwd, to.normalized) < 0.1f) continue;      // ahead?
                if (Vector3.Dot(left, to.normalized) < busStopLeftDot) continue; // on left?

                mustStopForBusStop = true;
                break;
            }

            if (mustStopForBusStop)
            {
                targetSpeed = Mathf.Min(targetSpeed, cruiseSpeed * 0.25f);
                if (_currentSpeed < 0.2f && busDwellUntil <= Time.time)
                    busDwellUntil = Time.time + busStopDwellSeconds;
            }
        }

        // --- 4) Priority overrides ---
        if (mustStopForCrossing) targetSpeed = 0f;
        if (busDwellUntil > Time.time) targetSpeed = 0f;

        if (busDwellUntil > 0f && busDwellUntil <= Time.time)
        {
            busCooldownUntil = Time.time + busStopCooldown;
            busDwellUntil = 0f;
        }

        // --- 5) Speed control ---
        float dv = targetSpeed - _currentSpeed;
        float maxDelta = (dv >= 0f ? accel : brakingDecel) * Time.deltaTime;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, maxDelta);
        _currentSpeed = Mathf.Max(0f, _currentSpeed);

        // --- 6) Move along lane path (or straight if none) ---
        float stepDist = _currentSpeed * Time.deltaTime;
        if (path && path.World.Count >= 2) StepPath(stepDist);
        else transform.position += fwd * stepDist;

        // --- 7) Start junction grace after clearing a green signal ---
        CrosswalkZone nearestSig = null;
        float bestDot = -1f, bestDist = 999f;
        var zones = CrosswalkZone.All;
        for (int i = 0; i < zones.Count; i++)
        {
            var cz = zones[i];
            if (!cz || cz.type != CrosswalkType.Signalised) continue;
            Vector3 to = cz.transform.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 10f) continue;
            float dot = Vector3.Dot(transform.forward, to.normalized);
            if (dist < bestDist) { bestDist = dist; bestDot = dot; nearestSig = cz; }
        }

        if (nearestSig && _lastNearestSig == nearestSig)
        {
            if (_lastNearestSigDot > 0f && bestDot <= 0f)
            {
                if (!nearestSig.IsRedForVehicles())
                    junctionGraceUntil = Time.time + graceAfterGreenSec;
            }
        }
        _lastNearestSig = nearestSig;
        _lastNearestSigDot = bestDot;

        _lastPos = transform.position;
    }

    // ================= Path utilities =================
    public void SetStartPath(LanePath p, int startIdx, float offset)
    {
        path = p;
        segmentIndex = Mathf.Max(0, startIdx);
        distanceOnSeg = Mathf.Max(0f, offset);
        CacheSeg();
        ClampAdvance(0f);
        SnapToPathPose();
    }

    public void NudgeForward(float metres) => StepPath(metres);

    void CacheSeg()
    {
        if (!path || path.World.Count < 2) return;
        segmentIndex = Mathf.Clamp(segmentIndex, 0, path.World.Count - 2);
        _a = path.World[segmentIndex];
        _b = path.World[segmentIndex + 1];
        _segLen = Mathf.Max(0.001f, Vector3.Distance(_a, _b));
    }

    void StepPath(float distance)
    {
        if (!path || path.World.Count < 2) return;

        float remain = distance;
        while (remain > 0f)
        {
            float step = Mathf.Min(remain, _segLen - distanceOnSeg);
            distanceOnSeg += step;
            remain -= step;

            if (distanceOnSeg >= _segLen - 1e-3f)
            {
                segmentIndex++;
                if (segmentIndex >= path.World.Count - 1)
                {
                    var next = path.PickNext();
                    if (next)
                    {
                        path = next; segmentIndex = 0; distanceOnSeg = 0f;
                        CacheSeg();
                    }
                    else if (loopIfNoNext)
                    {
                        segmentIndex = 0; distanceOnSeg = 0f;
                        CacheSeg();
                    }
                    else
                    {
                        distanceOnSeg = _segLen;
                        break;
                    }
                }
                else
                {
                    distanceOnSeg = 0f;
                    CacheSeg();
                }
            }
        }

        // Pose
        Vector3 pos = Vector3.Lerp(_a, _b, Mathf.Clamp01(distanceOnSeg / _segLen));
        Vector3 dir = (_b - _a).normalized;
        float ahead = Mathf.Clamp(lookAhead, 0f, _segLen);
        Vector3 aheadPos = Vector3.Lerp(_a, _b, Mathf.Clamp01((distanceOnSeg + ahead) / _segLen));
        Vector3 aim = (aheadPos - pos); aim.y = 0f;
        if (aim.sqrMagnitude < 1e-4f) aim = dir;

        transform.position = pos;
        var targetRot = Quaternion.LookRotation(aim.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnRateDegPerSec * Time.deltaTime);
    }

    void ClampAdvance(float extra)
    {
        if (!path || path.World.Count < 2) return;
        while (distanceOnSeg > _segLen)
        {
            distanceOnSeg -= _segLen;
            segmentIndex++;
            if (segmentIndex >= path.World.Count - 1)
            {
                segmentIndex = path.World.Count - 2;
                distanceOnSeg = _segLen;
                break;
            }
            CacheSeg();
        }
    }

    void SnapToPathPose()
    {
        if (!path || path.World.Count < 2) return;
        Vector3 pos = Vector3.Lerp(_a, _b, Mathf.Clamp01(distanceOnSeg / _segLen));
        Vector3 dir = (_b - _a).normalized;
        transform.position = pos;
        if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }
}