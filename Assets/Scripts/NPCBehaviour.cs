using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NPCBehaviour : MonoBehaviour
{
    NavMeshAgent myAgent;

    Transform  targetTransform;
    Transform[] patrolPoints;
    [SerializeField] float idleTime = 1f;

    public string currentState;
    int   currentPatrolIndex = 0;
    bool  playerInSight = false;

    // Node-stroll
    PedNode currentNode, lastNode, claimedNode;

    [Header("Stroll Tuning")]
    [SerializeField] float  nodeMinDot = 0.0f;                  // forward cone bias
    [SerializeField] Vector2 idleRange = new Vector2(0.5f, 2f); // generic idle
    [SerializeField] float  arrivalRadius = 1.2f;               // soft arrival radius
    [SerializeField] float  arrivalJitter = 0.5f;               // random offset around node
    [SerializeField] float  busStopSlotSpacing = 0.9f;          // queue spacing at bus stops

    [Header("Variation")]
    [SerializeField] Vector2 speedMultiplier = new Vector2(0.9f, 1.2f);
    [SerializeField] Vector2 priorityRange  = new Vector2(30, 70);

    // -------- Global crossing lock (per-NPC) --------
    [Header("Crossing Lock")]
    [SerializeField] int   crossLimit   = 2;   // >2 crossings within window triggers lock
    [SerializeField] float windowSec    = 30f;
    [SerializeField] float cooldownSec  = 15f;
    readonly List<float> recentCrossTimes = new List<float>();
    float crossingLockUntil = 0f;

    void Awake()
    {
        myAgent = GetComponent<NavMeshAgent>();

        // per-NPC variation
        if (speedMultiplier.x > 0f && speedMultiplier.y > 0f)
            myAgent.speed = Mathf.Max(0.1f, myAgent.speed * Random.Range(speedMultiplier.x, speedMultiplier.y));
        myAgent.avoidancePriority = Mathf.Clamp(Mathf.RoundToInt(Random.Range(priorityRange.x, priorityRange.y)), 0, 99);

        // optional: patrol points from siblings tagged PatrolPoint
        if ((patrolPoints == null || patrolPoints.Length == 0) && transform.parent)
        {
            var pts = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in transform.parent)
                if (t.CompareTag("PatrolPoint")) pts.Add(t);
            pts.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            patrolPoints = pts.ToArray();
        }
    }

    void Start()
    {
        if ((patrolPoints == null || patrolPoints.Length == 0) && PedNodeManager.Instance != null)
            currentNode = PedNodeManager.Instance.Nearest(transform.position, 6f);

        StartCoroutine(SwitchState("Idle"));
    }

    void OnDisable() { if (claimedNode) { PedNodeManager.Instance?.Release(claimedNode); claimedNode = null; } }
    void OnDestroy() { if (claimedNode) { PedNodeManager.Instance?.Release(claimedNode); claimedNode = null; } }

    IEnumerator SwitchState(string newState)
    {
        if (currentState == newState) yield break;
        currentState = newState;
        StartCoroutine(currentState);
    }

    IEnumerator Idle()
    {
        float t = 0f;
        while (currentState == "Idle")
        {
            if (playerInSight && targetTransform) { StartCoroutine(SwitchState("ChaseTarget")); yield break; }
            t += Time.deltaTime;
            if (t >= idleTime) { StartCoroutine(SwitchState("Patrol")); yield break; }
            yield return null;
        }
    }

    IEnumerator Patrol()
    {
        // A) original patrolPoints route
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            while (currentState == "Patrol")
            {
                if (playerInSight && targetTransform) { StartCoroutine(SwitchState("ChaseTarget")); yield break; }
                if (patrolPoints.Length == 0) { StartCoroutine(SwitchState("Idle")); yield break; }

                var target = patrolPoints[currentPatrolIndex].position;
                myAgent.SetDestination(target);

                while (currentState == "Patrol" &&
                       (myAgent.pathPending || Vector3.Distance(transform.position, target) > 1f))
                {
                    if (playerInSight && targetTransform) { StartCoroutine(SwitchState("ChaseTarget")); yield break; }
                    yield return null;
                }

                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
                StartCoroutine(SwitchState("Idle")); yield break;
            }
        }
        else
        {
            // B) node-based stroll with anti-congestion + crossing guards
            while (currentState == "Patrol")
            {
                if (playerInSight && targetTransform) { StartCoroutine(SwitchState("ChaseTarget")); yield break; }

                if (!currentNode && PedNodeManager.Instance)
                    currentNode = PedNodeManager.Instance.Nearest(transform.position, 10f);

                // ---- pick + claim next node (apply filters) ----
                var next = PickAndClaimNextNode(out int slotIndex);
                if (next == null) { StartCoroutine(SwitchState("Idle")); yield break; }

                // compute destination with jitter / bus stop offset
                Vector3 dest = next.transform.position;
                Vector3 dir = (lastNode ? (next.transform.position - lastNode.transform.position) : transform.forward);
                dir.y = 0f; if (dir.sqrMagnitude < 0.0001f) dir = transform.forward; dir.Normalize();

                if (arrivalJitter > 0f)
                {
                    var rand = Random.insideUnitCircle * arrivalJitter;
                    dest += new Vector3(rand.x, 0f, rand.y);
                }
                if (next.type == PedNodeType.BusStop && slotIndex >= 0)
                    dest += dir * (busStopSlotSpacing * slotIndex);

                // face & go
                Vector3 look = dest - transform.position; look.y = 0f;
                if (look.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

                myAgent.SetDestination(dest);

                float r = Mathf.Max(0.1f, arrivalRadius);
                while (currentState == "Patrol" &&
                       (myAgent.pathPending || Vector3.SqrMagnitude(transform.position - dest) > r * r))
                {
                    if (playerInSight && targetTransform) { StartCoroutine(SwitchState("ChaseTarget")); yield break; }
                    yield return null;
                }

                // brief idle (longer at bus stops)
                float pause = Random.Range(idleRange.x, idleRange.y);
                if (next.type == PedNodeType.BusStop) pause += Random.Range(1f, 3f);
                yield return new WaitForSeconds(pause);

                // ---- GLOBAL CROSSING LOCK: count completed crossing ----
                if (PedNodeManager.AreOppositeCrossingSides(currentNode, next))
                {
                    PedNodeManager.Instance.UpdateGlobalCrossingHistory(
                        recentCrossTimes, ref crossingLockUntil,
                        crossLimit, windowSec, cooldownSec, Time.time);
                }
                // -----------------------------------------------

                // release claim
                if (claimedNode) { PedNodeManager.Instance?.Release(claimedNode); claimedNode = null; }

                // advance nodes
                lastNode = currentNode;
                currentNode = next;
            }
        }
    }

    IEnumerator ChaseTarget()
    {
        while (currentState == "ChaseTarget")
        {
            if (!playerInSight || !targetTransform) { StartCoroutine(SwitchState("Idle")); yield break; }
            myAgent.SetDestination(targetTransform.position);
            yield return null;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) { targetTransform = other.transform; playerInSight = true; }
    }
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) { playerInSight = false; targetTransform = null; }
    }

    // choose a next node with forward bias, capacity-aware claim,
    // and crossing filters (nearest curb only, no corner-to-corner across road, global lock)
    PedNode PickAndClaimNextNode(out int slotIndex)
    {
        slotIndex = 0;
        var mgr = PedNodeManager.Instance;
        if (mgr == null) return null;

        float radius = currentNode ? currentNode.radiusHint : 15f;
        Vector3 fwd = (myAgent.velocity.sqrMagnitude > 0.01f) ? myAgent.velocity.normalized : transform.forward;

        // forward-biased list
        var list = mgr.Candidates(transform.position, fwd, radius, lastNode, nodeMinDot);

        // if NOT currently on a Crossing, enforce:
        // - only nearest curb per crossing group
        // - forbid non-crossing targets that lie across the road
        bool onCrossingNow = (currentNode != null) && PedNodeManager.IsCrossing(currentNode);
        if (!onCrossingNow)
        {
            list = mgr.FilterCrossingsToNearestSide(list, transform.position);
            list = mgr.FilterThatWouldCrossRoad(list, transform.position);
        }

        // apply global crossing lock (if active)
        list = mgr.ApplyGlobalCrossingLock(list, Time.time, crossingLockUntil);

        // relax once if empty
        if (list == null || list.Count == 0)
        {
            list = mgr.Candidates(transform.position, fwd, radius * 1.5f, lastNode, -1f);
            if (!onCrossingNow)
            {
                list = mgr.FilterCrossingsToNearestSide(list, transform.position);
                list = mgr.FilterThatWouldCrossRoad(list, transform.position);
            }
            list = mgr.ApplyGlobalCrossingLock(list, Time.time, crossingLockUntil);
            if (list == null || list.Count == 0) return null;
        }

        // Try a few candidates: must be claimable and have a valid path
        var path = new NavMeshPath();
        int triesLimit = Mathf.Min(6, list.Count);
        for (int tries = 0; tries < triesLimit; tries++)
        {
            var n = list[Random.Range(0, list.Count)];
            if (!mgr.TryClaim(n, out slotIndex)) continue;

            if (NavMesh.CalculatePath(transform.position, n.transform.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                claimedNode = n;
                return n;
            }

            mgr.Release(n);
        }

        return null;
    }
}