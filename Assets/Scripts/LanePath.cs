using System.Collections.Generic;
using UnityEngine;

public class LanePath : MonoBehaviour
{
    [Tooltip("If empty, the path is taken from this object's children in order.")]
    public List<Transform> points = new();

    [Tooltip("Optional next lanes to continue onto (random pick).")]
    public List<LanePath> nextPaths = new();

    [Header("Debug")]
    public bool drawGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    // Expose baked world-space points for followers
    public IReadOnlyList<Vector3> World => _world;
    private readonly List<Vector3> _world = new();

    void Awake()     { Bake(); }
    void OnValidate(){ Bake(); }

    void Bake()
    {
        _world.Clear();

        // Use explicit list if provided
        if (points != null && points.Count >= 2)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i]) _world.Add(points[i].position);
            }
        }
        else
        {
            // Fallback: use children in order
            foreach (Transform c in transform)
                _world.Add(c.position);
        }

        // Ensure at least 2 points to define a segment
        if (_world.Count < 2)
        {
            // Add a tiny forward offset so followers don't crash
            _world.Add(transform.position);
            _world.Add(transform.position + transform.forward * 0.1f);
        }
    }

    public bool TryGetPoint(int i, out Vector3 p)
    {
        if (i >= 0 && i < _world.Count) { p = _world[i]; return true; }
        p = default; return false;
    }

    public LanePath PickNext()
    {
        if (nextPaths == null || nextPaths.Count == 0) return null;
        return nextPaths[Random.Range(0, nextPaths.Count)];
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Bake();
        Gizmos.color = gizmoColor;
        for (int i = 0; i < _world.Count - 1; i++)
            Gizmos.DrawLine(_world[i], _world[i + 1]);
        for (int i = 0; i < _world.Count; i++)
            Gizmos.DrawSphere(_world[i], 0.12f);
    }
}