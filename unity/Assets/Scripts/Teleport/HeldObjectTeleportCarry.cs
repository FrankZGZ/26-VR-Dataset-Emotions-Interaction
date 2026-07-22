using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Locomotion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps selected Meta Interaction objects with the player across an instant teleport.
/// Installed at runtime so every experiment scene gets the same behaviour.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class HeldObjectTeleportCarry : MonoBehaviour
{
    private sealed class HeldPose
    {
        public Transform Target;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Rigidbody Body;
    }

    private static HeldObjectTeleportCarry instance;

    private readonly List<TeleportInteractor> teleportInteractors = new();
    private readonly List<ILocomotionEventHandler> locomotionHandlers = new();
    private readonly List<HeldPose> heldPoses = new();
    private readonly HashSet<Transform> capturedTransforms = new();

    private Transform playerOrigin;
    private bool awaitingTeleport;
    private Coroutine settleRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null)
        {
            instance.RebindScene();
            return;
        }

        GameObject host = new GameObject("[VRME] Held Object Teleport Carry");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<HeldObjectTeleportCarry>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        RebindScene();
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindScene();
        instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindScene();
    }

    private void RebindScene()
    {
        UnbindScene();
        AllowParallelControllerGrabAndTeleport();
        playerOrigin = FindPlayerOrigin();

        TeleportInteractor[] interactors = FindObjectsByType<TeleportInteractor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TeleportInteractor interactor in interactors)
        {
            interactor.WhenLocomotionPerformed += OnTeleportRequested;
            teleportInteractors.Add(interactor);
        }

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is ILocomotionEventHandler handler)
            {
                handler.WhenLocomotionEventHandled += OnLocomotionHandled;
                locomotionHandlers.Add(handler);
            }
        }
    }

    private static void AllowParallelControllerGrabAndTeleport()
    {
        BestSelectInteractorGroup[] groups = FindObjectsByType<BestSelectInteractorGroup>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (BestSelectInteractorGroup group in groups)
        {
            if (group.Interactors == null)
            {
                continue;
            }

            bool containsTeleport = false;
            List<IInteractor> remainingGroupInteractors = new();
            List<GrabInteractor> independentGrabInteractors = new();

            foreach (IInteractor interactor in group.Interactors)
            {
                if (interactor is TeleportInteractor)
                {
                    containsTeleport = true;
                }

                if (interactor is GrabInteractor grabInteractor)
                {
                    independentGrabInteractors.Add(grabInteractor);
                }
                else
                {
                    remainingGroupInteractors.Add(interactor);
                }
            }

            if (!containsTeleport || independentGrabInteractors.Count == 0)
            {
                continue;
            }

            // BestSelectInteractorGroup disables every sibling interactor while one
            // member is selecting. A controller grab therefore disabled its teleport
            // interactor. Drive grabbing independently so both selections can coexist.
            group.InjectInteractors(remainingGroupInteractors);
            foreach (GrabInteractor grabInteractor in independentGrabInteractors)
            {
                grabInteractor.IsRootDriver = true;
                grabInteractor.Enable();
            }

            Debug.Log(
                $"[HeldObjectTeleportCarry] Enabled simultaneous grab and teleport for " +
                $"{group.gameObject.name}; separated {independentGrabInteractors.Count} grab interactor(s).");
        }
    }

    private void UnbindScene()
    {
        foreach (TeleportInteractor interactor in teleportInteractors)
        {
            if (interactor != null)
            {
                interactor.WhenLocomotionPerformed -= OnTeleportRequested;
            }
        }
        teleportInteractors.Clear();

        foreach (ILocomotionEventHandler handler in locomotionHandlers)
        {
            if (handler is Object unityObject && unityObject != null)
            {
                handler.WhenLocomotionEventHandled -= OnLocomotionHandled;
            }
        }
        locomotionHandlers.Clear();
        heldPoses.Clear();
        capturedTransforms.Clear();
        awaitingTeleport = false;
    }

    private void OnTeleportRequested(LocomotionEvent locomotionEvent)
    {
        if (!IsInstantTeleport(locomotionEvent))
        {
            return;
        }

        if (settleRoutine != null)
        {
            StopCoroutine(settleRoutine);
            settleRoutine = null;
        }
        heldPoses.Clear();
        capturedTransforms.Clear();
        awaitingTeleport = false;

        if (playerOrigin == null)
        {
            playerOrigin = FindPlayerOrigin();
        }
        if (playerOrigin == null)
        {
            return;
        }

        CaptureSelectedObjects<GrabInteractable>();
        CaptureSelectedObjects<HandGrabInteractable>();
        awaitingTeleport = heldPoses.Count > 0;

        if (awaitingTeleport)
        {
            Debug.Log($"[HeldObjectTeleportCarry] Carrying {heldPoses.Count} selected object(s) through teleport.");
        }
    }

    private void CaptureSelectedObjects<TInteractable>() where TInteractable : MonoBehaviour
    {
        TInteractable[] interactables = FindObjectsByType<TInteractable>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (TInteractable component in interactables)
        {
            bool selected = component switch
            {
                GrabInteractable grab => HasSelector(grab.SelectingInteractorViews),
                HandGrabInteractable handGrab => HasSelector(handGrab.SelectingInteractorViews),
                _ => false
            };

            if (selected)
            {
                Capture(component.transform);
            }
        }
    }

    private static bool HasSelector(IEnumerable<IInteractorView> selectors)
    {
        foreach (IInteractorView selector in selectors)
        {
            if (selector != null)
            {
                return true;
            }
        }
        return false;
    }

    private void Capture(Transform target)
    {
        if (target == null || !capturedTransforms.Add(target))
        {
            return;
        }

        heldPoses.Add(new HeldPose
        {
            Target = target,
            LocalPosition = playerOrigin.InverseTransformPoint(target.position),
            LocalRotation = Quaternion.Inverse(playerOrigin.rotation) * target.rotation,
            Body = target.GetComponent<Rigidbody>()
        });
    }

    private void OnLocomotionHandled(LocomotionEvent locomotionEvent, Pose delta)
    {
        if (!awaitingTeleport || !IsInstantTeleport(locomotionEvent))
        {
            return;
        }

        RestoreHeldObjects();
        if (settleRoutine != null)
        {
            StopCoroutine(settleRoutine);
        }
        settleRoutine = StartCoroutine(SettleAfterTeleport());
        awaitingTeleport = false;
    }

    private IEnumerator SettleAfterTeleport()
    {
        // Re-apply after the grab/physics systems finish their teleport frame.
        yield return new WaitForEndOfFrame();
        RestoreHeldObjects();
        yield return null;
        RestoreHeldObjects();
        heldPoses.Clear();
        capturedTransforms.Clear();
        settleRoutine = null;
    }

    private void RestoreHeldObjects()
    {
        if (playerOrigin == null)
        {
            playerOrigin = FindPlayerOrigin();
        }
        if (playerOrigin == null)
        {
            return;
        }

        foreach (HeldPose heldPose in heldPoses)
        {
            if (heldPose.Target == null)
            {
                continue;
            }

            Vector3 position = playerOrigin.TransformPoint(heldPose.LocalPosition);
            Quaternion rotation = playerOrigin.rotation * heldPose.LocalRotation;
            if (heldPose.Body != null)
            {
                heldPose.Body.position = position;
                heldPose.Body.rotation = rotation;
                heldPose.Body.linearVelocity = Vector3.zero;
                heldPose.Body.angularVelocity = Vector3.zero;
            }
            else
            {
                heldPose.Target.SetPositionAndRotation(position, rotation);
            }
        }
    }

    private static bool IsInstantTeleport(LocomotionEvent locomotionEvent)
    {
        return locomotionEvent.Translation == LocomotionEvent.TranslationType.Absolute
            || locomotionEvent.Translation == LocomotionEvent.TranslationType.AbsoluteEyeLevel;
    }

    private static Transform FindPlayerOrigin()
    {
        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        if (cameraRig != null)
        {
            return cameraRig.transform;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform.root : null;
    }
}
