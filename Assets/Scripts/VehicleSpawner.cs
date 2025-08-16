using System.Collections.Generic;
using UnityEngine;

public class VehicleSpawner : MonoBehaviour
{
    [Header("Prefabs & Lanes")]
    public List<GameObject> carPrefabs = new List<GameObject>(); // add your 10 prefabs
    public List<LanePath> lanes = new List<LanePath>();          // add your 5 lanes

    [Header("Spawn Rules")]
    [Min(0.1f)] public float spawnInterval = 2f;
    public float minStartGap = 8f;

    [Header("Limits")]
    public int maxAlive = 100;

    // internal
    readonly List<VehicleBehaviour> alive = new List<VehicleBehaviour>();
    readonly List<List<VehicleBehaviour>> perLane = new List<List<VehicleBehaviour>>();
    int nextLaneIndex = 0;
    float timer;

    void Awake()
    {
        perLane.Clear();
        for (int i = 0; i < lanes.Count; i++)
            perLane.Add(new List<VehicleBehaviour>());
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < spawnInterval) return;
        timer = 0f;

        TrySpawnRoundRobin();
        CleanupNulls();
    }

    void TrySpawnRoundRobin()
    {
        if (alive.Count >= maxAlive) return;
        if (lanes.Count == 0 || carPrefabs.Count == 0) return;

        for (int attempt = 0; attempt < lanes.Count; attempt++)
        {
            int laneIdx = (nextLaneIndex + attempt) % lanes.Count;
            var lane = lanes[laneIdx];
            if (lane == null || lane.Count < 2) continue;

            Vector3 start = lane.StartPos;
            Quaternion rot = lane.StartRot;

            if (!IsStartClear(laneIdx, start)) continue;

            var prefab = carPrefabs[Random.Range(0, carPrefabs.Count)];
            var go = Instantiate(prefab, start, rot);

            // ensure behaviour exists
            var vb = go.GetComponent<VehicleBehaviour>();
            if (!vb) vb = go.AddComponent<VehicleBehaviour>();

            // inject references
            vb.path = lane;
            vb.PathId = laneIdx;
            vb.SharedPathCars = alive;
            vb.waypointReachDist = Mathf.Max(vb.waypointReachDist, 0.5f);

            alive.Add(vb);
            perLane[laneIdx].Add(vb);

            nextLaneIndex = (laneIdx + 1) % lanes.Count;
            return; // spawned this tick
        }
        // if all starts blocked this tick → no spawn
    }

    bool IsStartClear(int laneIdx, Vector3 start)
    {
        var list = perLane[laneIdx];
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c == null) continue;

            if (Vector3.Distance(c.transform.position, start) < minStartGap) return false;
            if (c.DistanceAlongPath < 1f) return false; // just spawned
        }
        return true;
    }

    void CleanupNulls()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
            if (alive[i] == null) alive.RemoveAt(i);

        for (int p = 0; p < perLane.Count; p++)
        {
            var list = perLane[p];
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null) list.RemoveAt(i);
        }
    }
}