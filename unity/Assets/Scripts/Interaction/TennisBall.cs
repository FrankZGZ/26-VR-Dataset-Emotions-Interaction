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
    private InteractionTracker interactionTracker;
    private IPointableElement pointableElement;
    private bool armedForUserThrow;
    private bool selectedByUser;
    private float lastUserHandContactAt = float.NegativeInfinity;
    private float ignoreUserHandContactUntil = float.NegativeInfinity;
    private const float UserContactThrowWindowSeconds = 2.5f;

    // for the dog to catch the ball
    public static TennisBall lastThrownBall;
    public bool hasBeenThrown = false;
    [System.NonSerialized] public float fetchThrownAt = float.NegativeInfinity;
    private bool isCarriedByDog;
    [Tooltip("Minimum ball speed that starts the dog-fetch sequence.")]
    public float fetchThrowSpeedThreshold = 0.8f;

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

        interactionTracker = GetComponent<InteractionTracker>();

        SnapAboveGroundIfNeeded(force: true);
    }

    private void Start()
    {
        GrabInteractable grabInteractable = GetComponent<GrabInteractable>();
        pointableElement = grabInteractable != null ? grabInteractable.PointableElement : null;
        if (pointableElement != null)
        {
            pointableElement.WhenPointerEventRaised += OnPointerEventRaised;
        }
        else
        {
            Debug.LogWarning("[TennisBall] No PointableElement found; controller throw events will use held-state fallback.");
        }
    }

    void Update()
    {
        // While carried, DogMotion2 owns the transform and Rigidbody state.
        // Running grab/floor/still recovery here would fight that ownership and
        // leave later throws in an inconsistent kinematic state.
        if (isCarriedByDog)
        {
            return;
        }

        SnapAboveGroundIfNeeded(force: false);

        bool currentlyHeldByUser = selectedByUser;
        if (interactionTracker != null)
        {
            interactionTracker.RefreshCurrentHeldState();
            currentlyHeldByUser = currentlyHeldByUser || interactionTracker.isCurrentlyHeld;
            if (currentlyHeldByUser)
            {
                armedForUserThrow = true;
                hasBeenThrown = false;
            }
        }

        if (!armedForUserThrow && !hasBeenThrown &&
            Time.time - lastUserHandContactAt <= UserContactThrowWindowSeconds)
        {
            armedForUserThrow = true;
        }

        Vector3 horizontalVelocity = rb.linearVelocity;
        horizontalVelocity.y = 0f;
        if (armedForUserThrow && !currentlyHeldByUser && !hasBeenThrown &&
            horizontalVelocity.magnitude > Mathf.Max(0.1f, fetchThrowSpeedThreshold))
        {
            hasBeenThrown = true;
            armedForUserThrow = false;
            lastThrownBall = this;
            fetchThrownAt = Time.time;
            Debug.Log("TennisBall thrown by user!");
        }

        if (rb.linearVelocity.magnitude < 0.1f)
        {
            stillTime += Time.deltaTime;
            if (stillTime > stillThreshold)
            {
                hasBeenThrown = false;
                stillTime = 0f;
                Debug.Log("TennisBall reset after being still.");

                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                SnapAboveGroundIfNeeded(force: true);
            }
        }
        else
        {
            stillTime = 0f;
        }
    }

    public void MarkFetchCompleted()
    {
        isCarriedByDog = false;
        // The dog carries the ball kinematically. Always hand it back to the
        // interaction system as a dynamic body for the next throw.
        rb.isKinematic = false;
        hasBeenThrown = false;
        armedForUserThrow = false;
        selectedByUser = false;
        lastUserHandContactAt = float.NegativeInfinity;
        ignoreUserHandContactUntil = Time.time + 2f;
        stillTime = 0f;
        fetchThrownAt = float.NegativeInfinity;
        if (lastThrownBall == this)
        {
            lastThrownBall = null;
        }
    }

    public void BeginDogCarry()
    {
        isCarriedByDog = true;
        armedForUserThrow = false;
        selectedByUser = false;
        lastUserHandContactAt = float.NegativeInfinity;
    }

    private void OnPointerEventRaised(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select)
        {
            selectedByUser = true;
            armedForUserThrow = true;
            hasBeenThrown = false;
            Debug.Log("[TennisBall] Selected by user; fetch throw armed.");
        }
        else if (evt.Type == PointerEventType.Unselect)
        {
            selectedByUser = false;
            Debug.Log("[TennisBall] Released by user; waiting for throw velocity.");
        }
        else if (evt.Type == PointerEventType.Cancel)
        {
            selectedByUser = false;
            armedForUserThrow = false;
        }
    }

    private void OnDestroy()
    {
        if (pointableElement != null)
        {
            pointableElement.WhenPointerEventRaised -= OnPointerEventRaised;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterUserHandContact(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterUserHandContact(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        RegisterUserHandContact(collision != null ? collision.gameObject : null);
    }

    private void RegisterUserHandContact(GameObject contactObject)
    {
        if (isCarriedByDog || contactObject == null || Time.time < ignoreUserHandContactUntil)
        {
            return;
        }

        string objectName = contactObject.name;
        if (objectName.IndexOf("HandAnchor", System.StringComparison.OrdinalIgnoreCase) < 0 &&
            objectName.IndexOf("ControllerGrab", System.StringComparison.OrdinalIgnoreCase) < 0 &&
            objectName.IndexOf("HandGrab", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        lastUserHandContactAt = Time.time;
        // A grab can begin while the dog is still carrying the ball. In that
        // case Meta remembers the old kinematic state and may restore it on the
        // next release, leaving the second throw suspended in mid-air.
        if (!hasBeenThrown && rb.isKinematic)
        {
            rb.isKinematic = false;
            Debug.Log("[TennisBall] Restored dynamic Rigidbody for the next user throw.");
        }
        if (!armedForUserThrow)
        {
            armedForUserThrow = true;
            Debug.Log("[TennisBall] User hand/controller contact detected; fetch throw armed.");
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
        // Keep user grabs dynamic. Dog carrying still explicitly switches the
        // body to kinematic, but a user release must always be able to receive
        // Meta's calculated throw velocity.
        grabbable.InjectOptionalKinematicWhileSelected(false);
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
