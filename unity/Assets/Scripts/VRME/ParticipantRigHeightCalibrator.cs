using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

/// <summary>
/// Recalibrates every immersive scene from the current tracked headset pose so
/// the virtual eye/floor relationship is consistent. Runtime recenter events
/// are applied by OpenXR's reference space; this component detects missed Link
/// callbacks and then verifies height without applying a second world transform.
/// </summary>
public class ParticipantRigHeightCalibrator : MonoBehaviour
{
    private const float StandardSceneEyeHeight = 1.62f;
    private const float TutorialEyeHeight = 1.62f;

    public Transform centerEyeAnchor;
    [Tooltip("Desired headset height above the virtual floor after calibration.")]
    public float targetEyeHeight = 1.62f;
    public float calibrationDelay = 1.5f;
    public int sampleFrames = 30;
    [Tooltip("How long to wait for Quest tracking and scene colliders before giving up.")]
    public float calibrationTimeout = 10f;
    [Tooltip("Do not react to ordinary tracking noise or small differences in participant height.")]
    public float correctionThreshold = 0.03f;
    // Large enough to recover from a scene that starts the rig below its floor.
    public float maximumDownwardCorrection = 1.25f;
    public float maximumUpwardCorrection = 1.25f;
    public float groundRayHeight = 2.5f;
    public float groundRayDistance = 5f;

    private Coroutine calibrationRoutine;
    private OVRDisplay subscribedDisplay;
    private readonly List<XRInputSubsystem> subscribedInputSubsystems = new List<XRInputSubsystem>();
    private readonly List<XRInputSubsystem> discoveredInputSubsystems = new List<XRInputSubsystem>();
    private bool openXrRecenteringEnabled;
    private Coroutine sceneRecenterRoutine;
    private float lastSceneRecenterRequestTime = float.NegativeInfinity;
    private int observedOvrRecenterCount;
    private bool hasObservedOvrRecenterCount;
    private Vector3 previousTrackedHeadPosition;
    private float previousTrackedHeadYaw;
    private bool hasPreviousTrackedHeadPose;
    private float trackingStartedAt;
    private float calibratedRigY;
    private bool hasCalibratedRigY;
    private float lastRigYRestoreLogTime = float.NegativeInfinity;
    public bool InitialCalibrationCompleted { get; private set; }

