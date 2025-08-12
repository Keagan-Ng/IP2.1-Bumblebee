using UnityEngine;

public enum TLGroup { A, B }          // A = one direction (e.g., N/S), B = perpendicular (E/W)
public enum VehiclePhase { Green, Yellow, Red }

public class TrafficLightController : MonoBehaviour
{
    [Header("Cycle Durations (seconds)")]
    public float greenA  = 12f;
    public float yellowA = 2f;
    public float allRedA = 1f;  // safety all-red after A

    public float greenB  = 12f;
    public float yellowB = 2f;
    public float allRedB = 1f;  // safety all-red after B

    [Header("Sync")]
    [Tooltip("Positive values make this junction start as if it had already been running for this many seconds.")]
    public float startOffsetSeconds = 0f;

    float t0;

    void OnEnable()
    {
        // Pretend we started startOffsetSeconds ago
        t0 = Time.time - Mathf.Max(0f, startOffsetSeconds);
    }

    float TimeInCycle()
    {
        float cycle = greenA + yellowA + allRedA + greenB + yellowB + allRedB;
        if (cycle <= 0.01f) cycle = 1f;
        float u = (Time.time - t0) % cycle;
        if (u < 0f) u += cycle;
        return u;
    }

    public VehiclePhase GetPhase(TLGroup g)
    {
        float u = TimeInCycle();

        // A goes first
        if (u < greenA)                    return (g == TLGroup.A) ? VehiclePhase.Green  : VehiclePhase.Red;
        u -= greenA;
        if (u < yellowA)                   return (g == TLGroup.A) ? VehiclePhase.Yellow : VehiclePhase.Red;
        u -= yellowA;
        if (u < allRedA)                   return VehiclePhase.Red; // all-red both ways
        u -= allRedA;

        // then B
        if (u < greenB)                    return (g == TLGroup.B) ? VehiclePhase.Green  : VehiclePhase.Red;
        u -= greenB;
        if (u < yellowB)                   return (g == TLGroup.B) ? VehiclePhase.Yellow : VehiclePhase.Red;
        // final all-red
        return VehiclePhase.Red;
    }

    // NPCs should only cross when vehicles are strictly RED (not yellow)
    public bool IsVehiclesRed(TLGroup g) => GetPhase(g) == VehiclePhase.Red;
}