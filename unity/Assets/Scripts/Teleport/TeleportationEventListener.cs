using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;
public class TeleportationEventListener : MonoBehaviour
{
    public GameObject locomotionSystem;
    public GameObject player;
    public GameObject sceneController;
    public GameObject samTask;
    public GameObject leftTaskRay;
    public GameObject rightTaskRay;
    public OVRCameraRig ovrCameraRig;     
    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider teleportationProvider;
    public Vector3 teleportTargetPosition = Vector3.zero; 
    public float teleportTargetYRotation = 0f; 
    public GameObject[] disableAfterTeleport;
    public GameObject[] destroyAfterTeleport;
    public GameObject[] refreshAfterTeleport;
    public bool hideQuestionnaireOnStart = true;
    [Header("Keyboard testing")]
    public bool enableKeyboardExitTrigger = true;
    public KeyCode keyboardExitTriggerKey = KeyCode.E;
    [Tooltip("Move the XR rig to the scene's original questionnaire viewing point.")]
    public bool repositionRigForSurvey = true;
    [Tooltip("Legacy behavior that teleports the rig and resets scene objects. Keep disabled when showing the in-scene SAM/ASAQ UI.")]
    public bool applyLegacySceneResetAfterSurvey = false;
    private bool questionnaireTriggered;
    private float colliderTriggerArmedAt;

    private void Start()
    {
        // Some scenes (notably Tunnel) spawn the XR rig inside a large exit
        // trigger. Ignore that initial overlap so SAM does not appear at startup.
        colliderTriggerArmedAt = Time.unscaledTime + 1.5f;

        if (hideQuestionnaireOnStart && samTask != null)
        {
            samTask.SetActive(false);
        }

        if (locomotionSystem != null)
        {
            teleportationProvider = locomotionSystem.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
        }

        if (teleportationProvider == null)
        {
            teleportationProvider = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
        }

        if (teleportationProvider != null)
        {
            teleportationProvider.endLocomotion += OnEndLocomotion;
            Debug.Log("Teleportation provider setup succeeded!");
        }
        else
        {
            Debug.Log("Teleportation provider not found!");
        }
    }

    private void OnDestroy()
    {
        if (teleportationProvider != null)
        {
            teleportationProvider.endLocomotion -= OnEndLocomotion;
        }
    }

    private void Update()
    {
        if (enableKeyboardExitTrigger &&
            Input.GetKeyDown(keyboardExitTriggerKey) &&
            !questionnaireTriggered &&
            (samTask == null || !samTask.activeInHierarchy))
        {
            TriggerQuestionnaire("keyboard " + keyboardExitTriggerKey);
        }
    }

    private void OnEndLocomotion(LocomotionSystem system)
    {
        // This method is called when teleportation (or any other locomotion) ends.
        Debug.Log(string.Format("Teleported to {0:s}", player.transform.position.ToString()));
        // Insert any additional logic you want to happen after teleporting here.
    }

    // Triggered with colliders. 
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Collider Event Triggered! collider=" + other.name + ", tag=" + other.tag);

        if (Time.unscaledTime < colliderTriggerArmedAt ||
            questionnaireTriggered ||
            !IsPlayerCollider(other))
        {
            return;
        }

