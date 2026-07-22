using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds a clear, pulsing border above Lake's SplashLayer so the intended
/// airplane/stone landing area stays visible in the headset.
/// </summary>
[DisallowMultipleComponent]
public class LakeThrowTargetVisualizer : MonoBehaviour
{
    public Color targetColor = new Color(0.15f, 1f, 0.9f, 1f);
    public float heightAboveWater = 0.06f;
    public float baseWidth = 0.055f;
    public float pulseAmount = 0.025f;
    public float pulseSpeed = 2.2f;

    private LineRenderer border;
    private Material runtimeMaterial;
    private bool requestedVisible;
    private Vector3 requestedCenter;
    private float requestedRadius = 1.35f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterInstaller()
    {
        SceneManager.sceneLoaded -= InstallForScene;
        SceneManager.sceneLoaded += InstallForScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        InstallForScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void InstallForScene(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Lake", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        GameObject splashLayer = GameObject.Find("SplashLayer");
        if (splashLayer == null)
        {
            Debug.LogWarning("[LakeTarget] SplashLayer was not found; target visualization was not installed.");
            return;
        }

        if (splashLayer.GetComponent<LakeThrowTargetVisualizer>() == null)
        {
            splashLayer.AddComponent<LakeThrowTargetVisualizer>();
        }
    }

    private void Start()
    {
        BoxCollider targetCollider = GetComponent<BoxCollider>();
        if (targetCollider == null)
        {
            Debug.LogWarning("[LakeTarget] SplashLayer has no BoxCollider.");
            enabled = false;
            return;
        }

        GameObject borderObject = new GameObject("LakeThrowTargetBorder");
        borderObject.transform.SetParent(transform, true);
        border = borderObject.AddComponent<LineRenderer>();
        border.useWorldSpace = true;
        border.loop = true;
        border.positionCount = 48;
        border.numCornerVertices = 6;
        border.numCapVertices = 6;
        border.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        border.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader != null)
        {
            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "LakeThrowTargetRuntimeMaterial";
            border.material = runtimeMaterial;
        }

        border.startColor = targetColor;
        border.endColor = targetColor;
        ApplyRequestedVisibility();
        Debug.Log("[LakeTarget] Throw target installed over SplashLayer and waiting for the avatar briefing.");
    }

    private void Update()
    {
        if (border == null || !border.enabled)
        {
            return;
        }

        float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
        border.widthMultiplier = baseWidth + pulse * pulseAmount;
        Color pulsedColor = targetColor;
        pulsedColor.a = Mathf.Lerp(0.55f, 1f, pulse);
        border.startColor = pulsedColor;
        border.endColor = pulsedColor;
    }

    public void SetTaskHighlightVisible(bool visible, Vector3 targetCenter, float completionRadius)
    {
        requestedVisible = visible;
        requestedCenter = targetCenter;
        requestedRadius = Mathf.Max(0.1f, completionRadius);
        ApplyRequestedVisibility();
        Debug.Log("[LakeTarget] Throw target visible=" + visible + ".");
    }

    private void ApplyRequestedVisibility()
    {
        if (border == null)
        {
            return;
        }

        if (requestedVisible)
        {
            UpdateBorderPositions(requestedCenter, requestedRadius);
        }
        border.enabled = requestedVisible;
    }

    private void UpdateBorderPositions(Vector3 center, float radius)
    {
        float y = center.y + heightAboveWater;
        int pointCount = Mathf.Max(12, border.positionCount);
        for (int i = 0; i < pointCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / pointCount;
            border.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                y,
                center.z + Mathf.Sin(angle) * radius));
        }
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}
