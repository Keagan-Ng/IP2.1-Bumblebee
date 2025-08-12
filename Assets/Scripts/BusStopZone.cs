using System.Collections.Generic;
using UnityEngine;

public class BusStopZone : MonoBehaviour
{
    public static readonly List<BusStopZone> All = new();
    public float triggerRadius = 6f;

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f,0.5f,1f,0.35f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}