        TriggerQuestionnaire("collider " + other.name);
    }

    private void TriggerQuestionnaire(string source)
    {
        if (questionnaireTriggered || (samTask != null && samTask.activeInHierarchy))
        {
            return;
        }

        questionnaireTriggered = true;
        Debug.Log("[Survey] Exit triggered via " + source);

        CameraPoseSender cameraPoseSender = FindObjectOfType<CameraPoseSender>();
        if (cameraPoseSender != null)
        {
            cameraPoseSender.EndSessionAndSendData();
            Debug.Log("Camera pose data saved before starting SAM survey!");
        }
        else
        {
            Debug.LogWarning("CameraPoseSender not found in the scene!");
        }

        HandleTeleportPadInteraction();
        if (repositionRigForSurvey)
        {
            TeleportToOrigin();
        }
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        if (other.CompareTag("Player"))
        {
            return true;
        }

        Transform colliderTransform = other.transform;
        if (player != null &&
            (colliderTransform == player.transform ||
             colliderTransform.IsChildOf(player.transform) ||
             player.transform.IsChildOf(colliderTransform)))
        {
            return true;
        }

        if (ovrCameraRig != null && colliderTransform.IsChildOf(ovrCameraRig.transform))
        {
            return true;
        }

        Rigidbody attachedBody = other.attachedRigidbody;
        return attachedBody != null &&
               ovrCameraRig != null &&
               attachedBody.transform.IsChildOf(ovrCameraRig.transform);
    }

    
    private void HandleTeleportPadInteraction()
    {
        // The XR player interacts with the teleportation pad.
        Debug.Log("Interacted with Teleport Pad!");

        //  End the scene and start emotion survey.
        // sceneController.GetComponent<SceneController>().StartEmotionSurvey();

        // Activate the SAMTask object.
        if (samTask != null)
        {
            samTask.SetActive(true);
            Debug.Log("SAMTask GameObject activated");
        }
        else
        {
            Debug.LogError("samTask reference is null! Please assign it in the Inspector.");
        }
        
        // Activate all parts of the dedicated questionnaire rays. Merely setting
        // the child GameObject active is insufficient when an ancestor, line
        // renderer, or ray behaviour has been disabled by the gameplay setup.
        EnableQuestionnaireRay(leftTaskRay, "left");
        EnableQuestionnaireRay(rightTaskRay, "right");

        if (applyLegacySceneResetAfterSurvey)
        {
            // Legacy scene-transition behavior. This remains available for old flows,
            // but must stay disabled for the in-scene SAM/ASAQ questionnaire.
            if (disableAfterTeleport != null)
            {
                foreach (GameObject obj in disableAfterTeleport)
                {
                    if (obj != null)
                    {
                        obj.SetActive(false);
                    }
                }
            }

            if (destroyAfterTeleport != null)
            {
                foreach (GameObject obj in destroyAfterTeleport)
                {
                    if (obj != null)
                    {
                        Destroy(obj);
                    }
                }
            }

            if (refreshAfterTeleport != null)
            {
                foreach (GameObject obj in refreshAfterTeleport)
                {
                    if (obj != null)
                    {
                        Animator animator = obj.GetComponent<Animator>();
                        if (animator != null)
                        {
                            animator.Rebind();
                            animator.Update(0f);
                        }
                    }
                }
            }
        }
    }

    private static void EnableQuestionnaireRay(GameObject taskRay, string side)
    {
        if (taskRay == null)
        {
            Debug.LogWarning("[Survey] " + side + " task ray is not assigned.");
            return;
        }

        Transform current = taskRay.transform.parent;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }
            current = current.parent;
        }

        taskRay.SetActive(true);

        foreach (Behaviour behaviour in taskRay.GetComponents<Behaviour>())
        {
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }

        foreach (Renderer renderer in taskRay.GetComponents<Renderer>())
        {
            if (renderer != null)
            {
                renderer.enabled = true;
            }
        }

        Debug.Log("[Survey] Enabled " + side + " task ray. activeSelf=" +
            taskRay.activeSelf + ", activeInHierarchy=" + taskRay.activeInHierarchy);
    }

    private void TeleportToOrigin()
    {
        if (ovrCameraRig == null)
        {
            Debug.LogWarning("OVRCameraRig is not assigned; questionnaire opened without repositioning the rig.");
            return;
        }

        // 位置
        ovrCameraRig.transform.position = teleportTargetPosition;

        // 朝向
        ovrCameraRig.transform.rotation = Quaternion.Euler(0, teleportTargetYRotation, 0);
    }

}
