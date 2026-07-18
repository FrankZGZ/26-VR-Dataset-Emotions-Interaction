using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
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
    [Tooltip("Move the XR rig to the scene's original questionnaire viewing point. Keep disabled during VR gameplay so the participant camera height/position is never changed by the survey trigger.")]
    public bool repositionRigForSurvey = false;
    [Tooltip("Legacy behavior that teleports the rig and resets scene objects. Keep disabled when showing the in-scene SAM/ASAQ UI.")]
    public bool applyLegacySceneResetAfterSurvey = false;
    private bool questionnaireTriggered;
    private float colliderTriggerArmedAt;

    private void Awake()
    {
        // Every immersive scene already stores an authored, unobstructed survey
        // viewpoint. Restore that original flow so entering Exit never leaves
        // the participant at a doorway with the door between them and SAM.
        if (ShouldUseAuthoredSurveyViewpoint(SceneManager.GetActiveScene().name))
        {
            repositionRigForSurvey = true;
        }

        if (hideQuestionnaireOnStart)
        {
            HideQuestionnaireOnSceneEntry();
            StartCoroutine(HideQuestionnaireStartupObjectsForOpeningFrames());
        }
    }

    private static bool ShouldUseAuthoredSurveyViewpoint(string sceneName)
    {
        return string.Equals(sceneName, "Tutorial_Interaction", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Lake", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Attic", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Puppies", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "SolitaryConfinement", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Tunnel", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Elephant", System.StringComparison.OrdinalIgnoreCase);
    }

    private void Start()
    {
        // Some scenes (notably Tunnel) spawn the XR rig inside a large exit
        // trigger. Ignore that initial overlap so SAM does not appear at startup.
        colliderTriggerArmedAt = Time.unscaledTime + 1.5f;

        if (hideQuestionnaireOnStart)
        {
            HideQuestionnaireOnSceneEntry();
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
            IsBroadTunnelDoorTrigger() ||
            !IsPlayerCollider(other) ||
            !IsExitAvailable())
        {
            return;
        }

        TriggerQuestionnaire("collider " + other.name);
    }

    private bool IsBroadTunnelDoorTrigger()
    {
        return string.Equals(SceneManager.GetActiveScene().name, "Tunnel", System.StringComparison.OrdinalIgnoreCase) &&
               string.Equals(gameObject.name, "ExitDoor", System.StringComparison.OrdinalIgnoreCase);
    }

    private bool IsExitAvailable()
    {
        SceneController controller = sceneController != null
            ? sceneController.GetComponent<SceneController>()
            : FindObjectOfType<SceneController>();

        if (controller == null)
        {
            Debug.LogWarning("[Survey] SceneController not found; accepting the physical Exit trigger.");
            return true;
        }

        if (!controller.SceneConditionsMet)
        {
            Debug.Log("[Survey] Ignored Exit overlap because the one-minute/task gate has not opened yet.");
            return false;
        }

        return true;
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

        HideConversationalAvatarsForSurvey();
        if (repositionRigForSurvey)
        {
            TeleportToOrigin();
        }
        HandleTeleportPadInteraction();
    }

    public void TriggerQuestionnaireFromGuidedTask()
    {
        TriggerQuestionnaire("guided task completion");
    }

    public void HideQuestionnaireForGuidedTask()
    {
        HideQuestionnaireOnSceneEntry();
    }

    private static void HideConversationalAvatarsForSurvey()
    {
        VrmeAtticClient[] avatarClients = FindObjectsByType<VrmeAtticClient>(FindObjectsSortMode.None);
        int hiddenCount = 0;
        foreach (VrmeAtticClient avatarClient in avatarClients)
        {
            if (avatarClient == null || !avatarClient.gameObject.activeSelf)
            {
                continue;
            }

            avatarClient.gameObject.SetActive(false);
            hiddenCount++;
        }

        Debug.Log("[Survey] Hidden conversational avatars for questionnaire visibility. count=" + hiddenCount);
    }

    private void HideQuestionnaireOnSceneEntry()
    {
        if (samTask != null)
        {
            samTask.SetActive(false);
        }

        if (leftTaskRay != null)
        {
            leftTaskRay.SetActive(false);
        }

        if (rightTaskRay != null)
        {
            rightTaskRay.SetActive(false);
        }

        HideNamedQuestionnaireObjectsInLoadedScene();
        HideStartupRayComponentsInLoadedScene();
    }

    private IEnumerator HideQuestionnaireStartupObjectsForOpeningFrames()
    {
        float endTime = Time.unscaledTime + 1.5f;
        while (Time.unscaledTime < endTime)
        {
            HideQuestionnaireOnSceneEntry();
            yield return null;
        }
    }

    private static void HideNamedQuestionnaireObjectsInLoadedScene()
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj == null || !obj.scene.IsValid() || !obj.scene.isLoaded)
            {
                continue;
            }

            if (!IsStartupHiddenObjectName(obj.name) || !obj.activeSelf)
            {
                continue;
            }

            GameObject objectToHide = ResolveStartupHiddenRoot(obj);
            if (objectToHide != null && objectToHide.activeSelf)
            {
                objectToHide.SetActive(false);
                Debug.Log("[Survey] Hidden startup UI/ray object on scene entry: " + objectToHide.name);
            }
        }
    }

    private static void HideStartupRayComponentsInLoadedScene()
    {
        Behaviour[] behaviours = Resources.FindObjectsOfTypeAll<Behaviour>();
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour == null || !behaviour.gameObject.scene.IsValid() || !behaviour.gameObject.scene.isLoaded)
            {
                continue;
            }

            if (IsStartupRayVisualBehaviour(behaviour) && behaviour.enabled)
            {
                behaviour.enabled = false;
                Debug.Log("[Survey] Disabled startup ray behaviour: " + behaviour.GetType().Name + " on " + behaviour.gameObject.name);
            }
        }

        LineRenderer[] lineRenderers = Resources.FindObjectsOfTypeAll<LineRenderer>();
        foreach (LineRenderer lineRenderer in lineRenderers)
        {
            if (lineRenderer == null || !lineRenderer.gameObject.scene.IsValid() || !lineRenderer.gameObject.scene.isLoaded)
            {
                continue;
            }

            if (!IsQuestionnaireTaskRay(lineRenderer.gameObject) && lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
                Debug.Log("[Survey] Disabled startup ray line renderer on " + lineRenderer.gameObject.name);
            }
        }
    }

    private static bool IsStartupRayVisualBehaviour(Behaviour behaviour)
    {
        string typeName = behaviour.GetType().Name;
        return (typeName.IndexOf("LineVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("RayVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("InteractorDebugVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("TubeRenderer", System.StringComparison.OrdinalIgnoreCase) >= 0) &&
               !IsQuestionnaireTaskRay(behaviour.gameObject);
    }

    private static bool IsQuestionnaireTaskRay(GameObject obj)
    {
        Transform current = obj != null ? obj.transform : null;
        while (current != null)
        {
            if (string.Equals(current.name, "LeftXRTaskRay", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.name, "RightXRTaskRay", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsLikelyStartupRayHierarchy(Transform transformToCheck)
    {
        Transform current = transformToCheck;
        while (current != null)
        {
            string objectName = current.name;
            if (!string.IsNullOrWhiteSpace(objectName) &&
                (objectName.IndexOf("Ray", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 objectName.IndexOf("Cursor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 objectName.IndexOf("Pointer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 objectName.IndexOf("Interactor", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsStartupHiddenObjectName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        return string.Equals(objectName, "SAMTask", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "SurveySAM", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "SurveySlider", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "LeftXRTaskRay", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "RightXRTaskRay", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "ArcVisual", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "ArcVisuals", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "ProceduralArc", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "InteractorDebugVisual", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "TurnVisuals", System.StringComparison.OrdinalIgnoreCase) ||
               objectName.IndexOf("ControllerRay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("HandRay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("RaycasterCursorVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("OculusCursor", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GameObject ResolveStartupHiddenRoot(GameObject obj)
    {
        if (string.Equals(obj.name, "LeftXRTaskRay", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.name, "RightXRTaskRay", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.name, "ArcVisual", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.name, "ArcVisuals", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.name, "ProceduralArc", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.name, "InteractorDebugVisual", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(obj.name, "TurnVisuals", System.StringComparison.OrdinalIgnoreCase) ||
            obj.name.IndexOf("ControllerRay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            obj.name.IndexOf("HandRay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            obj.name.IndexOf("RaycasterCursorVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            obj.name.IndexOf("OculusCursor", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return obj;
        }

        Transform current = obj.transform;
        while (current != null)
        {
            if (string.Equals(current.name, "SAMTask", System.StringComparison.OrdinalIgnoreCase))
            {
                return current.gameObject;
            }

            current = current.parent;
        }

        return obj;
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
            PositionQuestionnaireInFrontOfView();
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
        StartCoroutine(ReassertQuestionnaireRaysForOpeningFrames());

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

    private void PositionQuestionnaireInFrontOfView()
    {
        if (samTask == null)
        {
            return;
        }

        // Scenes that provide an authored survey viewing point use the original
        // project flow: keep the questionnaire at its authored transform and
        // move the XR rig to teleportTargetPosition instead.
        if (repositionRigForSurvey)
        {
            Debug.Log("[Survey] Using authored questionnaire placement for " +
                SceneManager.GetActiveScene().name + "; the XR rig will move to the configured survey viewpoint.");
            return;
        }

        Transform view = ovrCameraRig != null && ovrCameraRig.centerEyeAnchor != null
            ? ovrCameraRig.centerEyeAnchor
            : (Camera.main != null ? Camera.main.transform : null);
        Canvas surveyCanvas = FindVisibleQuestionnaireCanvas();
        if (view == null || surveyCanvas == null)
        {
            Debug.LogWarning("[Survey] Could not place SAM in front of the headset; view or canvas was missing.");
            return;
        }

        Vector3 forward = view.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        RectTransform panel = surveyCanvas.transform as RectTransform;
        panel.position = view.position + forward * 1.35f;
        // Match the headset's horizontal viewing direction so the authored
        // world-space Canvas presents its front face to the participant.
        panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
        Debug.Log("[Survey] Positioned " + SceneManager.GetActiveScene().name +
            " SAM canvas " + surveyCanvas.name + " in front of headset at " + panel.position + ".");
    }

    private Canvas FindVisibleQuestionnaireCanvas()
    {
        if (samTask == null)
        {
            return null;
        }

        Canvas[] canvases = samTask.GetComponentsInChildren<Canvas>(true);
        Canvas firstActiveCanvas = null;
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (firstActiveCanvas == null)
            {
                firstActiveCanvas = canvas;
            }

            if (canvas.name.IndexOf("SAM", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return canvas;
            }
        }

        // Prefer an active page. In the prison scene SurveySlider is the first
        // serialized Canvas but is intentionally inactive while SurveySAM is
        // the page that must be shown.
        return firstActiveCanvas != null
            ? firstActiveCanvas
            : (canvases.Length > 0 ? canvases[0] : null);
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

        foreach (Behaviour behaviour in taskRay.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }

        foreach (Renderer renderer in taskRay.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null)
            {
                renderer.enabled = true;
            }
        }

        Debug.Log("[Survey] Enabled " + side + " task ray. activeSelf=" +
            taskRay.activeSelf + ", activeInHierarchy=" + taskRay.activeInHierarchy);
    }

    private IEnumerator ReassertQuestionnaireRaysForOpeningFrames()
    {
        // A startup ray suppressor can already be part-way through its frame
        // when SAM is activated. Reassert the dedicated UI rays afterwards.
        for (int frame = 0; frame < 3; frame++)
        {
            yield return new WaitForEndOfFrame();
            EnableQuestionnaireRay(leftTaskRay, "left");
            EnableQuestionnaireRay(rightTaskRay, "right");
        }
    }

    private void TeleportToOrigin()
    {
        if (ovrCameraRig == null)
        {
            Debug.LogWarning("OVRCameraRig is not assigned; questionnaire opened without repositioning the rig.");
            return;
        }

        // 位置
        Vector3 previousPosition = ovrCameraRig.transform.position;
        ovrCameraRig.transform.position = teleportTargetPosition;

        // 朝向
        ovrCameraRig.transform.rotation = Quaternion.Euler(0, teleportTargetYRotation, 0);
        Debug.Log("[Survey] Repositioned XR rig for questionnaire from " + previousPosition +
            " to " + ovrCameraRig.transform.position + ", yaw=" + teleportTargetYRotation + ".");
    }

}
