using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.IO; // Add for file operations
using System;

public class CameraPoseSender : MonoBehaviour
{
    public static string LatestRuntimeContextText = "No CameraPoseSender runtime context yet.";
    public static bool VoiceSamplingActive { get; private set; }

    // Serializable class representing each recorded pose
    [System.Serializable]
    public class CameraPose
    {
        public Vector3 position;
        public Quaternion orientation;
        public string timestamp;
    }

    [System.Serializable]
    public class GazeEyeSample
    {
        public bool isValid;
        public float confidence;
        public Vector3 position;
        public Quaternion orientation;
        public Vector3 direction;
    }

    [System.Serializable]
    public class GazeSample
    {
        public string timestamp;
        public bool available;
        public bool trackingEnabled;
        public double deviceTime;
        public GazeEyeSample leftEye;
        public GazeEyeSample rightEye;
        public bool usedHeadForwardFallback;
        public bool hitInteractionObject;
        public string hitObjectName;
        public string hitDisplayName;
        public Vector3 hitPoint;
        public float hitDistance;
    }

    [System.Serializable]
    public class GazeObjectSummary
    {
        public string objectName;
        public string displayName;
        public bool wasSeen;
        public bool attendedLongEnough;
        public int hitSampleCount;
        public int fixationCount;
        public float totalDwellSeconds;
        public float maxContinuousDwellSeconds;
        public string firstSeenTimestamp;
        public string lastSeenTimestamp;
    }

    [System.Serializable]
    public class FaceExpressionValue
    {
        public string name;
        public float value;
    }

    [System.Serializable]
    public class FaceExpressionSample
    {
        public string timestamp;
        public bool available;
        public bool trackingEnabled;
        public bool validExpressions;
        public bool eyeFollowingBlendshapesValid;
        public string dataSource;
        public float lowerFaceConfidence;
        public float upperFaceConfidence;
        public List<FaceExpressionValue> expressions = new List<FaceExpressionValue>();
    }

    [System.Serializable]
    public class HeartRateSample
    {
        public string timestamp;
        public bool available;
        public float bpm;
        public string source;
    }

    [System.Serializable]
    public class LiveDebugSnapshot
    {
        public string loginId;
        public string participantId;
        public string sceneName;
        public string timestamp;
        public string persistentDataPath;
        public int cameraPoseCount;
        public int gazeSampleCount;
        public int gazeHitSampleCount;
        public int gazeObjectSummaryCount;
        public int faceExpressionSampleCount;
        public int interactionTrackerCount;
        public int interactionSampleCount;
        public CameraPose latestCameraPose;
        public GazeSample latestGazeSample;
        public List<GazeObjectSummary> gazeObjectSummaries = new List<GazeObjectSummary>();
    }

    // Serializable class representing all the data to be sent to the server
    [System.Serializable]
    public class DataToSend
    {
        public string loginId;
        public string participantId;
        public string sceneName;
        public List<CameraPose> cameraPoses = new List<CameraPose>();
        public List<GazeSample> gazeSamples = new List<GazeSample>();
        public List<GazeObjectSummary> gazeObjectSummaries = new List<GazeObjectSummary>();
        public List<FaceExpressionSample> faceExpressionSamples = new List<FaceExpressionSample>();
        public List<HeartRateSample> heartRateSamples = new List<HeartRateSample>();
    }

    public GameObject cameraRig; // Reference to the camera or XR rig
    public GameObject backupTriggerObject; // If this object is active and data hasn't been saved, trigger saving.
    [Header("Optional multimodal logging")]
    public bool recordEyeGaze = true;
    public bool recordGazeObjectAttention = true;
    [Tooltip("When eye tracking is unavailable, use the headset forward direction as a coarse attention proxy.")]
    public bool useHeadForwardAsGazeFallback = true;
    public bool recordFaceExpressions = true;
    public bool recordHeartRatePlaceholder = true;
    [Tooltip("Minimum eye tracking confidence to mark the eye sample as valid for analysis.")]
    [Range(0f, 1f)] public float gazeConfidenceThreshold = 0.5f;
    [Range(0.1f, 100f)] public float gazeRayMaxDistance = 20f;
    [Range(1f, 30f)] public float gazeNearRayAngleDegrees = 14f;
    [Range(0.1f, 5f)] public float gazeLongAttentionThresholdSeconds = 0.5f;
    [Header("Sampling trigger")]
    [Tooltip("When on, head/gaze is recorded only while the VRME voice key is held.")]
    public bool sampleOnlyWhileVoiceRecording = true;
    [Tooltip("Records one sample immediately when the voice key is pressed and another when it is released.")]
    public bool sampleOnVoiceRecordingEdges = true;
    [Header("Debug output")]
    public bool logRuntimeTrackingState = true;
    public bool writeLiveDebugSnapshot = true;
    [Range(1f, 30f)] public float runtimeDebugIntervalSeconds = 5f;
    [Header("Gaze visualization")]
    public bool showGazeDebugMarker = true;
    public bool showGazeDebugRay = true;
    public bool showGazeDebugOnlyWhileVoiceSampling = true;
    [Range(0.01f, 0.25f)] public float gazeDebugMarkerSize = 0.06f;
    [Range(0.5f, 10f)] public float gazeDebugDefaultDistance = 3f;
    public Color gazeDebugColor = new Color(0f, 1f, 0.25f, 1f);

