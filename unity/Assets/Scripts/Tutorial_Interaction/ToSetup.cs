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
            if (obj.name.StartsWith("BlueCube", System.StringComparison.OrdinalIgnoreCase))
            {
                PlaceGrabCubeAboveGround(obj);
            }
        }
    }

    private static void PlaceGrabCubeAboveGround(GameObject cube)
    {
        GameObject ground = GameObject.Find("Ground");
        Collider cubeCollider = cube.GetComponent<Collider>();
        if (ground == null || cubeCollider == null)
        {
            Debug.LogWarning("[Tutorial] Could not repair grab-cube height for " + cube.name +
                ": Ground or Collider was not found.");
            return;
        }

        // The Editor can restore an unsaved Temp/__Backupscenes copy containing
        // the old near-zero cube Y. Position from the collider bottom instead of
        // trusting serialized coordinates, leaving a 20 cm grab clearance.
        const float grabClearance = 0.20f;
        Physics.SyncTransforms();
        float targetBottomY = ground.transform.position.y + grabClearance;
        float correctionY = targetBottomY - cubeCollider.bounds.min.y;
        cube.transform.position += Vector3.up * correctionY;
        Physics.SyncTransforms();

        Rigidbody body = cube.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        TutorialGrabCubeAnchor anchor = cube.GetComponent<TutorialGrabCubeAnchor>();
        if (anchor == null)
            anchor = cube.AddComponent<TutorialGrabCubeAnchor>();
        anchor.InitializeAtCurrentPose();

        Debug.Log("[Tutorial] Grab cube placed above ground. name=" + cube.name +
            ", centerY=" + cube.transform.position.y.ToString("0.00") +
            ", colliderBottomY=" + cubeCollider.bounds.min.y.ToString("0.00") + ".");
    }

}
