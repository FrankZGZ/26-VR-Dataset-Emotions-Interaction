using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class VRMERocketboxPrefabBuilder
{
    private const string ModelPath =
        "Assets/RocketboxTest/Microsoft-Rocketbox/Assets/Avatars/Adults/Female_Adult_01/Export/Female_Adult_01_facial.fbx";
    private const string PrefabPath = "Assets/RocketboxTest/VRME_Rocketbox_Female_Adult_01.prefab";
    private const string ControllerPath = "Assets/Animations/avatar_anim_ctrl.controller";

    static VRMERocketboxPrefabBuilder()
    {
        EditorApplication.delayCall += CreatePrefabIfNeeded;
    }

    [MenuItem("VRME/Create Rocketbox Test Prefab")]
    public static void CreatePrefabFromMenu()
    {
        CreatePrefab(forceOverwrite: true);
    }

    private static void CreatePrefabIfNeeded()
    {
        CreatePrefab(forceOverwrite: true);
    }

    private static void CreatePrefab(bool forceOverwrite)
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            Debug.Log("[VRME] Rocketbox model is not imported yet: " + ModelPath);
            return;
        }

        if (!forceOverwrite && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            return;
        }

        GameObject root = new GameObject("VRME_Rocketbox_Female_Adult_01");

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.name = "Rocketbox_Female_Adult_01_Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        Animator modelAnimator = model.GetComponent<Animator>();
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (modelAnimator != null && controller != null)
        {
            modelAnimator.runtimeAnimatorController = controller;
            modelAnimator.applyRootMotion = false;
        }

        GameObject voice = new GameObject("VRME Voice Audio");
        voice.transform.SetParent(root.transform, false);
        voice.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        AudioSource audioSource = voice.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.minDistance = 4.811101f;
        audioSource.maxDistance = 500f;

        GameObject faceLightObject = new GameObject("VRME Avatar Face Fill Light");
        faceLightObject.transform.SetParent(root.transform, false);
        faceLightObject.transform.localPosition = new Vector3(0f, 1.72f, 0.35f);
        Light faceLight = faceLightObject.AddComponent<Light>();
        faceLight.type = LightType.Point;
        faceLight.color = new Color(1f, 0.92f, 0.78f, 1f);
        faceLight.intensity = 0.65f;
        faceLight.range = 2.2f;
        faceLight.shadows = LightShadows.None;
        faceLight.renderMode = LightRenderMode.ForcePixel;

        VrmeAtticClient client = root.AddComponent<VrmeAtticClient>();
        client.serverUrl = "ws://localhost:8080/";
        client.recordKey = KeyCode.V;
        client.enableKeyboardRecordKey = true;
        client.enableControllerRecordButton = true;
        client.recordControllerButton = OVRInput.Button.One;
        client.recordController = OVRInput.Controller.RTouch;
        client.sampleRate = 16000;
        client.maxRecordSeconds = 12;
        client.autoConnectOnStart = true;
        client.connectTimeoutSeconds = 3f;
        client.showRuntimeMarker = false;
        client.includeInteractionContext = true;
        client.autoFindInteractionTrackers = true;
        client.maxRecentInteractionEvents = 5;
        client.playbackAudioSource = audioSource;
        client.streamReplyAudio = true;
        client.streamStartBufferSeconds = 0.18f;
        client.streamMaxSeconds = 45;
        client.autoIntroOnStart = false;
        client.autoIntroDelaySeconds = 10f;
        client.autoIntroPrompt =
            "Please introduce this VR scene to the participant. Describe the current environment, explain which interaction states or constraints are relevant, and state the task they should focus on. Keep it calm, clear, and complete without ending abruptly.";
        client.replyGain = 2.2f;

        FaceCameraOnYAxis faceCamera = root.AddComponent<FaceCameraOnYAxis>();
        faceCamera.turnSpeed = 720f;
        faceCamera.yawOffsetDegrees = 0f;

        root.AddComponent<AvatarGroundAligner>();

        AudioDrivenBlendShapeMouth mouth = root.AddComponent<AudioDrivenBlendShapeMouth>();
        mouth.audioSource = audioSource;
        mouth.renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        mouth.maxWeight = 65f;
        mouth.sensitivity = 28f;
        mouth.smoothing = 0.16f;

        VoiceDrivenAnimator voiceAnimator = root.AddComponent<VoiceDrivenAnimator>();
        voiceAnimator.animator = modelAnimator;
        voiceAnimator.voiceAudioSource = audioSource;
        voiceAnimator.speakingAnimationIndex = 3;
        voiceAnimator.idleAnimationIndex = 0;
        voiceAnimator.activationThreshold = 0.018f;
        voiceAnimator.releaseDelay = 0.45f;

        ProceduralAvatarIdle idle = root.AddComponent<ProceduralAvatarIdle>();
        idle.rigRoot = model.transform;
        idle.voiceAudioSource = audioSource;
        idle.letAnimatorDriveBody = true;
        idle.intensity = 0.65f;
        idle.speed = 0.85f;
        idle.speakingIntensity = 1.05f;
        idle.gestureIntensity = 0.85f;
        idle.speakingSmoothing = 0.16f;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[VRME] Created Rocketbox VRME prefab: " + PrefabPath);
    }
}
