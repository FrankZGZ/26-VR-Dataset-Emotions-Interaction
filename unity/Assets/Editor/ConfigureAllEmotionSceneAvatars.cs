using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ConfigureAllEmotionSceneAvatars
{
    private const string PrefabPath =
        "Assets/RocketboxTest/VRME_Rocketbox_Female_Adult_01.prefab";
    private const string AppliedKey = "VRME.ConfigureAllEmotionSceneAvatars.v1";

    private sealed class SceneAvatarSpec
    {
        public string Path;
        public string ParentName;
        public Vector3 Position;
        public Quaternion Rotation;
        public string Prompt;
    }

    private static readonly SceneAvatarSpec[] Specs =
    {
        new SceneAvatarSpec
        {
            Path = "Assets/Scenes/Attic.unity",
            Position = new Vector3(-1.04f, 0f, -1.25f),
            Rotation = new Quaternion(0f, -0.34840813f, 0f, -0.937343f),
            Prompt = "You are in a furnished attic facing a shouting man with a gun, creating a tense and threatening atmosphere. The participant can raise a shield to protect themselves."
        },
        new SceneAvatarSpec
        {
            Path = "Assets/Scenes/Elephant.unity",
            ParentName = "SceneLoader",
            Position = new Vector3(0f, -1.22f, 0f),
            Rotation = Quaternion.identity,
            Prompt = "You are surrounded by a herd of elephants in an exciting natural setting. The participant can approach, observe, and feed the elephants, whose reactions may be surprising."
        },
        new SceneAvatarSpec
        {
            Path = "Assets/Scenes/Lake.unity",
            Position = new Vector3(-1.3f, 0f, 4.5f),
            Rotation = new Quaternion(0f, 0.5847103f, 0f, 0.81124216f),
            Prompt = "You are on a peaceful jetty beside a lake, surrounded by calm water and nature. The participant can relax, throw stones, and launch paper planes."
        },
        new SceneAvatarSpec
        {
            Path = "Assets/Scenes/Puppies.unity",
            Position = new Vector3(0f, 0f, 1.5f),
            Rotation = new Quaternion(0f, 1f, 0f, 0f),
            Prompt = "You are in a warm, familiar indoor room with playful puppies. The participant can pet the puppies and play fetch with them using a ball."
        },
        new SceneAvatarSpec
        {
            Path = "Assets/Scenes/SolitaryConfinement.unity",
            Position = new Vector3(0.02f, 0f, 0.73f),
            Rotation = Quaternion.identity,
            Prompt = "You are inside a narrow, oppressive solitary-confinement cell with disturbing dripping-water sounds. The participant can examine the cup and book and interact with the cell door."
        },
        new SceneAvatarSpec
        {
            Path = "Assets/Scenes/Tunnel.unity",
            Position = new Vector3(-1.69f, 0.16f, 0f),
            Rotation = Quaternion.identity,
            Prompt = "You are in a dark, confined tunnel. The participant can use a flashlight to explore the darkness and feel more in control."
        }
    };

    static ConfigureAllEmotionSceneAvatars()
    {
        if (!EditorPrefs.GetBool(AppliedKey, false))
            EditorApplication.delayCall += ConfigureAll;
    }

    [MenuItem("Tools/VRME/Configure All Emotion Scene Avatars")]
    public static void ConfigureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[VRME Setup] Missing Avatar prefab: " + PrefabPath);
            return;
        }

        Scene originalScene = SceneManager.GetActiveScene();
        int configured = 0;

        foreach (SceneAvatarSpec spec in Specs)
        {
            Scene scene = SceneManager.GetSceneByPath(spec.Path);
            bool openedAdditively = !scene.IsValid() || !scene.isLoaded;
            if (openedAdditively)
                scene = EditorSceneManager.OpenScene(spec.Path, OpenSceneMode.Additive);

            SceneManager.SetActiveScene(scene);
            GameObject avatar = FindAvatar(scene);
            if (avatar == null)
            {
                avatar = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (avatar == null)
                    continue;
                avatar.name = "VRME_Rocketbox_Female_Adult_01";
            }

            Transform parent = string.IsNullOrEmpty(spec.ParentName)
                ? null
                : FindSceneObject(scene, spec.ParentName)?.transform;
            avatar.transform.SetParent(parent, false);
            avatar.transform.localPosition = spec.Position;
            avatar.transform.localRotation = spec.Rotation;

            VrmeAtticClient client = avatar.GetComponent<VrmeAtticClient>();
            if (client != null)
            {
                client.scenePrompt = spec.Prompt;
                EditorUtility.SetDirty(client);
            }

            AvatarDominanceBehaviorController behaviour =
                avatar.GetComponent<AvatarDominanceBehaviorController>();
            if (behaviour != null)
            {
                behaviour.normalizeAvatarEyeHeight = false;
                behaviour.defaultAvatarEyeHeight = 1.62f;
                behaviour.minimumMatchedEyeHeight = 1.62f;
                behaviour.maximumMatchedEyeHeight = 1.62f;
                EditorUtility.SetDirty(behaviour);
            }

            ConfigureSurvey(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            configured++;

            if (openedAdditively)
                EditorSceneManager.CloseScene(scene, true);
        }

        if (originalScene.IsValid() && originalScene.isLoaded)
            SceneManager.SetActiveScene(originalScene);

        EditorPrefs.SetBool(AppliedKey, true);
        Debug.Log("[VRME Setup] Configured D-drive-matched Avatars in " + configured + " emotion scenes.");
    }

    private static void ConfigureSurvey(Scene scene)
    {
        SAMSurveyEvents survey = Resources.FindObjectsOfTypeAll<SAMSurveyEvents>()
            .FirstOrDefault(component => component.gameObject.scene == scene);
        if (survey == null)
            return;

        survey.createLikertPageAtRuntime = true;
        survey.autoEnableTaskRaysOnSurveyStart = true;
        survey.allowControllerButtonSubmit = true;
        survey.positionQuestionnaireInFrontOfCamera = false;
        survey.hideQuestionnaireOnSceneStart = true;
        EditorUtility.SetDirty(survey);
    }

    private static GameObject FindAvatar(Scene scene)
    {
        return Resources.FindObjectsOfTypeAll<VrmeAtticClient>()
            .Where(component => component.gameObject.scene == scene)
            .Select(component => component.gameObject)
            .FirstOrDefault();
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .Where(transform => transform.gameObject.scene == scene)
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject => gameObject.name == objectName);
    }
}
