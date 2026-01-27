using UnityEngine;

public class HightlightObject : MonoBehaviour
{
    public Transform centerEyeAnchor;
    public float highlightDistance = 3.0f;

    private Outline outline;

    void Start()
    {
        outline = GetComponent<Outline>();
        outline.enabled = false;
    }

    void Update()
    {
        if (centerEyeAnchor == null) return;

        float distance = Vector3.Distance(transform.position, centerEyeAnchor.position);
        outline.enabled = distance < highlightDistance;
    }
}
