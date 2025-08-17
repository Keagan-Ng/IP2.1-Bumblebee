using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ShopkeeperInteractZone : MonoBehaviour
{
    public GameUIManager ui;
    public string playerTag = "Cowboy";

    void Awake()
    {
        if (!ui) ui = FindFirstObjectByType<GameUIManager>(FindObjectsInactive.Include);
        if (!ui) Debug.LogError("[ShopkeeperInteractZone] Could not find GameUIManager in scene.");

        // Ensure trigger + kinematic rigidbody so OnTrigger works reliably
        var c = GetComponent<Collider>();
        c.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!ui) return;
        if (other.CompareTag(playerTag))
        {
            ui.OnShopZoneEnter();
            // debug
            // Debug.Log("[ShopkeeperInteractZone] Player ENTER zone");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!ui) return;
        if (other.CompareTag(playerTag))
        {
            ui.OnShopZoneExit();
            // Debug.Log("[ShopkeeperInteractZone] Player EXIT zone");
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.35f);
        var c = GetComponent<Collider>();
        if (c is BoxCollider bc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(bc.center, bc.size);
        }
        else if (c is SphereCollider sc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawSphere(sc.center, sc.radius);
        }
        else if (c is CapsuleCollider cc)
        {
            // rough gizmo for capsule
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(cc.center + Vector3.up * (cc.height*0.5f - cc.radius), cc.radius);
            Gizmos.DrawWireSphere(cc.center - Vector3.up * (cc.height*0.5f - cc.radius), cc.radius);
        }
    }
#endif
}