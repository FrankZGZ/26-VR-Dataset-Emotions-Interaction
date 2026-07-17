using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the real conversational VRME avatar to the interaction tutorial and
/// teaches the right-controller A-button push-to-talk gesture.
/// </summary>
public class TutorialAvatarGuide : MonoBehaviour
{
    [Header("Tutorial flow")]
    [SerializeField] private GameObject waitUntilHidden;
    [SerializeField] private float startDelay = 0.5f;

    [Header("Placement")]
    [SerializeField] private GameObject avatarPrefab;
    [SerializeField] private Transform spawnBeside;
    [SerializeField] private float besideOffset = 0.8f;
    [SerializeField] private float fallbackDistance = 1.25f;

    [Header("UI copy")]
    [SerializeField] private float uiHeightAboveAvatar = 3.15f;
    [SerializeField] private string readyMessage =
        "Talk to the avatar\n\nHold A on the right controller while you speak.\nRelease A when you finish.";
    [SerializeField] private string listeningMessage =
        "Listening...\n\nKeep holding A while you speak.";
    [SerializeField] private string completeMessage =
        "Great!\n\nHold A whenever you want to talk to the avatar.";

    private GameObject avatarInstance;
    private Transform uiRoot;
    private TMP_Text instructionText;
    private bool wasPressed;
    private bool isReady;

    private IEnumerator Start()
    {
        // ToSetup disables LoginCanvas after the participant ID is submitted.
        while (waitUntilHidden != null && waitUntilHidden.activeInHierarchy)
            yield return null;

        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        Camera viewer = FindViewerCamera();
        if (viewer == null)
        {
            Debug.LogWarning("[TutorialAvatarGuide] No active viewer camera was found.");
            yield break;
        }

        SpawnAvatar(viewer.transform);
        if (avatarInstance == null)
            yield break;

        CreateInstructionUI();
        isReady = true;
    }

    private void Update()
    {
        if (!isReady || instructionText == null)
            return;

        bool isPressed = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        if (isPressed && !wasPressed)
            instructionText.text = listeningMessage;
        else if (!isPressed && wasPressed)
            instructionText.text = completeMessage;

        wasPressed = isPressed;
    }

    private void LateUpdate()
    {
        if (uiRoot == null || avatarInstance == null)
            return;

        Camera viewer = FindViewerCamera();
        if (viewer == null)
            return;

        uiRoot.position = avatarInstance.transform.position + Vector3.up * uiHeightAboveAvatar;
        Vector3 towardViewer = viewer.transform.position - uiRoot.position;
        towardViewer.y = 0f;
        if (towardViewer.sqrMagnitude > 0.001f)
            uiRoot.rotation = Quaternion.LookRotation(-towardViewer.normalized, Vector3.up);
    }

    private void SpawnAvatar(Transform viewer)
    {
        if (avatarPrefab == null)
        {
            Debug.LogError("[TutorialAvatarGuide] The VRME avatar prefab is not assigned.");
            return;
        }

        Vector3 viewerForward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up).normalized;
        if (viewerForward.sqrMagnitude < 0.01f)
            viewerForward = Vector3.forward;

        Vector3 position;
        if (spawnBeside != null)
        {
            Vector3 towardViewer = Vector3.ProjectOnPlane(
                viewer.position - spawnBeside.position, Vector3.up).normalized;
            if (towardViewer.sqrMagnitude < 0.01f)
                towardViewer = -viewerForward;

            Vector3 side = Vector3.Cross(Vector3.up, towardViewer).normalized;
            position = spawnBeside.position + side * besideOffset;
        }
        else
        {
            position = viewer.position + viewerForward * fallbackDistance;
        }

        // Ground alignment on the VRME prefab performs the final vertical correction.
        position.y = 0f;
        Vector3 faceViewer = Vector3.ProjectOnPlane(viewer.position - position, Vector3.up).normalized;
        Quaternion rotation = faceViewer.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(faceViewer, Vector3.up)
            : Quaternion.identity;

        avatarInstance = Instantiate(avatarPrefab, position, rotation);
        avatarInstance.name = "Tutorial VRME Conversation Avatar";

        // Keep the tutorial-authored position beside the grab objects. The
        // controller still supplies dominance posture/facing cues.
        AvatarDominanceBehaviorController behavior =
            avatarInstance.GetComponent<AvatarDominanceBehaviorController>();
        if (behavior != null)
        {
            behavior.placeFromHeadsetPose = false;
            behavior.minimumDistance = 0f;
            behavior.maximumDistance = 100f;
        }
    }

    private void CreateInstructionUI()
    {
        GameObject canvasObject = new GameObject(
            "Avatar Push-To-Talk UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        uiRoot = canvasObject.transform;
        uiRoot.position = avatarInstance.transform.position + Vector3.up * uiHeightAboveAvatar;
        uiRoot.localScale = Vector3.one * 0.0025f;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(820f, 280f);

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(uiRoot, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelObject.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.09f, 0.94f);

        GameObject textObject = new GameObject(
            "Instruction Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(42f, 24f);
        textRect.offsetMax = new Vector2(-42f, -24f);

        instructionText = textObject.GetComponent<TextMeshProUGUI>();
        instructionText.text = readyMessage;
        instructionText.fontSize = 38f;
        instructionText.color = Color.white;
        instructionText.alignment = TextAlignmentOptions.Center;
        instructionText.enableAutoSizing = true;
        instructionText.fontSizeMin = 24f;
        instructionText.fontSizeMax = 38f;
    }

    private static Camera FindViewerCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        foreach (Camera camera in Camera.allCameras)
        {
            if (camera.isActiveAndEnabled)
                return camera;
        }

        return null;
    }
}
