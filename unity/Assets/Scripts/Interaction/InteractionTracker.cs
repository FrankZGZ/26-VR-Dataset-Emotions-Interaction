using UnityEngine;
using Oculus.Interaction;
using System.Linq;

/// <summary>
/// This script tracks if an Oculus.Interaction interactable has been grabbed or triggered.
/// Attach this script to all interactable objects that need to be tracked.
/// </summary>
public class InteractionTracker : MonoBehaviour
{
    [Header("Tracking Settings")]
    [Tooltip("Indicates whether this object has been used.")]
    public bool isUsed { get; private set; } = false;
    private GrabInteractable grabInteractable;

    void Start()
    {
        grabInteractable = GetComponent<GrabInteractable>();
        if (grabInteractable != null)
        {
            grabInteractable.WhenSelectingInteractorAdded.Action += OnGrabbed;
        }
    }

    private void OnGrabbed(GrabInteractor interactor)
    {
        if (!isUsed)
        {
            isUsed = true;
            Debug.Log($"[InteractionTracker] Object {gameObject.name} has been used by grabbing.");

            // Unsubscribe after the first trigger to improve performance
            if (grabInteractable != null)
            {
                grabInteractable.WhenSelectingInteractorAdded.Action -= OnGrabbed;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isUsed)
        {
            isUsed = true;
            Debug.Log($"[InteractionTracker] Object {gameObject.name} has been used by collision with {other.name}.");
        }
    }

    void OnDestroy()
    {
        // When the object is destroyed, make sure to unsubscribe to prevent memory leaks.
        if (grabInteractable != null)
        {
            grabInteractable.WhenSelectingInteractorAdded.Action -= OnGrabbed;
        }
    }
} 