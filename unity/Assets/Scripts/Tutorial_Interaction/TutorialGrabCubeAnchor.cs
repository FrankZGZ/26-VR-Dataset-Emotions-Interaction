using System.Collections;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Starts a tutorial cube from a reachable pose, then keeps it from crossing
/// the visible floor. This is runtime-driven because the Editor may restore an
/// unsaved Temp/__Backupscenes copy containing the old underground transform.
/// </summary>
public class TutorialGrabCubeAnchor : MonoBehaviour
{
    private Rigidbody body;
    private GrabInteractable[] grabInteractables;
    private Vector3 anchoredPosition;
    private Quaternion anchoredRotation;
    private Collider cubeCollider;
    private float floorY;
    private bool anchored;
    private bool subscribed;

    public void InitializeAtCurrentPose()
    {
        body = GetComponent<Rigidbody>();
        cubeCollider = GetComponent<Collider>();
        anchoredPosition = transform.position;
        anchoredRotation = transform.rotation;
        GameObject ground = GameObject.Find("Ground");
        floorY = ground != null ? ground.transform.position.y : 0f;
        anchored = true;

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }

        SubscribeToGrabEvents();
        StartCoroutine(ReleaseAfterGrabSystemInitialization());
    }

    private void Start()
    {
        SubscribeToGrabEvents();
    }

    private void LateUpdate()
    {
        if (!anchored)
            return;

        transform.SetPositionAndRotation(anchoredPosition, anchoredRotation);
    }

    private void FixedUpdate()
    {
        if (anchored || body == null || cubeCollider == null)
            return;

        // Keep the complete visible cube just above the floor. Meta's grab
        // building block can temporarily switch the body back to kinematic
        // while caching its pose, so kinematic bodies must be repaired too.
        const float visibleFloorClearance = 0.02f;
        float penetration = floorY + visibleFloorClearance - cubeCollider.bounds.min.y;
        if (penetration <= 0f)
            return;

        if (body.isKinematic)
        {
            transform.position += Vector3.up * penetration;
            Physics.SyncTransforms();
        }
        else
        {
            body.position += Vector3.up * penetration;
            Vector3 velocity = body.linearVelocity;
            if (velocity.y < 0f)
                velocity.y = 0f;
            body.linearVelocity = velocity;
        }
    }

    private IEnumerator ReleaseAfterGrabSystemInitialization()
    {
        // Let the grab building block cache the corrected reachable pose, then
        // release gravity so the cube settles naturally onto the floor.
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        ReleaseAnchor("grab-system initialization");
        yield return new WaitForSeconds(1f);
        Physics.SyncTransforms();
        if (cubeCollider != null && body != null)
        {
            Debug.Log("[Tutorial] Grab cube settled. name=" + name +
                ", centerY=" + transform.position.y.ToString("0.000") +
                ", colliderBottomY=" + cubeCollider.bounds.min.y.ToString("0.000") +
                ", floorY=" + floorY.ToString("0.000") +
                ", isKinematic=" + body.isKinematic + ".");
        }
    }

    private void SubscribeToGrabEvents()
    {
        if (subscribed)
            return;

        grabInteractables = GetComponentsInChildren<GrabInteractable>(true);
        bool subscribedToAny = false;
        foreach (GrabInteractable interactable in grabInteractables)
        {
            if (interactable != null && interactable.PointableElement != null)
            {
                interactable.PointableElement.WhenPointerEventRaised += OnPointerEventRaised;
                subscribedToAny = true;
            }
        }
        subscribed = subscribedToAny;
    }

    private void OnPointerEventRaised(PointerEvent pointerEvent)
    {
        if (pointerEvent.Type != PointerEventType.Select)
            return;

        ReleaseAnchor("first select");
    }

    private void ReleaseAnchor(string source)
    {
        if (!anchored)
            return;

        anchored = false;
        if (body != null)
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.WakeUp();
        }

        Debug.Log("[Tutorial] Grab cube anchor released on " + source + ". name=" + name + ".");
    }

    private void OnDestroy()
    {
        if (grabInteractables == null)
            return;

        foreach (GrabInteractable interactable in grabInteractables)
        {
            if (interactable != null && interactable.PointableElement != null)
                interactable.PointableElement.WhenPointerEventRaised -= OnPointerEventRaised;
        }
    }
}
