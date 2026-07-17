using System.Collections;
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
    [Tooltip("Small separation used only when recovering a ball that has actually entered the floor.")]
    public float groundClearance = 0.002f;
    [Tooltip("Normal contact settling is left to physics; only recover after this much floor penetration.")]
    public float minimumGroundPenetrationForRecovery = 0.02f;
    [Tooltip("Rate-limit floor recovery checks so they cannot fight the Rigidbody every frame.")]
    public float groundRecoveryCheckInterval = 0.25f;
    public float maximumSnapUpDistance = 0.6f;

    [Header("Play-area containment")]
    public bool constrainToPlayArea = true;
    [Tooltip("Maximum flat distance from the authored ball spawn before the throw is stopped at the boundary.")]
    public float maximumHorizontalDistanceFromSpawn = 6.0f;
    public float maximumHeightAboveSpawn = 3.0f;
    public float maximumDepthBelowSpawn = 1.0f;

    private Rigidbody rb;
    private PhysicsMaterial physicsMaterial;
    private SphereCollider sphereCollider;
    private InteractionTracker interactionTracker;
    private GrabInteractable grabInteractable;
    private HandGrabInteractable handGrabInteractable;
    private IPointableElement pointableElement;
    private bool armedForUserThrow;
    private bool selectedByUser;
    private float lastUserHandContactAt = float.NegativeInfinity;
    private float ignoreUserHandContactUntil = float.NegativeInfinity;
    private float nextGroundRecoveryCheckAt;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float nextContainmentLogAt;
    private const float UserContactThrowWindowSeconds = 2.5f;
    private const float ActiveControllerContactGraceSeconds = 0.2f;

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
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
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
        grabInteractable = GetComponent<GrabInteractable>();
        handGrabInteractable = GetComponent<HandGrabInteractable>();
        Grabbable grabbable = GetComponent<Grabbable>();
        pointableElement = grabbable != null
            ? grabbable
            : (grabInteractable != null
                ? grabInteractable.PointableElement
                : (handGrabInteractable != null ? handGrabInteractable.PointableElement : null));
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

        // Meta can visually/physically control the ball through a controller
        // path whose Select event is not exposed to our tracker. Treat very
        // recent controller contact as a short physical-ownership grace period
        // so recovery/containment cannot pull the ball out of the hand.
        bool activeControllerContact = Time.time - lastUserHandContactAt <= ActiveControllerContactGraceSeconds;
        currentlyHeldByUser = currentlyHeldByUser || activeControllerContact;

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
            Debug.Log("TennisBall thrown by user! velocity=" + rb.linearVelocity.ToString("F2") +
                ", horizontalSpeed=" + horizontalVelocity.magnitude.ToString("0.00"));
        }

        // Register the throw first, then contain it. A very fast first frame
        // must still start the dog-fetch sequence at the boundary.
        if (!currentlyHeldByUser && ContainInsidePlayArea())
        {
            return;
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
        // Ignore only the release frame. A long grace period makes the returned
        // ball feel dead when the participant immediately reaches for it.
        ignoreUserHandContactUntil = Time.time + 0.2f;
        stillTime = 0f;
        fetchThrownAt = float.NegativeInfinity;
        if (lastThrownBall == this)
        {
            lastThrownBall = null;
        }
    }

    public void ReturnToPlayerPickupPoint(Vector3 playerPosition, Vector3 dogPosition)
    {
        isCarriedByDog = false;

        Vector3 awayFromPlayer = dogPosition - playerPosition;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude < 0.01f)
        {
            awayFromPlayer = Vector3.forward;
        }
        else
        {
            awayFromPlayer.Normalize();
        }

        // Place the ball outside the head/controller/body colliders. The old
        // dog-forward drop point could land inside Camera Offset and permanently
        // prevent Meta's grab selector from acquiring the ball again.
        transform.position = new Vector3(playerPosition.x, dogPosition.y + 0.2f, playerPosition.z) +
            awayFromPlayer * 0.75f;
        MarkFetchCompleted();
        SnapAboveGroundIfNeeded(force: true);

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }

        if (grabInteractable != null)
        {
            grabInteractable.enabled = true;
        }
        if (handGrabInteractable != null)
        {
            handGrabInteractable.enabled = true;
        }

        Debug.Log("[TennisBall] Returned to safe pickup point and re-enabled for continued interaction.");
    }

    public void BeginDogCarry()
    {
        isCarriedByDog = true;
        armedForUserThrow = false;
        selectedByUser = false;
        lastUserHandContactAt = float.NegativeInfinity;
    }

    private bool ContainInsidePlayArea()
    {
        if (!constrainToPlayArea || isCarriedByDog || rb == null)
        {
            return false;
        }

        float maximumDistance = Mathf.Max(1f, maximumHorizontalDistanceFromSpawn);
        Vector3 fromSpawn = transform.position - spawnPosition;
        Vector3 horizontalOffset = new Vector3(fromSpawn.x, 0f, fromSpawn.z);
        if (horizontalOffset.magnitude > maximumDistance)
        {
            Vector3 outward = horizontalOffset.normalized;
            Vector3 containedPosition = spawnPosition + outward * maximumDistance;
            containedPosition.y = transform.position.y;
            rb.position = containedPosition;

            float outwardSpeed = Vector3.Dot(rb.linearVelocity, outward);
            if (outwardSpeed > 0f)
            {
                rb.linearVelocity -= outward * outwardSpeed;
            }

            LogContainment("horizontal play-area boundary");
            return true;
        }

        if (transform.position.y > spawnPosition.y + maximumHeightAboveSpawn ||
            transform.position.y < spawnPosition.y - maximumDepthBelowSpawn)
        {
            rb.isKinematic = false;
            rb.position = spawnPosition;
            rb.rotation = spawnRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            hasBeenThrown = false;
            armedForUserThrow = false;
            fetchThrownAt = float.NegativeInfinity;
            stillTime = 0f;
            if (lastThrownBall == this)
            {
                lastThrownBall = null;
            }
            LogContainment("vertical out-of-bounds reset");
            return true;
        }

        return false;
    }

    private void LogContainment(string reason)
    {
        if (Time.time < nextContainmentLogAt)
        {
            return;
        }

        nextContainmentLogAt = Time.time + 1f;
        Debug.Log("[TennisBall] Contained at " + reason + "; ball remains available for fetch.");
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
            StartCoroutine(EnsureDynamicAfterUserRelease());
            Debug.Log("[TennisBall] Released by user; waiting for throw velocity.");
        }
        else if (evt.Type == PointerEventType.Cancel)
        {
            selectedByUser = false;
            armedForUserThrow = false;
            StartCoroutine(EnsureDynamicAfterUserRelease());
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
        // Meta's grab transformer moves a selected kinematic body without
        // collision forces fighting the controller. ThrowWhenUnselected then
        // restores dynamics and applies its measured release velocity.
        grabbable.InjectOptionalKinematicWhileSelected(true);
        grabbable.InjectOptionalThrowWhenUnselected(true);

        grabInteractable = GetComponent<GrabInteractable>();
        if (grabInteractable == null)
        {
            grabInteractable = gameObject.AddComponent<GrabInteractable>();
        }
        grabInteractable.InjectAllGrabInteractable(rb);
        grabInteractable.InjectOptionalPointableElement(grabbable);
        grabInteractable.UseClosestPointAsGrabSource = true;
        grabInteractable.ReleaseDistance = 0.45f;

        handGrabInteractable = GetComponent<HandGrabInteractable>();
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

    private bool IsActivelySelectedByUser()
    {
        if (selectedByUser)
        {
            return true;
        }

        if (grabInteractable != null)
        {
            foreach (var selectingInteractor in grabInteractable.SelectingInteractorViews)
            {
                if (selectingInteractor != null)
                {
                    return true;
                }
            }
        }


        if (handGrabInteractable != null)
        {
            foreach (var selectingInteractor in handGrabInteractable.SelectingInteractorViews)
            {
                if (selectingInteractor != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerator EnsureDynamicAfterUserRelease()
    {
        // Let Meta apply its sampled throw velocity first. This is only a guard
        // for a stale kinematic lock left behind by an interrupted grab.
        yield return new WaitForFixedUpdate();
        if (!isCarriedByDog && !IsActivelySelectedByUser() && rb != null && rb.isKinematic)
        {
            rb.isKinematic = false;
            Debug.Log("[TennisBall] Cleared stale kinematic state after user release.");
        }
    }

    private void SnapAboveGroundIfNeeded(bool force)
    {
        if (!keepAboveGround || rb == null || sphereCollider == null)
        {
            return;
        }

        // Never move the target out from under an active hand/controller grab.
        if (!force && (selectedByUser ||
            Time.time - lastUserHandContactAt <= ActiveControllerContactGraceSeconds ||
            (interactionTracker != null && interactionTracker.isCurrentlyHeld)))
        {
            return;
        }

        if (!force && Time.time < nextGroundRecoveryCheckAt)
        {
            return;
        }
        nextGroundRecoveryCheckAt = Time.time + Mathf.Max(0.05f, groundRecoveryCheckInterval);

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
        float requiredRecovery = force ? 0.005f : Mathf.Max(0.005f, minimumGroundPenetrationForRecovery);
        if (currentY >= targetY - requiredRecovery)
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