    private List<CameraPose> bufferedPoses = new List<CameraPose>();
    private List<GazeSample> bufferedGazeSamples = new List<GazeSample>();
    private List<FaceExpressionSample> bufferedFaceExpressionSamples = new List<FaceExpressionSample>();
    private List<HeartRateSample> bufferedHeartRateSamples = new List<HeartRateSample>();
    private float timeSinceLastRecord = 0.0f; // Timer for recording poses
    private const float recordInterval = 0.1f; // Time interval for recording poses
    private string localDirectoryPath;
    private bool isSessionEnded = false; // Flag to prevent multiple saves
    private OVRPlugin.EyeGazesState eyeGazesState;
    private OVRPlugin.FaceState faceState;
    private OVRFaceExpressions faceExpressions;
    private bool attemptedStartEyeTracking;
    private bool attemptedStartFaceTracking;
    private readonly Dictionary<string, GazeObjectAccumulator> gazeObjectAccumulators = new Dictionary<string, GazeObjectAccumulator>();
    private string currentGazeObjectKey;
    private float currentGazeObjectRunSeconds;
    private float runtimeDebugTimer;
    private CameraPose latestCameraPose;
    private GazeSample latestGazeSample;
    private int interactionSampleCount;
    private GameObject gazeDebugMarker;
    private LineRenderer gazeDebugRay;

    public static void BeginVoiceSampling()
    {
        VoiceSamplingActive = true;
        CameraPoseSender[] senders = FindObjectsByType<CameraPoseSender>(FindObjectsSortMode.None);
        foreach (CameraPoseSender sender in senders)
        {
            if (sender != null && sender.isActiveAndEnabled)
            {
                sender.BeginVoiceSamplingInstance();
            }
        }
    }

    public static void EndVoiceSampling()
    {
        VoiceSamplingActive = false;
        CameraPoseSender[] senders = FindObjectsByType<CameraPoseSender>(FindObjectsSortMode.None);
        foreach (CameraPoseSender sender in senders)
        {
            if (sender != null && sender.isActiveAndEnabled)
            {
                sender.EndVoiceSamplingInstance();
            }
        }
    }

    private void Start()
    {
        string participantId = PlayerData.participantId;
        localDirectoryPath = Path.Combine(Application.persistentDataPath, "CameraPoseData", participantId);
        if (!Directory.Exists(localDirectoryPath))
        {
            Directory.CreateDirectory(localDirectoryPath);
            Debug.Log("[Debug] Created directory: " + localDirectoryPath);
        }

        faceExpressions = FindObjectOfType<OVRFaceExpressions>();
        TryStartOptionalTracking();
        CreateGazeDebugVisuals();
    }

    private void Update()
    {
        // Update timers
        timeSinceLastRecord += Time.deltaTime;

        if (ShouldRecordTrackingSample())
        {
            timeSinceLastRecord = 0.0f;
            RecordTrackingSample();
        }

        if (logRuntimeTrackingState || writeLiveDebugSnapshot)
        {
            runtimeDebugTimer += Time.deltaTime;
            if (runtimeDebugTimer >= runtimeDebugIntervalSeconds)
            {
                runtimeDebugTimer = 0f;
                if (logRuntimeTrackingState)
                {
                    LogRuntimeTrackingState();
                }

                if (writeLiveDebugSnapshot)
                {
                    WriteLiveDebugSnapshot();
                }
            }
        }

        // Backup Trigger Logic: If data hasn't been saved yet and the backup object becomes active, save the data.
        if (!isSessionEnded && backupTriggerObject != null && backupTriggerObject.activeInHierarchy)
        {
            Debug.Log("[Debug] Backup trigger object is active. Saving camera pose data as a fallback.");
            EndSessionAndSendData();
        }

        if (showGazeDebugOnlyWhileVoiceSampling && !VoiceSamplingActive)
        {
            SetGazeDebugVisible(false);
        }
    }

    // Function to record the camera's pose
    private void RecordCameraPose()
    {
        CameraPose pose = new CameraPose
        {
            position = cameraRig.transform.position,
            orientation = cameraRig.transform.rotation,
            timestamp = System.DateTime.UtcNow.ToString("o")  // Using ISO 8601 format for timestamp
        };
        bufferedPoses.Add(pose);
        latestCameraPose = pose;
    }

