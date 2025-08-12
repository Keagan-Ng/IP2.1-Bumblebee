using System.Collections.Generic;
using UnityEngine;

public class VehicleSpawner : MonoBehaviour
{
    [System.Serializable]
    public class VehicleEntry
    {
        public GameObject prefab;
        [Range(0.01f, 10f)] public float weight = 1f;
        public bool isSBS = false;
    }

    [System.Serializable]
    public class Lane
    {
        public Transform spawn;      // oriented with traffic
        public LanePath path;        // path this lane follows
        public int startIndex = 0;   // starting point index on path
        public float startOffset = 0f; // metres after startIndex
    }

    [Header("Lanes")]
    public Lane[] lanes;

    [Header("Vehicles")]
    public List<VehicleEntry> vehicles;

    [Header("Rates & caps")]
    public int   maxAlive = 40;
    public float globalSpawnInterval = 1.2f;
    public float perLaneCooldown     = 0.6f;
    public float spawnMinSpacing     = 2.5f;

    [Header("Culling / layers")]
    public float despawnDistanceFromOrigin = 400f;
    public LayerMask vehicleLayer;

    float timer;
    readonly List<GameObject> alive = new();
    readonly Dictionary<Transform, float> laneNextTime = new();

    // Weighted table
    float[] cumulative;
    float totalW;

    void Awake()
    {
        BuildTable();
        if (lanes != null)
            for (int i = 0; i < lanes.Length; i++)
                if (lanes[i].spawn) laneNextTime[lanes[i].spawn] = 0f;
    }

    void BuildTable()
    {
        totalW = 0f;
        cumulative = new float[vehicles.Count];
        for (int i = 0; i < vehicles.Count; i++)
        {
            totalW += Mathf.Max(0.001f, vehicles[i].weight);
            cumulative[i] = totalW;
        }
    }

    void Update()
    {
        // Cull far vehicles
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            var obj = alive[i];
            if (!obj) { alive.RemoveAt(i); continue; }
            if ((obj.transform.position - Vector3.zero).sqrMagnitude >
                despawnDistanceFromOrigin * despawnDistanceFromOrigin)
            {
                Destroy(obj);
                alive.RemoveAt(i);
            }
        }

        if (alive.Count >= maxAlive || lanes == null || lanes.Length == 0 || vehicles.Count == 0) return;

        timer += Time.deltaTime;
        if (timer < globalSpawnInterval) return;
        timer = 0f;

        // pick a random lane that's off cooldown
        var lane = lanes[Random.Range(0, lanes.Length)];
        if (!lane.spawn || !lane.path) return;
        if (Time.time < laneNextTime[lane.spawn]) return;

        // spacing at spawn
        if (Physics.CheckSphere(lane.spawn.position, spawnMinSpacing, vehicleLayer, QueryTriggerInteraction.Ignore))
            return;

        // weighted vehicle pick
        var entry = PickVehicle();
        if (!entry.prefab) return;

        var spawned = Instantiate(entry.prefab, lane.spawn.position, lane.spawn.rotation);
        var vb = spawned.GetComponent<VehicleBehaviour>();
        if (vb)
        {
            vb.isSBS = entry.isSBS;
            vb.SetStartPath(lane.path, lane.startIndex, lane.startOffset);
            vb.NudgeForward(0.5f); // avoid bumper stack
        }

        alive.Add(spawned);
        laneNextTime[lane.spawn] = Time.time + perLaneCooldown;
    }

    VehicleEntry PickVehicle()
    {
        if (cumulative == null || cumulative.Length != vehicles.Count) BuildTable();
        float r = Random.Range(0f, totalW);
        int lo = 0, hi = cumulative.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (r <= cumulative[mid]) hi = mid;
            else lo = mid + 1;
        }
        return vehicles[Mathf.Clamp(lo, 0, vehicles.Count - 1)];
    }

    void OnDrawGizmosSelected()
    {
        if (lanes == null) return;
        Gizmos.color = Color.cyan;
        foreach (var L in lanes) if (L != null && L.spawn) Gizmos.DrawRay(L.spawn.position, L.spawn.forward * 3f);
    }
}