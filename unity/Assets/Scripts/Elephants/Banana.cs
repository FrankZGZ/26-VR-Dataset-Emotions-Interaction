using UnityEngine;
using Oculus.Interaction;

public class Banana : MonoBehaviour
{
    private GrabInteractable grabInteractable;
    private IPointableElement pointableElement;
    private Rigidbody rb;
    public GameObject Arrow;
    public GameObject ArrowPoint; 
    
    void Start()
    {
        grabInteractable = GetComponent<GrabInteractable>();
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
        }

        if (grabInteractable != null)
        {
            pointableElement = grabInteractable.PointableElement;
            if (pointableElement != null)
            {
                pointableElement.WhenPointerEventRaised += OnPointerEventRaised;
            }
        }
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        if (rb == null) return;

        if (evt.Type == PointerEventType.Select)
        {
            rb.useGravity = true;
            if (Arrow != null)
            {
                Arrow.SetActive(false);
            }
            if (ArrowPoint != null)
            {
                ArrowPoint.SetActive(false);
            }
        }
    }

    void OnDestroy()
    {
        if (pointableElement != null)
        {
            pointableElement.WhenPointerEventRaised -= OnPointerEventRaised;
        }
    }

    // Update is called once per frame

}
