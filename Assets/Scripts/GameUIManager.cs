using System.Collections.Generic;
using System.Text;
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
    [SerializeField] private Animator segmentedBarAnimator;           // triggers "1clear".."12clear"
    [SerializeField, Min(1)] private int totalEvents = 12;
    [SerializeField] private string countTriggerFormat = "{0}clear";  // -> "1clear".."12clear"

    [Header("Debug (read-only)")]
    [SerializeField] private int eventsSeen = 0;                      // 0..totalEvents
    [SerializeField, Range(0f,1f)] private float progress01 = 0f;

    // ---------- EVENT PANEL ----------
    [Header("Event Panel (UI)")]
    [SerializeField] private GameObject eventPanelRoot;
    [SerializeField] private Animator   eventPanelAnimator;

    [Header("Event Text & Buttons")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text choiceALabel;
    [SerializeField] private TMP_Text choiceBLabel;
    [SerializeField] private Button   choiceAButton;
    [SerializeField] private Button   choiceBButton;

    [Header("Result Text")]
    [SerializeField] private TMP_Text resultText;           // shown by your "Answered" animation
    [SerializeField] private string correctText = "Correct!";
    [SerializeField] private string wrongText   = "Wrong!";
    [SerializeField] private bool   colorizeResult = true;
    [SerializeField] private Color  correctColor = new Color(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color  wrongColor   = new Color(1f,   0.4f, 1f, 1f);

    [Header("Event Behaviour")]
    [SerializeField] private bool   pauseOnBegin = true;
    [SerializeField] private string defaultQuestion = "What should you do?";

    // ---------- SHOP / ROAD SPAWN ----------
    [Header("Shop Spawn (Road)")]
    [SerializeField] private Transform player;                 // your player transform
    [SerializeField] private float innerNpcRadius = 40f;       // search radius for lanes around player
    [SerializeField] private GameObject shopPrefab;            // prefab contains trigger with ShopkeeperInteractZone
    [SerializeField] private float laneSideOffset = 3.0f;      // offset from lane center to place the shop
    [SerializeField] private Vector3 clearBoxHalfExtents = new Vector3(18f, 6f, 18f); // who to clear around shop
    [SerializeField] private LayerMask groundMask = ~0;        // for ground snap raycast

    // ---------- INTERACT PROMPT ----------
    [Header("Interact Prompt")]
    [SerializeField] private GameObject interactPromptRoot;    // small UI panel "[E] Interact w/ Shopkeeper"
    [SerializeField] private TMP_Text   interactPromptText;
    [SerializeField] private string     interactPromptString = "[E] Interact w/ Shopkeeper";
    [SerializeField] private KeyCode    interactKey = KeyCode.E;

    // ---------- END PANEL ----------
    [Header("End Panel")]
    [SerializeField] private GameObject endPanelRoot;
    [SerializeField] private Animator   endPanelAnimator;       // has triggers: Finished, ShowFirst, NextExplanation, EndCredits
    [SerializeField] private TMP_Text   scoreText;              // "X / total"
    [SerializeField] private TMP_Text   wrongListText;          // (legacy multiline; not shown in the new flow)
    [SerializeField] private string     noMistakesText = "Perfect run! 🎉";

    // ---------- END PANEL - EXPLANATION SWAP LAYOUT ----------
    [Header("End Panel - Explanation Sets")]
    [SerializeField] private GameObject     scoreGroup;        // EndPanel/Score group (the 4 TMPs)
    [SerializeField] private RectTransform  afterTextRoot;     // EndPanel/AfterText
    [SerializeField] private RectTransform  setOld;            // AfterText/Set_Old
    [SerializeField] private RectTransform  setNew;            // AfterText/Set_New
    [SerializeField] private TMP_Text       oldScenario;       // Set_Old/S: Scenario
    [SerializeField] private TMP_Text       oldChosen;         // Set_Old/A: Chosen Answer
    [SerializeField] private TMP_Text       oldExplanation;    // Set_Old/E: Explanation
    [SerializeField] private TMP_Text       newScenario;       // Set_New/S: Scenario
    [SerializeField] private TMP_Text       newChosen;         // Set_New/A: Chosen Answer
    [SerializeField] private TMP_Text       newExplanation;    // Set_New/E: Explanation
    [SerializeField] private GameObject     fullClear;         // AfterText/Full Clear

    [System.Serializable]
    public struct EventExplanation
    {
        public string eventId;                 // must match eventId passed to BeginEvent
        [TextArea(2,6)] public string explanation; // text shown if that event was answered wrong
    }
    [SerializeField] private List<EventExplanation> explanations = new List<EventExplanation>();

    // ---------- CURSOR / PLAYER CONTROL ----------
    [Header("Cursor & Player (Optional)")]
    [SerializeField] private bool manageCursor = true;              // unlock/show cursor during panels
    [SerializeField] private bool takeFocusOnOpen = true;           // select first button on open
    [SerializeField] private List<Behaviour> disableWhileEvent;     // player scripts to disable during EVENT/END panels

    // ---------- STATE ----------
    public bool IsEventBusy { get; private set; } = false;

    float prevTimeScale = 1f;
    PendingEvent currentEvent;
    bool choiceLocked = false;

    struct CursorState { public CursorLockMode lockMode; public bool visible; }
    CursorState prevCursor;

    readonly List<WrongAnswerRecord> wrongAnswers = new List<WrongAnswerRecord>();
    bool endShown = false;
    bool inShopZone = false;

    // explanation driving
    struct ExplainView { public string scenario, chosen, expl; }
    readonly Queue<ExplainView> _explainQueue = new Queue<ExplainView>();
    bool _stepDoneFlag = false;

    // ======== PUBLIC: PROFILE ========
    public void SetUsername(string username) { if (usernameText) usernameText.text = username ?? ""; }
    public void SetAvatar(Sprite sprite)     { if (avatarImage && sprite) avatarImage.sprite = sprite; }
    public void SetTotalEvents(int total)    { totalEvents = Mathf.Max(1, total); progress01 = eventsSeen/(float)totalEvents; }
    public void ResetProgress()              { eventsSeen = 0; progress01 = 0f; wrongAnswers.Clear(); endShown = false; }

    // ======== EVENT FLOW ========
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

        if (pauseOnBegin) { prevTimeScale = Time.timeScale; Time.timeScale = 0f; }

        if (titleText)    titleText.text    = currentEvent.title;
        if (questionText) questionText.text = currentEvent.question;
        if (choiceALabel) choiceALabel.text = currentEvent.choiceA;
        if (choiceBLabel) choiceBLabel.text = currentEvent.choiceB;
        if (resultText)   resultText.text   = "";

        if (eventPanelRoot) eventPanelRoot.SetActive(true);
        ForceUnscaledOnAnimators(eventPanelRoot);

        if (manageCursor) SaveAndUnlockCursor();
        TogglePlayerScripts(false);
        if (takeFocusOnOpen)
        {
            var first = choiceAButton ? choiceAButton.gameObject : (choiceBButton ? choiceBButton.gameObject : null);
            if (first) EventSystem.current?.SetSelectedGameObject(first);
        }

        if (!eventPanelAnimator || eventPanelAnimator.runtimeAnimatorController == null)
            Debug.LogError("[GameUIManager] EventPanel Animator missing/empty.");
        else
        {
            eventPanelAnimator.ResetTrigger("Answered");
            eventPanelAnimator.SetTrigger("ZoneTrigger");
        }

        SetChoicesInteractable(true);
    }

    public void OnChoiceA() => OnChoiceSelected(0);
    public void OnChoiceB() => OnChoiceSelected(1);

    public void OnChoiceSelected(int index)
    {
        if (!IsEventBusy || choiceLocked) return;
        choiceLocked = true;
        SetChoicesInteractable(false);

        bool isCorrect = (index == currentEvent.correctIndex);

        if (resultText)
        {
            resultText.text = isCorrect ? correctText : wrongText;
            if (colorizeResult) resultText.color = isCorrect ? correctColor : wrongColor;
        }

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

        if (eventPanelAnimator) eventPanelAnimator.SetTrigger("Answered");
        AdvanceCount(1);
    }

    /// Animation Event at end of "Answered" clip
    public void OnEventOutroComplete()
    {
        if (eventPanelRoot) eventPanelRoot.SetActive(false);

        bool completedAll = (eventsSeen >= totalEvents) && !endShown;
        if (completedAll)
        {
            // Resume gameplay (we’ll freeze the world selectively instead)
            if (pauseOnBegin) Time.timeScale = prevTimeScale;
            if (manageCursor) RestoreCursor();
            TogglePlayerScripts(true);
            EventSystem.current?.SetSelectedGameObject(null);

            SpawnShopOnRoadNearPlayer();   // road-aligned spawn + clear + world freeze (except player+shop)
        }
        else
        {
            // normal resume
            if (pauseOnBegin) Time.timeScale = prevTimeScale;
            if (manageCursor) RestoreCursor();
            TogglePlayerScripts(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        IsEventBusy  = false;
        choiceLocked = false;
        currentEvent = default;
        SetChoicesInteractable(true);
    }

    // ======== SHOP / INTERACT ========
    void SpawnShopOnRoadNearPlayer()
    {
        if (!shopPrefab)
        {
            Debug.LogError("[GameUIManager] shopPrefab not assigned.");
            return;
        }

        if (!TryFindRoadSpawn(out var pos, out var fwd))
        {
            Debug.LogWarning("[GameUIManager] No lane nearby; spawning in front of player.");
            pos = player ? player.position + player.forward * 5f : Vector3.zero;
            if (RaycastToGround(pos + Vector3.up * 20f, out var snap, 50f)) pos = snap;
            fwd = player ? player.forward.WithY(0f).normalized : Vector3.forward;
        }

        var rot = Quaternion.LookRotation(fwd, Vector3.up);
        var shop = Instantiate(shopPrefab, pos, rot);

        // Clear area AFTER spawn (vehicles/NPCs only)
        ClearAreaAABB(pos, clearBoxHalfExtents);

        // Freeze world except shop (timeScale stays 1 so shop FX/anim run)
        FreezeWorldExcept(shop.transform);

        // Play all confettis inside shop prefab (works with Play On Awake ON/OFF)
        var pss = shop.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in pss) { if (!ps) continue; ps.Clear(true); ps.Play(true); }

        // Prompt hidden; ShopkeeperInteractZone will show it on enter
        if (interactPromptRoot) interactPromptRoot.SetActive(false);
        if (interactPromptText) interactPromptText.text = interactPromptString;
    }

    public void OnShopZoneEnter() { inShopZone = true; if (interactPromptRoot) interactPromptRoot.SetActive(true); }
    public void OnShopZoneExit () { inShopZone = false; if (interactPromptRoot) interactPromptRoot.SetActive(false); }

    void Update()
    {
        // panic key if you ever get stuck paused
        if (IsEventBusy && Input.GetKeyDown(KeyCode.BackQuote)) OnEventOutroComplete();

        // Interact at shop
        if (!endShown && inShopZone && Input.GetKeyDown(interactKey))
            BeginEndSequence();
    }

    void BeginEndSequence()
    {
        endShown = true;

        // Pause gameplay and show end panel (world remains frozen until UnfreezeWorld)
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        if (manageCursor) SaveAndUnlockCursor();
        TogglePlayerScripts(false);

        if (endPanelRoot) endPanelRoot.SetActive(true);
        ForceUnscaledOnAnimators(endPanelRoot);

        // set visibility defaults
        if (scoreGroup) scoreGroup.SetActive(true);
        if (afterTextRoot) afterTextRoot.gameObject.SetActive(true);

        // fill score + build explanation queue
        PopulateEndPanelTexts();

        if (!endPanelAnimator || endPanelAnimator.runtimeAnimatorController == null)
            Debug.LogError("[GameUIManager] EndPanel Animator missing/empty.");
        else
            endPanelAnimator.SetTrigger("Finished"); // your entrance anim

        if (interactPromptRoot) interactPromptRoot.SetActive(false);

        // kick off the explanation sequence AFTER the Finished intro starts
        StartCoroutine(PlayExplanationSequence());
    }

    // ====== EXPLANATION FLOW ======
    void SafeTrigger(Animator anim, string trigger)
    {
        if (!anim) return;
        if (string.IsNullOrEmpty(trigger)) return;

    #if UNITY_EDITOR
        // only in editor, check if the trigger actually exists
        bool found = false;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == trigger)
            {
                found = true; break;
            }
        }
        if (!found)
        {
            Debug.LogWarning($"[GameUIManager] Animator '{anim.name}' has no trigger '{trigger}'.");
            return;
        }
    #endif

        anim.ResetTrigger(trigger); // clear stale state
        anim.SetTrigger(trigger);
    }

    System.Collections.IEnumerator PlayExplanationSequence()
    {
        if (_explainQueue.Count == 0)
            yield break;

        // --- FIRST explanation is handled by Finished anim itself ---
        var first = _explainQueue.Dequeue();
        SetNewTexts(first);

        // wait for Finished animation to complete (AE_MiniStepDone called at the end)
        _stepDoneFlag = false;
        yield return new WaitUntil(() => _stepDoneFlag);

        // --- LOOP remaining explanations with NextExplanation trigger ---
        while (_explainQueue.Count > 0)
        {
            var next = _explainQueue.Dequeue();
            SetNewTexts(next);
            SafeTrigger(endPanelAnimator, "NextExplanation");

            _stepDoneFlag = false;
            yield return new WaitUntil(() => _stepDoneFlag);
        }

        // --- DONE → EndCredits ---
        SafeTrigger(endPanelAnimator, "EndCredits");
    }

    void SetNewTexts(ExplainView v)
    {
        if (newScenario)    newScenario.text    = v.scenario;
        if (newChosen)      newChosen.text      = $"You chose: {v.chosen}";
        if (newExplanation) newExplanation.text = v.expl;
    }

    // Animation Event at ~90% of the step clip (ScoreOut_ExplainIn & ExplainSwap)
    public void AE_CommitSwap()
    {
        if (oldScenario && newScenario)           oldScenario.text       = newScenario.text;
        if (oldChosen && newChosen)               oldChosen.text         = newChosen.text;
        if (oldExplanation && newExplanation)     oldExplanation.text    = newExplanation.text;
    }

    // Animation Event at end of the step clip
    public void AE_MiniStepDone()
    {
        _stepDoneFlag = true;
    }

    void PopulateEndPanelTexts()
    {
        // SCORE (still used for your 4-TMP layout)
        int wrong = wrongAnswers.Count;
        int correct = Mathf.Clamp(totalEvents - wrong, 0, totalEvents);
        if (scoreText) scoreText.text = $"{correct} / {totalEvents}";

        // Build explanation map
        var map = new Dictionary<string, string>(explanations.Count);
        foreach (var e in explanations)
            if (!string.IsNullOrEmpty(e.eventId)) map[e.eventId] = e.explanation;

        _explainQueue.Clear();

        if (wrong == 0)
        {
            if (afterTextRoot) afterTextRoot.gameObject.SetActive(true);
            if (fullClear) fullClear.SetActive(true);
            if (setOld) setOld.gameObject.SetActive(false);
            if (setNew) setNew.gameObject.SetActive(false);
            // optional: also hide scoreGroup and just show a “Full Clear” banner
            return;
        }

        if (afterTextRoot) afterTextRoot.gameObject.SetActive(true);
        if (fullClear)     fullClear.SetActive(false);
        if (setOld)        setOld.gameObject.SetActive(true);
        if (setNew)        setNew.gameObject.SetActive(true);

        // clear the visible "old" texts so first swap looks clean
        if (oldScenario)     oldScenario.text = "";
        if (oldChosen)       oldChosen.text = "";
        if (oldExplanation)  oldExplanation.text = "";

        // queue all wrong answers
        foreach (var r in wrongAnswers)
        {
            string scenario = string.IsNullOrEmpty(r.title) ? r.eventId : r.title;
            string chosen   = r.selected == 0 ? r.choiceA : r.choiceB;
            string expl     = (map.TryGetValue(r.eventId, out var eText) && !string.IsNullOrWhiteSpace(eText))
                              ? eText.Trim()
                              : "No explanation set yet.";
            _explainQueue.Enqueue(new ExplainView { scenario = scenario, chosen = chosen, expl = expl });
        }
    }

    // ======== CLEAR & FREEZE ========
    void ClearAreaAABB(Vector3 center, Vector3 halfExtents)
    {
        var hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!h || !h.transform) continue;

            var veh = h.GetComponentInParent<VehicleBehaviour>();
            if (veh) { Destroy(veh.gameObject); continue; }

            var npc = h.GetComponentInParent<NPCBehaviour>();
            if (npc) { Destroy(npc.gameObject); continue; }
        }
    }

    // disable NPC/vehicles globally; keep player & shop alive (timeScale remains 1 so shop FX/anim run)
    List<Behaviour> _frozenBehaviours = new List<Behaviour>();
    void FreezeWorldExcept(Transform whitelistRoot)
    {
#if UNITY_2022_3_OR_NEWER
        var npcs = Object.FindObjectsByType<NPCBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var cars = Object.FindObjectsByType<VehicleBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var npcs = Object.FindObjectsOfType<NPCBehaviour>();
        var cars = Object.FindObjectsOfType<VehicleBehaviour>();
#endif
        _frozenBehaviours.Clear();

        bool IsWhitelisted(Component c)
        {
            if (!c || !whitelistRoot) return false;
            return c.transform == whitelistRoot || c.transform.IsChildOf(whitelistRoot);
        }

        foreach (var n in npcs)
        {
            if (!n || IsWhitelisted(n)) continue;
            var agent = n.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (agent) agent.isStopped = true;
            if (n.enabled) { n.enabled = false; _frozenBehaviours.Add(n); }
            var anim = n.GetComponentInChildren<Animator>(); if (anim && anim.enabled) { anim.enabled = false; _frozenBehaviours.Add(anim); }
        }

        foreach (var v in cars)
        {
            if (!v || IsWhitelisted(v)) continue;
            if (v.enabled) { v.enabled = false; _frozenBehaviours.Add(v); }
            var anim = v.GetComponentInChildren<Animator>(); if (anim && anim.enabled) { anim.enabled = false; _frozenBehaviours.Add(anim); }
            var rb = v.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    public void UnfreezeWorld()
    {
        for (int i = 0; i < _frozenBehaviours.Count; i++)
            if (_frozenBehaviours[i]) _frozenBehaviours[i].enabled = true;
        _frozenBehaviours.Clear();

#if UNITY_2022_3_OR_NEWER
        var npcs = Object.FindObjectsByType<NPCBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var npcs = Object.FindObjectsOfType<NPCBehaviour>();
#endif
        foreach (var n in npcs)
        {
            if (!n) continue;
            var agent = n.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (agent) agent.isStopped = false;
        }
    }

    // ======== ROAD PICK ========
    bool TryFindRoadSpawn(out Vector3 pos, out Vector3 fwd)
    {
        pos = Vector3.zero; fwd = Vector3.forward;
        if (!player) return false;

#if UNITY_2022_3_OR_NEWER
        var lanes = Object.FindObjectsByType<LanePath>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var lanes = Object.FindObjectsOfType<LanePath>();
#endif
        if (lanes == null || lanes.Length == 0) return false;

        var p = player.position;
        float bestScore = float.PositiveInfinity;
        Vector3 bestPos = Vector3.zero, bestFwd = Vector3.forward;

        foreach (var lane in lanes)
        {
            if (!lane || lane.Count < 2) continue;
            for (int i = 0; i < lane.Count - 1; i++)
            {
                var a = lane[i].position;
                var b = lane[i+1].position;

                float distToSeg = DistancePointToSegmentXZ(p, a, b);
                if (distToSeg > innerNpcRadius) continue;

                Vector3 mid = (a + b) * 0.5f;
                Vector3 t = (b - a); t.y = 0f;
                if (t.sqrMagnitude < 0.01f) continue;
                t.Normalize();

                float score = distToSeg; // prefer closer
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPos = mid;
                    bestFwd = t;
                }
            }
        }

        if (!bestScore.IsFinite() || bestScore > innerNpcRadius) return false;

        Vector3 right = Vector3.Cross(Vector3.up, bestFwd);
        Vector3 spawn = bestPos + right * laneSideOffset;

        if (RaycastToGround(spawn + Vector3.up * 20f, out var ground, 50f))
            spawn = ground;

        pos = spawn; fwd = bestFwd;
        return true;
    }

    static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ap = p - a; ap.y = 0f;
        Vector3 ab = b - a; ab.y = 0f;
        float denom = ab.sqrMagnitude;
        float t = denom > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / denom) : 0f;
        Vector3 proj = a + ab * t;
        return Vector3.Distance(p.WithY(0f), proj.WithY(0f));
    }

    bool RaycastToGround(Vector3 origin, out Vector3 hitPos, float maxDistance)
    {
        if (Physics.Raycast(origin, Vector3.down, out var hit, maxDistance, groundMask, QueryTriggerInteraction.Ignore))
        { hitPos = hit.point; return true; }
        hitPos = origin; return false;
    }

    // ======== MISC INTERNALS ========
    void TogglePlayerScripts(bool enable)
    {
        if (disableWhileEvent == null) return;
        for (int i = 0; i < disableWhileEvent.Count; i++)
        {
            var b = disableWhileEvent[i];
            if (b) b.enabled = enable;
        }
    }

    void Awake()
    {
        if (choiceAButton) { choiceAButton.onClick.RemoveAllListeners(); choiceAButton.onClick.AddListener(OnChoiceA); }
        if (choiceBButton) { choiceBButton.onClick.RemoveAllListeners(); choiceBButton.onClick.AddListener(OnChoiceB); }

        if (eventPanelRoot) eventPanelRoot.SetActive(false);
        if (endPanelRoot)   endPanelRoot.SetActive(false);
        if (interactPromptRoot) interactPromptRoot.SetActive(false);

#if UNITY_EDITOR
        if (eventPanelAnimator && eventPanelAnimator.runtimeAnimatorController)
        {
            bool hasZone=false, hasAnswered=false;
            foreach (var p in eventPanelAnimator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "ZoneTrigger") hasZone = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Answered")    hasAnswered = true;
            }
            if (!hasZone)    Debug.LogWarning("[GameUIManager] EventPanel missing 'ZoneTrigger'.");
            if (!hasAnswered)Debug.LogWarning("[GameUIManager] EventPanel missing 'Answered'.");
        }
        if (endPanelAnimator && endPanelAnimator.runtimeAnimatorController)
        {
            var names = new HashSet<string>();
            foreach (var p in endPanelAnimator.parameters) names.Add(p.name);
            if (!names.Contains("Finished"))       Debug.LogWarning("[GameUIManager] EndPanel missing 'Finished' trigger.");
            if (!names.Contains("ShowFirst"))      Debug.LogWarning("[GameUIManager] EndPanel missing 'ShowFirst' trigger.");
            if (!names.Contains("NextExplanation"))Debug.LogWarning("[GameUIManager] EndPanel missing 'NextExplanation' trigger.");
            if (!names.Contains("EndCredits"))     Debug.LogWarning("[GameUIManager] EndPanel missing 'EndCredits' trigger.");
        }
#endif
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
        FireCountTrigger(eventsSeen);
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

    void ForceUnscaledOnAnimators(GameObject root)
    {
        if (!root) return;
        var anims = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < anims.Length; i++)
            anims[i].updateMode = AnimatorUpdateMode.UnscaledTime;
    }

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

// small vector helpers
static class V3Ext
{
    public static bool IsFinite(this float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
}