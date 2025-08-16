// VehicleStopZone.cs (Unity 6)
// Attach to a GameObject with a BoxCollider covering the approach area BEFORE the stop line.
// Ensures a kinematic Rigidbody exists so triggers work even if cars have no RB.

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class VehicleStopZone : MonoBehaviour
{
    [Header("Signals")]
    [Tooltip("Component that implements IVehicleSignalSource (e.g., TrafficLightLeg).")]
    [SerializeField] private MonoBehaviour trafficLightSource; // must implement IVehicleSignalSource
    private IVehicleSignalSource signal;

    [Tooltip("Optional: also stop while someone is crossing this crosswalk.")]
    [SerializeField] private CrosswalkZone crosswalk;

    [Header("Stop Line")]
    [Tooltip("Exact point on the stop line for this lane. If null, uses this object's position.")]
    public Transform stopPoint;
    [Tooltip("Meters to back the bumper from the stop point.")]
    public float stopBackOffset = 1.0f;

    [Header("Setup")]
    [Tooltip("Auto-add a kinematic Rigidbody so triggers always fire.")]
    public bool ensureKinematicRB = true;

    void Reset()
    {
        // Make collider a trigger; add RB
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
        EnsureRB();
    }

    void Awake()
    {
        if (trafficLightSource != null)
        {
            signal = trafficLightSource as IVehicleSignalSource;
            if (signal == null)
                Debug.LogWarning($"{name}: Assigned trafficLightSource does not implement IVehicleSignalSource.");
        }

        var col = GetComponent<Collider>();
        if (col && !col.isTrigger) col.isTrigger = true;

        EnsureRB();
    }

    void EnsureRB()
    {
        if (!ensureKinematicRB) return;
        var rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    bool ShouldStop()
    {
        bool red = (signal != null) && signal.IsRedForVehicles();
        bool ped = crosswalk && crosswalk.someoneIsCrossing;
        return red || ped;
    }

    void OnTriggerStay(Collider other)
    {
        var car = other.GetComponentInParent<VehicleBehaviour>();
        if (!car) return;

        if (ShouldStop())
        {
            Vector3 line = stopPoint ? stopPoint.position : transform.position;
            // place the target slightly behind the line along the car's forward
            Vector3 target = line - car.transform.forward.normalized * stopBackOffset;

            car.shouldStop = true;
            car.stopTarget = target;
        }
        else
        {
            car.shouldStop = false;
        }
    }

    void OnTriggerExit(Collider other)
    {
        var car = other.GetComponentInParent<VehicleBehaviour>();
        if (!car) return;
        car.shouldStop = false;
    }
}