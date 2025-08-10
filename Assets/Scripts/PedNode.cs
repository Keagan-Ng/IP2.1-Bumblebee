using UnityEngine;

public enum PedNodeType { MidBlock, Corner, Crossing, BusStop }

public class PedNode : MonoBehaviour
{
    public PedNodeType type = PedNodeType.MidBlock;
    [Range(4f,30f)] public float radiusHint = 15f; // how far to look for next node
}