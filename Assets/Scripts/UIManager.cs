using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    [Header("Leaderboard - Foreground Text")]
    public TextMeshProUGUI nameColumnText;
    public TextMeshProUGUI pointsColumnText;
    public TextMeshProUGUI timeColumnText;

    [Header("Leaderboard - Background Text")]
    public TextMeshProUGUI nameColumnShadowText;
    public TextMeshProUGUI pointsColumnShadowText;
    public TextMeshProUGUI timeColumnShadowText;

    [Header("UI Animators")]
    public Animator playButtonAnimator;
    public Animator langButtonAnimator;
    public Animator leaderboardAnimator;
    public Animator usernameInputAnimator;
    public Animator backArrowAnimator;
    public Animator langSelectAnimator;

    [Header("Username Input Field")]
    public TMP_InputField usernameInputField;

    [Header("Game Settings")]
    public string gameSceneName = "MainGame";

    [Header("Leaderboard Entries")]
    public LeaderboardEntry[] entries = new LeaderboardEntry[4];

    [Header("Truncation Settings")]
    public float nameColumnMaxWidth = 15f;

    private bool isPlayButtonPressed = false;

    [System.Serializable]
    public class LeaderboardEntry
    {
        public string playerName;
        public int points;
        public string time;
    }

    void Start()
    {
        string emptyBlock = "\n\n\n";
        nameColumnText.text = pointsColumnText.text = timeColumnText.text = emptyBlock;
        nameColumnShadowText.text = pointsColumnShadowText.text = timeColumnShadowText.text = emptyBlock;
        
        entries[0] = new LeaderboardEntry { playerName = "Thomas", points = 1500, time = "01.23" };
        entries[1] = new LeaderboardEntry { playerName = "Mysteryboi", points = 1250, time = "01.47" };
        entries[2] = new LeaderboardEntry { playerName = "Cleo", points = 1100, time = "02.01" };
        entries[3] = new LeaderboardEntry { playerName = "Dan", points = 950,  time = "02.20" };

        UpdateLeaderboard();
    }

    public void UpdateLeaderboard()
    {
        var nameLines   = nameColumnText.text.Split('\n');
        var pointsLines = pointsColumnText.text.Split('\n');
        var timeLines   = timeColumnText.text.Split('\n');

        for (int i = 0; i < entries.Length && i < 4; i++)
        {
            if (i < nameLines.Length)
                nameLines[i]   = UsernameTruncator.TruncateToFit(entries[i].playerName, nameColumnMaxWidth);
            if (i < pointsLines.Length)
                pointsLines[i] = entries[i].points.ToString();
            if (i < timeLines.Length)
                timeLines[i]   = entries[i].time;
        }

        nameColumnText.text   = nameColumnShadowText.text   = string.Join("\n", nameLines);
        pointsColumnText.text = pointsColumnShadowText.text = string.Join("\n", pointsLines);
        timeColumnText.text   = timeColumnShadowText.text   = string.Join("\n", timeLines);
    }

    // Called by Play button
    public void OnPlayButtonPressed()
    {
        isPlayButtonPressed = true;
        TriggerExitMenu();
        usernameInputAnimator.SetTrigger("JumpIn");
    }

    // Called by Language button
    public void OnLangButtonPressed()
    {
        TriggerExitMenu();
        langSelectAnimator.SetTrigger("JumpIn");
    }

    // Common exit for main menu
    void TriggerExitMenu()
    {
        playButtonAnimator   .SetTrigger("JumpOut");
        langButtonAnimator   .SetTrigger("JumpOut");
        leaderboardAnimator  .SetTrigger("JumpOut");
        backArrowAnimator    .SetTrigger("JumpIn");
    }

    // Called by Back-arrow button
    public void OnBackPressed()
    {
        if (isPlayButtonPressed)
        {
            usernameInputAnimator.SetTrigger("JumpOut");
            isPlayButtonPressed = false;
        }
        else
        {
            langSelectAnimator.SetTrigger("JumpOut");
        }

        backArrowAnimator    .SetTrigger("JumpOut");

        playButtonAnimator   .SetTrigger("JumpIn");
        langButtonAnimator   .SetTrigger("JumpIn");
        leaderboardAnimator  .SetTrigger("JumpIn");
    }

    // <-- Updated only this method: grab the field, send it to GameManager, then load scene -->
    public void OnStartPressed()
    {
        // 1) read & validate
        string username = usernameInputField.text.Trim();
        if (string.IsNullOrEmpty(username))
        {
            Debug.LogWarning("Please enter a username before starting.");
            return;
        }

        // 2) hand off to GameManager (which handles saving/cloud/PlayerPrefs)
        GameManager.Instance.SaveUsername(username);

        // 3) load your game
        SceneManager.LoadScene(gameSceneName);
    }

    private static class UsernameTruncator
    {
        private static readonly Dictionary<char, float> charWidths = new Dictionary<char, float>
        {
            ['a'] = 2.14f, ['b'] = 1.88f, ['c'] = 1.67f, ['d'] = 1.88f,
            ['e'] = 1.88f, ['f'] = 1.88f, ['g'] = 1.88f, ['h'] = 1.88f,
            ['i'] = 1f,   ['j'] = 1.5f,  ['k'] = 1.88f, ['l'] = 1f,
            ['m'] = 2.5f, ['n'] = 1.88f, ['o'] = 1.88f, ['p'] = 2.14f,
            ['q'] = 1.88f, ['r'] = 1.67f, ['s'] = 1.5f, ['t'] = 1.67f,
            ['u'] = 2.14f, ['v'] = 1.88f, ['w'] = 3f,   ['x'] = 2.14f,
            ['y'] = 1.88f, ['z'] = 1.88f, ['A'] = 2.5f, ['B'] = 2.14f,
            ['C'] = 2.14f, ['D'] = 2.14f, ['E'] = 2.14f, ['F'] = 2.14f,
            ['G'] = 2.5f, ['H'] = 2.5f,  ['I'] = 1.5f,  ['J'] = 1.88f,
            ['K'] = 2.14f, ['L'] = 2.5f,  ['M'] = 2.5f,  ['N'] = 2.5f,
            ['O'] = 2.14f, ['P'] = 2.14f, ['Q'] = 2.5f,  ['R'] = 2.14f,
            ['S'] = 1.88f, ['T'] = 2.14f, ['U'] = 2.14f, ['V'] = 2.5f,
            ['W'] = 3f,    ['X'] = 2.14f, ['Y'] = 2.14f, ['Z'] = 2.14f,
            ['0'] = 2.14f, ['1'] = 1.07f, ['2'] = 1.88f, ['3'] = 1.88f,
            ['4'] = 2.5f,  ['5'] = 1.88f, ['6'] = 1.67f, ['7'] = 1.67f,
            ['8'] = 1.88f, ['9'] = 1.5f,  [' '] = 1.25f, ['.'] = 1.15f,
        };

        public static string TruncateToFit(string username, float maxWidth, float ellipsisWidth = 3.45f)
        {
            float totalWidth = 0f;
            int index = 0;
            foreach (char c in username)
            {
                float charWidth = charWidths.TryGetValue(c, out float w) ? w : 1.88f;
                if (totalWidth + charWidth > maxWidth - ellipsisWidth) break;
                totalWidth += charWidth;
                index++;
            }
            return index < username.Length
                ? username.Substring(0, index).TrimEnd() + "…"
                : username;
        }
    }
}