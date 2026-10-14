using UnityEngine;

public class CampusScreenNavigation : MonoBehaviour
{
    public GameObject mainPanel;
    public GameObject destinationPanel;
    public GameObject qrMainPanel;
    public GameObject qrScanningPanel;
    public GameObject arScreenPanel;
    public GameObject destARScreenPanel;

    public void ShowMainPanel()
    {
        HideAllPanels();
        mainPanel.SetActive(true);
    }

    public void ShowDestinationPanel()
    {
        HideAllPanels();
        destinationPanel.SetActive(true);
    }

    public void ShowQRMainPanel()
    {
        HideAllPanels();
        qrMainPanel.SetActive(true);
    }

    public void ShowQRScanningPanel()
    {
        HideAllPanels();
        qrScanningPanel.SetActive(true);
    }

    public void ShowARScreenPanel()
    {
        HideAllPanels();
        arScreenPanel.SetActive(true);
    }

    public void ShowDestARScreenPanel()
    {
        HideAllPanels();
        destARScreenPanel.SetActive(true);
    }

    private void HideAllPanels()
    {
        mainPanel.SetActive(false);
        destinationPanel.SetActive(false);
        qrMainPanel.SetActive(false);
        qrScanningPanel.SetActive(false);
        arScreenPanel.SetActive(false);
        destARScreenPanel.SetActive(false);
    }
}