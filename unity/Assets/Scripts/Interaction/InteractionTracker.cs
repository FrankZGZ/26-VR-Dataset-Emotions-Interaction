using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
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
    public bool isCurrentlyHeld { get; private set; } = false;
    public string lastUseSource { get; private set; } = "";
    private GrabInteractable grabInteractable;
    private HandGrabInteractable handGrabInteractable;
    private IPointableElement pointableElement;
    private readonly HashSet<int> activePointerIds = new HashSet<int>();

    public string ContextName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public string InteractionStateSummary =>
        "attentionOnly=" + attentionOnlyTarget +
        ", everControllerGrabbed=" + wasGrabbedByController +
        ", everCollisionUsed=" + wasCollisionUsed +
        ", everUsed=" + isUsed +
        ", currentHeld=" + isCurrentlyHeld +
        (string.IsNullOrWhiteSpace(lastUseSource) ? "" : ", lastUseSource=" + lastUseSource);

    public void RefreshCurrentHeldState()
    {
        if (attentionOnlyTarget)
        {
            isCurrentlyHeld = false;
            activePointerIds.Clear();
            return;
        }

        bool actuallySelected = HasSelectingInteractor();
        if (isCurrentlyHeld && !actuallySelected)
        {
            isCurrentlyHeld = false;
            activePointerIds.Clear();
            Debug.Log($"[InteractionTracker] Corrected stale held state for {gameObject.name}; no selecting interactor remains.");
            AddRecentEvent("release:corrected", "refresh");
        }
        else if (!isCurrentlyHeld && actuallySelected)
        {
            isCurrentlyHeld = true;
            wasGrabbedByController = true;
            isUsed = true;
            lastUseSource = "controller_grab";
            Debug.Log($"[InteractionTracker] Corrected missed held state for {gameObject.name}; selecting interactor is active.");
            AddRecentEvent("grab:corrected", "refresh");
        }
    }

    private bool HasSelectingInteractor()
    {
        if (grabInteractable != null)
        {
            foreach (var interactor in grabInteractable.SelectingInteractorViews)
            {
                if (interactor != null)
                {
                    return true;
                }
            }

        }

        if (handGrabInteractable != null)
        {
            foreach (var interactor in handGrabInteractable.SelectingInteractorViews)
            {
                if (interactor != null)
                {
                    return true;
                }
            }
        }

        // Both controller GrabInteractable and HandGrabInteractable selection
        // views are authoritative. Pointer ids are only a fallback when an
        // object has neither component.
        if (grabInteractable != null || handGrabInteractable != null)
        {
            return false;
        }

        return activePointerIds.Count > 0;
    }

    void Start()
    {
        grabInteractable = GetComponent<GrabInteractable>();
        handGrabInteractable = GetComponent<HandGrabInteractable>();
        pointableElement = grabInteractable != null
            ? grabInteractable.PointableElement
            : (handGrabInteractable != null ? handGrabInteractable.PointableElement : null);
        if (pointableElement != null)
        {
            pointableElement.WhenPointerEventRaised += OnPointerEventRaised;
        }
        else if (grabInteractable != null)
        {
            grabInteractable.WhenSelectingInteractorAdded.Action += OnGrabbed;
            grabInteractable.WhenSelectingInteractorRemoved.Action += OnReleased;
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
        isCurrentlyHeld = true;
        wasGrabbedByController = true;
        lastUseSource = "controller_grab";
        Debug.Log($"[InteractionTracker] Object {gameObject.name} grabbed. firstUse={firstUse}");
        AddRecentEvent(firstUse ? "grab:first" : "grab:repeat", interactor != null ? interactor.name : "");
    }

    private void OnReleased(GrabInteractor interactor)
    {
        if (attentionOnlyTarget)
        {
            return;
        }

        isCurrentlyHeld = false;
        activePointerIds.Clear();
        Debug.Log($"[InteractionTracker] Object {gameObject.name} released.");
        AddRecentEvent("release", interactor != null ? interactor.name : "");
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        if (attentionOnlyTarget)
        {
            return;
        }

        if (evt.Type == PointerEventType.Select)
        {
            activePointerIds.Add(evt.Identifier);
            bool firstUse = !isUsed;
            isUsed = true;
            isCurrentlyHeld = true;
            wasGrabbedByController = true;
            lastUseSource = "controller_grab";
            AddRecentEvent(firstUse ? "grab:first" : "grab:repeat", evt.Identifier.ToString());
        }
        else if (evt.Type == PointerEventType.Unselect || evt.Type == PointerEventType.Cancel)
        {
            activePointerIds.Remove(evt.Identifier);
            isCurrentlyHeld = activePointerIds.Count > 0;
            AddRecentEvent("release", evt.Identifier.ToString());
        }
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

    public static string GetRecentEventsTextSince(System.DateTime sinceUtc, int maxEvents)
    {
        if (recentEvents.Count == 0 || maxEvents <= 0)
        {
            return "none";
        }

        var matching = new List<string>();
        foreach (string line in recentEvents)
        {
            int separator = line.IndexOf(" | ", System.StringComparison.Ordinal);
            string timestampText = separator > 0 ? line.Substring(0, separator) : "";
            if (System.DateTime.TryParse(
                    timestampText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out System.DateTime eventUtc) &&
                eventUtc.ToUniversalTime() >= sinceUtc.ToUniversalTime())
            {
                matching.Add(line);
            }
        }

        if (matching.Count == 0)
        {
            return "none";
        }

        int start = Mathf.Max(0, matching.Count - maxEvents);
        return string.Join("\n", matching.GetRange(start, matching.Count - start));
    }

    public void RecordExternalEvent(string eventType, string sourceName = "")
    {
        if (attentionOnlyTarget || string.IsNullOrWhiteSpace(eventType))
        {
            return;
        }

        isUsed = true;
        lastUseSource = eventType;
        AddRecentEvent(eventType, sourceName);
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
            if (pointableElement == null)
            {
                grabInteractable.WhenSelectingInteractorAdded.Action -= OnGrabbed;
                grabInteractable.WhenSelectingInteractorRemoved.Action -= OnReleased;
            }
        }

        if (pointableElement != null)
        {
            pointableElement.WhenPointerEventRaised -= OnPointerEventRaised;
        }
    }
} 
