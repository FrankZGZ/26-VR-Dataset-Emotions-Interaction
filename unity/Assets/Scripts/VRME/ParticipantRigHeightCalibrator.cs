using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

/// <summary>
/// Recalibrates every immersive scene from the current tracked headset pose so
/// the virtual eye/floor relationship is consistent. Runtime recenter events
/// additionally put the tracked head back at the authored horizontal spawn and
/// align the participant's current look direction with the authored forward.
/// </summary>
public class ParticipantRigHeightCalibrator : MonoBehaviour
{
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
    private Vector3 authoredRigPosition;
    private float authoredRigYaw;
    private bool authoredPoseCaptured;
    private Coroutine sceneRecenterRoutine;
    private float lastSceneRecenterRequestTime = float.NegativeInfinity;

    private void Awake()
    {
        // This component is installed immediately after the scene loads, before
        // participant height calibration moves the rig vertically.
        CaptureAuthoredRigPose();
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
            if (rig != null && rig.isActiveAndEnabled &&
                rig.GetComponent<ParticipantRigHeightCalibrator>() == null)
            {
                rig.gameObject.AddComponent<ParticipantRigHeightCalibrator>();
                Debug.Log("[VRME] Participant height calibrator attached to " + rig.gameObject.name + ".");
            }
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

    private void CaptureAuthoredRigPose()
    {
        if (authoredPoseCaptured)
        {
            return;
        }

        authoredRigPosition = transform.position;
        authoredRigYaw = transform.eulerAngles.y;
        authoredPoseCaptured = true;
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
        CaptureAuthoredRigPose();

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

        Vector3 horizontalForward = Vector3.ProjectOnPlane(centerEyeAnchor.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude > 0.0001f)
        {
            float trackedHeadYaw = Quaternion.LookRotation(horizontalForward.normalized, Vector3.up).eulerAngles.y;
            float yawCorrection = Mathf.DeltaAngle(trackedHeadYaw, authoredRigYaw);
            transform.RotateAround(centerEyeAnchor.position, Vector3.up, yawCorrection);
        }

        // Re-read the head after rotating around it, then put it exactly at the
        // scene's authored X/Z start. Height remains the calibrator's concern.
        Vector3 headPosition = centerEyeAnchor.position;
        transform.position += new Vector3(
            authoredRigPosition.x - headPosition.x,
            0f,
            authoredRigPosition.z - headPosition.z);

        Debug.Log("[VRME] Scene recentered from " + source +
            ". headXZ=(" + centerEyeAnchor.position.x.ToString("0.00") + "," +
            centerEyeAnchor.position.z.ToString("0.00") + "), targetXZ=(" +
            authoredRigPosition.x.ToString("0.00") + "," + authoredRigPosition.z.ToString("0.00") +
            "), headYaw=" + centerEyeAnchor.eulerAngles.y.ToString("0.0") +
            ", targetYaw=" + authoredRigYaw.ToString("0.0") + ".");

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

        calibrationRoutine = StartCoroutine(CalibrateForScene(scene.name));
    }

    private IEnumerator CalibrateForScene(string sceneName)
    {
        if (!ShouldCalibrateScene(sceneName))
        {
            calibrationRoutine = null;
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
            calibrationRoutine = null;
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
            calibrationRoutine = null;
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
            calibrationRoutine = null;
            yield break;
        }

        float correction = requiredCorrection < 0f
            ? Mathf.Max(requiredCorrection, -maximumDownwardCorrection)
            : Mathf.Min(requiredCorrection, maximumUpwardCorrection);
        transform.position += Vector3.up * correction;
        yield return null;

        float resultingHeight = centerEyeAnchor.position.y - floorY;
        Debug.Log("[VRME] Participant rig height recalibrated from the current headset pose in " + sceneName +
            ". floorY=" + floorY.ToString("0.00") +
            ", measured=" + measuredHeight.ToString("0.00") + "m, target=" +
            targetEyeHeight.ToString("0.00") + "m, correctionY=" + correction.ToString("0.00") +
            "m, resultingHeight=" + resultingHeight.ToString("0.00") + "m.");
        calibrationRoutine = null;
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
}
