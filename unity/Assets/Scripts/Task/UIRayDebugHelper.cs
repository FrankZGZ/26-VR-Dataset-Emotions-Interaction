using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro; // Add TMPro namespace for TMP_InputField support

public class UIRayDebugHelper : MonoBehaviour
{
    public XRRayInteractor rayInteractor;
    public bool logUiHits = false;

    void Update()
    {
        if (rayInteractor == null) return;

        if (rayInteractor.TryGetCurrentUIRaycastResult(out RaycastResult result))
        {
            if (logUiHits)
            {
                Debug.Log("[UI Hit] Name: " + result.gameObject.name);
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
            {
               // Debug.Log("[OVR UI Press] Triggered on: " + result.gameObject.name);

                
                Toggle toggle = result.gameObject.GetComponentInParent<Toggle>();

                if (toggle != null && toggle.interactable)
                {
                    toggle.isOn = !toggle.isOn; // Manually switch the Toggle state
                   // Debug.Log("[Toggle] New State: " + toggle.isOn);
                }
                else
                {
                    // Check for TMP_InputField first
                    TMP_InputField tmpInputField = result.gameObject.GetComponentInParent<TMP_InputField>();
                    if (tmpInputField != null && tmpInputField.interactable)
                    {
                        tmpInputField.Select(); // Focus the input field
                        tmpInputField.ActivateInputField(); // Activate for text input
                        Debug.Log("[TMP_InputField] Activated: " + tmpInputField.name);
                    }
                    else
                    {
                        // Check for regular InputField
                        InputField inputField = result.gameObject.GetComponentInParent<InputField>();
                        if (inputField != null && inputField.interactable)
                        {
                            inputField.Select(); // Focus the input field
                            inputField.ActivateInputField(); // Activate for text input
                            Debug.Log("[InputField] Activated: " + inputField.name);
                        }
                        else
                        {
                            Button button = result.gameObject.GetComponentInParent<Button>();
                            if (button != null && button.interactable)
                            {
                                button.onClick.Invoke();
                                Debug.Log("[Button] Clicked: " + button.name);
                            }
                            else
                            {
                                Debug.LogWarning("UI is not Toggle, InputField, or Button, or it is not interactable");
                            }
                        }
                    }
                }
            }
        }
    }
}
