using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NPCSpawner : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public Camera gameplayCamera;
    public GameObject npcPrefab;         // has NPCRandomizer (randomizeOnEnable = true)

    [Header("Spawn timing & cap")]
    public float spawnInterval = 2.5f;
    public int   maxAlive      = 60;
    public int   attemptsPerTick = 3;

    [Header("Distances (meters)")]
    public float innerRadius   = 30f;    // don’t pop in too close
    public float outerRadius   = 55f;    // where spawns are allowed
    public float despawnRadius = 70f;    // remove when beyond this AND off-screen
    public float minSpacing    = 1.3f;   // clearance from other NPCs

    [Header("Layers / NavMesh")]
    public LayerMask sidewalkMask;       // set to your Sidewalk layer
    public LayerMask npcLayer;           // set to your NPC layer
    public bool      requireNavMesh = true;
    public float     navMeshProbeRadius = 1.0f;

    readonly List<Transform> alive = new List<Transform>();
    float timer;

    void Update()
    {
        if (!player || !gameplayCamera || !npcPrefab) return;

        // Try spawns on interval
        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            if (alive.Count < maxAlive)
            {
                for (int i = 0; i < attemptsPerTick && alive.Count < maxAlive; i++)
                {
                    if (TrySpawnOnce()) break; // stop after first success
                }
            }
        }

        // Despawn sweep (simple & cheap for <=60)
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            var t = alive[i];
            if (!t)
            {
                alive.RemoveAt(i);
                continue;
            }

            float sqrDist = (t.position - player.position).sqrMagnitude;
            if (sqrDist > despawnRadius * despawnRadius && !IsOnScreen(t.position))
            {
                Destroy(t.gameObject);
                alive.RemoveAt(i);
            }
        }
    }

    bool TrySpawnOnce()
    {
        // 1) pick a random point in the donut
        Vector3 pPos = player.position;
        float angle = Random.value * Mathf.PI * 2f;
        // uniform area in ring
        float rIn2 = innerRadius * innerRadius;
        float rOut2 = outerRadius * outerRadius;
        float radius = Mathf.Sqrt(Mathf.Lerp(rIn2, rOut2, Random.value));

        Vector3 candidate = pPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

        // 2) must be off-screen
        if (IsOnScreen(candidate)) return false;

        // 3) find sidewalk surface by raycasting down
        if (!Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, sidewalkMask, QueryTriggerInteraction.Ignore))
            return false;

        Vector3 spawnPoint = hit.point + Vector3.up * 0.05f; // slight lift to avoid z-fighting

        // 4) (optional) ensure this point is on the baked NavMesh
        if (requireNavMesh)
        {
            if (!NavMesh.SamplePosition(spawnPoint, out NavMeshHit navHit, navMeshProbeRadius, NavMesh.AllAreas))
                return false;
            spawnPoint = navHit.position;
        }

        // 5) min spacing from other NPCs
        if (Physics.CheckSphere(spawnPoint, minSpacing, npcLayer, QueryTriggerInteraction.Ignore))
            return false;

        // 6) instantiate
        var go = Instantiate(npcPrefab, spawnPoint, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        alive.Add(go.transform);
        return true;
    }

    bool IsOnScreen(Vector3 worldPos)
    {
        Vector3 v = gameplayCamera.WorldToViewportPoint(worldPos);
        return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
    }

    void OnDrawGizmosSelected()
    {
        if (!player) return;
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(player.position, innerRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(player.position, outerRadius);
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
        Gizmos.DrawWireSphere(player.position, despawnRadius);
    }
}