    private void TryStartOptionalTracking()
    {
        if (recordEyeGaze && !attemptedStartEyeTracking)
        {
            attemptedStartEyeTracking = true;
            if (!OVRPlugin.eyeTrackingEnabled && !OVRPlugin.StartEyeTracking())
            {
                Debug.LogWarning("[Debug] Eye tracking did not start. This is expected on devices without eye tracking or without permission.");
            }
        }

        if (recordFaceExpressions && faceExpressions == null && !attemptedStartFaceTracking)
        {
            attemptedStartFaceTracking = true;
            OVRPlugin.FaceTrackingDataSource[] sources =
            {
                OVRPlugin.FaceTrackingDataSource.Visual
            };

            if (!OVRPlugin.faceTracking2Enabled && !OVRPlugin.StartFaceTracking2(sources))
            {
                Debug.LogWarning("[Debug] Face tracking did not start. This is expected on devices without face tracking or without permission.");
            }
        }
    }

    private bool ShouldRecordTrackingSample()
    {
        if (!sampleOnlyWhileVoiceRecording)
        {
            return timeSinceLastRecord >= recordInterval;
        }

        return VoiceSamplingActive && timeSinceLastRecord >= recordInterval;
    }

    private void BeginVoiceSamplingInstance()
    {
        timeSinceLastRecord = 0f;
        interactionSampleCount = 0;
        gazeObjectAccumulators.Clear();
        currentGazeObjectKey = null;
        currentGazeObjectRunSeconds = 0f;
        if (sampleOnVoiceRecordingEdges)
        {
            RecordTrackingSample();
        }
    }

    private void EndVoiceSamplingInstance()
    {
        if (sampleOnVoiceRecordingEdges)
        {
            RecordTrackingSample();
        }

        UpdateLatestRuntimeContextText();
    }

    private void RecordTrackingSample()
    {
        RecordCameraPose();
        RecordGaze();
        RecordFaceExpressions();
        RecordHeartRatePlaceholder();
        interactionSampleCount++;
        UpdateLatestRuntimeContextText();
    }

    private void RecordGaze()
    {
        if (!recordEyeGaze)
        {
            return;
        }

        string timestamp = System.DateTime.UtcNow.ToString("o");
        bool trackingEnabled = OVRPlugin.eyeTrackingEnabled;
        bool available = trackingEnabled && OVRPlugin.GetEyeGazesState(OVRPlugin.Step.Render, -1, ref eyeGazesState);

        GazeSample sample = new GazeSample
        {
            timestamp = timestamp,
            available = available,
            trackingEnabled = trackingEnabled,
            deviceTime = available ? eyeGazesState.Time : 0,
            leftEye = new GazeEyeSample(),
            rightEye = new GazeEyeSample()
        };

        if (available && eyeGazesState.EyeGazes != null && eyeGazesState.EyeGazes.Length >= 2)
        {
            sample.leftEye = BuildGazeEyeSample(eyeGazesState.EyeGazes[(int)OVRPlugin.Eye.Left]);
            sample.rightEye = BuildGazeEyeSample(eyeGazesState.EyeGazes[(int)OVRPlugin.Eye.Right]);
        }

        if (recordGazeObjectAttention)
        {
            UpdateGazeObjectHit(sample);
        }

        bufferedGazeSamples.Add(sample);
        latestGazeSample = sample;
    }

    private void LogRuntimeTrackingState()
    {
        int hitCount = CountGazeHitSamples();
        int trackerCount = FindObjectsByType<InteractionTracker>(FindObjectsSortMode.None).Length;

        Debug.Log("[CameraPoseSender] runtime " +
            "scene=" + SceneManager.GetActiveScene().name +
            ", poses=" + bufferedPoses.Count +
            ", gazeSamples=" + bufferedGazeSamples.Count +
            ", gazeHits=" + hitCount +
            ", trackers=" + trackerCount +
            ", interactionSamples=" + interactionSampleCount +
            ", " + LatestRuntimeContextText.Replace("\n", " | "));
    }

    private void UpdateLatestRuntimeContextText()
    {
        string latestGaze = latestGazeSample != null
            ? "attentionSource=" + (latestGazeSample.usedHeadForwardFallback ? "head_forward_fallback" : (latestGazeSample.available ? "eye_gaze" : "none")) +
              ", gaze available=" + latestGazeSample.available +
              ", trackingEnabled=" + latestGazeSample.trackingEnabled +
              ", headFallback=" + latestGazeSample.usedHeadForwardFallback +
              ", hit=" + latestGazeSample.hitInteractionObject +
              ", hitName=" + latestGazeSample.hitDisplayName +
              ", hitDistance=" + latestGazeSample.hitDistance.ToString("0.00")
            : "gaze none";

        string latestPose = latestCameraPose != null
            ? "head position=" + FormatVector(latestCameraPose.position) +
              ", head forward=" + FormatVector(latestCameraPose.orientation * Vector3.forward)
            : "head none";

        LatestRuntimeContextText =
            "voiceSamplingActive=" + VoiceSamplingActive + "\n" +
            latestPose + "\n" +
            latestGaze + "\n" +
            "attendedObjects=" + BuildAttendedObjectsContextText() + "\n" +
            "gazeSampleCount=" + bufferedGazeSamples.Count +
            ", gazeHitSampleCount=" + CountGazeHitSamples() +
            ", gazeObjectSummaryCount=" + gazeObjectAccumulators.Count +
            ", interactionSampleCount=" + interactionSampleCount;
    }

