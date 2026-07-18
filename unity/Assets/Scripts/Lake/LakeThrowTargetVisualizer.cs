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
        border.positionCount = 4;
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
        UpdateBorderPositions(targetCollider.bounds);
        Debug.Log("[LakeTarget] Visible throw target installed over SplashLayer.");
    }

    private void Update()
    {
        if (border == null)
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

    private void UpdateBorderPositions(Bounds bounds)
    {
        float y = bounds.max.y + heightAboveWater;
        border.SetPosition(0, new Vector3(bounds.min.x, y, bounds.min.z));
        border.SetPosition(1, new Vector3(bounds.max.x, y, bounds.min.z));
        border.SetPosition(2, new Vector3(bounds.max.x, y, bounds.max.z));
        border.SetPosition(3, new Vector3(bounds.min.x, y, bounds.max.z));
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}
