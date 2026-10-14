using UnityEngine;

public class ScreenNavigation : MonoBehaviour
{
    public GameObject selectionScreen;
    public GameObject destinationScreen;

    public void ShowDestinationScreen()
    {
        selectionScreen.SetActive(false);
        destinationScreen.SetActive(true);
    }

    public void ShowSelectionScreen()
    {
        destinationScreen.SetActive(false);
        selectionScreen.SetActive(true);
    }
}