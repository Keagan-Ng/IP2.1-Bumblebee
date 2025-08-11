using System.Collections.Generic;
using UnityEngine;

public enum CrosswalkType { Signalised, Zebra }

/// Place this on a GameObject centered on the crossing (ideally with a thin trigger covering the zebra).
/// For signalised crossings, assign a TrafficLightController-like source (any script exposing IsRedForVehicles()).
/// For zebra crossings, we use VehicleBehaviour.All to check nearest car distance.
public class CrosswalkZone : MonoBehaviour
{
    [Header("Type")]
    public CrosswalkType type = CrosswalkType.Zebra;

    [Header("Signalised (optional)")]
    public MonoBehaviour trafficLightSource; // any component that has a bool IsRedForVehicles() method

    [Header("Zebra settings")]
    [Tooltip("Pedestrians may start crossing when the nearest vehicle is farther than this (world units).")]
    public float zebraMinCarDistance = 3f;

    [Header("Debug")]
    public bool someoneIsCrossing = false; // set by pedestrians when they start/finish

    // --- API used by NPCs ---
    public bool CanPedestrianStartCrossing()
    {
        if (type == CrosswalkType.Signalised)
            return IsRedForVehicles(); // red for vehicles = walk for peds
        else
            return someoneIsCrossing || NearestVehicleDistance() > zebraMinCarDistance;
    }

    public void NotifyPedestrianStart() => someoneIsCrossing = true;
    public void NotifyPedestrianEnd()   => someoneIsCrossing = false;

    // --- Helpers ---
    public bool IsRedForVehicles()
    {
        if (trafficLightSource == null) return false;
        // duck-typed call: look for a method named "IsRedForVehicles" returning bool
        var m = trafficLightSource.GetType().GetMethod("IsRedForVehicles", System.Type.EmptyTypes);
        if (m == null || m.ReturnType != typeof(bool)) return false;
        return (bool)m.Invoke(trafficLightSource, null);
    }

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
        Gizmos.DrawWireCube(transform.position, new Vector3(12.62f, 0.05f, 3.36f));
        if (type == CrosswalkType.Zebra)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, zebraMinCarDistance);
        }
    }
}