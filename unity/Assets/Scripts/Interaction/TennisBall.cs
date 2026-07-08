using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class TennisBall : MonoBehaviour
{
    [Header("Basic Physical Parameters")]
    public float mass = 0.057f; // tennis ball standard mass
    public float radius = 0.033f; // tennis ball standard radius
    public float bounciness = 0.1f;
    public float friction = 1.0f;

    [Header("Grab / floor recovery")]
    public bool autoAddGrabComponents = true;
    public bool keepAboveGround = true;
    public float groundSearchHeight = 2.0f;
    public float groundSearchDistance = 4.0f;
    public float groundClearance = 0.015f;
    public float maximumSnapUpDistance = 0.6f;

    private Rigidbody rb;
    private PhysicsMaterial physicsMaterial;
    private SphereCollider sphereCollider;

    // for the dog to catch the ball
    public static TennisBall lastThrownBall;
    public bool hasBeenThrown = false;

    private float stillTime = 0f;
    public float stillThreshold = 2f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        physicsMaterial = new PhysicsMaterial("TennisBallMaterial");
        physicsMaterial.bounciness = bounciness;
        physicsMaterial.dynamicFriction = friction;
        physicsMaterial.staticFriction = friction;
        physicsMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        physicsMaterial.frictionCombine = PhysicsMaterialCombine.Average;

        sphereCollider = GetComponent<SphereCollider>();
        sphereCollider.radius = radius;
        sphereCollider.material = physicsMaterial;

        if (autoAddGrabComponents)
        {
            EnsureGrabComponents();
        }

        SnapAboveGroundIfNeeded(force: true);
    }

    void Update()
    {
        SnapAboveGroundIfNeeded(force: false);

        if (!hasBeenThrown && rb.linearVelocity.magnitude > 1.5f)
        {
            hasBeenThrown = true;
            lastThrownBall = this;
            Debug.Log("TennisBall thrown!");
        }

        if (rb.linearVelocity.magnitude < 0.1f)
        {
            stillTime += Time.deltaTime;
            if (stillTime > stillThreshold)
            {
                hasBeenThrown = false;
                stillTime = 0f;
                Debug.Log("TennisBall reset after being still.");

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                SnapAboveGroundIfNeeded(force: true);
            }
        }
        else
        {
            stillTime = 0f;
        }
    }

    private void EnsureGrabComponents()
    {
        Grabbable grabbable = GetComponent<Grabbable>();
        if (grabbable == null)
        {
            grabbable = gameObject.AddComponent<Grabbable>();
        }
        grabbable.InjectOptionalTargetTransform(transform);
        grabbable.InjectOptionalRigidbody(rb);
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
        grabInteractable.ReleaseDistance = 0.45f;

        HandGrabInteractable handGrabInteractable = GetComponent<HandGrabInteractable>();
        if (handGrabInteractable == null)
        {
            handGrabInteractable = gameObject.AddComponent<HandGrabInteractable>();
        }
        handGrabInteractable.InjectAllHandGrabInteractable(
            Oculus.Interaction.Grab.GrabTypeFlags.All,
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

        if (GetComponent<InteractionTracker>() == null)
        {
            InteractionTracker tracker = gameObject.AddComponent<InteractionTracker>();
            tracker.displayName = "tennis ball";
        }
    }

    private void SnapAboveGroundIfNeeded(bool force)
    {
        if (!keepAboveGround || rb == null || sphereCollider == null)
        {
            return;
        }

        if (!force && rb.linearVelocity.magnitude > 0.25f)
        {
            return;
        }

        if (!TryFindGroundBelow(out RaycastHit groundHit))
        {
            return;
        }

        float worldRadius = sphereCollider.radius * Mathf.Max(
            transform.lossyScale.x,
            transform.lossyScale.y,
            transform.lossyScale.z);
        float targetY = groundHit.point.y + worldRadius + groundClearance;
        float currentY = transform.position.y;
        if (currentY >= targetY - 0.005f)
        {
            return;
        }

        float snapDistance = targetY - currentY;
        if (!force && snapDistance > maximumSnapUpDistance)
        {
            return;
        }

        transform.position = new Vector3(transform.position.x, targetY, transform.position.z);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, Mathf.Max(0f, rb.linearVelocity.y), rb.linearVelocity.z);
        Debug.Log($"[TennisBall] Snapped above ground by {snapDistance:0.000}m at {groundHit.collider.name}.");
    }

    private bool TryFindGroundBelow(out RaycastHit bestHit)
    {
        Vector3 origin = transform.position + Vector3.up * groundSearchHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            groundSearchHeight + groundSearchDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        bestHit = default;
        float bestY = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.normal.y < 0.45f || !LooksLikeGround(hit.collider.gameObject))
            {
                continue;
            }

            if (hit.point.y > bestY)
            {
                bestY = hit.point.y;
                bestHit = hit;
            }
        }

        return bestY > float.NegativeInfinity;
    }

    private static bool LooksLikeGround(GameObject candidate)
    {
        if (candidate.CompareTag("Ground"))
        {
            return true;
        }

        string name = candidate.name.ToLowerInvariant();
        return name.Contains("ground") || name.Contains("floor") || name.Contains("terrain");
    }
}
