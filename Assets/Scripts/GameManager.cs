using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public string Username { get; private set; }

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadUsername();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    public void SaveUsername(string username)
    {
        Username = username;
        PlayerPrefs.SetString("username", username);
        PlayerPrefs.Save();
        Debug.Log("Saved username: " + username);
    }
    void LoadUsername()
    {
        Username = PlayerPrefs.GetString("username", "");
        Debug.Log("Loaded username: " + Username);
    }
}
