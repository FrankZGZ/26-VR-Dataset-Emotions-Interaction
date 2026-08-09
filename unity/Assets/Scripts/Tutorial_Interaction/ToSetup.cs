using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ToSetup : MonoBehaviour
{
    private const string ExitMovementInstruction =
        "\n\n<b>Exit movement:</b> Use the controller thumbstick to move to the marked Exit position. Once you are there, press B on the right controller to open the door and finish. You do not need to physically walk there.";

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public Button submitButton; // submit button
    public TMP_InputField inputField; // input field 
    public GameObject instructionObject; // Instructions
    public Text message; // Instruction text
    public GameObject[] objectsToHide; // Objects to hide
    public GameObject[] objectsToShow; // Objects to show

    public bool IsParticipantInputVisible =>
        submitButton != null && submitButton.gameObject.activeInHierarchy;

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
            // Hide only the participant-ID UI branch. CameraPoseSender also lives
            // on SetupModule, so disabling this whole GameObject would silently
            // stop gaze sampling as soon as the tutorial objects become visible.
            HideSetupInterface();
            // Show instruction object.
            instructionObject.SetActive(true);
            // Hide objects.
            HideObjects();
            // Show objects.
            ShowObjects();
            AppendExitMovementInstruction();
        }
    }

    private void HideSetupInterface()
    {
        if (submitButton == null)
        {
            Debug.LogWarning("[ToSetup] Submit button is missing; keeping SetupModule active so runtime tracking continues.");
            return;
        }

        Transform uiBranch = submitButton.transform;
        while (uiBranch.parent != null && uiBranch.parent != transform)
        {
            uiBranch = uiBranch.parent;
        }

        if (uiBranch.parent == transform)
        {
            uiBranch.gameObject.SetActive(false);
            Debug.Log("[ToSetup] Participant-ID UI hidden; SetupModule remains active for gaze tracking.");
            return;
        }

        // Fallback for scenes whose input UI is not grouped under SetupModule.
        submitButton.gameObject.SetActive(false);
        if (inputField != null)
        {
            inputField.gameObject.SetActive(false);
        }
        Debug.LogWarning("[ToSetup] Could not resolve the setup UI branch; hid the input controls individually.");
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

    private void AppendExitMovementInstruction()
    {
        if (instructionObject == null)
        {
            return;
        }

        TMP_Text[] instructionTexts = instructionObject.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text instructionText in instructionTexts)
        {
            if (instructionText == null ||
                instructionText.text.IndexOf("Exit", System.StringComparison.OrdinalIgnoreCase) < 0 ||
                instructionText.text.Contains("press B on the right controller"))
            {
                continue;
            }

            instructionText.text += ExitMovementInstruction;
        }
    }

}
