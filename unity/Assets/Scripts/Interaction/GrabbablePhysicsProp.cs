using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;

/// <summary>
/// Bootstraps the Meta grab pipeline (Grabbable/GrabInteractable/HandGrabInteractable/
/// PhysicsGrabbable) plus an InteractionTracker on a plain physics prop, mirroring
/// TennisBall.EnsureGrabComponents() without the ball-specific gameplay. Attach this to
/// any Rigidbody prop that needs to be pickable and needs its held state reported to the
/// voice backend (CURRENT_HELD_OBJECTS) but otherwise has no special behavior.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GrabbablePhysicsProp : MonoBehaviour
{
    [Tooltip("Optional name shown to the voice backend via the attached InteractionTracker. Leave empty to use the GameObject name.")]
    public string displayName = "";

    private void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();

        Grabbable grabbable = GetComponent<Grabbable>();
        if (grabbable == null)
        {
            grabbable = gameObject.AddComponent<Grabbable>();
        }
        grabbable.InjectOptionalTargetTransform(transform);
        grabbable.InjectOptionalRigidbody(rb);
        // Meta's grab transformer moves a selected kinematic body without collision
        // forces fighting the controller. ThrowWhenUnselected then restores dynamics
        // and applies its measured release velocity, matching TennisBall's setup.
        grabbable.InjectOptionalKinematicWhileSelected(true);
        grabbable.InjectOptionalThrowWhenUnselected(true);

        GrabInteractable grabInteractable = GetComponent<GrabInteractable>();
        if (grabInteractable == null)
        {
            grabInteractable = gameObject.AddComponent<GrabInteractable>();
        }
        grabInteractable.InjectAllGrabInteractable(rb);
        grabInteractable.InjectOptionalPointableElement(grabbable);
        grabInteractable.UseClosestPointAsGrabSource = true;

        HandGrabInteractable handGrabInteractable = GetComponent<HandGrabInteractable>();
        if (handGrabInteractable == null)
        {
            handGrabInteractable = gameObject.AddComponent<HandGrabInteractable>();
        }
        handGrabInteractable.InjectAllHandGrabInteractable(
            GrabTypeFlags.All,
            rb,
            GrabbingRule.DefaultPinchRule,
            GrabbingRule.DefaultPalmRule);
        handGrabInteractable.InjectOptionalPointableElement(grabbable);

#pragma warning disable CS0618
        PhysicsGrabbable physicsGrabbable = GetComponent<PhysicsGrabbable>();
        if (physicsGrabbable == null)
        {
            physicsGrabbable = gameObject.AddComponent<PhysicsGrabbable>();
        }
        physicsGrabbable.InjectAllPhysicsGrabbable(grabbable, rb);
        grabInteractable.InjectOptionalPhysicsGrabbable(physicsGrabbable);
        handGrabInteractable.InjectOptionalPhysicsGrabbable(physicsGrabbable);
#pragma warning restore CS0618

        InteractionTracker tracker = GetComponent<InteractionTracker>();
        if (tracker == null)
        {
            tracker = gameObject.AddComponent<InteractionTracker>();
        }
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            tracker.displayName = displayName;
        }
    }
}
