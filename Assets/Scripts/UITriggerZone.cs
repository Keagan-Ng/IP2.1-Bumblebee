using System.Collections;
using UnityEngine;

public class UITriggerZone : MonoBehaviour
{
    [Header("Who can trigger")]
    public string playerTag = "Player";

    [Header("What to show")]
    public GameObject panelRoot;       // your Canvas/Panel
    public Animator panelAnimator;     // optional (Show/Hide triggers)
    public string showTrigger = "Show";
    public string hideTrigger = "Hide";
    public CanvasGroup canvasGroup;    // optional fade

    [Header("Feedback (assign your green/red backgrounds)")]
    public GameObject green_back;      // shown on correct choice
    public GameObject red_back;        // shown on wrong choice
    public float feedbackHoldSeconds = 0.25f; // how long to show green/red before hiding

    [Header("Behavior")]
    public bool showOnEnter = true;
    public bool hideOnExit  = false;   // usually keep false for pause panels
    public bool oneShot     = false;

    [Header("Player control & cursor")]
    public bool manageCursor = true;
    public MonoBehaviour[] disableWhileOpen; // e.g. FirstPersonController, your look script, etc.

    CursorLockMode _prevLock;
    bool _prevVisible;


    static int s_pauseLocks = 0;       // simple pause “stack”
    bool _visible;
    bool _consumed;

    void Awake()
    {
        if (panelAnimator) panelAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

        // start hidden
        if (panelRoot && !panelAnimator && !canvasGroup) panelRoot.SetActive(false);
        if (canvasGroup)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (green_back) green_back.SetActive(false);
        if (red_back)   red_back.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!showOnEnter || _consumed) return;
        if (!other.CompareTag(playerTag)) return;

        ShowAndPause();
        if (oneShot) _consumed = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (!hideOnExit) return;
        if (!other.CompareTag(playerTag)) return;
        HideAndResume(); // only used if you want exit to close too
    }

    // ------- Button hooks -------
    public void OnAnswerCorrect()
    {
        StartCoroutine(ShowFeedbackThenClose(correct:true));
    }

    public void OnAnswerWrong()
    {
        StartCoroutine(ShowFeedbackThenClose(correct:false));
    }

    IEnumerator ShowFeedbackThenClose(bool correct)
    {
        if (green_back) green_back.SetActive(correct);
        if (red_back)   red_back.SetActive(!correct);

        // brief unscaled hold so it’s visible even while paused
        float t = 0f;
        while (t < feedbackHoldSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (green_back) green_back.SetActive(false);
        if (red_back)   red_back.SetActive(false);

        HideAndResume();
    }

    // ------- Internals -------
    void ShowAndPause()
    {
        if (_visible) return;
        _visible = true;

        if (panelRoot) panelRoot.SetActive(true);
        if (panelAnimator) { panelAnimator.ResetTrigger(hideTrigger); panelAnimator.SetTrigger(showTrigger); }
        else if (canvasGroup) { StopAllCoroutines(); StartCoroutine(FadeCanvas(0f, 1f)); }

        // NEW: disable player + unlock cursor
        foreach (var mb in disableWhileOpen) if (mb) mb.enabled = false;
        if (manageCursor)
        {
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        PauseGame(true);
    }

    void HideAndResume()
    {
        if (!_visible) return;
        _visible = false;

        if (panelAnimator) { panelAnimator.ResetTrigger(showTrigger); panelAnimator.SetTrigger(hideTrigger); }
        else if (canvasGroup) { StopAllCoroutines(); StartCoroutine(FadeCanvas(1f, 0f, deactivateAtEnd:true)); }
        else if (panelRoot) panelRoot.SetActive(false);

        // NEW: re-enable player + restore cursor
        foreach (var mb in disableWhileOpen) if (mb) mb.enabled = true;
        if (manageCursor)
        {
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevVisible;
        }

        PauseGame(false);
    }

    static void PauseGame(bool on)
    {
        if (on) s_pauseLocks++;
        else    s_pauseLocks = Mathf.Max(0, s_pauseLocks - 1);

        bool paused = s_pauseLocks > 0;
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
    }

    IEnumerator FadeCanvas(float from, float to, bool deactivateAtEnd=false)
    {
        float fade = 0.2f;
        float t = 0f;
        if (panelRoot) panelRoot.SetActive(true);
        canvasGroup.alpha = from;
        canvasGroup.interactable = to > 0.5f;
        canvasGroup.blocksRaycasts = to > 0.5f;

        while (t < fade)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fade);
            yield return null;
        }
        canvasGroup.alpha = to;
        canvasGroup.interactable = to > 0.5f;
        canvasGroup.blocksRaycasts = to > 0.5f;

        if (deactivateAtEnd && panelRoot && to <= 0f) panelRoot.SetActive(false);
    }

    IEnumerator DeactivateAtEnd(GameObject go, float delayUnscaled)
    {
        if (!go) yield break;
        float t = 0f;
        while (t < delayUnscaled) { t += Time.unscaledDeltaTime; yield return null; }
    }
}