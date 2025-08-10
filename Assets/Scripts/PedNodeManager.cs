using System.Collections.Generic;
using UnityEngine;

public class PedNodeManager : MonoBehaviour
{
    public static PedNodeManager Instance { get; private set; }
    public List<PedNode> Nodes { get; private set; } = new List<PedNode>();

    [Header("Filters")]
    [Tooltip("Set this to your Road layer so we can linecast and reject non-crossing targets across the road.")]
    public LayerMask roadLayerMask;

    // live target counts per node (capacity / de-bunching)
    readonly Dictionary<PedNode, int> _claims = new Dictionary<PedNode, int>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Nodes.Clear();
        Nodes.AddRange(FindObjectsByType<PedNode>(FindObjectsInactive.Include, FindObjectsSortMode.None));
    }

    // ---------- Basics ----------
    public PedNode Nearest(Vector3 pos, float maxDist = 6f)
    {
        PedNode best = null;
        float bestSqr = maxDist * maxDist;
        for (int i = 0; i < Nodes.Count; i++)
        {
            var d = Nodes[i].transform.position - pos;
            float s = d.sqrMagnitude;
            if (s < bestSqr) { bestSqr = s; best = Nodes[i]; }
        }
        return best;
    }

    // ---------- Claims (bus stop capacity & spread) ----------
    public int GetClaimCount(PedNode n) => n && _claims.TryGetValue(n, out var c) ? c : 0;

    /// Try to claim a node. Returns true and slotIndex (0..cap-1) on success.
    public bool TryClaim(PedNode n, out int slotIndex)
    {
        slotIndex = 0;
        if (!n) return false;

        int cap = (n.type == PedNodeType.BusStop) ? Mathf.Max(0, n.capacity) : int.MaxValue;
        _claims.TryGetValue(n, out var c);
        if (c >= cap) return false;

        slotIndex = c;              // queue index
        _claims[n] = c + 1;
        return true;
    }

    public void Release(PedNode n)
    {
        if (!n) return;
        if (_claims.TryGetValue(n, out var c))
        {
            c = Mathf.Max(0, c - 1);
            if (c == 0) _claims.Remove(n);
            else _claims[n] = c;
        }
    }

    // ---------- Candidate builder ----------
    /// Nodes within radius, forward-cone filtered, excluding 'last'. Sorted by fewest current claims first.
    public List<PedNode> Candidates(Vector3 pos, Vector3 forward, float radius, PedNode last, float minDot = 0f)
    {
        var list = new List<PedNode>(8);
        float r2 = radius * radius;
        var f = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i];
            if (n == last) continue;

            Vector3 to = n.transform.position - pos; to.y = 0f;
            float s = to.sqrMagnitude;
            if (s < 0.01f || s > r2) continue;

            if (Vector3.Dot(f, to.normalized) < minDot) continue;

            list.Add(n);
        }

        list.Sort((a, b) => GetClaimCount(a).CompareTo(GetClaimCount(b)));
        return list;
    }

    // ---------- Crossing helpers / global lock ----------
    public static bool IsCrossing(PedNode n) => n && n.type == PedNodeType.Crossing;

    /// True if a and b are opposite sides of the SAME crosswalk (share parent).
    public static bool AreOppositeCrossingSides(PedNode a, PedNode b)
    {
        if (!IsCrossing(a) || !IsCrossing(b)) return false;
        var pa = a.transform.parent; var pb = b.transform.parent;
        return pa && pb && pa == pb && a != b;
    }

    /// Remove all Crossing nodes if a global lock is active.
    public List<PedNode> ApplyGlobalCrossingLock(List<PedNode> src, float now, float lockUntil)
    {
        if (src == null) return null;
        if (now >= lockUntil) return src; // not locked
        var outList = new List<PedNode>(src.Count);
        for (int i = 0; i < src.Count; i++)
            if (!IsCrossing(src[i])) outList.Add(src[i]);
        return outList;
    }

    /// Maintain per-NPC crossing history & maybe start a lock.
    /// Pass in a rolling list of timestamps (seconds); prunes to windowSec and sets lockUntil when count > crossLimit.
    public void UpdateGlobalCrossingHistory(List<float> recentCrossTimes, ref float lockUntil,
                                            int crossLimit, float windowSec, float cooldownSec, float now)
    {
        if (recentCrossTimes == null) return;

        // prune old
        for (int i = recentCrossTimes.Count - 1; i >= 0; i--)
            if (now - recentCrossTimes[i] > windowSec) recentCrossTimes.RemoveAt(i);

        // record now
        recentCrossTimes.Add(now);

        // check
        if (recentCrossTimes.Count > crossLimit)
            lockUntil = Mathf.Max(lockUntil, now + cooldownSec);
    }

    /// Keep only the NEAREST curb per crossing group (parent) from a given position.
    public List<PedNode> FilterCrossingsToNearestSide(List<PedNode> src, Vector3 fromPos)
    {
        if (src == null || src.Count == 0) return src;

        var outList = new List<PedNode>(src.Count);
        var nearestByGroup = new Dictionary<Transform, PedNode>();
        var bestDist2ByGroup = new Dictionary<Transform, float>();

        // choose nearest curb per crosswalk parent
        for (int i = 0; i < src.Count; i++)
        {
            var n = src[i];
            if (!IsCrossing(n)) continue;

            var grp = n.transform.parent; if (!grp) continue;
            float d2 = (n.transform.position - fromPos).sqrMagnitude;
            if (!nearestByGroup.TryGetValue(grp, out var cur) || d2 < bestDist2ByGroup[grp])
            {
                nearestByGroup[grp] = n;
                bestDist2ByGroup[grp] = d2;
            }
        }

        // keep non-crossings + only nearest side for crossings
        for (int i = 0; i < src.Count; i++)
        {
            var n = src[i];
            if (!IsCrossing(n)) { outList.Add(n); continue; }

            var grp = n.transform.parent;
            if (grp && nearestByGroup.TryGetValue(grp, out var keep) && keep == n)
                outList.Add(n);
        }

        return outList;
    }

    /// Reject non-crossing candidates that lie straight across a road from 'fromPos'.
    /// Forces Corner/MidBlock -> near Crossing workflow instead of corner-to-corner across the carriageway.
    public List<PedNode> FilterThatWouldCrossRoad(List<PedNode> src, Vector3 fromPos)
    {
        if (src == null || src.Count == 0) return src;
        if (roadLayerMask.value == 0) return src; // not configured, skip

        var outList = new List<PedNode>(src.Count);
        Vector3 start = fromPos + Vector3.up * 0.25f;

        for (int i = 0; i < src.Count; i++)
        {
            var n = src[i];

            // Allow aiming at Crossing nodes; near-side filtering handled elsewhere.
            if (IsCrossing(n)) { outList.Add(n); continue; }

            Vector3 end = n.transform.position + Vector3.up * 0.25f;
            bool crossesRoad = Physics.Linecast(start, end, roadLayerMask, QueryTriggerInteraction.Ignore);

            if (!crossesRoad) outList.Add(n);
            // else: drop it — would cut across a road without using a crossing
        }

        return outList;
    }
}