using UnityEngine;
using System.Collections;
using Oculus.Interaction;


public class FlashlightOnGrab : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public Transform leftAnchor;
    public Transform rightAnchor;
    public GameObject flashlight;
    private GrabInteractable grabInteractable;
    private IPointableElement pointableElement;

    void Start()
    {     
        // Get the interactable component
        grabInteractable = GetComponent<GrabInteractable>();
        
        if (grabInteractable != null)
        {
        
            pointableElement = grabInteractable.PointableElement;
            
            if (pointableElement != null)
            {
                pointableElement.WhenPointerEventRaised += OnPointerEventRaised;
               
            }
            else
            {
                Debug.LogError("[FlashlightOnGrab] No PointableElement found on GrabInteractable");
            }
        }
        else
        {
            Debug.LogError("[FlashlightOnGrab] No GrabInteractable found on this object!");
        }
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        // Check if this is a select event (grab)
        if (evt.Type == PointerEventType.Select)
        {
            Debug.Log($"[FlashlightOnGrab] Grab event detected from pointer: {evt.Identifier}");
            
            // Find the interactor by identifier
            GrabInteractor interactor = FindInteractorById(evt.Identifier);
            if (interactor != null)
            {
                bool isLeftHand = IsLeftHand(interactor.transform);
                Debug.Log($"[FlashlightOnGrab] Object grabbed by {(isLeftHand ? "left" : "right")} hand");
                OnGrabbed(isLeftHand);
            }
        }
        // Check if this is an unselect event (release)
        else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
        {
            Debug.Log($"[FlashlightOnGrab] Release event detected from pointer: {evt.Identifier}");
            
            // Find the interactor by identifier
            GrabInteractor interactor = FindInteractorById(evt.Identifier);
            if (interactor != null)
            {
                bool isLeftHand = IsLeftHand(interactor.transform);
                Debug.Log($"[FlashlightOnGrab] Object released by {(isLeftHand ? "left" : "right")} hand");
                OnReleased(isLeftHand);
            }
        }
    }

    private GrabInteractor FindInteractorById(int id)
    {
        // Find all grab interactors in the scene
        GrabInteractor[] interactors = FindObjectsOfType<GrabInteractor>();
        
        // Find the one with matching identifier
        foreach (var interactor in interactors)
        {
            if (interactor.Identifier == id)
            {
                return interactor;
            }
        }
        
        return null;
    }
    
    private bool IsLeftHand(Transform interactorTransform)
    {
        // Check interactor name for left/right indicators
        string name = interactorTransform.name.ToLower();
        if (name.Contains("left"))
            return true;
        if (name.Contains("right"))
            return false;
            
        // If name doesn't help, try to use position relative to anchors
        if (leftAnchor != null && rightAnchor != null)
        {
            float distToLeft = Vector3.Distance(interactorTransform.position, leftAnchor.position);
            float distToRight = Vector3.Distance(interactorTransform.position, rightAnchor.position);
            return distToLeft < distToRight;
        }
        
        // Default to right hand if we can't determine
        return false;
    }

    public void OnGrabbed(bool isLeftHand)
    {
        Debug.Log($"[FlashlightOnGrab] OnGrabbed called, isLeftHand: {isLeftHand}");
        
        if (isLeftHand)
        {
            Debug.Log("[FlashlightOnGrab] Playing haptic on left controller");
            flashlight.SetActive(true);
        }
        else
        {
            Debug.Log("[FlashlightOnGrab] Playing haptic on right controller");
            flashlight.SetActive(true);
        }
    }

    public void OnReleased(bool isLeftHand)
    {
        Debug.Log($"[FlashlightOnGrab] OnReleased called, isLeftHand: {isLeftHand}");
        
        if (isLeftHand)
        {
            Debug.Log("[FlashlightOnGrab] Stopping flashlight on left controller");
            flashlight.SetActive(false);
        }
        else
        {
            Debug.Log("[FlashlightOnGrab] Stopping flashlight on right controller");
            flashlight.SetActive(false);
        }
    }
    
    void OnDestroy()
    {
        // Unregister from events
        if (pointableElement != null)
        {
            pointableElement.WhenPointerEventRaised -= OnPointerEventRaised;
        }
    }
}
