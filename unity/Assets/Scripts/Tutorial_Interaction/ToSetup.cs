using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ToSetup : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public Button submitButton; // submit button
    public TMP_InputField inputField; // input field 
    public GameObject instructionObject; // Instructions
    public Text message; // Instruction text
    public GameObject[] objectsToHide; // Objects to hide
    public GameObject[] objectsToShow; // Objects to show

    void Start()
    {
        // Button listener.
        submitButton.onClick.AddListener(delegate { SubmitButtonClicked(); });
    }

    private void SubmitButtonClicked()
    {
        // Get input code.
        string inputCode = inputField.text;
        
        if (inputCode.Length < 1)
        {
            message.text = "Please enter the correct participant ID.";
            return;
        }
        else
        {
            // Update participant ID in global variables
            PlayerData.participantId = inputCode;
            Debug.Log("[ToSetup] Participant ID set successfully: " + PlayerData.participantId);         
            // Hide this object.
            this.gameObject.SetActive(false);
            // Show instruction object.
            instructionObject.SetActive(true);
            // Hide objects.
            HideObjects();
            // Show objects.
            ShowObjects();
        }
    }

    private void HideObjects()
    {
        foreach (GameObject obj in objectsToHide)
        {
            obj.SetActive(false);
        }
    }

    private void ShowObjects()
    {
        foreach (GameObject obj in objectsToShow)
        {
            obj.SetActive(true);
        }
    }

}
