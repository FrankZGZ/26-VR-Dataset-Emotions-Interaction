using UnityEngine;
using Oculus.Interaction;
using System.Collections.Generic;

/// <summary>
/// This script tracks if an Oculus.Interaction interactable has been grabbed or triggered.
/// Attach this script to all interactable objects that need to be tracked.
/// </summary>
public class InteractionTracker : MonoBehaviour
{
    private const int MaxStoredEvents = 20;
    private static readonly List<string> recentEvents = new List<string>();

    [Header("Tracking Settings")]
    [Tooltip("Optional name shown to the voice backend. Leave empty to use the GameObject name.")]
    public string displayName = "";

    [Tooltip("Indicates whether this object has been used.")]
    public bool isUsed { get; private set; } = false;
    [Tooltip("True for gaze-only targets such as avatars. They can be looked at but should not be interpreted as held or used.")]
    public bool attentionOnlyTarget = false;
    [Tooltip("When false, trigger/collision events will not mark this object as used.")]
    public bool trackTriggerCollisions = true;
    public bool wasGrabbedByController { get; private set; } = false;
    public bool wasCollisionUsed { get; private set; } = false;
    public string lastUseSource { get; private set; } = "";
    private GrabInteractable grabInteractable;

    public string ContextName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public string InteractionStateSummary =>
        "attentionOnly=" + attentionOnlyTarget +
        ", controllerGrabbed=" + wasGrabbedByController +
        ", collisionUsed=" + wasCollisionUsed +
        ", used=" + isUsed +
        (string.IsNullOrWhiteSpace(lastUseSource) ? "" : ", lastUseSource=" + lastUseSource);

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
        if (attentionOnlyTarget)
        {
            Debug.Log($"[InteractionTracker] Ignored grab on attention-only target {gameObject.name}.");
            return;
        }

        bool firstUse = !isUsed;
        isUsed = true;
        wasGrabbedByController = true;
        lastUseSource = "controller_grab";
        Debug.Log($"[InteractionTracker] Object {gameObject.name} grabbed. firstUse={firstUse}");
        AddRecentEvent(firstUse ? "grab:first" : "grab:repeat", interactor != null ? interactor.name : "");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (attentionOnlyTarget || !trackTriggerCollisions)
        {
            return;
        }

        bool firstUse = !isUsed;
        isUsed = true;
        wasCollisionUsed = true;
        lastUseSource = "collision_or_trigger";
        Debug.Log($"[InteractionTracker] Object {gameObject.name} collision with {(other != null ? other.name : "unknown")}. firstUse={firstUse}");
        AddRecentEvent(firstUse ? "collision:first" : "collision:repeat", other != null ? other.name : "");
    }

    private void AddRecentEvent(string eventType, string sourceName)
    {
        Vector3 p = transform.position;
        string line = $"{System.DateTime.UtcNow:o} | {eventType} | {ContextName} | objectPos=({p.x:0.00},{p.y:0.00},{p.z:0.00})";
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            line += $" | source={sourceName}";
        }

        recentEvents.Add(line);
        while (recentEvents.Count > MaxStoredEvents)
        {
            recentEvents.RemoveAt(0);
        }
    }

    public static string GetRecentEventsText(int maxEvents)
    {
        if (recentEvents.Count == 0 || maxEvents <= 0)
        {
            return "none";
        }

        int start = Mathf.Max(0, recentEvents.Count - maxEvents);
        return string.Join("\n", recentEvents.GetRange(start, recentEvents.Count - start));
    }

    public static void ClearRecentEvents()
    {
        recentEvents.Clear();
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
