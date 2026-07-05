using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.SceneManagement;
using System.IO;

public class SAMSurveyEvents : MonoBehaviour
{
    public ToggleGroup valenceButtons;
    public ToggleGroup arousalButtons;
    public ToggleGroup dominanceButtons;
    public Button submitButton;
    public Text debugInfo;

    [Header("Runtime Likert page")]
    public bool createLikertPageAtRuntime = true;
    public bool autoEnableTaskRaysOnSurveyStart = true;
    public bool allowControllerButtonSubmit = true;
    [Tooltip("Keep disabled to use the scene-authored SAMTask position from the original project.")]
    public bool positionQuestionnaireInFrontOfCamera = false;
    public Vector2 samNextButtonPosition = new Vector2(350f, -500f);
    public RectTransform likertRoot;
    public Vector2 likertPanelSize = new Vector2(980f, 720f);
    public Vector2 likertStartPosition = new Vector2(52f, -78f);
    public float likertItemSpacing = 46f;
    public Vector2 likertSliderSize = new Vector2(300f, 18f);
    public float likertWorldVerticalOffset = -2.0f;

    public GameObject[] disableAfterSubmit;
    public GameObject[] enableAfterSubmit;
    public bool hideQuestionnaireOnSceneStart = true;

    private Slider socialPresenceSlider;
    private Slider autonomySlider;
    private Slider agencySlider;
    private Slider helpfulnessSlider;
    private Slider trustSlider;
    private Slider intrusivenessSlider;
    private Slider attentionAccuracySlider;
    private Slider conversationNaturalnessSlider;
    private GameObject likertPanel;
    private Button samOverlaySubmitButton;
    private Button[] likertChoiceButtons;
    private Button likertSubmitButton;
    private readonly float[] likertQuestionValues = new float[] { 4f, 4f, 4f, 4f };
    private readonly string[] likertQuestionLabels = new string[]
    {
        "The emotions I felt during the interaction were caused by the agent.",
        "The agent had a distinctive character.",
        "The agent seemed to know what it was doing.",
        "The agent felt like a social entity."
    };
    private readonly List<GameObject> samPageChildren = new List<GameObject>();
    private bool isSubmitting;
    private bool likertPageVisible;
    private bool hiddenOnSceneStart;

    private void Awake()
    {
        RemoveLegacyLoadingVisuals();

        // The parent SAMTask is already inactive in each experiment scene and is
        // enabled by TeleportationEventListener. Disabling this child here would
        // immediately hide it again on its first activation.
        hiddenOnSceneStart = true;
    }

    private void RemoveLegacyLoadingVisuals()
    {
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant == null || descendant == transform)
            {
                continue;
            }

