using UnityEngine;
using UnityEngine.EventSystems;
using TMPro; using UnityEngine.UI;

public class OptionHoverCircle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] Graphic circle; // Image or TMP with Raycast Target OFF
    [SerializeField] bool useCanvasGroup = false;
    CanvasGroup cg;

    void Awake() {
        if (!circle && transform.parent) {
            var t = transform.parent.Find("Circle");
            if (t) circle = t.GetComponent<Graphic>();
        }
        if (useCanvasGroup) { cg = circle ? circle.GetComponent<CanvasGroup>() : null;
            if (!cg && circle) cg = circle.gameObject.AddComponent<CanvasGroup>();
            if (cg) cg.alpha = 0f;
        } else if (circle) circle.enabled = false;
    }

    public void OnPointerEnter(PointerEventData _) {
        if (useCanvasGroup && cg) cg.alpha = 1f; else if (circle) circle.enabled = true;
    }
    public void OnPointerExit(PointerEventData _) {
        if (useCanvasGroup && cg) cg.alpha = 0f; else if (circle) circle.enabled = false;
    }
}