using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor; // for labels & arrow caps
#endif

[ExecuteAlways]
public class LanePath : MonoBehaviour
{
    [Tooltip("Ordered waypoints that define this lane. Leave empty to auto-fill from children.")]
    public List<Transform> waypoints = new List<Transform>();

    [Header("Gizmo Settings")]
    public bool drawGizmos = true;
    public bool drawIndices = true;
    public bool drawDirection = true;
    [Tooltip("Approx lane width purely for gizmo viz (not physics).")]
    public float laneWidth = 3.0f;
    [Tooltip("How thick to draw the path ribbon in scene view.")]
    public float ribbonThickness = 0.15f;
    [Tooltip("Draw an arrow roughly every N meters.")]
    public float arrowEveryMeters = 8f;
    public float arrowSize = 0.8f;

    [Header("Gizmo Colors")]
    public Color startColor = new Color(0.2f, 1f, 0.2f, 0.9f);
    public Color nodeColor  = new Color(0.2f, 0.9f, 1f, 0.9f);
    public Color endColor   = new Color(1f, 0.6f, 0.2f, 0.9f);
    public Color ribbonColor = new Color(1f, 0.95f, 0.3f, 0.85f);

    [Header("Node Viz")]
    public float startNodeRadius = 0.6f;
    public float nodeRadius = 0.25f;
    public float endNodeRadius = 0.4f;

    public int Count => waypoints.Count;
    public Transform this[int i] => waypoints[i];

    public Vector3 StartPos => waypoints[0].position;
    public Quaternion StartRot =>
        Count > 1
            ? Quaternion.LookRotation((waypoints[1].position - waypoints[0].position).normalized, Vector3.up)
            : Quaternion.identity;

    void OnValidate()
    {
        if (waypoints == null) waypoints = new List<Transform>();
        if (waypoints.Count == 0)
        {
            foreach (Transform t in transform) waypoints.Add(t);
        }
        laneWidth = Mathf.Max(0.1f, laneWidth);
        ribbonThickness = Mathf.Max(0.02f, ribbonThickness);
        arrowEveryMeters = Mathf.Max(2f, arrowEveryMeters);
        arrowSize = Mathf.Max(0.2f, arrowSize);
        startNodeRadius = Mathf.Max(0.05f, startNodeRadius);
        nodeRadius = Mathf.Max(0.05f, nodeRadius);
        endNodeRadius = Mathf.Max(0.05f, endNodeRadius);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || waypoints == null || waypoints.Count == 0) return;

        // --- Nodes ---
        if (waypoints[0])
        {
            Gizmos.color = startColor;
            Gizmos.DrawSphere(waypoints[0].position, startNodeRadius);
        }

        for (int i = 1; i < waypoints.Count - 1; i++)
        {
            var t = waypoints[i];
            if (!t) continue;
            Gizmos.color = nodeColor;
            Gizmos.DrawSphere(t.position, nodeRadius);
        }

        if (waypoints.Count > 1 && waypoints[^1])
        {
            Gizmos.color = endColor;
            Gizmos.DrawSphere(waypoints[^1].position, endNodeRadius);
        }

        // --- Ribbon (thick line) + arrows ---
        Gizmos.color = ribbonColor;

        float leftoverForArrow = 0f; // keep spacing consistent across segments
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            var aT = waypoints[i];
            var bT = waypoints[i + 1];
            if (!aT || !bT) continue;

            Vector3 a = aT.position;
            Vector3 b = bT.position;
            Vector3 seg = b - a;
            float len = seg.magnitude;
            if (len < 0.001f) continue;

            Vector3 dir = seg / len;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            float halfW = laneWidth * 0.5f;

            // draw a thin box "ribbon" to fake thickness
            // center, rotation, scale
            Vector3 center = (a + b) * 0.5f + Vector3.up * 0.02f;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 scale = new Vector3(laneWidth, ribbonThickness, len);
            Matrix4x4 m = Matrix4x4.TRS(center, rot, scale);
            Gizmos.matrix = m;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;

#if UNITY_EDITOR
            if (drawDirection)
            {
                // Place arrow caps along the segment at a regular spacing
                float step = arrowEveryMeters;
                float t = Mathf.Max(leftoverForArrow, step);
                while (t < len)
                {
                    Vector3 p = a + dir * t + Vector3.up * 0.05f;
                    Handles.color = ribbonColor;
                    Handles.ArrowHandleCap(
                        controlID: 0,
                        position: p,
                        rotation: Quaternion.LookRotation(dir, Vector3.up),
                        size: arrowSize,
                        eventType: EventType.Repaint);
                    t += step;
                }
                // compute leftover to carry onto next segment
                float consumed = Mathf.Floor((len - Mathf.Max(leftoverForArrow, step)) / step) * step;
                leftoverForArrow = (consumed > 0f) ? (step - ((len - Mathf.Max(leftoverForArrow, step)) - consumed)) : (step - (len - Mathf.Max(leftoverForArrow, step)));
                leftoverForArrow = Mathf.Repeat(leftoverForArrow, step);
            }
#endif
        }

#if UNITY_EDITOR
        // --- Indices / labels ---
        if (drawIndices)
        {
            GUIStyle s = new GUIStyle(EditorStyles.boldLabel);
            s.normal.textColor = Color.white;

            for (int i = 0; i < waypoints.Count; i++)
            {
                var t = waypoints[i];
                if (!t) continue;
                Handles.Label(t.position + Vector3.up * 0.25f, i == 0 ? $"0 (Start)" : i == waypoints.Count - 1 ? $"{i} (End)" : i.ToString(), s);
            }
        }
#endif
    }
}