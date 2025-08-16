using UnityEngine;

public class EventPanelEventRelay : MonoBehaviour
{
    public GameUIManager ui;

    // Hook this in the Animation Event at the end of "Answered"
    public void OnEventOutroComplete()
    {
        if (ui) ui.OnEventOutroComplete();
    }
}