            if (descendant.name == "LoadingIcon" || descendant.name == "LoadingCircle")
            {
                descendant.gameObject.SetActive(false);
                Destroy(descendant.gameObject);
            }
        }
    }

    private void OnEnable()
    {
        MoveSurveyInFrontOfCamera();
    }

    private void Start()
    {
        Debug.Log("[Survey] SAM survey start.");
        MoveSurveyInFrontOfCamera();
        EnableTaskRaysIfNeeded();
        CacheSamPageChildren();
        MoveOriginalSubmitIntoView();
        CreateLikertPageIfNeeded();
        CreateSamOverlaySubmitIfNeeded();
        ShowSamPage();

        submitButton.onClick.AddListener(delegate { StartCoroutine(SubmitCurrentPage()); });

        string participantId = PlayerData.participantId;
        string directoryPath = Path.Combine(Application.persistentDataPath, "SurveyData", participantId);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            Debug.Log("[Survey] Created directory: " + directoryPath);
        }
    }

    private IEnumerator SubmitCurrentPage()
    {
        if (isSubmitting)
        {
            yield break;
        }

        if (likertPageVisible)
        {
            yield return SubmitLikertPage();
            yield break;
        }
        else
        {
            yield return SubmitSamPage();
        }
    }

    private IEnumerator SubmitSamPage()
    {
        Debug.Log("[Survey] SAM submit requested.");

        if (!valenceButtons.AnyTogglesOn() || !arousalButtons.AnyTogglesOn() || !dominanceButtons.AnyTogglesOn())
        {
            debugInfo.text = "Please answer the SAM questions.";
            Debug.LogWarning("[Survey] SAM submit blocked. valence=" + valenceButtons.AnyTogglesOn() +
                ", arousal=" + arousalButtons.AnyTogglesOn() +
                ", dominance=" + dominanceButtons.AnyTogglesOn());
            yield break;
        }

        isSubmitting = true;
        submitButton.interactable = false;
        SetSubmitText("Saving SAM ...");

        EmotionSurveySingle surveySingle = BuildBaseSurveyRecord("sam_only_v1");
        surveySingle.valenceValue = valenceButtons.GetComponent<RadioButtonEvents>().selectedToggleValue;
        surveySingle.arousalValue = arousalButtons.GetComponent<RadioButtonEvents>().selectedToggleValue;
        surveySingle.dominanceValue = dominanceButtons.GetComponent<RadioButtonEvents>().selectedToggleValue;
        SetLikertFields(surveySingle, -1f);

        string timeStr = System.DateTime.Now.ToString("dd-MMM_HH-mm-ss");
        bool saveSuccess = SaveSurveyDataToFile(JsonUtility.ToJson(surveySingle), surveySingle.participantId, surveySingle.sceneName, timeStr, "SAMSurvey");
        yield return new WaitForSeconds(0.25f);

        if (!saveSuccess)
        {
            isSubmitting = false;
            submitButton.interactable = true;
            SetSubmitText("Submit");
            debugInfo.text = "[Error] Could not save SAM data.";
            yield break;
        }

        Debug.Log("[Survey] SAM saved. Moving to Likert page.");
        ShowLikertPage();
        isSubmitting = false;
    }

    private IEnumerator SubmitLikertPage()
    {
        Debug.Log("[Survey] Likert submit requested.");
        isSubmitting = true;

        EmotionSurveySingle surveySingle = BuildBaseSurveyRecord("avatar_asaq_short_v1");
        surveySingle.valenceValue = -1f;
        surveySingle.arousalValue = -1f;
        surveySingle.dominanceValue = -1f;
        SetLikertFields(surveySingle, -1f);
        surveySingle.asaqUserEmotionPresenceValue = likertQuestionValues[0];
        surveySingle.asaqAgentPersonalityPresenceValue = likertQuestionValues[1];
        surveySingle.asaqAgentIntentionalityValue = likertQuestionValues[2];
        surveySingle.asaqSocialPresenceValue = likertQuestionValues[3];
        surveySingle.socialPresenceValue = surveySingle.asaqSocialPresenceValue;

        string timeStr = System.DateTime.Now.ToString("dd-MMM_HH-mm-ss");
        bool saveSuccess = SaveSurveyDataToFile(JsonUtility.ToJson(surveySingle), surveySingle.participantId, surveySingle.sceneName, timeStr, "LikertSurvey");
        yield return new WaitForSeconds(0.25f);

        if (!saveSuccess)
        {
            isSubmitting = false;
            debugInfo.text = "[Error] Could not save Likert data.";
            yield break;
        }

        Debug.Log("[Survey] Likert saved. Survey complete.");
        FinishSurvey();
    }

    private EmotionSurveySingle BuildBaseSurveyRecord(string surveyVersion)
    {
        return new EmotionSurveySingle
        {
            participantId = PlayerData.participantId,
            loginId = PlayerData.loginId,
            sessionId = PlayerData.sessionId,
            avatarCondition = PlayerData.avatarCondition,
            timestampUtcUnixMs = System.DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            sceneIndex = PlayerData.currentSceneIndex,
            sceneSequenceLength = PlayerData.sceneSequence != null ? PlayerData.sceneSequence.Length : 0,
            surveyVersion = surveyVersion
        };
    }

    private void SetLikertFields(EmotionSurveySingle surveySingle, float value)
    {
        surveySingle.socialPresenceValue = value;
        surveySingle.autonomyValue = value;
        surveySingle.agencyValue = value;
        surveySingle.helpfulnessValue = value;
        surveySingle.trustValue = value;
        surveySingle.intrusivenessValue = value;
        surveySingle.warmthValue = value;
        surveySingle.competenceValue = value;
        surveySingle.contextAwarenessValue = value;
        surveySingle.guidanceClarityValue = value;
        surveySingle.attentionAccuracyValue = value;
        surveySingle.conversationNaturalnessValue = value;
        surveySingle.asaqUserEmotionPresenceValue = value;
        surveySingle.asaqAgentPersonalityPresenceValue = value;
        surveySingle.asaqAgentIntentionalityValue = value;
        surveySingle.asaqSocialPresenceValue = value;
    }

    private bool SaveSurveyDataToFile(string jsonData, string participantId, string sceneName, string timeStr, string prefix)
    {
        try
        {
            string fileName = prefix + "_" + participantId + "_" + sceneName + "_" + timeStr + ".json";
            string directoryPath = Path.Combine(Application.persistentDataPath, "SurveyData", participantId);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string filePath = Path.Combine(directoryPath, fileName);
            File.WriteAllText(filePath, jsonData);
            Debug.Log("[Survey] Data saved to: " + filePath);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Survey] Failed to save survey data: " + e.Message);
            return false;
        }
    }

    private void ShowSamPage()
    {
        likertPageVisible = false;
        MoveSurveyInFrontOfCamera();
        SetSamPageVisible(true);
        if (likertPanel != null)
        {
            likertPanel.SetActive(false);
        }

        submitButton.gameObject.SetActive(true);
        submitButton.interactable = true;
        if (samOverlaySubmitButton != null)
        {
            samOverlaySubmitButton.gameObject.SetActive(true);
            samOverlaySubmitButton.interactable = true;
        }

        SetSubmitText("NEXT");
        if (debugInfo != null)
        {
            debugInfo.text = "";
        }
    }

    private void ShowLikertPage()
    {
        likertPageVisible = true;
        MoveSurveyInFrontOfCamera();
        if (positionQuestionnaireInFrontOfCamera)
        {
            transform.position += Vector3.up * likertWorldVerticalOffset;
        }
        SetSamPageVisible(false);
        submitButton.gameObject.SetActive(false);
        if (samOverlaySubmitButton != null)
        {
            samOverlaySubmitButton.gameObject.SetActive(false);
        }

        if (likertPanel != null)
        {
            UpdateLikertTable();
            likertPanel.SetActive(true);
            likertPanel.transform.SetAsLastSibling();
            Debug.Log("[Survey] Likert page visible. panel=" + likertPanel.name + ", active=" + likertPanel.activeInHierarchy);
        }
        else
        {
            Debug.LogError("[Survey] Likert page requested but likertPanel is missing.");
        }

        if (debugInfo != null)
        {
            debugInfo.text = "";
        }
    }

    private void CacheSamPageChildren()
    {
        samPageChildren.Clear();
        foreach (Transform child in transform)
        {
            if (child.GetComponent<EventSystem>() != null)
            {
                continue;
            }

            if (likertPanel == null || child.gameObject != likertPanel)
            {
                samPageChildren.Add(child.gameObject);
            }
        }
    }

    private void SetSamPageVisible(bool visible)
    {
        foreach (GameObject child in samPageChildren)
        {
            if (child != null)
            {
                child.SetActive(visible);
            }
        }
    }

    private void FinishSurvey()
    {
        if (disableAfterSubmit != null)
        {
            foreach (GameObject obj in disableAfterSubmit)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    Debug.Log("[Survey] Disabled: " + obj.name);
                }
            }
        }

        // Tutorial has no follow-up task list. Move straight to the passthrough
        // baseline; Real's scene loader will advance after its configured delay.
        // Do this before enabling TaskLoader to avoid two competing scene loads.
        if (SceneManager.GetActiveScene().name == "Tutorial_Interaction")
        {
            Debug.Log("[Survey] Tutorial complete. Loading Real baseline.");
            SceneManager.LoadScene("Real");
            return;
        }

        if (enableAfterSubmit != null)
        {
            foreach (GameObject obj in enableAfterSubmit)
            {
                if (obj != null)
                {
                    obj.SetActive(true);
                    Debug.Log("[Survey] Enabled: " + obj.name);
                }
            }
        }

        GameObject centerEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
        if (centerEyeAnchor != null)
        {
            transform.position = centerEyeAnchor.transform.position;
        }
    }

    private void MoveSurveyInFrontOfCamera()
    {
        if (!positionQuestionnaireInFrontOfCamera)
        {
            return;
        }

        Transform centerEyeAnchor = FindCenterEyeAnchor();
        if (centerEyeAnchor == null)
        {
            return;
        }

        Vector3 horizontalForward = Vector3.ProjectOnPlane(centerEyeAnchor.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
        {
            horizontalForward = transform.forward;
            horizontalForward.y = 0f;
        }

        if (horizontalForward.sqrMagnitude < 0.001f)
        {
            horizontalForward = Vector3.forward;
        }

        horizontalForward.Normalize();
        // Keep the world-space questionnaire at eye height. Adding 1.85 m to the
        // eye position placed it above the participant's visible field.
        transform.position = centerEyeAnchor.position + horizontalForward * 1.6f;
        transform.rotation = Quaternion.LookRotation(horizontalForward, Vector3.up);
    }

    private static Transform FindCenterEyeAnchor()
    {
        GameObject centerEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
        if (centerEyeAnchor != null)
        {
            return centerEyeAnchor.transform;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private void SetSubmitText(string text)
    {
        SetButtonText(submitButton, text);
    }

    private static void SetButtonText(Button button, string text)
    {
        if (button == null)
        {
            return;
        }

        Text submitText = button.GetComponentInChildren<Text>();
        if (submitText != null)
        {
            submitText.text = text;
        }
    }

    private static void SetButtonTextColor(Button button, Color color)
    {
        if (button == null)
        {
            return;
        }

        Text submitText = button.GetComponentInChildren<Text>();
        if (submitText != null)
        {
            submitText.color = color;
        }
    }

    private static void SetButtonBackground(Button button, Color color)
    {
        if (button == null)
        {
            return;
        }

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
            image.raycastTarget = true;
        }
    }

    private void MoveOriginalSubmitIntoView()
    {
        if (submitButton == null)
        {
            return;
        }

        RectTransform rect = submitButton.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = samNextButtonPosition;
        rect.sizeDelta = new Vector2(240f, 64f);
        submitButton.transform.SetAsLastSibling();

        Image image = submitButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = new Color(0.15f, 0.35f, 0.8f, 1f);
            image.raycastTarget = true;
        }
    }

    private void EnableTaskRaysIfNeeded()
    {
        if (!autoEnableTaskRaysOnSurveyStart)
        {
            return;
        }

        EnableNamedTaskRay("LeftXRTaskRay");
        EnableNamedTaskRay("RightXRTaskRay");
    }

    private static void EnableNamedTaskRay(string objectName)
    {
        GameObject taskRay = GameObject.Find(objectName);
        if (taskRay == null)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform candidate in transforms)
            {
                if (candidate != null && candidate.name == objectName)
                {
                    taskRay = candidate.gameObject;
                    break;
                }
            }
        }

        if (taskRay != null && !taskRay.activeSelf)
        {
            taskRay.SetActive(true);
            Debug.Log("[Survey] Enabled task ray: " + objectName);
        }

        if (taskRay != null)
        {
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

            Debug.Log("[Survey] Task ray state " + objectName +
                ": activeSelf=" + taskRay.activeSelf +
                ", activeInHierarchy=" + taskRay.activeInHierarchy);
        }
    }

    private void CreateLikertPageIfNeeded()
    {
        if (!createLikertPageAtRuntime)
        {
            return;
        }

        RectTransform root = likertRoot != null ? likertRoot : transform as RectTransform;
        if (root == null)
        {
            Debug.LogWarning("[Survey] Could not create Likert page because no RectTransform root was found.");
            return;
        }

        Font font = GetDefaultUiFont();
        likertPanel = new GameObject("AvatarLikertPage");
        likertPanel.transform.SetParent(root, false);
        RectTransform panelRect = likertPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        Vector2 rootSize = root.rect.size;
        panelRect.sizeDelta = rootSize.x > 1f && rootSize.y > 1f ? rootSize : likertPanelSize;
        panelRect.localScale = Vector3.one;
        panelRect.localRotation = Quaternion.identity;
        panelRect.localPosition = new Vector3(panelRect.localPosition.x, panelRect.localPosition.y, 0f);

        likertPanel.AddComponent<CanvasRenderer>();
        Image background = likertPanel.AddComponent<Image>();
        background.color = Color.white;
        background.raycastTarget = false;

        float contentYOffset = 24f;

        Text title = CreateText(likertPanel.transform, font, "Avatar experience", new Vector2(38f, -22f + contentYOffset), new Vector2(520f, 38f), TextAnchor.MiddleLeft, 27);
        title.color = Color.black;
        Text instruction = CreateText(likertPanel.transform, font, "Select one score for each item. 1 = strongly disagree, 7 = strongly agree.", new Vector2(38f, -58f + contentYOffset), new Vector2(860f, 28f), TextAnchor.MiddleLeft, 16);
        instruction.color = Color.black;

        float labelX = 42f;
        float firstChoiceX = 506f;
        float headerY = -96f;
        float rowStartY = -146f;
        float rowSpacing = 86f;
        float choiceSpacing = 58f;
        Vector2 choiceSize = new Vector2(52f, 44f);

        Text lowText = CreateText(likertPanel.transform, font, "Disagree", new Vector2(firstChoiceX - 98f, headerY + contentYOffset), new Vector2(88f, 24f), TextAnchor.MiddleCenter, 13);
        lowText.color = Color.black;
        Text highText = CreateText(likertPanel.transform, font, "Agree", new Vector2(firstChoiceX + choiceSpacing * 6f + 58f, headerY + contentYOffset), new Vector2(64f, 24f), TextAnchor.MiddleCenter, 13);
        highText.color = Color.black;

        likertChoiceButtons = new Button[likertQuestionLabels.Length * 7];
        for (int questionIndex = 0; questionIndex < likertQuestionLabels.Length; questionIndex++)
        {
            float rowY = rowStartY - rowSpacing * questionIndex + contentYOffset;
            Text label = CreateText(likertPanel.transform, font, likertQuestionLabels[questionIndex], new Vector2(labelX, rowY + 6f), new Vector2(430f, 62f), TextAnchor.MiddleLeft, 15);
            label.color = Color.black;

            for (int valueIndex = 0; valueIndex < 7; valueIndex++)
            {
                int capturedQuestion = questionIndex;
                int capturedValue = valueIndex + 1;
                Button choiceButton = CreateButton(likertPanel.transform, font, capturedValue.ToString(), new Vector2(firstChoiceX + valueIndex * choiceSpacing, rowY), choiceSize);
                choiceButton.name = "Likert_Q" + (capturedQuestion + 1) + "_" + capturedValue;
                SetButtonTextColor(choiceButton, Color.black);
                SetButtonBackground(choiceButton, new Color(0.9f, 0.9f, 0.9f, 1f));
                AddLikertHitArea(choiceButton.gameObject, capturedQuestion, capturedValue, false);
                choiceButton.onClick.AddListener(delegate { SetLikertAnswer(capturedQuestion, capturedValue); });
                likertChoiceButtons[questionIndex * 7 + valueIndex] = choiceButton;
            }
        }

        likertSubmitButton = CreateButton(likertPanel.transform, font, "Submit", new Vector2(360f, -540f + contentYOffset), new Vector2(280f, 58f));
        AddLikertHitArea(likertSubmitButton.gameObject, -1, -1, true);
        likertSubmitButton.onClick.AddListener(delegate { StartCoroutine(SubmitLikertPage()); });

        UpdateLikertTable();
        likertPanel.SetActive(false);
    }

    private void SetLikertAnswer(int questionIndex, int value)
    {
        if (questionIndex < 0 || questionIndex >= likertQuestionValues.Length)
        {
            return;
        }

        likertQuestionValues[questionIndex] = value;
        Debug.Log("[Survey] Likert answer selected. question=" + (questionIndex + 1) + ", value=" + value);
        UpdateLikertTable();
    }

    private void AddLikertHitArea(GameObject target, int questionIndex, int value, bool isSubmit)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        BoxCollider collider = target.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = target.AddComponent<BoxCollider>();
        }

        collider.isTrigger = true;
        collider.center = new Vector3(rect.sizeDelta.x * 0.5f, -rect.sizeDelta.y * 0.5f, 0f);
        collider.size = new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 0.08f);

        LikertButtonHitArea hitArea = target.GetComponent<LikertButtonHitArea>();
        if (hitArea == null)
        {
            hitArea = target.AddComponent<LikertButtonHitArea>();
        }

        hitArea.Initialize(this, questionIndex, value, isSubmit);
    }

    internal void SelectLikertHitArea(int questionIndex, int value, bool isSubmit)
    {
        if (!likertPageVisible)
        {
            return;
        }

        if (isSubmit)
        {
            StartCoroutine(SubmitLikertPage());
            return;
        }

        SetLikertAnswer(questionIndex, value);
    }

    private void UpdateLikertTable()
    {
        if (likertChoiceButtons == null)
        {
            return;
        }

        for (int questionIndex = 0; questionIndex < likertQuestionLabels.Length; questionIndex++)
        {
            for (int valueIndex = 0; valueIndex < 7; valueIndex++)
            {
                int buttonIndex = questionIndex * 7 + valueIndex;
                Image image = likertChoiceButtons[buttonIndex] != null ? likertChoiceButtons[buttonIndex].GetComponent<Image>() : null;
                if (image != null)
                {
                    bool selected = Mathf.Approximately(likertQuestionValues[questionIndex], valueIndex + 1);
                    image.color = selected ? new Color(0.05f, 0.55f, 0.25f, 1f) : new Color(0.9f, 0.9f, 0.9f, 1f);
                    SetButtonTextColor(likertChoiceButtons[buttonIndex], selected ? Color.white : Color.black);
                }
            }
        }
    }

    private void CreateSamOverlaySubmitIfNeeded()
    {
        RectTransform root = likertRoot != null ? likertRoot : transform as RectTransform;
        if (root == null || samOverlaySubmitButton != null)
        {
            return;
        }

        Font font = GetDefaultUiFont();
        samOverlaySubmitButton = CreateButton(root, font, "NEXT", samNextButtonPosition, new Vector2(240f, 64f));
        samOverlaySubmitButton.name = "SAM_VisibleNextButton";
        samOverlaySubmitButton.transform.SetAsLastSibling();
        samOverlaySubmitButton.onClick.AddListener(delegate { StartCoroutine(SubmitCurrentPage()); });
    }

    private Slider CreateLikertSlider(RectTransform root, Font font, int index, string label)
    {
        GameObject item = new GameObject("Likert_" + label.Replace(" ", ""));
        item.transform.SetParent(root, false);
        RectTransform itemRect = item.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 1f);
        itemRect.anchorMax = new Vector2(0f, 1f);
        itemRect.pivot = new Vector2(0f, 1f);
        itemRect.anchoredPosition = likertStartPosition + new Vector2(0f, -likertItemSpacing * index);
        itemRect.sizeDelta = new Vector2(830f, likertItemSpacing);

        Text labelText = CreateText(item.transform, font, label + " (1-7)", new Vector2(0f, -2f), new Vector2(300f, 22f), TextAnchor.MiddleLeft, 15);
        labelText.color = Color.black;

        Slider slider = CreateSlider(item.transform, new Vector2(320f, -7f), likertSliderSize);
        slider.minValue = 1f;
        slider.maxValue = 7f;
        slider.wholeNumbers = true;
        slider.value = 4f;

        Text minText = CreateText(item.transform, font, "1", new Vector2(316f, -29f), new Vector2(24f, 16f), TextAnchor.MiddleCenter, 11);
        Text maxText = CreateText(item.transform, font, "7", new Vector2(596f, -29f), new Vector2(24f, 16f), TextAnchor.MiddleCenter, 11);
        minText.color = Color.black;
        maxText.color = Color.black;
        return slider;
    }

    private static Text CreateText(Transform parent, Font font, string text, Vector2 anchoredPosition, Vector2 size, TextAnchor alignment, int fontSize)
    {
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        textObject.AddComponent<CanvasRenderer>();
        Text textComponent = textObject.AddComponent<Text>();
        textComponent.font = font;
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.alignment = alignment;
        textComponent.raycastTarget = false;
        return textComponent;
    }

    private static Font GetDefaultUiFont()
    {
        try
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch (System.Exception)
        {
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }

    private static Slider CreateSlider(Transform parent, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject sliderObject = new GameObject("Slider");
        sliderObject.transform.SetParent(parent, false);
        RectTransform rect = sliderObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Slider slider = sliderObject.AddComponent<Slider>();
        GameObject background = CreateImage("Background", sliderObject.transform, Color.gray, false);
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.25f);
        backgroundRect.anchorMax = new Vector2(1f, 0.75f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObject.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;

        GameObject fill = CreateImage("Fill", fillArea.transform, new Color(0.15f, 0.35f, 0.8f), false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObject.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(6f, 0f);
        handleAreaRect.offsetMax = new Vector2(-6f, 0f);

        GameObject handle = CreateImage("Handle", handleArea.transform, Color.white, true);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18f, 18f);

        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Button CreateButton(Transform parent, Font font, string text, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject buttonObject = CreateImage("Button", parent, new Color(0.15f, 0.35f, 0.8f), true);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        Text label = CreateText(buttonObject.transform, font, text, Vector2.zero, size, TextAnchor.MiddleCenter, 18);
        label.color = Color.white;
        return button;
    }

    private static GameObject CreateImage(string name, Transform parent, Color color, bool raycastTarget = true)
    {
        GameObject imageObject = new GameObject(name);
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        imageObject.AddComponent<CanvasRenderer>();
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return imageObject;
    }

    private static float ReadOptionalSlider(Slider slider)
    {
        return slider != null ? slider.value : -1f;
    }

    private void Update()
    {
        if (!allowControllerButtonSubmit || isSubmitting)
        {
            return;
        }

        bool keyboardSubmit = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.S);
        // Keep the right-hand A button free for hold-to-record voice input.
        // The left-hand X button remains available as the survey submit shortcut.
        bool controllerSubmit = OVRInput.GetDown(
            OVRInput.Button.Three,
            OVRInput.Controller.LTouch);

        if (keyboardSubmit || (!likertPageVisible && controllerSubmit))
        {
            StartCoroutine(SubmitCurrentPage());
        }

        if (likertPageVisible &&
            (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
             OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)))
        {
            TrySelectLikertFromTaskRay();
        }
    }

    private void TrySelectLikertFromTaskRay()
    {
        XRRayInteractor[] rays = FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None);
        foreach (XRRayInteractor ray in rays)
        {
            if (ray == null || !ray.isActiveAndEnabled)
            {
                continue;
            }

            Ray worldRay = new Ray(ray.transform.position, ray.transform.forward);
            if (Physics.Raycast(worldRay, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Collide))
            {
                LikertButtonHitArea hitArea = hit.collider.GetComponent<LikertButtonHitArea>();
                if (hitArea != null && hitArea.Owner == this)
                {
                    hitArea.Select();
                    return;
                }
            }
        }
    }
}

internal class LikertButtonHitArea : MonoBehaviour
{
    public SAMSurveyEvents Owner { get; private set; }
    private int questionIndex;
    private int value;
    private bool isSubmit;

    public void Initialize(SAMSurveyEvents owner, int questionIndex, int value, bool isSubmit)
    {
        Owner = owner;
        this.questionIndex = questionIndex;
        this.value = value;
        this.isSubmit = isSubmit;
    }

    public void Select()
    {
        if (Owner != null)
        {
            Owner.SelectLikertHitArea(questionIndex, value, isSubmit);
        }
    }
}