    private void Awake()
    {
        trackingStartedAt = Time.unscaledTime;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneInstaller()
    {
        SceneManager.sceneLoaded -= InstallForLoadedScene;
        SceneManager.sceneLoaded += InstallForLoadedScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnAllActiveVrRigs()
    {
        if (!ShouldCalibrateScene(SceneManager.GetActiveScene().name))
        {
            return;
        }

        OVRCameraRig[] rigs = Object.FindObjectsByType<OVRCameraRig>(FindObjectsSortMode.None);
        foreach (OVRCameraRig rig in rigs)
        {
            if (rig == null || !rig.isActiveAndEnabled)
                continue;

            ParticipantRigHeightCalibrator calibrator =
                rig.GetComponent<ParticipantRigHeightCalibrator>();
            if (calibrator == null)
            {
                calibrator = rig.gameObject.AddComponent<ParticipantRigHeightCalibrator>();
                Debug.Log("[VRME] Participant height calibrator attached to " + rig.gameObject.name + ".");
            }

            calibrator.targetEyeHeight = GetTargetEyeHeight(SceneManager.GetActiveScene().name);
        }
    }

    private static void InstallForLoadedScene(Scene scene, LoadSceneMode mode)
    {
        if (ShouldCalibrateScene(scene.name))
        {
            InstallOnAllActiveVrRigs();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySubscribeToRuntimeRecenter();
        TryEnableAndSubscribeOpenXRRecentering();
    }

    private void Start()
    {
        TrySubscribeToRuntimeRecenter();
        TryEnableAndSubscribeOpenXRRecentering();
        RequestCalibration(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        // OVRManager.display can be created after this component is enabled.
        if (subscribedDisplay == null)
        {
            TrySubscribeToRuntimeRecenter();
        }
        if (!openXrRecenteringEnabled || subscribedInputSubsystems.Count == 0)
        {
            TryEnableAndSubscribeOpenXRRecentering();
        }
        TryDetectOvrRecenterCounterChange();
    }

    private void LateUpdate()
    {
        DetectTrackingPoseRecenter();
        PreserveCalibratedRigHeightAfterLocomotion();
    }

    private void PreserveCalibratedRigHeightAfterLocomotion()
    {
        if (!InitialCalibrationCompleted || !hasCalibratedRigY)
            return;

        float changedBy = transform.position.y - calibratedRigY;
        if (Mathf.Abs(changedBy) < 0.005f)
            return;

        // Oculus teleport locomotion correctly changes X/Z, but writes the
        // destination surface Y onto the OVRCameraRig root. That discards the
        // participant floor correction and makes every floor object physically
        // unreachable again. Preserve horizontal locomotion and restore only Y.
        Vector3 correctedPosition = transform.position;
        correctedPosition.y = calibratedRigY;
        transform.position = correctedPosition;

        if (Time.unscaledTime - lastRigYRestoreLogTime > 0.5f)
        {
            Debug.Log("[VRME] Restored calibrated rig Y after locomotion. overwrittenY=" +
                (calibratedRigY + changedBy).ToString("0.000") +
                ", restoredY=" + calibratedRigY.ToString("0.000") + ".");
            lastRigYRestoreLogTime = Time.unscaledTime;
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (subscribedDisplay != null)
        {
            subscribedDisplay.RecenteredPose -= OnRuntimeRecenteredPose;
            subscribedDisplay = null;
        }
        foreach (XRInputSubsystem subsystem in subscribedInputSubsystems)
        {
            if (subsystem != null)
            {
                subsystem.trackingOriginUpdated -= OnOpenXRTrackingOriginUpdated;
            }
        }
        subscribedInputSubsystems.Clear();
        if (calibrationRoutine != null)
        {
            StopCoroutine(calibrationRoutine);
            calibrationRoutine = null;
        }
        if (sceneRecenterRoutine != null)
        {
            StopCoroutine(sceneRecenterRoutine);
            sceneRecenterRoutine = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RequestCalibration(scene);
    }

    private void TrySubscribeToRuntimeRecenter()
    {
        OVRDisplay display = OVRManager.display;
        if (display == null || display == subscribedDisplay)
        {
            return;
        }

        if (subscribedDisplay != null)
        {
            subscribedDisplay.RecenteredPose -= OnRuntimeRecenteredPose;
        }

        subscribedDisplay = display;
        subscribedDisplay.RecenteredPose += OnRuntimeRecenteredPose;
        Debug.Log("[VRME] Oculus long-press recenter synchronization enabled.");
    }

    private void OnRuntimeRecenteredPose()
    {
        Debug.Log("[VRME] Oculus runtime recentered the headset; aligning the current scene to the new pose.");
        RequestSceneRecenter("OVRDisplay");
    }

    private void TryEnableAndSubscribeOpenXRRecentering()
    {
        if (!openXrRecenteringEnabled)
        {
            try
            {
                OpenXRSettings.SetAllowRecentering(true, targetEyeHeight);
                openXrRecenteringEnabled = OpenXRSettings.AllowRecentering;
                Debug.Log("[VRME] OpenXR Oculus-button recentering enabled=" + openXrRecenteringEnabled +
                    ", floorOffset=" + OpenXRSettings.FloorOffset.ToString("0.00") + "m.");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[VRME] Could not enable OpenXR recentering yet: " + exception.Message);
            }
        }

        discoveredInputSubsystems.Clear();
        SubsystemManager.GetSubsystems(discoveredInputSubsystems);
        foreach (XRInputSubsystem subsystem in discoveredInputSubsystems)
        {
            if (subsystem == null || subscribedInputSubsystems.Contains(subsystem))
            {
                continue;
            }

            subsystem.trackingOriginUpdated += OnOpenXRTrackingOriginUpdated;
            subscribedInputSubsystems.Add(subsystem);
            Debug.Log("[VRME] Listening for OpenXR tracking-origin recenter events from " + subsystem.subsystemDescriptor.id + ".");
        }
    }

    private void OnOpenXRTrackingOriginUpdated(XRInputSubsystem subsystem)
    {
        Debug.Log("[VRME] OpenXR tracking origin updated after Oculus-button recenter; aligning current scene.");
        RequestSceneRecenter("OpenXR");
    }

    private void TryDetectOvrRecenterCounterChange()
    {
        int recenterCount;
        try
        {
            recenterCount = OVRPlugin.GetLocalTrackingSpaceRecenterCount();
        }
        catch
        {
            return;
        }

        if (!hasObservedOvrRecenterCount)
        {
            observedOvrRecenterCount = recenterCount;
            hasObservedOvrRecenterCount = true;
            return;
        }

        if (recenterCount == observedOvrRecenterCount)
        {
            return;
        }

        int previousCount = observedOvrRecenterCount;
        observedOvrRecenterCount = recenterCount;
        try
        {
            // Unity's OpenXR InputSubsystem callback is missed by Link for some
            // XR_OCULUS_recenter_event events. Regenerate the recenter space
            // explicitly when Meta's own monotonically increasing count changes.
            OpenXRSettings.RefreshRecenterSpace();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[VRME] Could not refresh OpenXR recenter space: " + exception.Message);
        }

        Debug.Log("[VRME] Meta recenter counter changed " + previousCount + " -> " + recenterCount + ".");
        RequestSceneRecenter("Meta recenter counter");
    }

    private void DetectTrackingPoseRecenter()
    {
        if (centerEyeAnchor == null)
        {
            OVRCameraRig rig = GetComponent<OVRCameraRig>();
            centerEyeAnchor = rig != null ? rig.centerEyeAnchor : transform.Find("TrackingSpace/CenterEyeAnchor");
        }
        if (centerEyeAnchor == null)
        {
            return;
        }

        Vector3 trackedPosition = transform.InverseTransformPoint(centerEyeAnchor.position);
        Vector3 trackedForward = transform.InverseTransformDirection(centerEyeAnchor.forward);
        trackedForward = Vector3.ProjectOnPlane(trackedForward, Vector3.up);
        float trackedYaw = trackedForward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(trackedForward.normalized, Vector3.up).eulerAngles.y
            : previousTrackedHeadYaw;

        if (hasPreviousTrackedHeadPose && Time.unscaledTime - trackingStartedAt > 2f)
        {
            float horizontalJump = Vector2.Distance(
                new Vector2(previousTrackedHeadPosition.x, previousTrackedHeadPosition.z),
                new Vector2(trackedPosition.x, trackedPosition.z));
            float yawJump = Mathf.Abs(Mathf.DeltaAngle(previousTrackedHeadYaw, trackedYaw));

            // A person cannot move this far between rendered frames. These
            // discontinuities are the reliable fallback when Link logs the
            // Oculus recenter event but omits trackingOriginUpdated.
            if (horizontalJump > 0.18f || yawJump > 50f)
            {
                Debug.Log("[VRME] Tracking-pose recenter detected. horizontalJump=" +
                    horizontalJump.ToString("0.00") + "m, yawJump=" + yawJump.ToString("0.0") + "deg.");
                RequestSceneRecenter("tracking-pose reset");
            }
        }

        previousTrackedHeadPosition = trackedPosition;
        previousTrackedHeadYaw = trackedYaw;
        hasPreviousTrackedHeadPose = true;
    }

    private void RequestSceneRecenter(string source)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // Meta's OVRDisplay and OpenXR callbacks can describe the same button
        // recenter. Apply it once so the second callback cannot move the rig again.
        if (Time.unscaledTime - lastSceneRecenterRequestTime < 0.35f)
        {
            return;
        }

        lastSceneRecenterRequestTime = Time.unscaledTime;
        if (sceneRecenterRoutine != null)
        {
            StopCoroutine(sceneRecenterRoutine);
        }
        sceneRecenterRoutine = StartCoroutine(ApplySceneRecenter(source));
    }

    private IEnumerator ApplySceneRecenter(string source)
    {
        // Let OpenXR finish updating the tracking origin before reading the
        // CenterEyeAnchor. Reading it inside the callback gives the old pose.
        yield return null;
        yield return new WaitForEndOfFrame();

        if (centerEyeAnchor == null)
        {
            OVRCameraRig rig = GetComponent<OVRCameraRig>();
            centerEyeAnchor = rig != null ? rig.centerEyeAnchor : transform.Find("TrackingSpace/CenterEyeAnchor");
        }

        if (centerEyeAnchor == null)
        {
            Debug.LogWarning("[VRME] Scene recenter skipped: CenterEyeAnchor not found.");
            sceneRecenterRoutine = null;
            yield break;
        }

        // OpenXR already changed the reference space, which is the safe way to
        // make the whole virtual world recenter around a room-scale participant.
        // Applying another transform here cancels that visible change. Rotating
        // scene roots also breaks Unity Terrain detail/grass rendering.
        Vector3 horizontalForward = Vector3.ProjectOnPlane(centerEyeAnchor.forward, Vector3.up);
        float headYaw = horizontalForward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(horizontalForward.normalized, Vector3.up).eulerAngles.y
            : centerEyeAnchor.eulerAngles.y;
        Debug.Log("[VRME] Native OpenXR whole-world recenter accepted from " + source +
            ". headXZ=(" + centerEyeAnchor.position.x.ToString("0.00") + "," +
            centerEyeAnchor.position.z.ToString("0.00") + "), headYaw=" +
            headYaw.ToString("0.0") +
            ". No second scene transform was applied; Terrain and grass remain fixed.");

        sceneRecenterRoutine = null;
        RequestCalibration(SceneManager.GetActiveScene());
    }

    private void RequestCalibration(Scene scene)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (calibrationRoutine != null)
        {
            StopCoroutine(calibrationRoutine);
        }

        InitialCalibrationCompleted = false;
        calibrationRoutine = StartCoroutine(CalibrateForScene(scene.name));
    }

    private void FinishCalibration()
    {
        calibrationRoutine = null;
        InitialCalibrationCompleted = true;
    }

    private IEnumerator CalibrateForScene(string sceneName)
    {
        if (!ShouldCalibrateScene(sceneName))
        {
            FinishCalibration();
            yield break;
        }

        if (centerEyeAnchor == null)
        {
            OVRCameraRig rig = GetComponent<OVRCameraRig>();
            centerEyeAnchor = rig != null ? rig.centerEyeAnchor : null;
        }

        if (centerEyeAnchor == null)
        {
            centerEyeAnchor = transform.Find("TrackingSpace/CenterEyeAnchor");
        }

        if (centerEyeAnchor == null)
        {
            Debug.LogWarning("[VRME] Participant height calibration skipped in " + sceneName +
                ": CenterEyeAnchor not found.");
            FinishCalibration();
            yield break;
        }

        yield return new WaitForSeconds(calibrationDelay);

        float deadline = Time.unscaledTime + Mathf.Max(2f, calibrationTimeout);
        List<float> eyeSamples = new List<float>();
        float floorY = 0f;
        while (Time.unscaledTime < deadline)
        {
            eyeSamples.Clear();
            int frames = Mathf.Max(10, sampleFrames);
            for (int i = 0; i < frames; i++)
            {
                // Tracking validity must use the eye's local height. World Y can
                // legitimately be below zero when the authored rig is misplaced.
                if (centerEyeAnchor.localPosition.y > 0.3f)
                {
                    eyeSamples.Add(centerEyeAnchor.position.y);
                }
                yield return null;
            }

            if (eyeSamples.Count >= 10 && TryResolveFloorY(out floorY))
            {
                break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        if (eyeSamples.Count < 10 || !TryResolveFloorY(out floorY))
        {
            Debug.LogWarning("[VRME] Participant height calibration skipped in " + sceneName +
                ": tracking or floor was unavailable.");
            FinishCalibration();
            yield break;
        }

        eyeSamples.Sort();
        float measuredEyeY = eyeSamples[eyeSamples.Count / 2];
        float measuredHeight = measuredEyeY - floorY;
        float requiredCorrection = targetEyeHeight - measuredHeight;

        if (Mathf.Abs(requiredCorrection) < correctionThreshold)
        {
            Debug.Log("[VRME] Participant rig height already matches the global scene eye height in " + sceneName +
                ". measured=" + measuredHeight.ToString("0.00") + "m, target=" +
                targetEyeHeight.ToString("0.00") + "m, no vertical correction applied.");
            calibratedRigY = transform.position.y;
            hasCalibratedRigY = true;
            FinishCalibration();
            yield break;
        }

        float correction = requiredCorrection < 0f
            ? Mathf.Max(requiredCorrection, -maximumDownwardCorrection)
            : Mathf.Min(requiredCorrection, maximumUpwardCorrection);
        transform.position += Vector3.up * correction;
        calibratedRigY = transform.position.y;
        hasCalibratedRigY = true;
        yield return null;

        float resultingHeight = centerEyeAnchor.position.y - floorY;
        Debug.Log("[VRME] Participant rig height recalibrated from the current headset pose in " + sceneName +
            ". floorY=" + floorY.ToString("0.00") +
            ", measured=" + measuredHeight.ToString("0.00") + "m, target=" +
            targetEyeHeight.ToString("0.00") + "m, correctionY=" + correction.ToString("0.00") +
            "m, resultingHeight=" + resultingHeight.ToString("0.00") + "m.");
        FinishCalibration();
    }

    private bool TryResolveFloorY(out float floorY)
    {
        // Prefer an explicitly authored root Ground in every scene. This avoids
        // choosing decorative floors or ceilings (the prison's imported
        // FloorFloor mesh, for example, also contains its 2.8m ceiling).
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root != null && string.Equals(root.name, "Ground", System.StringComparison.OrdinalIgnoreCase))
            {
                floorY = root.transform.position.y;
                return true;
            }
        }

        Vector3 eyePosition = centerEyeAnchor != null ? centerEyeAnchor.position : transform.position;
        Vector3 origin = eyePosition + Vector3.up * groundRayHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            groundRayHeight + groundRayDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        floorY = float.NegativeInfinity;
        float bestDifference = float.PositiveInfinity;
        bool foundNamedFloor = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.normal.y < 0.65f)
            {
                continue;
            }

            if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            string objectName = hit.collider.name;
            bool looksLikeFloor = hit.collider.CompareTag("Ground") ||
                objectName.IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("ground", System.StringComparison.OrdinalIgnoreCase) >= 0;
            // Pick the named floor closest to the tracked eye. Do not compare it
            // with rig-root Y: that root is exactly what may be badly authored.
            float difference = Mathf.Abs((hit.point.y + targetEyeHeight) - eyePosition.y);
            // Prefer an explicitly named floor, but Tunnel's walkable mesh has
            // no Ground/Floor name, so accept a sufficiently horizontal surface
            // when no named floor is available.
            if ((looksLikeFloor && !foundNamedFloor) ||
                (looksLikeFloor == foundNamedFloor && difference < bestDifference))
            {
                foundNamedFloor = looksLikeFloor;
                bestDifference = difference;
                floorY = hit.point.y;
            }
        }

        return !float.IsNegativeInfinity(floorY);
    }

    private static bool ShouldCalibrateScene(string sceneName)
    {
        return string.Equals(sceneName, "Tutorial_Interaction", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Lake", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Attic", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Puppies", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "SolitaryConfinement", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Tunnel", System.StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sceneName, "Elephant", System.StringComparison.OrdinalIgnoreCase);
    }

    private static float GetTargetEyeHeight(string sceneName)
    {
        return string.Equals(sceneName, "Tutorial_Interaction", System.StringComparison.OrdinalIgnoreCase)
            ? TutorialEyeHeight
            : StandardSceneEyeHeight;
    }
}