    private string BuildAttendedObjectsContextText()
    {
        List<GazeObjectSummary> summaries = BuildGazeObjectSummaries();
        if (summaries.Count == 0)
        {
            return "none";
        }

        summaries.Sort((a, b) =>
        {
            int attendedCompare = b.attendedLongEnough.CompareTo(a.attendedLongEnough);
            if (attendedCompare != 0)
            {
                return attendedCompare;
            }

            int dwellCompare = b.totalDwellSeconds.CompareTo(a.totalDwellSeconds);
            if (dwellCompare != 0)
            {
                return dwellCompare;
            }

            return b.hitSampleCount.CompareTo(a.hitSampleCount);
        });

        List<string> parts = new List<string>();
        int count = Mathf.Min(5, summaries.Count);
        for (int i = 0; i < count; i++)
        {
            GazeObjectSummary summary = summaries[i];
            string name = string.IsNullOrWhiteSpace(summary.displayName) ? summary.objectName : summary.displayName;
            parts.Add(name +
                " seen=" + summary.wasSeen +
                " attendedLongEnough=" + summary.attendedLongEnough +
                " dwell=" + summary.totalDwellSeconds.ToString("0.0") + "s" +
                " maxDwell=" + summary.maxContinuousDwellSeconds.ToString("0.0") + "s" +
                " hits=" + summary.hitSampleCount);
        }

        return string.Join("; ", parts);
    }

    private void WriteLiveDebugSnapshot()
    {
        try
        {
            string participantId = PlayerData.participantId;
            string sceneName = SceneManager.GetActiveScene().name;
            string directoryPath = Path.Combine(Application.persistentDataPath, "CameraPoseData", participantId);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            LiveDebugSnapshot snapshot = new LiveDebugSnapshot
            {
                loginId = PlayerData.loginId,
                participantId = participantId,
                sceneName = sceneName,
                timestamp = System.DateTime.UtcNow.ToString("o"),
                persistentDataPath = Application.persistentDataPath,
                cameraPoseCount = bufferedPoses.Count,
                gazeSampleCount = bufferedGazeSamples.Count,
                gazeHitSampleCount = CountGazeHitSamples(),
                gazeObjectSummaryCount = gazeObjectAccumulators.Count,
                faceExpressionSampleCount = bufferedFaceExpressionSamples.Count,
                interactionTrackerCount = FindObjectsByType<InteractionTracker>(FindObjectsSortMode.None).Length,
                interactionSampleCount = interactionSampleCount,
                latestCameraPose = latestCameraPose,
                latestGazeSample = latestGazeSample,
                gazeObjectSummaries = BuildGazeObjectSummaries()
            };

            string filePath = Path.Combine(directoryPath, "CameraPose_LIVE_" + sceneName + ".json");
            File.WriteAllText(filePath, JsonUtility.ToJson(snapshot, true));
            Debug.Log("[CameraPoseSender] Live debug snapshot saved to: " + filePath);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[CameraPoseSender] Failed to write live debug snapshot: " + e.Message);
        }
    }

