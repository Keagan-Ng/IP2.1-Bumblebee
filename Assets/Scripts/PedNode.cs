using UnityEngine;

public enum PedNodeType { MidBlock, Corner, Crossing, BusStop }

public class PedNode : MonoBehaviour
{
    public PedNodeType type = PedNodeType.MidBlock;

    [Tooltip("How far NPCs look for their next node from here.")]
    [Range(4f, 30f)] public float radiusHint = 15f;

    [Tooltip("Max NPCs allowed to target/idle here at once (BusStop only).")]
    [Range(0, 8)] public int capacity = 2;
}