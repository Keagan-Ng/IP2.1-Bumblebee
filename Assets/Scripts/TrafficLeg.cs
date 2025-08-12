using UnityEngine;

public class TrafficLightLeg : MonoBehaviour, IVehicleSignalSource
{
    public TrafficLightController controller;
    public TLGroup group = TLGroup.A;

    [Header("Visuals (optional)")]
    public GameObject vehRed;
    public GameObject vehYellow;
    public GameObject vehGreen;

    public bool IsRedForVehicles()
    {
        if (!controller) return false;
        return controller.IsVehiclesRed(group);
    }

    void Update()
    {
        if (!controller) return;

        var phase = controller.GetPhase(group);
        // toggle vehicle lights if assigned
        if (vehRed)    vehRed   .SetActive(phase == VehiclePhase.Red);
        if (vehYellow) vehYellow.SetActive(phase == VehiclePhase.Yellow);
        if (vehGreen)  vehGreen .SetActive(phase == VehiclePhase.Green);
    }
}