    private int CountGazeHitSamples()
    {
        int count = 0;
        foreach (GazeSample sample in bufferedGazeSamples)
        {
            if (sample != null && sample.hitInteractionObject)
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.00},{value.y:0.00},{value.z:0.00})";
    }

    private GazeEyeSample BuildGazeEyeSample(OVRPlugin.EyeGazeState eyeState)
    {
        OVRPose pose = eyeState.Pose.ToOVRPose();
        bool valid = eyeState.IsValid && eyeState.Confidence >= gazeConfidenceThreshold;
        return new GazeEyeSample
        {
            isValid = valid,
            confidence = eyeState.Confidence,
            position = pose.position,
            orientation = pose.orientation,
            direction = pose.orientation * Vector3.forward
        };
    }

    private void UpdateGazeObjectHit(GazeSample sample)
    {
        if (!TryGetGazeRay(sample, out Ray gazeRay, out bool usedHeadFallback))
        {
            SetGazeDebugVisible(false);
            UpdateContinuousGazeRun(null, null, false, sample.timestamp);
            return;
        }

        sample.usedHeadForwardFallback = usedHeadFallback;

        InteractionTracker tracker = RaycastInteractionTracker(gazeRay, out RaycastHit hit);
        Vector3 hitPoint = hit.point;
        float hitDistance = hit.distance;
        if (tracker == null)
        {
            tracker = FindInteractionTrackerNearRay(gazeRay, out hitPoint, out hitDistance);
            if (tracker == null)
            {
                UpdateGazeDebugVisuals(gazeRay, gazeRay.origin + gazeRay.direction.normalized * gazeDebugDefaultDistance);
                UpdateContinuousGazeRun(null, null, false, sample.timestamp);
                return;
            }
        }

        string objectKey = tracker.gameObject.GetInstanceID().ToString();
        string displayName = tracker.ContextName;
        sample.hitInteractionObject = true;
        sample.hitObjectName = tracker.gameObject.name;
        sample.hitDisplayName = displayName;
        sample.hitPoint = hitPoint;
        sample.hitDistance = hitDistance;
        UpdateGazeDebugVisuals(gazeRay, hitPoint);

        UpdateContinuousGazeRun(objectKey, tracker, true, sample.timestamp);
        GazeObjectAccumulator accumulator = GetGazeAccumulator(objectKey, tracker);
        accumulator.hitSampleCount++;
        accumulator.totalDwellSeconds += recordInterval;
        accumulator.lastSeenTimestamp = sample.timestamp;
        if (string.IsNullOrEmpty(accumulator.firstSeenTimestamp))
        {
            accumulator.firstSeenTimestamp = sample.timestamp;
        }
    }

    private bool TryGetGazeRay(GazeSample sample, out Ray gazeRay, out bool usedHeadFallback)
    {
        usedHeadFallback = false;

        bool leftValid = sample.leftEye != null && sample.leftEye.isValid;
        bool rightValid = sample.rightEye != null && sample.rightEye.isValid;
        if (sample.available && (leftValid || rightValid))
        {
            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.zero;
            int count = 0;

            if (leftValid)
            {
                origin += sample.leftEye.position;
                direction += sample.leftEye.direction;
                count++;
            }

            if (rightValid)
            {
                origin += sample.rightEye.position;
                direction += sample.rightEye.direction;
                count++;
            }

            gazeRay = new Ray(origin / count, direction.normalized);
            return true;
        }

        bool shouldUseHeadFallback = useHeadForwardAsGazeFallback || !sample.available || !sample.trackingEnabled;
        if (shouldUseHeadFallback)
        {
            Transform head = ResolveHeadTransform();
            if (head != null)
            {
                usedHeadFallback = true;
                gazeRay = new Ray(head.position, head.forward);
                return true;
            }
        }

        gazeRay = new Ray();
        return false;
    }

    private Transform ResolveHeadTransform()
    {
        if (cameraRig != null)
        {
            Camera cameraInRig = cameraRig.GetComponentInChildren<Camera>();
            if (cameraInRig != null)
            {
                return cameraInRig.transform;
            }

            return cameraRig.transform;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private InteractionTracker RaycastInteractionTracker(Ray gazeRay, out RaycastHit bestHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(gazeRay, gazeRayMaxDistance, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            InteractionTracker tracker = hit.collider.GetComponentInParent<InteractionTracker>();
            if (tracker != null && tracker.isActiveAndEnabled)
            {
                bestHit = hit;
                return tracker;
            }
        }

        bestHit = new RaycastHit();
        return null;
    }

    private InteractionTracker FindInteractionTrackerNearRay(Ray gazeRay, out Vector3 bestPoint, out float bestDistance)
    {
        bestPoint = Vector3.zero;
        bestDistance = 0f;
        InteractionTracker[] trackers = FindObjectsByType<InteractionTracker>(FindObjectsSortMode.None);
        InteractionTracker bestTracker = null;
        float bestScore = float.MaxValue;

        foreach (InteractionTracker tracker in trackers)
        {
            if (tracker == null || !tracker.isActiveAndEnabled)
            {
                continue;
            }

            Vector3 targetPoint = GetTrackerAimPoint(tracker);
            Vector3 toTarget = targetPoint - gazeRay.origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.01f || distance > gazeRayMaxDistance)
            {
                continue;
            }

            Vector3 directionToTarget = toTarget / distance;
            float angle = Vector3.Angle(gazeRay.direction, directionToTarget);
            if (angle > gazeNearRayAngleDegrees)
            {
                continue;
            }

            float score = angle + distance * 0.03f;
            if (score < bestScore)
            {
                bestScore = score;
                bestTracker = tracker;
                bestPoint = targetPoint;
                bestDistance = distance;
            }
        }

        return bestTracker;
    }

    private static Vector3 GetTrackerAimPoint(InteractionTracker tracker)
    {
        Collider collider = tracker.GetComponentInChildren<Collider>();
        if (collider != null)
        {
            return collider.bounds.center;
        }

        Renderer renderer = tracker.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            return renderer.bounds.center;
        }

        return tracker.transform.position;
    }

    private void CreateGazeDebugVisuals()
    {
        if (!showGazeDebugMarker && !showGazeDebugRay)
        {
            return;
        }

        if (showGazeDebugMarker && gazeDebugMarker == null)
        {
            gazeDebugMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gazeDebugMarker.name = "VRME Gaze Debug Marker";
            gazeDebugMarker.transform.localScale = Vector3.one * gazeDebugMarkerSize;

            Collider markerCollider = gazeDebugMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = gazeDebugMarker.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                markerRenderer.material = CreateGazeDebugMaterial();
            }
        }

        if (showGazeDebugRay && gazeDebugRay == null)
        {
            GameObject rayObject = new GameObject("VRME Gaze Debug Ray");
            gazeDebugRay = rayObject.AddComponent<LineRenderer>();
            gazeDebugRay.positionCount = 2;
            gazeDebugRay.startWidth = 0.012f;
            gazeDebugRay.endWidth = 0.004f;
            gazeDebugRay.useWorldSpace = true;
            gazeDebugRay.material = CreateGazeDebugMaterial();
        }

        SetGazeDebugVisible(false);
    }

    private Material CreateGazeDebugMaterial()
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.color = gazeDebugColor;
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", gazeDebugColor);
            material.EnableKeyword("_EMISSION");
        }

        return material;
    }

    private void UpdateGazeDebugVisuals(Ray gazeRay, Vector3 targetPoint)
    {
        if (showGazeDebugOnlyWhileVoiceSampling && !VoiceSamplingActive)
        {
            SetGazeDebugVisible(false);
            return;
        }

        CreateGazeDebugVisuals();

        if (gazeDebugMarker != null)
        {
            gazeDebugMarker.transform.position = targetPoint;
            gazeDebugMarker.transform.localScale = Vector3.one * gazeDebugMarkerSize;
            gazeDebugMarker.SetActive(showGazeDebugMarker);
        }

        if (gazeDebugRay != null)
        {
            gazeDebugRay.SetPosition(0, gazeRay.origin);
            gazeDebugRay.SetPosition(1, targetPoint);
            gazeDebugRay.enabled = showGazeDebugRay;
        }
    }

    private void SetGazeDebugVisible(bool visible)
    {
        if (gazeDebugMarker != null)
        {
            gazeDebugMarker.SetActive(visible && showGazeDebugMarker);
        }

        if (gazeDebugRay != null)
        {
            gazeDebugRay.enabled = visible && showGazeDebugRay;
        }
    }

    private void UpdateContinuousGazeRun(string objectKey, InteractionTracker tracker, bool hitObject, string timestamp)
    {
        if (!hitObject || string.IsNullOrEmpty(objectKey))
        {
            FinalizeCurrentGazeRun();
            currentGazeObjectKey = null;
            currentGazeObjectRunSeconds = 0f;
            return;
        }

        if (currentGazeObjectKey == objectKey)
        {
            currentGazeObjectRunSeconds += recordInterval;
            return;
        }

        FinalizeCurrentGazeRun();
        currentGazeObjectKey = objectKey;
        currentGazeObjectRunSeconds = recordInterval;

        GazeObjectAccumulator accumulator = GetGazeAccumulator(objectKey, tracker);
        accumulator.fixationCount++;
        if (string.IsNullOrEmpty(accumulator.firstSeenTimestamp))
        {
            accumulator.firstSeenTimestamp = timestamp;
        }
    }

    private void FinalizeCurrentGazeRun()
    {
        if (string.IsNullOrEmpty(currentGazeObjectKey))
        {
            return;
        }

        if (gazeObjectAccumulators.TryGetValue(currentGazeObjectKey, out GazeObjectAccumulator accumulator))
        {
            accumulator.maxContinuousDwellSeconds = Mathf.Max(
                accumulator.maxContinuousDwellSeconds,
                currentGazeObjectRunSeconds);
        }
    }

    private GazeObjectAccumulator GetGazeAccumulator(string objectKey, InteractionTracker tracker)
    {
        if (!gazeObjectAccumulators.TryGetValue(objectKey, out GazeObjectAccumulator accumulator))
        {
            accumulator = new GazeObjectAccumulator
            {
                objectName = tracker.gameObject.name,
                displayName = tracker.ContextName
            };
            gazeObjectAccumulators.Add(objectKey, accumulator);
        }

        return accumulator;
    }

    private List<GazeObjectSummary> BuildGazeObjectSummaries()
    {
        FinalizeCurrentGazeRun();
        List<GazeObjectSummary> summaries = new List<GazeObjectSummary>();
        foreach (GazeObjectAccumulator accumulator in gazeObjectAccumulators.Values)
        {
            summaries.Add(new GazeObjectSummary
            {
                objectName = accumulator.objectName,
                displayName = accumulator.displayName,
                wasSeen = accumulator.hitSampleCount > 0,
                attendedLongEnough = accumulator.maxContinuousDwellSeconds >= gazeLongAttentionThresholdSeconds,
                hitSampleCount = accumulator.hitSampleCount,
                fixationCount = accumulator.fixationCount,
                totalDwellSeconds = accumulator.totalDwellSeconds,
                maxContinuousDwellSeconds = accumulator.maxContinuousDwellSeconds,
                firstSeenTimestamp = accumulator.firstSeenTimestamp,
                lastSeenTimestamp = accumulator.lastSeenTimestamp
            });
        }

        return summaries;
    }

    private class GazeObjectAccumulator
    {
        public string objectName;
        public string displayName;
        public int hitSampleCount;
        public int fixationCount;
        public float totalDwellSeconds;
        public float maxContinuousDwellSeconds;
        public string firstSeenTimestamp;
        public string lastSeenTimestamp;
    }

    private void RecordFaceExpressions()
    {
        if (!recordFaceExpressions)
        {
            return;
        }

        string timestamp = System.DateTime.UtcNow.ToString("o");
        FaceExpressionSample sample = new FaceExpressionSample
        {
            timestamp = timestamp,
            available = false,
            trackingEnabled = OVRPlugin.faceTracking2Enabled,
            validExpressions = false,
            eyeFollowingBlendshapesValid = false,
            dataSource = "Unavailable",
            lowerFaceConfidence = 0f,
            upperFaceConfidence = 0f
        };

        if (faceExpressions != null)
        {
            sample.trackingEnabled = faceExpressions.FaceTrackingEnabled;
            sample.available = faceExpressions.ValidExpressions;
            sample.validExpressions = faceExpressions.ValidExpressions;
            sample.eyeFollowingBlendshapesValid = faceExpressions.EyeFollowingBlendshapesValid;

            if (faceExpressions.TryGetFaceTrackingDataSource(out OVRFaceExpressions.FaceTrackingDataSource dataSource))
            {
                sample.dataSource = dataSource.ToString();
            }

            if (faceExpressions.TryGetWeightConfidence(OVRFaceExpressions.FaceRegionConfidence.Lower, out float lowerConfidence))
            {
                sample.lowerFaceConfidence = lowerConfidence;
            }

            if (faceExpressions.TryGetWeightConfidence(OVRFaceExpressions.FaceRegionConfidence.Upper, out float upperConfidence))
            {
                sample.upperFaceConfidence = upperConfidence;
            }

            if (faceExpressions.ValidExpressions)
            {
                foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
                {
                    if (expression == OVRFaceExpressions.FaceExpression.Invalid ||
                        expression == OVRFaceExpressions.FaceExpression.Max)
                    {
                        continue;
                    }

                    if (faceExpressions.TryGetFaceExpressionWeight(expression, out float weight))
                    {
                        sample.expressions.Add(new FaceExpressionValue
                        {
                            name = expression.ToString(),
                            value = weight
                        });
                    }
                }
            }
        }
        else if (OVRPlugin.GetFaceState2(OVRPlugin.Step.Render, -1, ref faceState))
        {
            sample.available = faceState.Status.IsValid;
            sample.validExpressions = faceState.Status.IsValid;
            sample.eyeFollowingBlendshapesValid = faceState.Status.IsEyeFollowingBlendshapesValid;
            sample.dataSource = faceState.DataSource.ToString();

            if (faceState.ExpressionWeightConfidences != null)
            {
                if (faceState.ExpressionWeightConfidences.Length > (int)OVRFaceExpressions.FaceRegionConfidence.Lower)
                {
                    sample.lowerFaceConfidence = faceState.ExpressionWeightConfidences[(int)OVRFaceExpressions.FaceRegionConfidence.Lower];
                }

                if (faceState.ExpressionWeightConfidences.Length > (int)OVRFaceExpressions.FaceRegionConfidence.Upper)
                {
                    sample.upperFaceConfidence = faceState.ExpressionWeightConfidences[(int)OVRFaceExpressions.FaceRegionConfidence.Upper];
                }
            }

            if (sample.validExpressions && faceState.ExpressionWeights != null)
            {
                foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
                {
                    if (expression == OVRFaceExpressions.FaceExpression.Invalid ||
                        expression == OVRFaceExpressions.FaceExpression.Max)
                    {
                        continue;
                    }

                    int index = (int)expression;
                    if (index >= 0 && index < faceState.ExpressionWeights.Length)
                    {
                        sample.expressions.Add(new FaceExpressionValue
                        {
                            name = expression.ToString(),
                            value = faceState.ExpressionWeights[index]
                        });
                    }
                }
            }
        }

        bufferedFaceExpressionSamples.Add(sample);
    }

    private void RecordHeartRatePlaceholder()
    {
        if (!recordHeartRatePlaceholder)
        {
            return;
        }

        bufferedHeartRateSamples.Add(new HeartRateSample
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            available = false,
            bpm = 0f,
            source = "No heart-rate sensor connected"
        });
    }

    // Function to save camera pose data to a local file
    private bool SaveCameraPoseDataToFile(string jsonData, string participantId, string sceneName, string timeStr)
    {
        try
        {
            string fileName = $"CameraPose_{participantId}_{sceneName}_{timeStr}.json";
            string directoryPath = Path.Combine(Application.persistentDataPath, "CameraPoseData", participantId);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            string filePath = Path.Combine(directoryPath, fileName);
            File.WriteAllText(filePath, jsonData);
            Debug.Log($"[Debug] Camera pose data saved to: {filePath}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Error] Failed to save camera pose data: {e.Message}");
            return false;
        }
    }

    // Function to be called when the session ends
    public void EndSessionAndSendData()
    {
        // Prevent this from running more than once
        if (isSessionEnded)
        {
            return;
        }
        isSessionEnded = true; // Mark session as ended immediately to prevent race conditions

        // Ensure there are poses to save
        if (bufferedPoses.Count > 0)
        {
            // Create the data object with all buffered poses
            DataToSend data = new DataToSend
            {
                loginId = PlayerData.loginId, 
                participantId = PlayerData.participantId, 
                sceneName = SceneManager.GetActiveScene().name,
                cameraPoses = new List<CameraPose>(bufferedPoses), // Use a copy of the list
                gazeSamples = new List<GazeSample>(bufferedGazeSamples),
                gazeObjectSummaries = BuildGazeObjectSummaries(),
                faceExpressionSamples = new List<FaceExpressionSample>(bufferedFaceExpressionSamples),
                heartRateSamples = new List<HeartRateSample>(bufferedHeartRateSamples)
            };

            string jsonData = JsonUtility.ToJson(data);
            string timeStr = System.DateTime.Now.ToString("dd-MMM_HH-mm-ss");
            
            // Save all data to a single file
            SaveCameraPoseDataToFile(jsonData, data.participantId, data.sceneName, timeStr);
            
            bufferedPoses.Clear(); // Clear the buffer after saving
            bufferedGazeSamples.Clear();
            gazeObjectAccumulators.Clear();
            currentGazeObjectKey = null;
            currentGazeObjectRunSeconds = 0f;
            bufferedFaceExpressionSamples.Clear();
            bufferedHeartRateSamples.Clear();
            Debug.Log("[Debug] All camera pose data for the session saved.");
        }
        else
        {
            Debug.LogWarning("[Debug] No camera pose data to save at the end of session.");
        }
        // Any additional end session logic can be added here
    }

    // Function to save buffered data to file (replaces sending to server)
    // This method is now primarily called by EndSessionAndSendData
    private void SaveBufferedDataToFile()
    {
        if (bufferedPoses.Count > 0)
        {
            DataToSend data = new DataToSend
            {
                loginId = PlayerData.loginId,  // Replace with your actual login ID
                participantId = PlayerData.participantId,  // Replace with your actual participant ID
                sceneName = SceneManager.GetActiveScene().name,
                cameraPoses = bufferedPoses,
                gazeSamples = bufferedGazeSamples,
                gazeObjectSummaries = BuildGazeObjectSummaries(),
                faceExpressionSamples = bufferedFaceExpressionSamples,
                heartRateSamples = bufferedHeartRateSamples
            };

            string jsonData = JsonUtility.ToJson(data);
            string timeStr = System.DateTime.Now.ToString("dd-MMM_HH-mm-ss");
            SaveCameraPoseDataToFile(jsonData, data.participantId, data.sceneName, timeStr);
            // bufferedPoses.Clear(); // Clearing is now handled in EndSessionAndSendData after copying
        }
    }

    // --- network related code ---
    /*
    // Function to send buffered data to the server
    private void SendBufferedDataToServer()
    {
        if (bufferedPoses.Count > 0)
        {
            DataToSend data = new DataToSend
            {
                loginId = PlayerData.loginId,  // Replace with your actual login ID
                participantId = PlayerData.participantId,  // Replace with your actual participant ID
                sceneName = SceneManager.GetActiveScene().name,
                cameraPoses = bufferedPoses
            };

            string jsonData = JsonUtility.ToJson(data);
            StartCoroutine(PostDataCoroutine(jsonData));
            bufferedPoses.Clear();
        }
    }

    // Coroutine for sending a POST request with the given JSON data
    private IEnumerator PostDataCoroutine(string bodyJsonString)
    {
        using (UnityWebRequest request = UnityWebRequest.PostWwwForm(serverURL, bodyJsonString))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Request failed: {request.error}");
            }
            else if (request.responseCode == 200)
            {
                Debug.Log("Data sent successfully!");
            }
            else
            {
                Debug.LogError($"Server returned response code: {request.responseCode}");
            }
        }
    }
    */
}
