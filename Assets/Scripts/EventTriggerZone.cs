// Attach to triggerzone
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EventTriggerZone : MonoBehaviour
{
    [Header("Find Player By Tag")]
    [SerializeField] private string playerTag = "Player";

    [Header("Event Data")]
    [SerializeField] private string eventId;           // leave empty to use this GameObject's name
    [SerializeField] private string scenarioTitle;
    [SerializeField] private bool   overrideQuestion = false;
    [SerializeField, TextArea] private string customQuestion = "What should you do?";
    [Space(4)]
    [SerializeField] private string choiceA = "Choice A";
    [SerializeField] private string choiceB = "Choice B";
    [SerializeField, Tooltip("Tick if A is the correct answer. Untick if B is correct.")]
    private bool correctIsA = true;

    [Header("Usage")]
    [SerializeField] private bool oneShot = true;
    [SerializeField, Min(0f)] private float rearmDelay = 0f;

    private Collider col;
    private bool armed = true;
    private GameUIManager ui;

    void Reset()
    {
        col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void Awake()
    {
        col = GetComponent<Collider>();
        if (col && !col.isTrigger) col.isTrigger = true;

        // Find the UI manager once
        ui = Object.FindFirstObjectByType<GameUIManager>(FindObjectsInactive.Include);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!armed || ui == null || ui.IsEventBusy) return;
        if (!other.CompareTag(playerTag)) return;

        string q = overrideQuestion ? customQuestion : "What should you do?";
        string id = string.IsNullOrWhiteSpace(eventId) ? name : eventId;
        int correctIndex = correctIsA ? 0 : 1;

        ui.BeginEvent(id, scenarioTitle, q, choiceA, choiceB, correctIndex);

        if (oneShot)
        {
            armed = false;
            if (col) col.enabled = false;
        }
        else if (rearmDelay > 0f)
        {
            armed = false;
            Invoke(nameof(ReArm), rearmDelay);
        }
    }

    void ReArm()
    {
        armed = true;
        if (col) col.enabled = true;
    }
}