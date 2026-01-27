using UnityEngine;
using Oculus.Haptics;
using System.Collections;
using Oculus.Interaction;

public class HapticOnGrab : MonoBehaviour
{
    public HapticClip clip;
    public Transform leftAnchor;
    public Transform rightAnchor;

    public bool isLooping = false; 

    private HapticClipPlayer leftPlayer;
    private HapticClipPlayer rightPlayer;

    private GrabInteractable grabInteractable;
    private IPointableElement pointableElement;

    void Start()
    {
        leftPlayer = new HapticClipPlayer(clip);
        rightPlayer = new HapticClipPlayer(clip);

        leftPlayer.isLooping = isLooping;
        rightPlayer.isLooping = isLooping;
        
        // Get the interactable component
        grabInteractable = GetComponent<GrabInteractable>();
        
        if (grabInteractable != null)
        {
            Debug.Log("[HapticOnGrab] Found GrabInteractable");
            pointableElement = grabInteractable.PointableElement;
            
            if (pointableElement != null)
            {
                pointableElement.WhenPointerEventRaised += OnPointerEventRaised;
                Debug.Log("[HapticOnGrab] Successfully registered for pointer events");
            }
            else
            {
                Debug.LogError("[HapticOnGrab] No PointableElement found on GrabInteractable");
            }
        }
        else
        {
            Debug.LogError("[HapticOnGrab] No GrabInteractable found on this object!");
        }
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        // Check if this is a select event (grab)
        if (evt.Type == PointerEventType.Select)
        {
            Debug.Log($"[HapticOnGrab] Grab event detected from pointer: {evt.Identifier}");
            
            // Find the interactor by identifier
            GrabInteractor interactor = FindInteractorById(evt.Identifier);
            if (interactor != null)
            {
                bool isLeftHand = IsLeftHand(interactor.transform);
                Debug.Log($"[HapticOnGrab] Object grabbed by {(isLeftHand ? "left" : "right")} hand");
                OnGrabbed(isLeftHand);
            }
        }
        // Check if this is an unselect event (release)
        else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
        {
            Debug.Log($"[HapticOnGrab] Release event detected from pointer: {evt.Identifier}");
            
            // Find the interactor by identifier
            GrabInteractor interactor = FindInteractorById(evt.Identifier);
            if (interactor != null)
            {
                bool isLeftHand = IsLeftHand(interactor.transform);
                Debug.Log($"[HapticOnGrab] Object released by {(isLeftHand ? "left" : "right")} hand");
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
        Debug.Log($"[HapticOnGrab] OnGrabbed called, isLeftHand: {isLeftHand}");
        
        if (isLeftHand)
        {
            Debug.Log("[HapticOnGrab] Playing haptic on left controller");
            leftPlayer.Play(Controller.Left);
        }
        else
        {
            Debug.Log("[HapticOnGrab] Playing haptic on right controller");
            rightPlayer.Play(Controller.Right);
        }
    }

    public void OnReleased(bool isLeftHand)
    {
        Debug.Log($"[HapticOnGrab] OnReleased called, isLeftHand: {isLeftHand}");
        
        if (isLeftHand)
        {
            Debug.Log("[HapticOnGrab] Stopping haptic on left controller");
            leftPlayer.Stop();
        }
        else
        {
            Debug.Log("[HapticOnGrab] Stopping haptic on right controller");
            rightPlayer.Stop();
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
