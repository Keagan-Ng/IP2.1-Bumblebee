using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NPCSpawner : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public Camera   gameplayCamera;
    public GameObject npcPrefab; // has NPCRandomizer etc.

    [Header("Spawn timing & cap")]
    public float spawnInterval = 2.5f;
    public int   maxAlive      = 60;
    public int   attemptsPerTick = 3;

    [Header("Distances (m)")]
    public float innerRadius   = 30f;
    public float outerRadius   = 55f;
    public float despawnRadius = 70f;
    public float minSpacing    = 1.3f;

    [Header("Local density cap near spawn")]
    public float localDensityRadius = 9f;
    public int   maxLocalNPCs       = 6;

    [Header("Layers / NavMesh")]
    public LayerMask npcLayer;
    public bool      requireNavMesh = true;
    public float     navMeshProbeRadius = 1.0f;

    readonly List<Transform> alive = new List<Transform>();
    float timer;

    void Update()
    {
        if (!player || !gameplayCamera || !npcPrefab) return;

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            if (alive.Count < maxAlive)
                for (int i = 0; i < attemptsPerTick && alive.Count < maxAlive; i++)
                    if (TrySpawnOnce()) break;
        }

        // simple despawn sweep
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            var t = alive[i];
            if (!t) { alive.RemoveAt(i); continue; }

            float d2 = (t.position - player.position).sqrMagnitude;
            if (d2 > despawnRadius * despawnRadius && !IsOnScreen(t.position))
            {
                Destroy(t.gameObject);
                alive.RemoveAt(i);
            }
        }
    }

    bool TrySpawnOnce()
    {
        Vector3 p = player.position;
        float angle = Random.value * Mathf.PI * 2f;
        float rIn2 = innerRadius * innerRadius, rOut2 = outerRadius * outerRadius;
        float radius = Mathf.Sqrt(Mathf.Lerp(rIn2, rOut2, Random.value));
        Vector3 candidate = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

        if (IsOnScreen(candidate)) return false;

        // project to ground
        if (!Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Collide))
            return false;

        Vector3 spawnPoint = hit.point + Vector3.up * 0.05f;

        if (requireNavMesh)
        {
            if (!NavMesh.SamplePosition(spawnPoint, out NavMeshHit navHit, navMeshProbeRadius, NavMesh.AllAreas))
                return false;
            spawnPoint = navHit.position;
        }

        // min spacing
        if (Physics.CheckSphere(spawnPoint, minSpacing, npcLayer, QueryTriggerInteraction.Ignore))
            return false;

        // local density cap
        int nearby = 0;
        float r2 = localDensityRadius * localDensityRadius;
        for (int i = 0; i < alive.Count; i++)
        {
            var t = alive[i];
            if (!t) continue;
            if ((t.position - spawnPoint).sqrMagnitude <= r2)
            {
                nearby++;
                if (nearby >= maxLocalNPCs) return false;
            }
        }

        var go = Instantiate(npcPrefab, spawnPoint, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        alive.Add(go.transform);
        return true;
    }

    bool IsOnScreen(Vector3 worldPos)
    {
        var v = gameplayCamera.WorldToViewportPoint(worldPos);
        return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
    }

    void OnDrawGizmosSelected()
    {
        if (!player) return;
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f); Gizmos.DrawWireSphere(player.position, innerRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f); Gizmos.DrawWireSphere(player.position, outerRadius);
        Gizmos.color = new Color(1f, 0f, 0f, 0.15f); Gizmos.DrawWireSphere(player.position, despawnRadius);
    }
}