using UnityEngine;

public class TelescopeTrigger : MonoBehaviour
{
    public GameObject uiCanvas;

    void Start()
    {
        if (uiCanvas != null)
            uiCanvas.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Trigger entered: " + other.name);
        if (other.name == "CenterEyeAnchor")
        {
            uiCanvas.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.name == "CenterEyeAnchor")
        {
            uiCanvas.SetActive(false);
        }
    }
}

