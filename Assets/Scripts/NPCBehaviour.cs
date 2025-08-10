using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class NPCBehaviour : MonoBehaviour
{
    NavMeshAgent myAgent; // Reference to the NavMeshAgent component

    Transform targetTransform;      // The target to chase
    Transform[] patrolPoints;       // Patrol points for patrolling
    [SerializeField] float idleTime = 1f; // Time to idle before patrolling

    public string currentState;     // Current state of the agent
    int  currentPatrolIndex = 0;    // Index of the current patrol point
    bool playerInSight = false;     // Is the player detected?

    // --- PedNode strolling (used when no patrolPoints are assigned) ---
    PedNode currentNode, lastNode;
    [SerializeField] float  nodeMinDot = 0.0f;             // 0 = 180° cone, 0.3 ≈ ±70°
    [SerializeField] Vector2 idleRange = new Vector2(0.5f, 2.0f); // pause at nodes

    void Awake()
    {
        myAgent = GetComponent<NavMeshAgent>();

        // If patrolPoints wasn't set manually, grab all siblings tagged "PatrolPoint"
        if ((patrolPoints == null || patrolPoints.Length == 0) && transform.parent)
        {
            var pts = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in transform.parent)  // iterate empty parent’s children
            {
                if (t.CompareTag("PatrolPoint"))
                    pts.Add(t);
            }
            // Optional: stable order by name
            pts.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            patrolPoints = pts.ToArray();
        }
    }

    void Start()
    {
        // If using node stroll and we spawned near a node, snap to it
        if ((patrolPoints == null || patrolPoints.Length == 0) && PedNodeManager.Instance != null)
        {
            currentNode = PedNodeManager.Instance.Nearest(transform.position, 6f);
        }

        StartCoroutine(SwitchState("Idle")); // Start in Idle state
    }

    // Switches the agent's state and starts the corresponding coroutine
    IEnumerator SwitchState(string newState)
    {
        if (currentState == newState)
            yield break; // already in this state

        currentState = newState;
        StartCoroutine(currentState); // start coroutine by method name
    }

    // Idle state: waits for a set time or until player is detected
    IEnumerator Idle()
    {
        float timer = 0f;

        while (currentState == "Idle")
        {
            if (playerInSight && targetTransform != null)
            {
                StartCoroutine(SwitchState("ChaseTarget"));
                yield break;
            }

            timer += Time.deltaTime;
            if (timer >= idleTime)
            {
                StartCoroutine(SwitchState("Patrol"));
                yield break;
            }

            yield return null;
        }
    }

    // Patrol state: either uses fixed patrolPoints, or PedNodes if none
    IEnumerator Patrol()
    {
        // --- A) Your existing patrol route using patrolPoints (unchanged) ---
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            while (currentState == "Patrol")
            {
                if (playerInSight && targetTransform != null)
                {
                    StartCoroutine(SwitchState("ChaseTarget"));
                    yield break;
                }

                if (patrolPoints.Length == 0)
                {
                    StartCoroutine(SwitchState("Idle"));
                    yield break;
                }

                Transform currentTarget = patrolPoints[currentPatrolIndex];
                Vector3 worldTarget = currentTarget.position;
                myAgent.SetDestination(worldTarget);

                while (myAgent.pathPending || Vector3.Distance(transform.position, worldTarget) > 1f)
                {
                    if (playerInSight && targetTransform != null)
                    {
                        StartCoroutine(SwitchState("ChaseTarget"));
                        yield break;
                    }
                    yield return null;
                }

                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;

                StartCoroutine(SwitchState("Idle"));
                yield break;
            }
        }
        else
        {
            // --- B) Node-based stroll (corners/junction markers) ---
            while (currentState == "Patrol")
            {
                if (playerInSight && targetTransform != null)
                {
                    StartCoroutine(SwitchState("ChaseTarget"));
                    yield break;
                }

                // Ensure a starting node
                if (!currentNode && PedNodeManager.Instance)
                    currentNode = PedNodeManager.Instance.Nearest(transform.position, 10f);

                // Pick next node
                PedNode next = PickNextNode();
                if (next == null)
                {
                    StartCoroutine(SwitchState("Idle"));
                    yield break;
                }

                // Face and go
                Vector3 look = next.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

                myAgent.SetDestination(next.transform.position);

                // Travel until arrival
                while (currentState == "Patrol" &&
                       (myAgent.pathPending || myAgent.remainingDistance > myAgent.stoppingDistance + 0.05f))
                {
                    if (playerInSight && targetTransform != null)
                    {
                        StartCoroutine(SwitchState("ChaseTarget"));
                        yield break;
                    }
                    yield return null;
                }

                // Idle briefly (longer at bus stops)
                float pause = Random.Range(idleRange.x, idleRange.y);
                if (next.type == PedNodeType.BusStop) pause += Random.Range(1.0f, 3.0f);
                yield return new WaitForSeconds(pause);

                // Advance node state
                lastNode = currentNode;
                currentNode = next;
                // loop to choose another leg
            }
        }
    }

    // ChaseTarget state: chases the player while in sight
    IEnumerator ChaseTarget()
    {
        while (currentState == "ChaseTarget")
        {
            if (!playerInSight || targetTransform == null)
            {
                StartCoroutine(SwitchState("Idle"));
                yield break;
            }

            myAgent.SetDestination(targetTransform.position);
            yield return null;
        }
    }

    // Detects when the player enters the trigger collider
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            targetTransform = other.transform;
            playerInSight = true;
        }
    }

    // Detects when the player exits the trigger collider
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInSight = false;
            targetTransform = null;
        }
    }

    // --- helper: choose a next node with forward bias & no U-turn ---
    PedNode PickNextNode()
    {
        if (PedNodeManager.Instance == null) return null;

        // radius from current node hint, else default
        float radius = currentNode ? currentNode.radiusHint : 15f;

        // forward bias: prefer where we're heading
        Vector3 fwd = (myAgent.velocity.sqrMagnitude > 0.01f) ? myAgent.velocity.normalized : transform.forward;

        // forward-cone first
        var list = PedNodeManager.Instance.Candidates(transform.position, fwd, radius, lastNode, nodeMinDot);

        // relax cone / expand radius if empty
        if (list.Count == 0)
            list = PedNodeManager.Instance.Candidates(transform.position, fwd, radius * 1.5f, lastNode, -1f);

        if (list.Count == 0) return null;

        // Try a few random picks that have a valid NavMesh path
        for (int tries = 0; tries < 4; tries++)
        {
            var candidate = list[Random.Range(0, list.Count)];
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, candidate.transform.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                return candidate;
            }
        }

        // Fallback: first in list
        return list[0];
    }
}