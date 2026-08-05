using System.Collections.Generic;
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
    private const string AppliedKey = "VRME.ConfigureEmotionTutorial.Applied.v9";
    private const string GeneratedAssetFolder = "Assets/Generated/VRMETutorial";
    private const string PrismMeshPath = GeneratedAssetFolder + "/TutorialTriangularPrism.asset";
    private const string PrismMaterialPath = GeneratedAssetFolder + "/TutorialTriangularPrism.mat";
    private const string CylinderMaterialPath = GeneratedAssetFolder + "/TutorialCylinder.mat";

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
        changed |= ConfigureTutorialShapes(scene);
        changed |= ConfigureTutorialInteractableTracking(scene);
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
            behaviour.normalizeAvatarEyeHeight = false;
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

    private static bool ConfigureTutorialShapes(Scene scene)
    {
        GameObject interactionRoot = FindSceneObject(scene, "Interaction");
        Camera viewer = Resources.FindObjectsOfTypeAll<Camera>()
            .FirstOrDefault(camera => camera.gameObject.scene == scene && camera.CompareTag("MainCamera"))
            ?? Resources.FindObjectsOfTypeAll<Camera>()
                .FirstOrDefault(camera => camera.gameObject.scene == scene);
        if (interactionRoot == null || viewer == null)
        {
            Debug.LogError("[VRME Setup] Tutorial shapes require the Interaction root and a scene camera.");
            return false;
        }

        Vector3 forward = Vector3.ProjectOnPlane(viewer.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 targetCenter = viewer.transform.position + forward * 3.2f;
        targetCenter.y = FindTutorialGroundHeight(targetCenter, viewer.transform.position) + 0.13f;
        float sideOffset = 0.72f;
        GameObject firstCube = FindSceneObject(scene, "BlueCube");
        GameObject secondCube = FindSceneObject(scene, "BlueCube (1)");
        if (firstCube != null && secondCube != null)
        {
            Vector3 cubeAxis = secondCube.transform.position - firstCube.transform.position;
            cubeAxis.y = 0f;
            if (cubeAxis.sqrMagnitude > 0.01f)
                right = cubeAxis.normalized;
            targetCenter = (firstCube.transform.position + secondCube.transform.position) * 0.5f;
        }
        Quaternion faceViewer = Quaternion.LookRotation(-forward, Vector3.up);

        Mesh prismMesh = LoadOrCreatePrismMesh();
        Mesh cylinderMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        Material prismMaterial = LoadOrCreateMaterial(
            PrismMaterialPath,
            "Tutorial Triangular Prism Material",
            new Color(1f, 0.43f, 0.25f, 1f));
        Material cylinderMaterial = LoadOrCreateMaterial(
            CylinderMaterialPath,
            "Tutorial Cylinder Material",
            new Color(0.12f, 0.75f, 1f, 1f));

        if (firstCube == null || secondCube == null)
        {
            Debug.LogError("[VRME Setup] Both BlueCube objects are required to build matching interactive tutorial shapes.");
            return false;
        }

        bool changed = ReplaceTutorialShapeFromCube(
            scene,
            firstCube,
            interactionRoot.transform,
            "Tutorial Triangular Prism Target",
            targetCenter - right * sideOffset,
            faceViewer,
            Vector3.one * 0.21f,
            prismMesh,
            prismMaterial);
        changed |= ReplaceTutorialShapeFromCube(
            scene,
            secondCube,
            interactionRoot.transform,
            "Tutorial Cylinder Target",
            targetCenter + right * sideOffset,
            faceViewer,
            new Vector3(0.26f, 0.13f, 0.26f),
            cylinderMesh,
            cylinderMaterial);

        ToSetup setup = Resources.FindObjectsOfTypeAll<ToSetup>()
            .FirstOrDefault(component => component.gameObject.scene == scene);
        if (setup != null)
        {
            GameObject prism = FindSceneObject(scene, "Tutorial Triangular Prism Target");
            GameObject cylinder = FindSceneObject(scene, "Tutorial Cylinder Target");
            GameObject[] current = setup.objectsToShow ?? new GameObject[0];
            // Do not let participant setup reactivate the legacy visual instructions.
            setup.objectsToShow = current
                .Where(item => item != null &&
                    !string.Equals(item.name, "Arrow", System.StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.name, "ArrowPoint", System.StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { prism, cylinder })
                .Where(item => item != null)
                .Distinct()
                .ToArray();
            EditorUtility.SetDirty(setup);
            changed = true;
        }
        return changed;
    }

    private static bool ReplaceTutorialShapeFromCube(
        Scene scene,
        GameObject cubeTemplate,
        Transform parent,
        string objectName,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        Mesh mesh,
        Material material)
    {
        if (cubeTemplate == null || mesh == null)
            return false;

        GameObject oldTarget = FindSceneObject(scene, objectName);
        if (oldTarget != null)
        {
            Undo.DestroyObjectImmediate(oldTarget);
        }

        GameObject target = Object.Instantiate(cubeTemplate);
        target.name = objectName;
        SceneManager.MoveGameObjectToScene(target, scene);
        target.transform.SetParent(parent, true);
        target.transform.SetPositionAndRotation(position, rotation);

        MeshFilter templateFilter = cubeTemplate.GetComponentInChildren<MeshFilter>(true);
        MeshFilter filter = target.GetComponentInChildren<MeshFilter>(true);
        if (filter == null)
            filter = target.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        target.transform.localScale = MatchVisibleSize(cubeTemplate, templateFilter, mesh);

        MeshRenderer renderer = target.GetComponentInChildren<MeshRenderer>(true);
        if (renderer == null)
            renderer = target.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        BoxCollider boxCollider = target.GetComponent<BoxCollider>();
        if (boxCollider == null)
            boxCollider = target.AddComponent<BoxCollider>();
        boxCollider.center = mesh.bounds.center;
        boxCollider.size = mesh.bounds.size;

        AlignMeshBottomWithTemplate(templateFilter, filter, target.transform);

        target.SetActive(cubeTemplate.activeSelf);
        Undo.RegisterCreatedObjectUndo(target, "Add interactive tutorial shape");
        EditorUtility.SetDirty(target);
        Debug.Log("[VRME Setup] Rebuilt " + objectName + " from " + cubeTemplate.name +
            " with matching grab/physics components and visual size.");
        return true;
    }

    private static void AlignMeshBottomWithTemplate(
        MeshFilter templateFilter,
        MeshFilter targetFilter,
        Transform targetRoot)
    {
        if (templateFilter == null || templateFilter.sharedMesh == null ||
            targetFilter == null || targetFilter.sharedMesh == null || targetRoot == null)
            return;

        Bounds templateBounds = templateFilter.sharedMesh.bounds;
        Bounds targetBounds = targetFilter.sharedMesh.bounds;
        float templateBottom = templateFilter.transform.TransformPoint(
            new Vector3(templateBounds.center.x, templateBounds.min.y, templateBounds.center.z)).y;
        float targetBottom = targetFilter.transform.TransformPoint(
            new Vector3(targetBounds.center.x, targetBounds.min.y, targetBounds.center.z)).y;
        targetRoot.position += Vector3.up * (templateBottom - targetBottom);
    }

    private static bool ConfigureTutorialInteractableTracking(Scene scene)
    {
        var interactableObjects = new HashSet<GameObject>();
        foreach (MonoBehaviour component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (component == null || component.gameObject.scene != scene)
                continue;

            string typeName = component.GetType().Name;
            if (typeName == "GrabInteractable" || typeName == "HandGrabInteractable")
                interactableObjects.Add(component.gameObject);
        }

        bool changed = false;
        foreach (GameObject interactable in interactableObjects)
        {
            InteractionTracker tracker = interactable.GetComponent<InteractionTracker>();
            if (tracker == null)
            {
                tracker = Undo.AddComponent<InteractionTracker>(interactable);
                changed = true;
            }

            tracker.displayName = TutorialDisplayName(interactable);
            tracker.attentionOnlyTarget = false;
            tracker.trackTriggerCollisions = true;
            EditorUtility.SetDirty(tracker);
        }

        if (changed)
        {
            Debug.Log("[VRME Setup] Added gaze/grab InteractionTracker coverage to " +
                interactableObjects.Count + " Tutorial interactables.");
        }
        return changed;
    }

    private static string TutorialDisplayName(GameObject interactable)
    {
        Transform current = interactable != null ? interactable.transform : null;
        while (current != null)
        {
            string value = current.name ?? "";
            if (value.IndexOf("Prism", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "triangular prism";
            if (value.IndexOf("Cylinder", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "cylinder";
            if (value.IndexOf("BlueCube (1)", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "second blue cube";
            if (value.IndexOf("BlueCube", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "blue cube";
            current = current.parent;
        }

        return interactable != null
            ? interactable.name.Replace("(Clone)", "").Trim()
            : "interactive object";
    }

    private static Vector3 MatchVisibleSize(GameObject cubeTemplate, MeshFilter templateFilter, Mesh targetMesh)
    {
        if (cubeTemplate == null || templateFilter == null || templateFilter.sharedMesh == null || targetMesh == null)
            return Vector3.one * 0.13f;

        Vector3 sourceScale = cubeTemplate.transform.localScale;
        Vector3 sourceSize = templateFilter.sharedMesh.bounds.size;
        Vector3 targetSize = targetMesh.bounds.size;
        return new Vector3(
            targetSize.x > 0.0001f ? sourceScale.x * sourceSize.x / targetSize.x : sourceScale.x,
            targetSize.y > 0.0001f ? sourceScale.y * sourceSize.y / targetSize.y : sourceScale.y,
            targetSize.z > 0.0001f ? sourceScale.z * sourceSize.z / targetSize.z : sourceScale.z);
    }

    private static float FindTutorialGroundHeight(Vector3 candidate, Vector3 viewerPosition)
    {
        RaycastHit hit;
        float rayStartY = Mathf.Max(candidate.y, viewerPosition.y) + 1f;
        if (Physics.Raycast(
            new Vector3(candidate.x, rayStartY, candidate.z),
            Vector3.down,
            out hit,
            20f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return viewerPosition.y > 1f ? viewerPosition.y - 1.45f : viewerPosition.y;
    }

    private static Mesh LoadOrCreatePrismMesh()
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(PrismMeshPath);
        if (existing != null)
            return existing;

        EnsureGeneratedAssetFolder();
        var mesh = new Mesh { name = "Tutorial Triangular Prism" };
        mesh.vertices = new[]
        {
            new Vector3(-0.62f, -0.62f, -0.42f),
            new Vector3(0.62f, -0.62f, -0.42f),
            new Vector3(0f, 0.62f, -0.42f),
            new Vector3(-0.62f, -0.62f, 0.42f),
            new Vector3(0.62f, -0.62f, 0.42f),
            new Vector3(0f, 0.62f, 0.42f)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 3, 4, 5,
            0, 1, 4, 0, 4, 3,
            1, 2, 5, 1, 5, 4,
            2, 0, 3, 2, 3, 5
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, PrismMeshPath);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    private static Material LoadOrCreateMaterial(string path, string materialName, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        EnsureGeneratedAssetFolder();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = materialName, color = color };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void EnsureGeneratedAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
            AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(GeneratedAssetFolder))
            AssetDatabase.CreateFolder("Assets/Generated", "VRMETutorial");
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
