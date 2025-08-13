using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ImpromptuUIEvents : MonoBehaviour
{
    [Header("Scope")]
    [Tooltip("If set, only scan under this Transform; otherwise scans this GameObject.")]
    public Transform scanRoot;

    [Header("Discovery")]
    [Tooltip("If non-empty, prefer a child Image with this name (e.g. \"Image\" or \"Hover\").")]
    public string hoverChildNameHint = "Image";

    [Header("Options")]
    public bool includeInactive = true;   // find buttons even if their parents are inactive
    public bool alsoOnSelect = true;      // keyboard/gamepad UI navigation

    readonly Dictionary<Button, GameObject> hoverByButton = new();

    void Awake()
    {
        var root = scanRoot ? scanRoot : transform;
        var buttons = root.GetComponentsInChildren<Button>(includeInactive);

        foreach (var btn in buttons)
        {
            var hover = FindHoverChild(btn);
            if (!hover) continue;

            // make sure the hover image never blocks pointer events
            var img = hover.GetComponent<Image>();
            if (img) img.raycastTarget = false;

            hover.SetActive(false);
            hoverByButton[btn] = hover;

            WireEvents(btn);
        }
    }

    void OnDisable()
    {
        foreach (var kv in hoverByButton) if (kv.Value) kv.Value.SetActive(false);
    }

    GameObject FindHoverChild(Button btn)
    {
        var target = btn.targetGraphic ? btn.targetGraphic.gameObject : null;

        // 1) try by name hint under this button
        if (!string.IsNullOrEmpty(hoverChildNameHint))
        {
            foreach (var img in btn.GetComponentsInChildren<Image>(true))
            {
                var go = img.gameObject;
                if (go == btn.gameObject || go == target) continue;
                if (go.name == hoverChildNameHint || go.name.Contains(hoverChildNameHint))
                    return go;
            }
        }

        // 2) fallback: first child Image that's not the targetGraphic
        foreach (var img in btn.GetComponentsInChildren<Image>(true))
        {
            var go = img.gameObject;
            if (go == btn.gameObject || go == target) continue;
            return go;
        }

        return null;
    }

    void WireEvents(Button btn)
    {
        var et = btn.gameObject.GetComponent<EventTrigger>();
        if (!et) et = btn.gameObject.AddComponent<EventTrigger>();

        AddEntry(et, EventTriggerType.PointerEnter, (e) => SetHover(btn, true));
        AddEntry(et, EventTriggerType.PointerExit,  (e) => SetHover(btn, false));

        if (alsoOnSelect)
        {
            AddEntry(et, EventTriggerType.Select,   (e) => SetHover(btn, true));
            AddEntry(et, EventTriggerType.Deselect, (e) => SetHover(btn, false));
        }
    }

    void AddEntry(EventTrigger et, EventTriggerType type, System.Action<BaseEventData> handler)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback = new EventTrigger.TriggerEvent();
        entry.callback.AddListener((data) => handler(data));
        et.triggers.Add(entry);
    }

    void SetHover(Button btn, bool on)
    {
        if (hoverByButton.TryGetValue(btn, out var hover) && hover)
            hover.SetActive(on);
    }
}