using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ConfigureEmotionTutorial
{
    private const string TutorialScenePath = "Assets/Scenes/Tutorial_Interaction.unity";
    private const string AvatarPrefabPath =
        "Assets/RocketboxTest/VRME_Rocketbox_Female_Adult_01.prefab";
    private const string AvatarName = "Tutorial VRME Conversation Avatar";
    private const string AppliedKey = "VRME.ConfigureEmotionTutorial.Applied.v3";

    static ConfigureEmotionTutorial()
    {
        if (EditorPrefs.GetBool(AppliedKey, false))
            return;

        EditorApplication.delayCall += ConfigureCurrentTutorial;
    }

    [MenuItem("Tools/VRME/Configure Emotion Tutorial")]
    public static void ConfigureCurrentTutorial()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene originalScene = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(TutorialScenePath);
        bool openedAdditively = !scene.IsValid() || !scene.isLoaded;
        if (openedAdditively)
            scene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Additive);

        SceneManager.SetActiveScene(scene);

        bool changed = ConfigureAvatar(scene);
        changed |= ConfigureSurvey(scene);
        changed |= ConfigurePerception(scene);

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[VRME Setup] Tutorial Avatar and post-SAM questionnaire configured and saved.");
        }

        EditorPrefs.SetBool(AppliedKey, true);
        if (originalScene.IsValid() && originalScene.isLoaded)
            SceneManager.SetActiveScene(originalScene);
        if (openedAdditively)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static bool ConfigureAvatar(Scene scene)
    {
        GameObject avatar = FindSceneObject(scene, AvatarName);
        bool changed = false;

        if (avatar == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[VRME Setup] Avatar prefab was not found at " + AvatarPrefabPath);
                return false;
            }

            avatar = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (avatar == null)
                return false;

            avatar.name = AvatarName;
            PlaceLikeDDriveTutorial(scene, avatar.transform);
            Undo.RegisterCreatedObjectUndo(avatar, "Add tutorial VRME Avatar");
            changed = true;
        }

        AvatarDominanceBehaviorController behaviour =
            avatar.GetComponent<AvatarDominanceBehaviorController>();
        if (behaviour != null)
        {
            behaviour.normalizeAvatarEyeHeight = true;
            behaviour.defaultAvatarEyeHeight = 1.62f;
            behaviour.minimumMatchedEyeHeight = 1.62f;
            behaviour.maximumMatchedEyeHeight = 1.62f;
            behaviour.placeFromHeadsetPose = false;
            behaviour.minimumDistance = 0f;
            behaviour.maximumDistance = 100f;
            EditorUtility.SetDirty(behaviour);
            changed = true;
        }

        AvatarGroundAligner aligner = avatar.GetComponent<AvatarGroundAligner>();
        if (aligner != null)
        {
            EditorUtility.SetDirty(aligner);
            changed = true;
        }

        return changed;
    }

    private static void PlaceLikeDDriveTutorial(Scene scene, Transform avatar)
    {
        GameObject blueCube = FindSceneObject(scene, "BlueCube");
        Camera viewer = Resources.FindObjectsOfTypeAll<Camera>()
            .FirstOrDefault(camera => camera.gameObject.scene == scene && camera.CompareTag("MainCamera"))
            ?? Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(camera => camera.gameObject.scene == scene);

        Vector3 viewerPosition = viewer != null ? viewer.transform.position : Vector3.zero;
        Vector3 anchorPosition = blueCube != null ? blueCube.transform.position : viewerPosition + Vector3.forward;
        Vector3 towardViewer = Vector3.ProjectOnPlane(
            viewerPosition - anchorPosition, Vector3.up).normalized;
        if (towardViewer.sqrMagnitude < 0.01f)
            towardViewer = Vector3.back;

        Vector3 side = Vector3.Cross(Vector3.up, towardViewer).normalized;
        Vector3 position = anchorPosition + side * 0.8f;
        position.y = 0f;

        Vector3 faceViewer = Vector3.ProjectOnPlane(viewerPosition - position, Vector3.up).normalized;
        Quaternion rotation = faceViewer.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(faceViewer, Vector3.up)
            : Quaternion.identity;

        avatar.SetPositionAndRotation(position, rotation);
    }

    private static bool ConfigureSurvey(Scene scene)
    {
        SAMSurveyEvents survey = Resources.FindObjectsOfTypeAll<SAMSurveyEvents>()
            .FirstOrDefault(component => component.gameObject.scene == scene);
        if (survey == null)
        {
            Debug.LogError("[VRME Setup] SurveySAM/SAMSurveyEvents was not found in Tutorial_Interaction.");
            return false;
        }

        survey.createLikertPageAtRuntime = true;
        survey.autoEnableTaskRaysOnSurveyStart = true;
        survey.allowControllerButtonSubmit = true;
        survey.positionQuestionnaireInFrontOfCamera = false;
        survey.samNextButtonPosition = new Vector2(350f, -500f);
        survey.likertPanelSize = new Vector2(980f, 720f);
        survey.likertStartPosition = new Vector2(52f, -78f);
        survey.likertItemSpacing = 46f;
        survey.likertSliderSize = new Vector2(300f, 18f);
        survey.likertWorldVerticalOffset = -2f;
        survey.hideQuestionnaireOnSceneStart = true;
        EditorUtility.SetDirty(survey);
        return true;
    }

    private static bool ConfigurePerception(Scene scene)
    {
        GameObject setupModule = FindSceneObject(scene, "SetupModule");
        GameObject cameraRig = FindSceneObject(scene, "Camera Rig");
        GameObject samTask = FindSceneObject(scene, "SAMTask");
        if (setupModule == null || cameraRig == null)
        {
            Debug.LogError("[VRME Setup] Tutorial perception setup could not find SetupModule or Camera Rig.");
            return false;
        }

        CameraPoseSender sender = setupModule.GetComponent<CameraPoseSender>();
        if (sender == null)
            sender = Undo.AddComponent<CameraPoseSender>(setupModule);

        sender.cameraRig = cameraRig;
        sender.backupTriggerObject = samTask;
        sender.recordEyeGaze = true;
        sender.recordGazeObjectAttention = true;
        sender.useHeadForwardAsGazeFallback = true;
        sender.recordFaceExpressions = true;
        sender.recordHeartRatePlaceholder = true;
        sender.autoAttachAvatarGazeTargets = true;
        sender.forceHideGazeDebugVisuals = true;
        sender.disableHandTrackingObjects = false;
        sender.sampleOnlyWhileVoiceRecording = false;
        sender.sampleOnVoiceRecordingEdges = true;
        sender.logRuntimeTrackingState = true;
        sender.writeLiveDebugSnapshot = true;
        EditorUtility.SetDirty(sender);
        return true;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(transform => transform.gameObject.scene == scene)
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject => gameObject.name == objectName);
    }
}
