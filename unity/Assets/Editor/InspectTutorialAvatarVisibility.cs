using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class InspectTutorialAvatarVisibility
{
    private const string Key = "VRME.InspectTutorialAvatarVisibility.v1";

    static InspectTutorialAvatarVisibility()
    {
        if (EditorPrefs.GetBool(Key, false))
            return;

        EditorApplication.delayCall += Inspect;
    }

    private static void Inspect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path != "Assets/Scenes/Tutorial_Interaction.unity")
            return;

        GameObject avatar = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(transform => transform.gameObject.scene == SceneManager.GetActiveScene())
            .Select(transform => transform.gameObject)
            .FirstOrDefault(gameObject => gameObject.name == "Tutorial VRME Conversation Avatar");

        if (avatar == null)
        {
            Debug.LogError("[VRME Visibility] Avatar is absent from the loaded Tutorial scene.");
            return;
        }

        Renderer[] renderers = avatar.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(avatar.transform.position, Vector3.zero);
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Camera camera = Resources.FindObjectsOfTypeAll<Camera>()
            .FirstOrDefault(item => item.gameObject.scene == SceneManager.GetActiveScene() &&
                                    item.CompareTag("MainCamera"));
        Vector3 viewport = camera != null
            ? camera.WorldToViewportPoint(hasBounds ? bounds.center : avatar.transform.position)
            : new Vector3(float.NaN, float.NaN, float.NaN);

        Debug.Log("[VRME Visibility] activeSelf=" + avatar.activeSelf +
                  ", activeInHierarchy=" + avatar.activeInHierarchy +
                  ", position=" + avatar.transform.position.ToString("F3") +
                  ", renderers=" + renderers.Length +
                  ", bounds=" + (hasBounds ? bounds.ToString() : "none") +
                  ", camera=" + (camera != null ? camera.transform.position.ToString("F3") : "missing") +
                  ", viewport=" + viewport.ToString("F3"));

        Selection.activeGameObject = avatar;
        EditorGUIUtility.PingObject(avatar);
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        EditorPrefs.SetBool(Key, true);
    }
}
