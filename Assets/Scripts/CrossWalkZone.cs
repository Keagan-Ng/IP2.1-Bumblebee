using System.Collections.Generic;
using UnityEngine;

public enum CrosswalkType { Signalised, Zebra }

public class CrosswalkZone : MonoBehaviour
{
    [Header("Type")]
    public CrosswalkType type = CrosswalkType.Zebra;

    [Header("Signalised (assign a TrafficLightLeg here)")]
    [SerializeField] MonoBehaviour trafficLightSource; // must implement IVehicleSignalSource
    IVehicleSignalSource signalProvider;               // cached cast

    [Header("Zebra settings")]
    [Tooltip("Peds may start when the nearest vehicle is farther than this (units).")]
    public float zebraMinCarDistance = 3f;

    [Header("Debug")]
    public bool someoneIsCrossing = false; // set by pedestrians when they start/finish

    void Awake()
    {
        if (trafficLightSource != null)
        {
            signalProvider = trafficLightSource as IVehicleSignalSource;
            if (signalProvider == null)
                Debug.LogWarning($"{name}: trafficLightSource does not implement IVehicleSignalSource.");
        }
    }

    // --- API used by NPCs ---
    public bool CanPedestrianStartCrossing()
    {
        if (type == CrosswalkType.Signalised)
            return signalProvider != null && signalProvider.IsRedForVehicles(); // vehicles red => peds walk
        else
            return someoneIsCrossing || NearestVehicleDistance() > zebraMinCarDistance;
    }

    public bool IsRedForVehicles()
    {
        if (type != CrosswalkType.Signalised) return false;
        return signalProvider != null && signalProvider.IsRedForVehicles();
    }


    public void NotifyPedestrianStart() => someoneIsCrossing = true;
    public void NotifyPedestrianEnd()   => someoneIsCrossing = false;

    // --- Helpers ---
    public float NearestVehicleDistance()
    {
        float best = float.PositiveInfinity;
        var vehicles = VehicleBehaviour.All;
        for (int i = 0; i < vehicles.Count; i++)
        {
            var v = vehicles[i];
            if (!v) continue;
            float d = Vector3.Distance(transform.position, v.transform.position);
            if (d < best) best = d;
        }
        return best;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = (type == CrosswalkType.Zebra) ? new Color(0f,1f,0f,0.25f) : new Color(1f,0.8f,0f,0.25f);
        if (type == CrosswalkType.Zebra)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, zebraMinCarDistance);
        }
    }

    // (optional) global registry for vehicles to auto-detect crossings
    public static readonly List<CrosswalkZone> All = new List<CrosswalkZone>();
    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }
}
