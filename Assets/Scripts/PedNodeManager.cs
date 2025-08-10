using System.Collections.Generic;
using UnityEngine;

public class PedNodeManager : MonoBehaviour
{
    public static PedNodeManager Instance { get; private set; }
    public List<PedNode> Nodes { get; private set; } = new List<PedNode>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Grab all PedNodes in the scene (active & inactive)
            Nodes.AddRange(FindObjectsByType<PedNode>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public PedNode Nearest(Vector3 pos, float maxDist = 6f)
    {
        PedNode best = null;
        float bestSqr = maxDist * maxDist;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var d = Nodes[i].transform.position - pos;
            float s = d.sqrMagnitude;
            if (s < bestSqr)
            {
                bestSqr = s;
                best = Nodes[i];
            }
        }
        return best;
    }

    /// <summary>
    /// Nodes within radius, forward-cone filtered, excluding 'last'.
    /// </summary>
    public List<PedNode> Candidates(Vector3 pos, Vector3 forward, float radius, PedNode last, float minDot = 0f)
    {
        var outList = new List<PedNode>(8);
        float r2 = radius * radius;

        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n == last) continue;

            Vector3 to = n.transform.position - pos;
            to.y = 0f;
            float s = to.sqrMagnitude;
            if (s < 0.01f || s > r2) continue;

            Vector3 dir = to.normalized;
            if (forward.sqrMagnitude > 0.0001f && Vector3.Dot(forward.normalized, dir) < minDot) continue;

            outList.Add(n);
        }

        return outList;
    }
}
