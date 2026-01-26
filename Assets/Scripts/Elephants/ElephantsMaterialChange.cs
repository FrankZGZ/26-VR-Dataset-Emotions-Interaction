using UnityEngine;

public class ElephantsMaterialChange : MonoBehaviour
{
    public Transform centerEyeAnchor;
    public Material transparentMaterial;
    public float distanceThreshold = 3.0f;

    private SkinnedMeshRenderer objectRenderer;
    private Material originalMaterial;
    private bool isTransparent = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        objectRenderer = GetComponent<SkinnedMeshRenderer>();
        if (objectRenderer != null)
        {
            originalMaterial = objectRenderer.sharedMaterial;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (centerEyeAnchor == null || objectRenderer == null || transparentMaterial == null || originalMaterial == null)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, centerEyeAnchor.position);
        

        if (distance < distanceThreshold)
        {
            if (!isTransparent)
            {
                objectRenderer.sharedMaterial = transparentMaterial;
                isTransparent = true;
            }
        }
        else
        {
            if (isTransparent)
            {
                objectRenderer.sharedMaterial = originalMaterial;
                isTransparent = false;
            }
        }
    }
}
