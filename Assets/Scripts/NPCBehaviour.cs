using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class NPCBehaviour : MonoBehaviour
{
    NavMeshAgent myAgent; // Reference to the NavMeshAgent component

    Transform targetTransform;   // The target to chase
    Transform[] patrolPoints;    // Patrol points for patrolling
    [SerializeField] float idleTime = 1f;         // Time to idle before patrolling

    public string currentState;                   // Current state of the agent
    int currentPatrolIndex = 0;                   // Index of the current patrol point
    bool playerInSight = false;                   // Is the player detected?

    void Awake()
    {
        myAgent = GetComponent<NavMeshAgent>();

        // If patrolPoints wasn't set manually, grab all siblings tagged "PatrolPoint"
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            var pts = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in transform.parent)  // iterate empty parent’s children
            {
                if (t.CompareTag("PatrolPoint"))
                    pts.Add(t);
            }
            patrolPoints = pts.ToArray();
        }
    }

    void Start()
    {
        StartCoroutine(SwitchState("Idle"));      // Start in Idle state
    }

    // Switches the agent's state and starts the corresponding coroutine
    IEnumerator SwitchState(string newState)
    {
        if (currentState == newState)
            yield break;                          // Do nothing if already in this state

        currentState = newState;                  // Update state
        StartCoroutine(currentState);             // Start the new state's coroutine
    }

    // Idle state: waits for a set time or until player is detected
    IEnumerator Idle()
    {
        float timer = 0f;                         // Timer for idle duration

        while (currentState == "Idle")
        {
            // If player is detected, switch to ChaseTarget state
            if (playerInSight && targetTransform != null)
            {
                StartCoroutine(SwitchState("ChaseTarget"));
                yield break;
            }

            timer += Time.deltaTime;              // Increment timer
            if (timer >= idleTime)                // If idle time is up, start patrolling
            {
                StartCoroutine(SwitchState("Patrol"));
                yield break;
            }

            yield return null;                    // Wait for next frame
        }
    }

    // Patrol state: moves between patrol points
    IEnumerator Patrol()
    {
        while (currentState == "Patrol")
        {
            // If player is detected, switch to ChaseTarget state
            if (playerInSight && targetTransform != null)
            {
                StartCoroutine(SwitchState("ChaseTarget"));
                yield break;
            }

            // If no patrol points, go back to Idle
            if (patrolPoints.Length == 0)
            {
                StartCoroutine(SwitchState("Idle"));
                yield break;
            }

            Transform currentTarget = patrolPoints[currentPatrolIndex]; // Get current patrol point
            Vector3 worldTarget = currentTarget.position;
            myAgent.SetDestination(worldTarget);             // Move to patrol point

            // Wait until agent reaches the patrol point
            while (Vector3.Distance(transform.position, worldTarget) > 1f)
            {
                // If player is detected during patrol, chase player
                if (playerInSight && targetTransform != null)
                {
                    StartCoroutine(SwitchState("ChaseTarget"));
                    yield break;
                }

                yield return null;                // Wait for next frame
            }

            // Move to next patrol point, loop if at end
            if (currentPatrolIndex < patrolPoints.Length - 1)
            {
                currentPatrolIndex++;
            }
            else
            {
                currentPatrolIndex = 0;           // Loop back to first patrol point
            }

            // After reaching patrol point, go idle
            StartCoroutine(SwitchState("Idle"));
            yield break;
        }
    }

    // ChaseTarget state: chases the player while in sight
    IEnumerator ChaseTarget()
    {
        while (currentState == "ChaseTarget")
        {
            // If player is lost, go back to Idle
            if (!playerInSight || targetTransform == null)
            {
                StartCoroutine(SwitchState("Idle"));
                yield break;
            }

            myAgent.SetDestination(targetTransform.position); // Chase the player
            yield return null;                                // Wait for next frame
        }
    }

    // Detects when the player enters the trigger collider
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            targetTransform = other.transform;    // Set player as target
            playerInSight = true;                 // Mark player as detected
        }
    }

    // Detects when the player exits the trigger collider
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInSight = false;                // Mark player as not detected
            targetTransform = null;               // Clear target
        }
    }
}