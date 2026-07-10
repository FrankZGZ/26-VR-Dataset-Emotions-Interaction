using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TunnelStartupRaySuppressor : MonoBehaviour
{
    private const string HelperName = "TunnelStartupRaySuppressor_Runtime";
    private const float MaxSuppressSeconds = 90f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoadedHandler()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Tunnel", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (GameObject.Find(HelperName) != null)
        {
            return;
        }

        GameObject helper = new GameObject(HelperName);
        helper.hideFlags = HideFlags.HideAndDontSave;
        helper.AddComponent<TunnelStartupRaySuppressor>();
    }

    private void Awake()
    {
        StartCoroutine(SuppressRayVisualsUntilSurvey());
    }

    private IEnumerator SuppressRayVisualsUntilSurvey()
    {
        float endTime = Time.unscaledTime + MaxSuppressSeconds;
        while (Time.unscaledTime < endTime && !IsSurveyVisible())
        {
            SuppressNow();
            yield return null;
            SuppressNow();
            yield return new WaitForEndOfFrame();
        }

        Destroy(gameObject);
    }

    private static bool IsSurveyVisible()
    {
        GameObject samTask = GameObject.Find("SAMTask");
        return samTask != null && samTask.activeInHierarchy;
    }

    private static void SuppressNow()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, "Tunnel", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisableLineRenderers();
        DisableRayVisualBehaviours();
        DisableRayVisualRenderers();
        DisableNamedRayVisualObjects();
    }

    private static void DisableLineRenderers()
    {
        LineRenderer[] lineRenderers = Resources.FindObjectsOfTypeAll<LineRenderer>();
        foreach (LineRenderer lineRenderer in lineRenderers)
        {
            if (IsLoadedSceneObject(lineRenderer != null ? lineRenderer.gameObject : null) && lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
            }
        }
    }

    private static void DisableRayVisualBehaviours()
    {
        Behaviour[] behaviours = Resources.FindObjectsOfTypeAll<Behaviour>();
        foreach (Behaviour behaviour in behaviours)
        {
            if (!IsLoadedSceneObject(behaviour != null ? behaviour.gameObject : null) || !behaviour.enabled)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("LineVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("RayVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("TubeRenderer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("CursorVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("InteractorDebugVisual", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                behaviour.enabled = false;
            }
        }
    }

    private static void DisableRayVisualRenderers()
    {
        Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (!IsLoadedSceneObject(renderer != null ? renderer.gameObject : null) || !renderer.enabled)
            {
                continue;
            }

            if (IsRayVisualHierarchy(renderer.transform))
            {
                renderer.enabled = false;
            }
        }
    }

    private static void DisableNamedRayVisualObjects()
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject obj in objects)
        {
            if (!IsLoadedSceneObject(obj) || !obj.activeSelf)
            {
                continue;
            }

            if (IsRayVisualName(obj.name))
            {
                obj.SetActive(false);
            }
        }
    }

    private static bool IsLoadedSceneObject(GameObject obj)
    {
        return obj != null && obj.scene.IsValid() && obj.scene.isLoaded;
    }

    private static bool IsRayVisualHierarchy(Transform transformToCheck)
    {
        Transform current = transformToCheck;
        while (current != null)
        {
            if (IsRayVisualName(current.name))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsRayVisualName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        return objectName.IndexOf("Ray", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("Cursor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("Pointer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("ArcVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("ProceduralArc", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("InteractorDebugVisual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("TurnVisuals", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
