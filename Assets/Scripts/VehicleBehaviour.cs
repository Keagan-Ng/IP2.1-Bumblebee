using System.Collections.Generic;
using UnityEngine;

/// Super-light vehicle logic focused on yielding to pedestrians / red light.
/// Plug this into your car movement later; for now it just controls a desiredSpeed.
[RequireComponent(typeof(Collider))]
public class VehicleBehaviour : MonoBehaviour
{
    // Global registry so CrosswalkZone can check distances cheaply.
    public static readonly List<VehicleBehaviour> All = new List<VehicleBehaviour>();

    [Header("Basic movement (stub)")]
    public float cruiseSpeed = 6f;     // units/sec
    public float brakingDecel = 8f;    // units/sec^2
    public float stopLookahead = 8f;   // check crossings within this distance ahead

    [Header("What crossings does this car care about?")]
    public List<CrosswalkZone> observedCrossings = new List<CrosswalkZone>();

    float _currentSpeed;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        _currentSpeed = cruiseSpeed;
    }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        // Decide if we must yield to any observed crossing ahead
        bool mustStop = false;
        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward;

        for (int i = 0; i < observedCrossings.Count; i++)
        {
            var cz = observedCrossings[i];
            if (!cz) continue;

            // is it roughly ahead and close?
            Vector3 to = cz.transform.position - pos; to.y = 0f;
            float dist = to.magnitude;
            if (dist > stopLookahead) continue;
            if (Vector3.Dot(fwd, to.normalized) < 0.2f) continue; // not in front

            // Yield if signal says red for vehicles, or a ped is currently crossing
            if (cz.IsRedForVehicles() || cz.someoneIsCrossing)
            {
                mustStop = true;
                break;
            }
        }

        // Simple speed control (replace with your path/spline physics later)
        float target = mustStop ? 0f : cruiseSpeed;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, target, brakingDecel * Time.deltaTime);

        // Move forward (placeholder). Replace with waypoint/spline controller.
        transform.position += transform.forward * (_currentSpeed * Time.deltaTime);
    }
}