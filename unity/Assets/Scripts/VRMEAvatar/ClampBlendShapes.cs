using UnityEngine;

[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class ClampBlendShapes : MonoBehaviour
{
    private SkinnedMeshRenderer smr;
    [Range(0f, 1f)] public float scaleFactor = 0.03f;

    void Awake()
    {
        smr = GetComponent<SkinnedMeshRenderer>();
    }

    void LateUpdate()
    {
        if (smr == null || smr.sharedMesh == null) return;

        int count = smr.sharedMesh.blendShapeCount;
        for (int i = 0; i < count; i++)
        {
            float weight = smr.GetBlendShapeWeight(i);
            smr.SetBlendShapeWeight(i, Mathf.Clamp(weight * scaleFactor, 0f, 100f));
        }
    }
}
