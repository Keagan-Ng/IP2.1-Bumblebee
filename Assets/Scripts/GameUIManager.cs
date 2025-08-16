using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class GameUIManager : MonoBehaviour
{
    // ---------- PROFILE HUD ----------
    [Header("Profile HUD")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private TMP_Text usernameText;

    // ---------- PROGRESS (COUNT-BASED) ----------
    [Header("Progress (Animator-driven, COUNT-based)")]
    [SerializeField] private Animator segmentedBarAnimator;        // stacked bar Animator
    [SerializeField, Min(1)] private int totalEvents = 12;         // you decided 12
    [SerializeField] private string countTriggerFormat = "{0}clear"; // -> "1clear".."12clear"

    [Header("Debug (read-only)")]
    [SerializeField] private int eventsSeen = 0;                       // 0..totalEvents
    [SerializeField, Range(0f,1f)] private float progress01 = 0f;      // preview only

    // ---------- EVENT PANEL ----------
    [Header("Event Panel (UI)")]
    [SerializeField] private GameObject eventPanelRoot;     // whole panel root to toggle
    [SerializeField] private Animator   eventPanelAnimator; // Animator on EventPanel root

    [Header("Event Text & Buttons")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text choiceALabel;
    [SerializeField] private TMP_Text choiceBLabel;
    [SerializeField] private Button   choiceAButton;
    [SerializeField] private Button   choiceBButton;

    [Header("Result Text")]
    [SerializeField] private TMP_Text resultText;           // shown by your Answered animation
    [SerializeField] private string correctText = "Correct!";
    [SerializeField] private string wrongText   = "Wrong!";
    [SerializeField] private bool   colorizeResult = true;
    [SerializeField] private Color  correctColor = new Color(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color  wrongColor   = new Color(1f,   0.4f, 0.4f, 1f);

    [Header("Behavior")]
    [SerializeField] private bool   pauseOnBegin = true;
    [SerializeField] private string defaultQuestion = "What should you do?";

    // ---------- CURSOR / INPUT CONTROL (no action maps) ----------
    [Header("Cursor & Input (Optional)")]
    [SerializeField] private bool manageCursor = true;             // unlock/show cursor while panel is open
    [SerializeField] private bool takeFocusOnOpen = true;          // set UI selection to first button
    [SerializeField] private List<Behaviour> disableWhileEvent;    // drop your player controller / mouse look scripts here

    // ---------- EVENT STATE ----------
    public bool IsEventBusy { get; private set; } = false;

    float prevTimeScale = 1f;
    PendingEvent currentEvent;
    bool choiceLocked = false;   // blocks double-clicks until outro

    // cursor state cache
    struct CursorState { public CursorLockMode lockMode; public bool visible; }
    CursorState prevCursor;

    // wrong answers log (for end screen)
    readonly List<WrongAnswerRecord> wrongAnswers = new List<WrongAnswerRecord>();

    // ======== PUBLIC API: PROFILE ========
    public void SetUsername(string username) { if (usernameText) usernameText.text = username ?? ""; }
    public void SetAvatar(Sprite sprite)     { if (avatarImage && sprite) avatarImage.sprite = sprite; }

    public void SetTotalEvents(int total)
    {
        totalEvents = Mathf.Max(1, total);
        progress01 = (totalEvents > 0) ? (eventsSeen / (float)totalEvents) : 0f;
    }

    public void ResetProgress()
    {
        eventsSeen = 0;
        progress01 = 0f;
        // segmentedBarAnimator?.SetTrigger("reset"); // if you add a reset anim later
    }

    // ======== PUBLIC API: EVENT START ========
    /// Start an event. correctIndex: 0 = A, 1 = B.
    public void BeginEvent(string eventId, string title, string question,
                           string choiceA, string choiceB, int correctIndex)
    {
        if (IsEventBusy) return;

        currentEvent = new PendingEvent
        {
            eventId      = string.IsNullOrWhiteSpace(eventId) ? "Event" : eventId,
            title        = title ?? "",
            question     = string.IsNullOrWhiteSpace(question) ? defaultQuestion : question,
            choiceA      = choiceA ?? "Choice A",
            choiceB      = choiceB ?? "Choice B",
            correctIndex = Mathf.Clamp(correctIndex, 0, 1)
        };

        IsEventBusy  = true;
        choiceLocked = false;

        // Pause gameplay
        if (pauseOnBegin) { prevTimeScale = Time.timeScale; Time.timeScale = 0f; }

        // Populate UI text
        if (titleText)    titleText.text    = currentEvent.title;
        if (questionText) questionText.text = currentEvent.question;
        if (choiceALabel) choiceALabel.text = currentEvent.choiceA;
        if (choiceBLabel) choiceBLabel.text = currentEvent.choiceB;
        if (resultText)   resultText.text   = ""; // clear previous result

        // Show panel
        if (eventPanelRoot) eventPanelRoot.SetActive(true);

        // Make sure ALL animators under the panel run while paused (fix for "freeze but nothing plays")
        ForceUnscaledOnPanelAnimators();

        // --- INPUT / CURSOR OVERRIDES ---
        if (manageCursor) SaveAndUnlockCursor();
        TogglePlayerScripts(false);
        if (takeFocusOnOpen)
        {
            var first = choiceAButton ? choiceAButton.gameObject : (choiceBButton ? choiceBButton.gameObject : null);
            if (first) EventSystem.current?.SetSelectedGameObject(first);
        }

        // Kick entry anim
        if (!eventPanelAnimator || eventPanelAnimator.runtimeAnimatorController == null)
        {
            Debug.LogError("[GameUIManager] EventPanel Animator missing/empty.");
        }
        else
        {
            eventPanelAnimator.ResetTrigger("Answered");
            eventPanelAnimator.SetTrigger("ZoneTrigger");
        }

        SetChoicesInteractable(true);
    }

    /// Hook these to the two buttons
    public void OnChoiceA() => OnChoiceSelected(0);
    public void OnChoiceB() => OnChoiceSelected(1);

    public void OnChoiceSelected(int index)
    {
        if (!IsEventBusy || choiceLocked) return;
        choiceLocked = true;
        SetChoicesInteractable(false);

        bool isCorrect = (index == currentEvent.correctIndex);

        // Set result text (your "Answered" animation will show this)
        if (resultText)
        {
            resultText.text = isCorrect ? correctText : wrongText;
            if (colorizeResult)
                resultText.color = isCorrect ? correctColor : wrongColor;
        }

        // Log wrong answers for end screen
        if (!isCorrect)
        {
            wrongAnswers.Add(new WrongAnswerRecord
            {
                eventId  = currentEvent.eventId,
                title    = currentEvent.title,
                selected = index,
                correct  = currentEvent.correctIndex,
                choiceA  = currentEvent.choiceA,
                choiceB  = currentEvent.choiceB
            });
        }

        // Play the single "Answered" outro; the clip should call OnEventOutroComplete() at its end
        if (eventPanelAnimator) eventPanelAnimator.SetTrigger("Answered");

        // Bump progress (fires exactly one trigger: "Nclear")
        AdvanceCount(1);
    }

    /// Animation Event: call at end of the "Answered" clip
    public void OnEventOutroComplete()
    {
        if (eventPanelRoot) eventPanelRoot.SetActive(false);
        if (pauseOnBegin) Time.timeScale = prevTimeScale;

        // --- RESTORE INPUT / CURSOR ---
        if (manageCursor) RestoreCursor();
        TogglePlayerScripts(true);
        EventSystem.current?.SetSelectedGameObject(null);

        IsEventBusy  = false;
        choiceLocked = false;
        currentEvent = default;
        SetChoicesInteractable(true);
    }

    // ======== WRONG-ANSWER REPORTING ========
    public int  WrongAnswerCount => wrongAnswers.Count;
    public IReadOnlyList<WrongAnswerRecord> WrongAnswers => wrongAnswers;
    public void ClearWrongAnswers() => wrongAnswers.Clear();

    // ======== INTERNALS ========
    void Awake()
    {
        // Wire buttons (safe even if already wired in Inspector)
        if (choiceAButton)
        {
            choiceAButton.onClick.RemoveAllListeners();
            choiceAButton.onClick.AddListener(OnChoiceA);
        }
        if (choiceBButton)
        {
            choiceBButton.onClick.RemoveAllListeners();
            choiceBButton.onClick.AddListener(OnChoiceB);
        }

        // Start hidden
        if (eventPanelRoot) eventPanelRoot.SetActive(false);

        // Editor hint if triggers are missing
#if UNITY_EDITOR
        if (eventPanelAnimator && eventPanelAnimator.runtimeAnimatorController)
        {
            bool hasZone = false, hasAnswered = false;
            foreach (var p in eventPanelAnimator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "ZoneTrigger") hasZone = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Answered")    hasAnswered = true;
            }
            if (!hasZone)    Debug.LogWarning("[GameUIManager] Animator has no 'ZoneTrigger' trigger. Check controller/typo.");
            if (!hasAnswered)Debug.LogWarning("[GameUIManager] Animator has no 'Answered' trigger. Check controller/typo.");
        }
#endif
    }

    // Handy while testing: backquote (`) will unpause & close the panel if something went wrong
    void Update()
    {
        if (IsEventBusy && Input.GetKeyDown(KeyCode.BackQuote))
        {
            Debug.LogWarning("[GameUIManager] Panic unpause/close");
            OnEventOutroComplete();
        }
    }

    void SetChoicesInteractable(bool v)
    {
        if (choiceAButton) choiceAButton.interactable = v;
        if (choiceBButton) choiceBButton.interactable = v;
    }

    void AdvanceCount(int delta)
    {
        int newSeen = Mathf.Clamp(eventsSeen + delta, 0, totalEvents);
        if (newSeen == eventsSeen) return;

        eventsSeen = newSeen;
        progress01 = eventsSeen / (float)totalEvents;
        FireCountTrigger(eventsSeen); // e.g., "7clear" when eventsSeen = 7
    }

    public void SetProgressByCount(int seen, int total)
    {
        totalEvents = Mathf.Max(1, total);
        int clamped = Mathf.Clamp(seen, 0, totalEvents);
        if (clamped != eventsSeen)
        {
            eventsSeen = clamped;
            progress01 = eventsSeen / (float)totalEvents;
            FireCountTrigger(eventsSeen);
        }
    }

    public void SetProgress01(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        int clamped = Mathf.Clamp(Mathf.RoundToInt(normalized * totalEvents), 0, totalEvents);
        if (clamped != eventsSeen)
        {
            eventsSeen = clamped;
            progress01 = eventsSeen / (float)totalEvents;
            FireCountTrigger(eventsSeen);
        }
    }

    void FireCountTrigger(int count)
    {
        if (!segmentedBarAnimator || count <= 0) return;
        segmentedBarAnimator.SetTrigger(string.Format(countTriggerFormat, count));
    }

    // Force ALL animators under the panel to UnscaledTime (so they still tick while the game is paused)
    void ForceUnscaledOnPanelAnimators()
    {
        if (!eventPanelRoot) return;
        var anims = eventPanelRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
            anims[i].updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    // ----- Cursor/Input helpers -----
    void SaveAndUnlockCursor()
    {
        prevCursor.lockMode = Cursor.lockState;
        prevCursor.visible  = Cursor.visible;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    void RestoreCursor()
    {
        Cursor.lockState = prevCursor.lockMode;
        Cursor.visible   = prevCursor.visible;
    }

    void TogglePlayerScripts(bool enable)
    {
        if (disableWhileEvent == null) return;
        foreach (var b in disableWhileEvent)
        {
            if (!b) continue;
            b.enabled = enable;
        }
    }

    // ---------- SUPPORT TYPES ----------
    private struct PendingEvent
    {
        public string eventId;
        public string title;
        public string question;
        public string choiceA;
        public string choiceB;
        public int    correctIndex; // 0 = A, 1 = B
    }

    [System.Serializable]
    public struct WrongAnswerRecord
    {
        public string eventId;
        public string title;
        public int    selected;   // 0 = A, 1 = B
        public int    correct;    // 0 = A, 1 = B
        public string choiceA;
        public string choiceB;
    }
}