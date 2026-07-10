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
    public static string LatestVoiceRuntimeContextText = "No voice-window context yet.";
    public static string LatestVoiceAttentionSummary = "currentAttention=unknown";
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
        public string selectionSource;
        public bool attentionOnly;
        public bool controllerGrabbed;
        public bool interactionUsed;
        public bool currentlyHeld;
        public Vector3 hitPoint;
        public float hitDistance;
    }

    [System.Serializable]
    public class GazeObjectSummary
    {
        public string objectName;
        public string displayName;
        public bool wasSeen;
        public bool attentionOnly;
        public bool controllerGrabbed;
        public bool interactionUsed;
        public bool currentlyHeld;
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
        public string sessionId;
        public string avatarCondition;
        public string sceneName;
        public int sceneIndex;
        public int sceneSequenceLength;
        public string timestamp;
        public string persistentDataPath;
        public bool sampleOnlyWhileVoiceRecording;
        public bool sampleOnVoiceRecordingEdges;
        public bool recordEyeGaze;
        public bool useHeadForwardAsGazeFallback;
        public float gazeConfidenceThreshold;
        public float gazeRayMaxDistance;
        public float gazeNearRayAngleDegrees;
        public float gazeLongAttentionThresholdSeconds;
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
        public string sessionId;
        public string avatarCondition;
        public string sceneName;
        public int sceneIndex;
        public int sceneSequenceLength;
        public string startedAtUtc;
        public string endedAtUtc;
        public bool sampleOnlyWhileVoiceRecording;
        public bool sampleOnVoiceRecordingEdges;
        public bool recordEyeGaze;
        public bool recordGazeObjectAttention;
        public bool useHeadForwardAsGazeFallback;
        public bool recordFaceExpressions;
        public bool recordHeartRatePlaceholder;
        public float gazeConfidenceThreshold;
        public float gazeRayMaxDistance;
        public float gazeNearRayAngleDegrees;
        public float gazeLongAttentionThresholdSeconds;
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
    [Range(1, 20)] public int gazeVoiceMinimumHitSamples = 3;
    [Range(0.1f, 2f)] public float gazeVoiceMinimumDwellSeconds = 0.3f;
    [Tooltip("Name hints for avatar/person objects that should block gaze raycasts even when they do not already have an InteractionTracker.")]
    public string gazeAvatarObjectNameHints = "Rocketbox,ReadyPlayerMe,DigitalHuman,SocialAgent,CompanionAvatar";
    public bool autoAttachAvatarGazeTargets = true;
    public bool forceHideGazeDebugVisuals = true;
    public bool forceShowGazeDebugVisuals = false;
    public bool disableHandTrackingObjects = true;
    public string handTrackingObjectNameHints = "OVRHandDataSource,OVRLeftHandVisual,OVRRightHandVisual,OpenXRLeftHand,OpenXRRightHand,HandRayInteractor,HandRay,ControllerRay,ControllerRayInteractor,RayInteractor,OVRHands,UnityXRHands";
    [Header("Sampling trigger")]
    [Tooltip("When on, head/gaze is recorded only while the VRME voice key is held.")]
    public bool sampleOnlyWhileVoiceRecording = false;
    [Tooltip("Records one sample immediately when the voice key is pressed and another when it is released.")]
    public bool sampleOnVoiceRecordingEdges = true;
    [Header("Debug output")]
    public bool logRuntimeTrackingState = true;
    public bool writeLiveDebugSnapshot = true;
    [Range(1f, 30f)] public float runtimeDebugIntervalSeconds = 5f;
    [Header("Gaze visualization")]
    public bool showGazeDebugMarker = false;
    public bool showGazeDebugRay = false;
    public bool showGazeDebugOnlyWhileVoiceSampling = false;
    [Range(0.01f, 0.25f)] public float gazeDebugMarkerSize = 0.12f;
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
    private string recordingStartedAtUtc;
    private int voiceWindowPoseStartIndex;
    private int voiceWindowGazeStartIndex;
    private float voiceWindowStartedAtRealtime;

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
        forceHideGazeDebugVisuals = true;
        forceShowGazeDebugVisuals = false;
        if (forceHideGazeDebugVisuals)
        {
            showGazeDebugMarker = false;
            showGazeDebugRay = false;
        }
        else if (forceShowGazeDebugVisuals)
        {
            useHeadForwardAsGazeFallback = true;
            showGazeDebugMarker = true;
            showGazeDebugRay = false;
        }

        if (autoAttachAvatarGazeTargets)
        {
            int attachedCount = AttachAvatarGazeTargets();
            Debug.Log("[Gaze] Auto-attached avatar/person gaze targets: " + attachedCount);
        }

        if (disableHandTrackingObjects)
        {
            int disabledCount = DisableHandTrackingObjects();
            Debug.Log("[Input] Disabled hand-tracking objects/components: " + disabledCount);
        }

        CreateGazeDebugVisuals();
        recordingStartedAtUtc = System.DateTime.UtcNow.ToString("o");
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

        UpdateGazeDebugFromCurrentPose();

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
        Transform poseSource = ResolveHeadTransform();
        if (poseSource == null && cameraRig != null)
        {
            poseSource = cameraRig.transform;
        }

        if (poseSource == null)
        {
            return;
        }

        CameraPose pose = new CameraPose
        {
            position = poseSource.position,
            orientation = poseSource.rotation,
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
        voiceWindowPoseStartIndex = bufferedPoses.Count;
        voiceWindowGazeStartIndex = bufferedGazeSamples.Count;
        voiceWindowStartedAtRealtime = Time.realtimeSinceStartup;
        LatestVoiceRuntimeContextText = "voiceRecordingActive=True\nvoice window just started";
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
        UpdateLatestVoiceRuntimeContextText();
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

        GazeSample sample = BuildCurrentGazeSample();

        if (recordGazeObjectAttention)
        {
            UpdateGazeObjectHit(sample);
        }

        bufferedGazeSamples.Add(sample);
        latestGazeSample = sample;
    }

    private GazeSample BuildCurrentGazeSample()
    {
        bool trackingEnabled = OVRPlugin.eyeTrackingEnabled;
        bool available = trackingEnabled && OVRPlugin.GetEyeGazesState(OVRPlugin.Step.Render, -1, ref eyeGazesState);

        GazeSample sample = new GazeSample
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
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

        return sample;
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
              ", selectionSource=" + latestGazeSample.selectionSource +
              ", attentionOnly=" + latestGazeSample.attentionOnly +
              ", everControllerGrabbed=" + latestGazeSample.controllerGrabbed +
              ", everInteractionUsed=" + latestGazeSample.interactionUsed +
              ", currentHeld=" + latestGazeSample.currentlyHeld +
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

    private void UpdateLatestVoiceRuntimeContextText()
    {
        int poseStart = Mathf.Clamp(voiceWindowPoseStartIndex, 0, bufferedPoses.Count);
        int gazeStart = Mathf.Clamp(voiceWindowGazeStartIndex, 0, bufferedGazeSamples.Count);
        int poseCount = bufferedPoses.Count - poseStart;
        int gazeCount = bufferedGazeSamples.Count - gazeStart;
        float duration = Mathf.Max(0f, Time.realtimeSinceStartup - voiceWindowStartedAtRealtime);

        CameraPose lastPose = bufferedPoses.Count > 0 ? bufferedPoses[bufferedPoses.Count - 1] : latestCameraPose;
        GazeSample currentVoiceGaze = GetLatestVoiceWindowAttention(gazeStart, out int currentHitCount);

        string poseText = lastPose != null
            ? "latestHead position=" + FormatVector(lastPose.position) +
              ", forward=" + FormatVector(lastPose.orientation * Vector3.forward)
            : "latestHead none";

        string gazeText = currentVoiceGaze != null
            ? "voiceWindowAttention source=" + (currentVoiceGaze.usedHeadForwardFallback ? "head_forward_fallback" : (currentVoiceGaze.available ? "eye_gaze" : "none")) +
              ", hit=true" +
              ", hitName=" + currentVoiceGaze.hitDisplayName +
              ", selectionSource=" + currentVoiceGaze.selectionSource +
              ", attentionOnly=" + currentVoiceGaze.attentionOnly +
              ", currentHeld=" + currentVoiceGaze.currentlyHeld +
              ", recentHitsForSameTarget=" + currentHitCount +
              ", hitDistance=" + currentVoiceGaze.hitDistance.ToString("0.00") +
              ", note=latest_attention_at_voice_release_not_holding_state"
            : "voiceWindowAttention none (no sustained tracked attention during speech)";

        LatestVoiceAttentionSummary = currentVoiceGaze != null
            ? "currentAttention=" + (string.IsNullOrWhiteSpace(currentVoiceGaze.hitDisplayName) ? currentVoiceGaze.hitObjectName : currentVoiceGaze.hitDisplayName) +
              ", source=" + (currentVoiceGaze.usedHeadForwardFallback ? "head_forward_fallback" : (currentVoiceGaze.available ? "eye_gaze" : "head_or_unknown")) +
              ", attentionOnly=" + currentVoiceGaze.attentionOnly +
              ", socialInteractionTarget=" + currentVoiceGaze.attentionOnly +
              ", currentHeld=" + currentVoiceGaze.currentlyHeld +
              ", hitDistance=" + currentVoiceGaze.hitDistance.ToString("0.00")
            : "currentAttention=none, reason=no_tracked_gaze_hit_in_current_voice_window";

        LatestVoiceRuntimeContextText =
            "voiceWindow duration=" + duration.ToString("0.00") + "s" +
            ", poseSamples=" + poseCount +
            ", gazeSamples=" + gazeCount + "\n" +
            poseText + "\n" +
            gazeText + "\n" +
            "attendedDuringSpeech=" + BuildVoiceWindowAttendedObjectsContext(gazeStart);

        Debug.Log("[CameraPoseSender] Voice context finalized: " + LatestVoiceAttentionSummary +
            ", gazeStart=" + gazeStart +
            ", gazeSamplesInWindow=" + gazeCount +
            ", poseSamplesInWindow=" + poseCount);
    }

    private string BuildVoiceWindowAttendedObjectsContext(int gazeStartIndex)
    {
        if (bufferedGazeSamples.Count <= gazeStartIndex)
        {
            return "none";
        }

        var hitCounts = new Dictionary<string, int>();
        var maxDistances = new Dictionary<string, float>();
        for (int i = Mathf.Max(0, gazeStartIndex); i < bufferedGazeSamples.Count; i++)
        {
            GazeSample sample = bufferedGazeSamples[i];
            if (sample == null || !sample.hitInteractionObject || sample.attentionOnly)
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(sample.hitDisplayName) ? sample.hitObjectName : sample.hitDisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!hitCounts.ContainsKey(name))
            {
                hitCounts[name] = 0;
                maxDistances[name] = sample.hitDistance;
            }

            hitCounts[name]++;
            maxDistances[name] = Mathf.Max(maxDistances[name], sample.hitDistance);
        }

        if (hitCounts.Count == 0)
        {
            return "none";
        }

        var parts = new List<string>();
        foreach (var pair in hitCounts)
        {
            float dwellSeconds = pair.Value * recordInterval;
            if (pair.Value < gazeVoiceMinimumHitSamples || dwellSeconds < gazeVoiceMinimumDwellSeconds)
            {
                continue;
            }

            parts.Add(pair.Key + " attentionHits=" + pair.Value + " distance~" + maxDistances[pair.Key].ToString("0.0") + "m source=voice_window_attention_not_held_state");
        }

        if (parts.Count == 0)
        {
            return "none";
        }

        parts.Sort();
        int count = Mathf.Min(5, parts.Count);
        return string.Join("; ", parts.GetRange(0, count));
    }

    private GazeSample GetDominantVoiceWindowAttention(int gazeStartIndex, out int dominantHitCount)
    {
        dominantHitCount = 0;
        if (bufferedGazeSamples.Count <= gazeStartIndex)
        {
            return null;
        }

        var hitCounts = new Dictionary<string, int>();
        var latestSamples = new Dictionary<string, GazeSample>();
        for (int i = Mathf.Max(0, gazeStartIndex); i < bufferedGazeSamples.Count; i++)
        {
            GazeSample sample = bufferedGazeSamples[i];
            if (sample == null || !sample.hitInteractionObject || sample.attentionOnly)
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(sample.hitDisplayName) ? sample.hitObjectName : sample.hitDisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!hitCounts.ContainsKey(name))
            {
                hitCounts[name] = 0;
            }

            hitCounts[name]++;
            latestSamples[name] = sample;
        }

        string bestName = null;
        int bestCount = 0;
        foreach (var pair in hitCounts)
        {
            if (pair.Value > bestCount)
            {
                bestName = pair.Key;
                bestCount = pair.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(bestName) ||
            bestCount < gazeVoiceMinimumHitSamples ||
            bestCount * recordInterval < gazeVoiceMinimumDwellSeconds)
        {
            dominantHitCount = bestCount;
            return null;
        }

        dominantHitCount = bestCount;
        return latestSamples[bestName];
    }

    private GazeSample GetLatestVoiceWindowAttention(int gazeStartIndex, out int hitCountForSameTarget)
    {
        hitCountForSameTarget = 0;
        if (bufferedGazeSamples.Count <= gazeStartIndex)
        {
            return null;
        }

        int startIndex = Mathf.Max(0, gazeStartIndex);
        int recentSampleCount = Mathf.Max(3, Mathf.CeilToInt(0.7f / recordInterval));
        int recentStartIndex = Mathf.Max(startIndex, bufferedGazeSamples.Count - recentSampleCount);

        GazeSample recentNonAvatarHit = null;
        string recentNonAvatarName = null;
        for (int i = bufferedGazeSamples.Count - 1; i >= recentStartIndex; i--)
        {
            GazeSample sample = bufferedGazeSamples[i];
            if (sample == null || !sample.hitInteractionObject || sample.attentionOnly)
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(sample.hitDisplayName) ? sample.hitObjectName : sample.hitDisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            recentNonAvatarHit = sample;
            recentNonAvatarName = name;
            break;
        }

        GazeSample latestHit = recentNonAvatarHit;
        string latestName = recentNonAvatarName;
        if (latestHit == null)
        {
            for (int i = bufferedGazeSamples.Count - 1; i >= startIndex; i--)
            {
                GazeSample sample = bufferedGazeSamples[i];
                if (sample == null || !sample.hitInteractionObject)
                {
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(sample.hitDisplayName) ? sample.hitObjectName : sample.hitDisplayName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                latestHit = sample;
                latestName = name;
                break;
            }
        }

        if (latestHit == null)
        {
            return null;
        }

        for (int i = startIndex; i < bufferedGazeSamples.Count; i++)
        {
            GazeSample sample = bufferedGazeSamples[i];
            if (sample == null || !sample.hitInteractionObject)
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(sample.hitDisplayName) ? sample.hitObjectName : sample.hitDisplayName;
            if (string.Equals(name, latestName, StringComparison.Ordinal))
            {
                hitCountForSameTarget++;
            }
        }

        return latestHit;
    }

    private bool IsVoiceWindowAttentionQualified(GazeSample targetSample, int gazeStartIndex)
    {
        if (targetSample == null || !targetSample.hitInteractionObject)
        {
            return false;
        }

        string targetName = string.IsNullOrWhiteSpace(targetSample.hitDisplayName)
            ? targetSample.hitObjectName
            : targetSample.hitDisplayName;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return false;
        }

        int hitCount = 0;
        for (int i = Mathf.Max(0, gazeStartIndex); i < bufferedGazeSamples.Count; i++)
        {
            GazeSample sample = bufferedGazeSamples[i];
            if (sample == null || !sample.hitInteractionObject || sample.attentionOnly)
            {
                continue;
            }

            string sampleName = string.IsNullOrWhiteSpace(sample.hitDisplayName)
                ? sample.hitObjectName
                : sample.hitDisplayName;
            if (string.Equals(sampleName, targetName, StringComparison.Ordinal))
            {
                hitCount++;
            }
        }

        return hitCount >= gazeVoiceMinimumHitSamples &&
               hitCount * recordInterval >= gazeVoiceMinimumDwellSeconds;
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
                " currentHeld=" + summary.currentlyHeld +
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
                sessionId = PlayerData.sessionId,
                avatarCondition = PlayerData.avatarCondition,
                sceneName = sceneName,
                sceneIndex = PlayerData.currentSceneIndex,
                sceneSequenceLength = PlayerData.sceneSequence != null ? PlayerData.sceneSequence.Length : 0,
                timestamp = System.DateTime.UtcNow.ToString("o"),
                persistentDataPath = Application.persistentDataPath,
                sampleOnlyWhileVoiceRecording = sampleOnlyWhileVoiceRecording,
                sampleOnVoiceRecordingEdges = sampleOnVoiceRecordingEdges,
                recordEyeGaze = recordEyeGaze,
                useHeadForwardAsGazeFallback = useHeadForwardAsGazeFallback,
                gazeConfidenceThreshold = gazeConfidenceThreshold,
                gazeRayMaxDistance = gazeRayMaxDistance,
                gazeNearRayAngleDegrees = gazeNearRayAngleDegrees,
                gazeLongAttentionThresholdSeconds = gazeLongAttentionThresholdSeconds,
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
        Transform trackingSpace = ResolveTrackingSpaceTransform();
        Vector3 position = pose.position;
        Quaternion orientation = pose.orientation;
        if (trackingSpace != null)
        {
            position = trackingSpace.TransformPoint(pose.position);
            orientation = trackingSpace.rotation * pose.orientation;
        }

        return new GazeEyeSample
        {
            isValid = valid,
            confidence = eyeState.Confidence,
            position = position,
            orientation = orientation,
            direction = orientation * Vector3.forward
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
        InteractionTracker nearTracker = FindInteractionTrackerNearRay(gazeRay, out Vector3 nearPoint, out float nearDistance);
        if (nearTracker != null && ShouldPreferNearInteractionTarget(tracker, nearTracker, hitDistance, nearDistance))
        {
            tracker = nearTracker;
            hitPoint = nearPoint;
            hitDistance = nearDistance;
        }

        if (tracker == null)
        {
            UpdateGazeDebugVisuals(gazeRay, gazeRay.origin + gazeRay.direction.normalized * gazeDebugDefaultDistance);
            UpdateContinuousGazeRun(null, null, false, sample.timestamp);
            return;
        }

        string objectKey = tracker.gameObject.GetInstanceID().ToString();
        tracker.RefreshCurrentHeldState();
        string displayName = tracker.ContextName;
        sample.hitInteractionObject = true;
        sample.hitObjectName = tracker.gameObject.name;
        sample.hitDisplayName = displayName;
        sample.selectionSource = "gaze_attention";
        sample.attentionOnly = tracker.attentionOnlyTarget;
        sample.controllerGrabbed = tracker.wasGrabbedByController;
        sample.interactionUsed = tracker.isUsed;
        sample.currentlyHeld = tracker.isCurrentlyHeld;
        sample.hitPoint = hitPoint;
        sample.hitDistance = hitDistance;
        UpdateGazeDebugVisuals(gazeRay, hitPoint);

        UpdateContinuousGazeRun(objectKey, tracker, true, sample.timestamp);
        GazeObjectAccumulator accumulator = GetGazeAccumulator(objectKey, tracker);
        accumulator.hitSampleCount++;
        accumulator.totalDwellSeconds += recordInterval;
        accumulator.lastSeenTimestamp = sample.timestamp;
        accumulator.controllerGrabbed = accumulator.controllerGrabbed || tracker.wasGrabbedByController;
        accumulator.interactionUsed = accumulator.interactionUsed || tracker.isUsed;
        accumulator.currentlyHeld = tracker.isCurrentlyHeld;
        accumulator.attentionOnly = accumulator.attentionOnly || tracker.attentionOnlyTarget;
        if (string.IsNullOrEmpty(accumulator.firstSeenTimestamp))
        {
            accumulator.firstSeenTimestamp = sample.timestamp;
        }
    }

    private void UpdateGazeDebugFromCurrentPose()
    {
        if (!showGazeDebugMarker && !showGazeDebugRay)
        {
            return;
        }

        if (showGazeDebugOnlyWhileVoiceSampling && !VoiceSamplingActive)
        {
            SetGazeDebugVisible(false);
            return;
        }

        GazeSample sample = BuildCurrentGazeSample();
        if (!TryGetGazeRay(sample, out Ray gazeRay, out bool usedHeadFallback))
        {
            SetGazeDebugVisible(false);
            return;
        }

        Vector3 targetPoint = gazeRay.origin + gazeRay.direction.normalized * gazeDebugDefaultDistance;
        InteractionTracker tracker = RaycastInteractionTracker(gazeRay, out RaycastHit hit);
        if (tracker != null)
        {
            targetPoint = hit.point;
        }
        float hitDistance = tracker != null ? hit.distance : float.PositiveInfinity;
        InteractionTracker nearTracker = FindInteractionTrackerNearRay(gazeRay, out Vector3 nearPoint, out float nearDistance);
        if (nearTracker != null && ShouldPreferNearInteractionTarget(tracker, nearTracker, hitDistance, nearDistance))
        {
            tracker = nearTracker;
            targetPoint = nearPoint;
        }

        if (!usedHeadFallback && !IsWorldPointVisibleToMainCamera(targetPoint))
        {
            if (TryGetHeadForwardDebugRay(out Ray headRay))
            {
                gazeRay = headRay;
                targetPoint = headRay.origin + headRay.direction.normalized * gazeDebugDefaultDistance;
            }
        }

        UpdateGazeDebugVisuals(gazeRay, targetPoint);
    }

    private bool TryGetHeadForwardDebugRay(out Ray headRay)
    {
        Transform head = ResolveHeadTransform();
        if (head != null)
        {
            headRay = new Ray(head.position, head.forward);
            return true;
        }

        headRay = new Ray();
        return false;
    }

    private bool IsWorldPointVisibleToMainCamera(Vector3 worldPoint)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Transform head = ResolveHeadTransform();
            return head == null || Vector3.Dot(head.forward, worldPoint - head.position) > 0f;
        }

        Vector3 viewportPoint = camera.WorldToViewportPoint(worldPoint);
        return viewportPoint.z > camera.nearClipPlane &&
               viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
               viewportPoint.y >= 0f && viewportPoint.y <= 1f;
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
        GameObject centerEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
        if (centerEyeAnchor != null && centerEyeAnchor.activeInHierarchy)
        {
            return centerEyeAnchor.transform;
        }

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

    private Transform ResolveTrackingSpaceTransform()
    {
        if (cameraRig != null)
        {
            Transform trackingSpace = cameraRig.transform.Find("TrackingSpace");
            if (trackingSpace != null)
            {
                return trackingSpace;
            }
        }

        GameObject trackingSpaceObject = GameObject.Find("OVRCameraRig/TrackingSpace");
        return trackingSpaceObject != null ? trackingSpaceObject.transform : null;
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
                if (tracker.attentionOnlyTarget)
                {
                    bestHit = hit;
                    return tracker;
                }

                bestHit = hit;
                return tracker;
            }

            tracker = TryGetOrCreateAvatarTracker(hit.collider);
            if (tracker != null && tracker.isActiveAndEnabled)
            {
                bestHit = hit;
                return tracker;
            }

            // A solid, untracked collider occludes anything behind it. Do not let the
            // gaze ray "see through" walls, furniture, or other scene geometry.
            if (!hit.collider.isTrigger && hit.distance > 0.02f)
            {
                break;
            }
        }

        bestHit = new RaycastHit();
        return null;
    }

    private bool ShouldPreferNearInteractionTarget(InteractionTracker raycastTracker, InteractionTracker nearTracker, float raycastDistance, float nearDistance)
    {
        if (nearTracker == null || nearTracker.attentionOnlyTarget)
        {
            return false;
        }

        if (raycastTracker == null)
        {
            return true;
        }

        if (raycastTracker == nearTracker)
        {
            return false;
        }

        if (raycastTracker.attentionOnlyTarget)
        {
            return false;
        }

        // If the direct ray hit a larger/background tracked surface behind a
        // smaller object, use the closer near-ray target. This prevents a cup or
        // book close to the line of sight from being mislabeled as a door/wall.
        if (nearDistance + 0.20f < raycastDistance)
        {
            return true;
        }

        return false;
    }

    private InteractionTracker TryGetOrCreateAvatarTracker(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return null;
        }

        GameObject avatarRoot = ResolveAvatarGazeRoot(hitCollider.gameObject);
        if (avatarRoot == null)
        {
            return null;
        }

        InteractionTracker tracker = avatarRoot.GetComponent<InteractionTracker>();
        if (tracker == null)
        {
            tracker = avatarRoot.AddComponent<InteractionTracker>();
            tracker.displayName = "Avatar/Social Agent";
            tracker.attentionOnlyTarget = true;
            tracker.trackTriggerCollisions = false;
            Debug.Log("[Gaze] Added InteractionTracker to avatar/person object: " + avatarRoot.name);
        }
        else if (string.IsNullOrWhiteSpace(tracker.displayName))
        {
            tracker.displayName = "Avatar/Social Agent";
        }
        tracker.attentionOnlyTarget = true;
        tracker.trackTriggerCollisions = false;

        return tracker;
    }

    private int AttachAvatarGazeTargets()
    {
        var seenObjects = new HashSet<GameObject>();
        int attachedCount = 0;

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject avatarRoot = ResolveAvatarGazeRoot(renderer.gameObject);
            if (AttachAvatarGazeTarget(avatarRoot, seenObjects))
            {
                attachedCount++;
            }
        }

        Animator[] animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
        foreach (Animator animator in animators)
        {
            if (animator == null || !animator.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject avatarRoot = ResolveAvatarGazeRoot(animator.gameObject);
            if (AttachAvatarGazeTarget(avatarRoot, seenObjects))
            {
                attachedCount++;
            }
        }

        return attachedCount;
    }

    private bool AttachAvatarGazeTarget(GameObject avatarRoot, HashSet<GameObject> seenObjects)
    {
        if (avatarRoot == null || seenObjects.Contains(avatarRoot))
        {
            return false;
        }

        seenObjects.Add(avatarRoot);
        InteractionTracker tracker = avatarRoot.GetComponent<InteractionTracker>();
        if (tracker == null)
        {
            tracker = avatarRoot.AddComponent<InteractionTracker>();
        }

        tracker.displayName = "Avatar/Social Agent";
        tracker.attentionOnlyTarget = true;
        tracker.trackTriggerCollisions = false;
        EnsureAvatarGazeCollider(avatarRoot);
        return true;
    }

    private GameObject ResolveAvatarGazeRoot(GameObject hitObject)
    {
        Transform current = hitObject != null ? hitObject.transform : null;
        while (current != null)
        {
            if (IsAvatarGazeCandidate(current.gameObject))
            {
                return current.gameObject;
            }

            current = current.parent;
        }

        return null;
    }

    private bool IsAvatarGazeCandidate(GameObject candidate)
    {
        if (candidate == null || !candidate.activeInHierarchy)
        {
            return false;
        }

        // A VrmeAtticClient identifies the actual conversational avatar. Accept
        // this explicit marker while keeping broad humanoid-name matching disabled.
        if (candidate.GetComponent<VrmeAtticClient>() != null)
        {
            return true;
        }

        if (candidate.GetComponent<CameraPoseSender>() != null)
        {
            return false;
        }

        string[] hints = ParseCommaSeparatedHints(gazeAvatarObjectNameHints);
        foreach (string hint in hints)
        {
            if (candidate.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        MonoBehaviour[] behaviours = candidate.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("FaceCameraOnYAxis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("VoiceDrivenAnimator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("AudioDrivenBlendShapeMouth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("ProceduralAvatarIdle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureAvatarGazeCollider(GameObject avatarRoot)
    {
        if (avatarRoot == null || avatarRoot.GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        Bounds bounds;
        if (!TryGetRendererBounds(avatarRoot, out bounds))
        {
            bounds = new Bounds(avatarRoot.transform.position + Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.5f));
        }

        CapsuleCollider collider = avatarRoot.AddComponent<CapsuleCollider>();
        collider.isTrigger = true;
        collider.direction = 1;
        collider.height = Mathf.Max(1.2f, bounds.size.y);
        collider.radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.35f, 0.18f, 0.45f);
        collider.center = avatarRoot.transform.InverseTransformPoint(bounds.center);
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds(root.transform.position, Vector3.zero);
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private int DisableHandTrackingObjects()
    {
        int disabledCount = 0;
        string[] hints = ParseCommaSeparatedHints(handTrackingObjectNameHints);

        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform transform in transforms)
        {
            if (transform == null || transform.gameObject == gameObject)
            {
                continue;
            }

            if (IsHandTrackingObject(transform, hints))
            {
                if (transform.gameObject.activeSelf)
                {
                    transform.gameObject.SetActive(false);
                    disabledCount++;
                }
            }
        }

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (IsHandTrackingTypeName(typeName) && behaviour.enabled)
            {
                behaviour.enabled = false;
                disabledCount++;
            }
        }

        return disabledCount;
    }

    private static bool IsHandTrackingObject(Transform transform, string[] hints)
    {
        string objectName = transform.gameObject.name;
        foreach (string hint in hints)
        {
            if (objectName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        string path = GetTransformPath(transform);
        bool isUserRigObject =
            path.IndexOf("OVR", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("XR Origin", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("CameraRig", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("Interaction", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isUserRigObject)
        {
            return false;
        }

        return string.Equals(objectName, "LeftHand", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "RightHand", StringComparison.OrdinalIgnoreCase) ||
               objectName.IndexOf("HandVisual", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("HandRay", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsHandTrackingTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        string[] typeHints =
        {
            "OVRHand",
            "OVRSkeleton",
            "OVRMesh",
            "HandRayInteractor",
            "ControllerRayInteractor",
            "RayInteractor",
            "HandVisual",
            "HandGrab",
            "FromOVRHandDataSource",
            "FromUnityXRHandDataSource"
        };

        foreach (string hint in typeHints)
        {
            if (typeName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "";
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static string[] ParseCommaSeparatedHints(string hintList)
    {
        if (string.IsNullOrWhiteSpace(hintList))
        {
            return Array.Empty<string>();
        }

        string[] rawHints = hintList.Split(',');
        var hints = new List<string>();
        foreach (string rawHint in rawHints)
        {
            string hint = rawHint.Trim();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                hints.Add(hint);
            }
        }

        return hints.ToArray();
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

            // Social/avatar targets must be directly raycast. The angular fallback is
            // intentionally reserved for small physical interaction targets.
            if (tracker.attentionOnlyTarget)
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
            gazeDebugRay.startWidth = 0.004f;
            gazeDebugRay.endWidth = 0.0015f;
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
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        material.SetInt("_ZWrite", 0);
        material.renderQueue = 5000;
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
                displayName = tracker.ContextName,
                attentionOnly = tracker.attentionOnlyTarget
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
                attentionOnly = accumulator.attentionOnly,
                controllerGrabbed = accumulator.controllerGrabbed,
                interactionUsed = accumulator.interactionUsed,
                currentlyHeld = accumulator.currentlyHeld,
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
        public bool attentionOnly;
        public bool controllerGrabbed;
        public bool interactionUsed;
        public bool currentlyHeld;
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
                sessionId = PlayerData.sessionId,
                avatarCondition = PlayerData.avatarCondition,
                sceneName = SceneManager.GetActiveScene().name,
                sceneIndex = PlayerData.currentSceneIndex,
                sceneSequenceLength = PlayerData.sceneSequence != null ? PlayerData.sceneSequence.Length : 0,
                startedAtUtc = recordingStartedAtUtc,
                endedAtUtc = System.DateTime.UtcNow.ToString("o"),
                sampleOnlyWhileVoiceRecording = sampleOnlyWhileVoiceRecording,
                sampleOnVoiceRecordingEdges = sampleOnVoiceRecordingEdges,
                recordEyeGaze = recordEyeGaze,
                recordGazeObjectAttention = recordGazeObjectAttention,
                useHeadForwardAsGazeFallback = useHeadForwardAsGazeFallback,
                recordFaceExpressions = recordFaceExpressions,
                recordHeartRatePlaceholder = recordHeartRatePlaceholder,
                gazeConfidenceThreshold = gazeConfidenceThreshold,
                gazeRayMaxDistance = gazeRayMaxDistance,
                gazeNearRayAngleDegrees = gazeNearRayAngleDegrees,
                gazeLongAttentionThresholdSeconds = gazeLongAttentionThresholdSeconds,
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
                sessionId = PlayerData.sessionId,
                avatarCondition = PlayerData.avatarCondition,
                sceneName = SceneManager.GetActiveScene().name,
                sceneIndex = PlayerData.currentSceneIndex,
                sceneSequenceLength = PlayerData.sceneSequence != null ? PlayerData.sceneSequence.Length : 0,
                startedAtUtc = recordingStartedAtUtc,
                endedAtUtc = System.DateTime.UtcNow.ToString("o"),
                sampleOnlyWhileVoiceRecording = sampleOnlyWhileVoiceRecording,
                sampleOnVoiceRecordingEdges = sampleOnVoiceRecordingEdges,
                recordEyeGaze = recordEyeGaze,
                recordGazeObjectAttention = recordGazeObjectAttention,
                useHeadForwardAsGazeFallback = useHeadForwardAsGazeFallback,
                recordFaceExpressions = recordFaceExpressions,
                recordHeartRatePlaceholder = recordHeartRatePlaceholder,
                gazeConfidenceThreshold = gazeConfidenceThreshold,
                gazeRayMaxDistance = gazeRayMaxDistance,
                gazeNearRayAngleDegrees = gazeNearRayAngleDegrees,
                gazeLongAttentionThresholdSeconds = gazeLongAttentionThresholdSeconds,
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
