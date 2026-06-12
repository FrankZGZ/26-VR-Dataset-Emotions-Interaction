using System;
using System.Collections.Generic;
using UnityEngine;

public class AudioDrivenBlendShapeMouth : MonoBehaviour
{
    public AudioSource audioSource;
    public SkinnedMeshRenderer[] renderers = Array.Empty<SkinnedMeshRenderer>();
    [Range(0f, 100f)] public float maxWeight = 65f;
    [Range(1f, 80f)] public float sensitivity = 28f;
    [Range(0.01f, 1f)] public float smoothing = 0.16f;

    private readonly float[] samples = new float[256];
    private readonly List<BlendTarget> targets = new List<BlendTarget>();
    private float currentWeight;

    private static readonly string[] PreferredNames =
    {
        "viseme_aa", "viseme_oh", "viseme_ih", "viseme",
        "jawopen", "jaw_open", "mouthopen", "mouth_open", "aa", "oh"
    };

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponentInChildren<AudioSource>();
        }

        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        ResolveBlendTargets();
    }

    private void LateUpdate()
    {
        if (audioSource == null || targets.Count == 0)
        {
            return;
        }

        float targetWeight = 0f;
        if (audioSource.isPlaying)
        {
            audioSource.GetOutputData(samples, 0);
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            float rms = Mathf.Sqrt(sum / samples.Length);
            targetWeight = Mathf.Clamp(rms * sensitivity * 100f, 0f, maxWeight);
        }

        currentWeight = Mathf.Lerp(currentWeight, targetWeight, 1f - Mathf.Exp(-Time.deltaTime / smoothing));
        foreach (BlendTarget target in targets)
        {
            target.Renderer.SetBlendShapeWeight(target.Index, currentWeight);
        }
    }

    private void ResolveBlendTargets()
    {
        targets.Clear();
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            if (renderer == null || renderer.sharedMesh == null)
            {
                continue;
            }

            int index = FindBestBlendShape(renderer.sharedMesh);
            if (index >= 0)
            {
                targets.Add(new BlendTarget(renderer, index));
                Debug.Log("[VRME] Audio mouth blendshape: " + renderer.sharedMesh.GetBlendShapeName(index));
            }
        }
    }

    private static int FindBestBlendShape(Mesh mesh)
    {
        for (int p = 0; p < PreferredNames.Length; p++)
        {
            string preferred = PreferredNames[p];
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i).ToLowerInvariant();
                if (name.Contains(preferred))
                {
                    return i;
                }
            }
        }

        return mesh.blendShapeCount > 0 ? 0 : -1;
    }

    private readonly struct BlendTarget
    {
        public readonly SkinnedMeshRenderer Renderer;
        public readonly int Index;

        public BlendTarget(SkinnedMeshRenderer renderer, int index)
        {
            Renderer = renderer;
            Index = index;
        }
    }
}
