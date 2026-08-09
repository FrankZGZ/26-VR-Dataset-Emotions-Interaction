using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class VrmeAtticClient : MonoBehaviour
{
    public string serverUrl = "ws://127.0.0.1:8080/";
    public KeyCode recordKey = KeyCode.V;
    public bool enableKeyboardRecordKey = true;
    public bool enableControllerRecordButton = true;
    public OVRInput.Button recordControllerButton = OVRInput.Button.One;
    public OVRInput.Controller recordController = OVRInput.Controller.RTouch;
    public int sampleRate = 16000;
    public int maxRecordSeconds = 12;
    public bool autoConnectOnStart = false;
    public float connectTimeoutSeconds = 10f;
    [Range(0.5f, 30f)] public float reconnectDelaySeconds = 2f;
    public bool showRuntimeMarker = true;
    public Color markerColor = new Color(0.1f, 0.6f, 1f, 1f);
    [TextArea(3, 8)] public string scenePrompt = "";
    public bool includeInteractionContext = true;
    public bool autoFindInteractionTrackers = true;
    public InteractionTracker[] keyInteractables = Array.Empty<InteractionTracker>();
    public bool autoAttachInteractionTrackersToSceneObjects = true;
    public bool autoAttachAvatarAttentionTracker = true;
    public bool autoDiscoverSceneObjectsForContext = false;
    public bool sendLiveContextOnlyForVoiceTurn = true;
    [Tooltip("Comma-separated object name hints used when InteractionTracker components are not present.")]
    public string sceneObjectNameHints = "BlueCube,Cube,TriangularPrism,Prism,Cylinder,HandTorch,Flashlight,Torch,Shield,Shield01,Airplane,Plane,Stone,Rock,Banana,Fruit,Elephant,Dog,Puppy,Ball,Baseball,Book,Cup,Telescope,ManScreaming,Man,Gun,Sign,Door,Handle,Bar,Key,Switch,Button,Lever,Panel,Light,Exit";
    [Tooltip("Comma-separated object/script name hints used to mark the conversational avatar as a social attention target.")]
    public string avatarObjectNameHints = "Rocketbox,ReadyPlayerMe,DigitalHuman,SocialAgent,CompanionAvatar";
    [Range(1, 40)] public int maxDiscoveredSceneObjects = 20;
    [Range(1f, 100f)] public float maxContextObjectDistance = 25f;
    [Tooltip("Surface nearby non-interactive props (renderer-only, no InteractionTracker) as grounded conversation material, distinct from interactable objects. Reduces repetitive replies without letting the avatar invent objects.")]
    public bool enableNearbyStaticSceneryContext = true;
    [Range(1, 20)] public int maxStaticSceneryObjects = 6;
    [Range(1f, 30f)] public float maxStaticSceneryDistance = 10f;
    public Transform playerTransform;
    public int maxRecentInteractionEvents = 5;
    public AudioSource playbackAudioSource;
    [Range(0.1f, 5f)] public float replyGain = 2.2f;
    public bool streamReplyAudio = true;
    [Tooltip("Avatar condition sent to the backend when PlayerData has not already been set. Use backend to let the server .env choose warm/cold.")]
    public string avatarCondition = "backend";
    [Range(0.02f, 1f)] public float streamStartBufferSeconds = 0.08f;
    [Range(5, 120)] public int streamMaxSeconds = 45;
    public bool autoIntroOnStart = true;
    [Tooltip("Legacy server-push mode. Keep this off; the reliable path sends one auto briefing request after the delay.")]
    public bool useBackendProactiveIntro = false;
    [Range(0f, 60f)] public float autoIntroDelaySeconds = 0f;
    [Tooltip("Tutorial-only delay after the participant has completed the first UI.")]
    [Range(0f, 5f)] public float tutorialIntroDelayAfterFirstUi = 0.75f;
    [Tooltip("Require sustained attention toward the avatar before it proactively starts the task briefing.")]
    public bool requireAvatarAttentionBeforeAutoIntro = true;
    [Range(0.5f, 5f)] public float autoIntroAttentionWindowSeconds = 1f;
    // 750ms follows Reddy et al. (2024)'s SPN-based intention-detection dwell
    // threshold, cited via "Anticipation Before Action: EEG-Based Implicit
    // Intent Detection for Adaptive Gaze Interaction in Mixed Reality"
    // (arXiv:2601.18750), which places it within the typical 500-1000ms window
    // for SPN elicitation.
    [Range(0.1f, 5f)] public float autoIntroRequiredAvatarAttentionSeconds = 0.75f;
    [Tooltip("If the participant has not looked at the avatar by this scene age, stop waiting silently and speak one short 'Hi, I'm here' attention-getter instead of the full briefing. The full briefing is still only spoken once the gaze gate is actually satisfied.")]
    [Range(0f, 15f)] public float autoIntroMaximumAttentionWaitSeconds = 5f;
    [Tooltip("Repeat the short 'Hi, I'm here' attention-getter at this interval for as long as the participant still has not looked at the avatar.")]
    public bool enableAutoIntroAttentionReminder = true;
    [Range(10f, 120f)] public float autoIntroAttentionReminderDelaySeconds = 30f;
    private const float TutorialInitialAttentionReminderSeconds = 3f;
    [Range(5f, 120f)] public float textPromptReplyTimeoutSeconds = 60f;
    [TextArea(3, 8)] public string autoIntroPrompt =
        "Greet the participant briefly in one short sentence, in whatever style fits the selected avatar condition, then ask them to describe in their own words what they notice around them. Keep it to one short open question. Do not mention any task, objective, goal, or specific interactive object.";
    public bool enableTaskHighlights = true;
    [Tooltip("Send scene context with the initial config so backend proactive guidance can be context-aware without showing highlights early.")]
    public bool sendSceneContextWithConfig = true;
    public Color taskObjectHighlightColor = new Color(1f, 0.82f, 0.1f, 1f);
    public Color taskTargetHighlightColor = new Color(0.1f, 0.95f, 1f, 1f);
    [Range(1f, 10f)] public float taskObjectOutlineWidth = 6f;

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly ConcurrentQueue<PendingAudioTurn> pendingAudioTurns = new ConcurrentQueue<PendingAudioTurn>();
    private ClientWebSocket websocket;
    private CancellationTokenSource cancellation;
    private AudioSource audioSource;
    private StreamingPcmPlayer streamingPlayer;
    private AudioClip recordingClip;
    private string activeMicrophoneDevice = "";
    private bool isRecording;
    private DateTime voiceTurnStartedAtUtc = DateTime.MinValue;
    private bool isSending;
    private bool isReceivingBackendProactiveIntro;
    private bool recordKeyWasDown;
    private bool autoIntroSent;
    // The auto-briefing's LLM+TTS round trip is fetched as soon as it's known
    // to be needed, in parallel with the gaze-gate wait, so its latency is
    // hidden behind the time the participant naturally spends settling into
    // the scene. Audible playback is held in this buffer until the gaze gate
    // actually resolves, so the avatar still never speaks before the
    // participant is looking at it.
    private bool autoIntroPlaybackHeld;
    private readonly List<Action> heldAutoIntroPlaybackActions = new List<Action>();
    private bool taskHighlightsActivated;
    private bool guidedTaskActive;
    private bool guidedTaskCompleted;
    private bool guidedTaskProgressSent;
    private DateTime guidedTaskActivatedAtUtc = DateTime.MinValue;
    private CancellationTokenSource lifetimeCancellation;
    private float sceneStartedAtRealtime;
    private SceneTaskHighlightSpec activeGuidedTaskSpec;
    private readonly List<GameObject> activeGuidedTaskObjects = new List<GameObject>();
    private readonly List<GameObject> activeGuidedTaskTargets = new List<GameObject>();
    private readonly List<GameObject> activeGuidedTaskMarkers = new List<GameObject>();
    private GameObject tutorialExitHighlightMarker;
    private TutorialControlStage tutorialControlStage = TutorialControlStage.Inactive;
    private string tutorialFirstObjectKey = "";
    private string tutorialVoiceHeldAtStartKey = "";
    private DateTime tutorialFirstInteractionArmedAtUtc = DateTime.MinValue;
    private Vector3 tutorialFirstInteractionStartPosition;
    private static int persistentSocketSceneHandle = int.MinValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPersistentConnectionForPlaySession()
    {
        PersistentWebSocket.Close();
        persistentSocketSceneHandle = int.MinValue;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorSocketCleanup()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ClosePersistentSocketBeforeAssemblyReload;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ClosePersistentSocketBeforeAssemblyReload;
        UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
    }

    private static void ClosePersistentSocketBeforeAssemblyReload()
    {
        PersistentWebSocket.Close();
    }

    private static void OnEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            PersistentWebSocket.Close();
        }
    }
#endif

    private void Start()
    {
        sceneStartedAtRealtime = Time.realtimeSinceStartup;
        lifetimeCancellation = new CancellationTokenSource();
        ResetPersistentConnectionForScene();
        activeMicrophoneDevice = ResolvePreferredMicrophoneDevice();
        ResetMicrophoneCapture("scene start");
        AvatarConditionCounterbalance.ApplyForActiveScene();
        BackendHeartRateClient.EnsureRunning(serverUrl);
        if (requireAvatarAttentionBeforeAutoIntro && useBackendProactiveIntro)
        {
            Debug.LogWarning("[VRME] Backend-push proactive intro was disabled because the avatar-attention gate must run in Unity before any briefing request.");
            useBackendProactiveIntro = false;
        }
        audioSource = playbackAudioSource != null ? playbackAudioSource : GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        streamingPlayer = new StreamingPcmPlayer();
        InteractionTracker.ClearRecentEvents();
        if (string.IsNullOrWhiteSpace(PlayerData.avatarCondition) ||
            string.Equals(PlayerData.avatarCondition, "unset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(PlayerData.avatarCondition, "backend", StringComparison.OrdinalIgnoreCase))
        {
            PlayerData.avatarCondition = NormalizeAvatarConditionForBackend(avatarCondition);
        }

        Debug.Log("[VRME] Client started. recordKey=" + (enableKeyboardRecordKey ? recordKey.ToString() : "disabled") +
            ", controllerRecordButton=" + (enableControllerRecordButton ? recordController + "/" + recordControllerButton : "disabled") +
            ", microphones=" + Microphone.devices.Length +
            ", micDeviceNames=[" + string.Join(", ", Microphone.devices) + "]" +
            ", selectedMicrophone=" + (string.IsNullOrWhiteSpace(activeMicrophoneDevice) ? "none" : activeMicrophoneDevice) +
            ", sessionId=" + PlayerData.sessionId +
            ", avatarCondition=" + PlayerData.avatarCondition);

        if (autoAttachInteractionTrackersToSceneObjects)
        {
            int attachedCount = AttachTrackersToSceneObjects();
            Debug.Log("[VRME] Auto-attached InteractionTracker to " + attachedCount + " scene objects.");
        }

        NormalizeLakeInteractionTrackers();
        AttachGunmanPresenceTracker();
        AttachExitDoorStateTracker();

        if (autoAttachAvatarAttentionTracker)
        {
            int avatarTrackerCount = AttachAvatarAttentionTrackers();
            Debug.Log("[VRME] Auto-attached avatar attention tracker to " + avatarTrackerCount + " avatar objects.");
        }

        if (showRuntimeMarker)
        {
            CreateRuntimeMarker();
        }

        // The socket is shared across scene clients. Keep a lightweight
        // connection loop alive even when the backend starts after Unity.
        _ = MaintainPersistentConnectionAsync(lifetimeCancellation.Token);

        if (autoConnectOnStart && !autoIntroOnStart)
        {
            _ = ConnectAndSyncBackendConditionAsync();
        }

        if (autoIntroOnStart)
        {
            _ = RunAutoIntroAsync();
        }

        // Task highlights are intentionally activated by the avatar audio
        // stream start event, so objects and target markers never lead speech.
    }

    private async Task ConnectAndSyncBackendConditionAsync()
    {
        try
        {
            await ConnectAsync();
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                return;
            }

            Debug.Log("[VRME] Startup backend sync connected; sending scene config now. backendProactive=" + useBackendProactiveIntro);
            await SendConfigAsync();
            Debug.Log("[VRME] Startup scene config send completed. backendProactive=" + useBackendProactiveIntro);

            if (useBackendProactiveIntro)
            {
                isReceivingBackendProactiveIntro = true;
                try
                {
                    Task receiveTask = ReceiveReplyAsync();
                    Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(5f, autoIntroDelaySeconds + textPromptReplyTimeoutSeconds)));
                    Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                    if (completedTask == receiveTask)
                    {
                        await receiveTask;
                    }
                    else
                    {
                        Debug.LogWarning("[VRME] Backend proactive guide receive timed out.");
                    }
                }
                finally
                {
                    isReceivingBackendProactiveIntro = false;
                }
            }
            else
            {
                Task receiveTask = ReceiveReplyAsync();
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(5f, textPromptReplyTimeoutSeconds)));
                Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                if (completedTask == receiveTask)
                {
                    await receiveTask;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Could not synchronize backend condition at startup: " + ex.Message);
            isReceivingBackendProactiveIntro = false;
        }
    }

    private async Task MaintainPersistentConnectionAsync(CancellationToken lifetimeToken)
    {
        bool waitingForBackendLogged = false;
        while (!lifetimeToken.IsCancellationRequested)
        {
            ClientWebSocket sharedSocket = PersistentWebSocket.Socket;
            if (sharedSocket == null || sharedSocket.State != WebSocketState.Open)
            {
                if (!waitingForBackendLogged)
                {
                    Debug.Log("[VRME] Persistent connection waiting for backend at " + serverUrl + ".");
                    waitingForBackendLogged = true;
                }

                await ConnectAsync();
                sharedSocket = PersistentWebSocket.Socket;
                if (sharedSocket != null && sharedSocket.State == WebSocketState.Open)
                {
                    websocket = sharedSocket;
                    cancellation = PersistentWebSocket.Cancellation;
                    waitingForBackendLogged = false;
                    Debug.Log("[VRME] Persistent connection is ready for scene=" + SceneManager.GetActiveScene().name);
                }
            }
            else
            {
                websocket = sharedSocket;
                cancellation = PersistentWebSocket.Cancellation;
                waitingForBackendLogged = false;
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Mathf.Max(0.5f, reconnectDelaySeconds)),
                    lifetimeToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        bool recordInputIsDown = IsRecordInputDown();
        if (recordInputIsDown && !recordKeyWasDown)
        {
            StartRecording();
        }

        if (!recordInputIsDown && recordKeyWasDown)
        {
            StopRecordingAndSend();
        }

        recordKeyWasDown = recordInputIsDown;
        streamingPlayer?.Update();
        CheckGuidedTaskProgress();
        CheckTutorialControlledInteraction();
        CheckGuidedTaskCompletion();
    }

    private void CheckGuidedTaskProgress()
    {
        if (!guidedTaskActive || guidedTaskCompleted || guidedTaskProgressSent || activeGuidedTaskSpec == null)
        {
            return;
        }

        // Tutorial exploration is intentionally discovery-led. Do not interrupt
        // the participant with the old automatic "now throw the blue cube" line
        // when they first pick up one of the shapes.
        if (IsTutorialScene())
        {
            return;
        }

        foreach (GameObject taskObject in activeGuidedTaskObjects)
        {
            if (taskObject == null)
            {
                continue;
            }

            InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
            bool hasReachedGrabStage = tracker != null &&
                (IsTutorialScene() ? tracker.wasGrabbedByController : tracker.isUsed);
            if (hasReachedGrabStage)
            {
                guidedTaskProgressSent = true;
                string sceneName = SceneManager.GetActiveScene().name;
                Debug.Log("[VRME] Guided task object first used; sending stage-progress trigger. scene=" + sceneName + ", object=" + tracker.ContextName);
                _ = SendStageProgressAsync(sceneName, tracker.ContextName);
                return;
            }
        }
    }

    private void OnGUI()
    {
        if (!enableKeyboardRecordKey)
        {
            return;
        }

        Event currentEvent = Event.current;
        if (currentEvent == null || currentEvent.keyCode != recordKey)
        {
            return;
        }

        if (currentEvent.type == EventType.KeyDown && !recordKeyWasDown)
        {
            recordKeyWasDown = true;
            StartRecording();
        }
        else if (currentEvent.type == EventType.KeyUp && recordKeyWasDown)
        {
            recordKeyWasDown = false;
            StopRecordingAndSend();
        }
    }

    private bool IsRecordInputDown()
    {
        if (enableControllerRecordButton && OVRInput.Get(recordControllerButton, recordController))
        {
            return true;
        }

        return enableKeyboardRecordKey && Input.GetKey(recordKey);
    }

    private void CreateRuntimeMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "VRME Visible Marker";
        marker.transform.SetParent(transform, false);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localScale = Vector3.one * 0.18f;

        var collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        var renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = markerColor;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = markerColor;
        Gizmos.DrawWireSphere(transform.position, 0.22f);
    }

    private async Task ConnectAsync()
    {
        try
        {
            await PersistentWebSocket.ConnectAsync(serverUrl, connectTimeoutSeconds);
            websocket = PersistentWebSocket.Socket;
            cancellation = PersistentWebSocket.Cancellation;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[VRME] Connect timed out after " + connectTimeoutSeconds + " seconds.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Could not connect. Start run_server.ps1 first. " + ex.Message);
        }
    }

    private void ResetWebSocket()
    {
        PersistentWebSocket.Reset(websocket);
        websocket = null;
        cancellation = null;
    }

    private async Task SendConfigAsync(bool includeSceneRuntimeContext = true)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            return;
        }

        string sceneContext = includeSceneRuntimeContext && sendSceneContextWithConfig
            ? BuildSceneContext()
            : "";
        string effectiveScenePrompt = GetEffectiveScenePrompt();
        string json =
            "{\"type\":\"config\",\"streamReplyAudio\":" + (streamReplyAudio ? "true" : "false") +
            ",\"mode\":\"ai\"" +
            ",\"backendProactiveGuide\":false" +
            ",\"proactiveGuideDelaySeconds\":" + Mathf.Max(0f, autoIntroDelaySeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"participantId\":\"" + EscapeJson(PlayerData.participantId) +
            "\",\"loginId\":\"" + EscapeJson(PlayerData.loginId) +
            "\",\"sessionId\":\"" + EscapeJson(PlayerData.sessionId) +
            "\",\"avatarCondition\":\"" + EscapeJson(PlayerData.avatarCondition) +
            "\",\"sceneName\":\"" + EscapeJson(SceneManager.GetActiveScene().name) +
            "\",\"sceneIndex\":" + PlayerData.currentSceneIndex +
            ",\"scenePrompt\":\"" + EscapeJson(effectiveScenePrompt) +
            "\",\"sceneContext\":\"" + EscapeJson(sceneContext) + "\"}";
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);
        await websocket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            true,
            cancellation.Token);
        Debug.Log("[VRME] Sent scene config. contextChars=" + sceneContext.Length + " preview=" + PreviewForLog(sceneContext, 1400));
    }

    private async Task SendTurnContextAsync()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            return;
        }

        string turnContext = BuildTurnContextString();
        string json = BuildTurnContextJson("turn_context", turnContext);
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);
        await websocket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            true,
            cancellation.Token);
        LogTurnContextSent(turnContext);
    }

    private string BuildTurnContextString(
        string voiceAttentionSummary = null,
        string voiceRuntimeContext = null,
        bool hasTutorialVoiceSnapshot = false,
        string tutorialHeldAtVoiceStartKey = "",
        string tutorialHeldAtVoiceEndKey = "")
    {
        using (var writer = new StringWriter())
        {
            bool hasUserTriggerContext = !string.IsNullOrWhiteSpace(voiceAttentionSummary) &&
                !string.IsNullOrWhiteSpace(voiceRuntimeContext);
            string currentHeldObjects = hasUserTriggerContext ? BuildCurrentHeldObjectsSummary() : "none";
            writer.WriteLine("[UNITY_CONTEXT_SUMMARY]");
            writer.WriteLine(hasUserTriggerContext
                ? voiceAttentionSummary
                : "currentAttention=none, reason=no_current_user_trigger_context");
            writer.WriteLine("currentHeldObjects=" + currentHeldObjects);
            writer.WriteLine(hasUserTriggerContext
                ? "authority=This context contains only observations sampled while the current User Trigger was held. It must not be reused for another turn."
                : "authority=No User Trigger perception window belongs to this automatic text request. Do not infer what the participant is looking at or holding from an earlier turn.");
            writer.WriteLine("[/UNITY_CONTEXT_SUMMARY]");

            writer.WriteLine("[IMMEDIATE_UNITY_STATE]");
            writer.WriteLine(hasUserTriggerContext
                ? BuildImmediateUnityStateContext()
                : "none; automatic request has no User Trigger state snapshot");
            writer.WriteLine("[/IMMEDIATE_UNITY_STATE]");

            writer.WriteLine("[LIVE_USER_OBSERVATIONS]");
            writer.WriteLine(hasUserTriggerContext
                ? voiceRuntimeContext
                : "none; automatic request has no User Trigger observation window");
            writer.WriteLine("[/LIVE_USER_OBSERVATIONS]");

            if (includeInteractionContext && hasUserTriggerContext)
            {
                writer.WriteLine(BuildCurrentTurnInteractionContext());
            }
            else
            {
                Transform player = ResolvePlayerTransform();
                writer.WriteLine(BuildGuidedTaskContext(
                    player,
                    player != null ? player.position : Vector3.zero));
            }

            if (IsTutorialScene())
            {
                bool heldThroughout = hasTutorialVoiceSnapshot &&
                    !string.IsNullOrWhiteSpace(tutorialHeldAtVoiceStartKey) &&
                    string.Equals(tutorialHeldAtVoiceStartKey, tutorialHeldAtVoiceEndKey, StringComparison.OrdinalIgnoreCase);
                writer.WriteLine("[TUTORIAL_CONTROL_STATE]");
                writer.WriteLine("stage=" + tutorialControlStage);
                writer.WriteLine("firstObjectKey=" + (string.IsNullOrWhiteSpace(tutorialFirstObjectKey) ? "none" : tutorialFirstObjectKey));
                writer.WriteLine("voiceSnapshot=" + (hasTutorialVoiceSnapshot ? "available" : "unavailable"));
                writer.WriteLine("heldThroughoutVoiceTurn=" + (hasTutorialVoiceSnapshot ? heldThroughout.ToString() : "unknown"));
                writer.WriteLine("heldObjectKey=" + (heldThroughout ? tutorialHeldAtVoiceEndKey : "none"));
                writer.WriteLine("instruction_for_avatar=Follow the fixed tutorial stage. Color and shape descriptions are never graded for correctness; only a meaningful voice response while the required object remains held advances a held-object stage.");
                writer.WriteLine("[/TUTORIAL_CONTROL_STATE]");
            }

            return writer.ToString().TrimEnd();
        }
    }

    private string BuildTurnContextJson(string messageType, string turnContext)
    {
        return
            "{\"type\":\"" + EscapeJson(messageType) + "\"" +
            ",\"participantId\":\"" + EscapeJson(PlayerData.participantId) +
            "\",\"loginId\":\"" + EscapeJson(PlayerData.loginId) +
            "\",\"sessionId\":\"" + EscapeJson(PlayerData.sessionId) +
            "\",\"avatarCondition\":\"" + EscapeJson(PlayerData.avatarCondition) +
            "\",\"sceneName\":\"" + EscapeJson(SceneManager.GetActiveScene().name) +
            "\",\"sceneIndex\":" + PlayerData.currentSceneIndex +
            ",\"scenePrompt\":\"" + EscapeJson(GetEffectiveScenePrompt()) +
            "\",\"sceneContext\":\"" + EscapeJson(turnContext) + "\"}";
    }

    private string GetEffectiveScenePrompt()
    {
        if (!string.IsNullOrWhiteSpace(scenePrompt))
        {
            return scenePrompt.Trim();
        }

        return GetSceneDescription(SceneManager.GetActiveScene().name);
    }

    private static string GetSceneDescription(string sceneName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(sceneName) ? "" : sceneName.Trim().ToLowerInvariant();
        switch (normalizedName)
        {
            case "tutorial_interaction":
                return "A VR interaction tutorial with two blue cubes, a triangular prism, and a cylinder placed together as equal exploration choices. All four objects share the same simple physics interaction: they can be picked up with the grip button, moved, released, and thrown. The participant should be warmly invited to discover this interaction with any shape without singling out a preferred object or immediately commanding a throw. They can speak to the nearby avatar by holding the right-controller A button, then finish by reaching the marked Exit and pressing B on the right controller.";
            case "lake":
                return "A jetty in front of a stone house by a calm lake, with hills covered by trees and grass. At the end of the jetty there are two stones and two paper planes. Stones can be grabbed and thrown into the lake, producing splash sounds and visible ripples on the water surface. Paper planes can also be picked up and thrown, with a visible trajectory during flight. This is background knowledge only, never volunteered. Naming an object the participant already named or is looking at is fine, but the interaction/function described here (what it can be used for or how) may only be revealed if the participant explicitly asks what they can do, what something is for, or otherwise clearly asks for help — merely naming or describing an object is not enough to unlock its function.";
            case "attic":
                return "A furnished attic. After a short delay, a man breaks in shouting and aiming a pistol at the participant. A metal riot shield leaning against the wall has a small bulletproof glass window; it can be grabbed for protection, used to block the view of the man, and the man can still be observed safely through the window. This is background knowledge only, never volunteered. Naming an object the participant already named or is looking at is fine, but the interaction/function described here (what it can be used for or how) may only be revealed if the participant explicitly asks what they can do, what something is for, or otherwise clearly asks for help — merely naming or describing an object is not enough to unlock its function.";
            case "puppies":
                return "A spacious furnished room with three puppies moving around and a tennis ball on a table. Petting a puppy makes it turn toward the participant and sit down. The tennis ball can be picked up and thrown; the puppies will chase it and bring it back. If nobody interacts with them for over 15 seconds, the puppies settle down and sit still. This is background knowledge only, never volunteered. Naming an object the participant already named or is looking at is fine, but the interaction/function described here (what it can be used for or how) may only be revealed if the participant explicitly asks what they can do, what something is for, or otherwise clearly asks for help — merely naming or describing an object is not enough to unlock its function.";
            case "solitaryconfinement":
                return "A confined, gloomy cell with a flashing light, a toilet seat, and a single bed. A book and a metal cup sit on a table. The iron door can be knocked on, producing loud knocking sounds. The book and cup can be picked up and thrown at the door. This is background knowledge only, never volunteered. Naming an object the participant already named or is looking at is fine, but the interaction/function described here (what it can be used for or how) may only be revealed if the participant explicitly asks what they can do, what something is for, or otherwise clearly asks for help — merely naming or describing an object is not enough to unlock its function.";
            case "tunnel":
                return "A long, dimly lit tunnel with pedestrians occasionally passing by. A flashlight lies on the tunnel floor; once picked up it turns on and can be used to illuminate different areas of the tunnel. This is background knowledge only, never volunteered. Naming an object the participant already named or is looking at is fine, but the interaction/function described here (what it can be used for or how) may only be revealed if the participant explicitly asks what they can do, what something is for, or otherwise clearly asks for help — merely naming or describing an object is not enough to unlock its function.";
            case "elephant":
                return "A green grassland with distant hills and a cloudy sky, where a herd of elephants slowly approaches. A banana floats above the grass; it can be picked up and thrown toward the elephants, who pick it up with their trunk and eat it. Touching an elephant makes it step back, raise its trunk, and vocalize. This is background knowledge only, never volunteered. Naming an object the participant already named or is looking at is fine, but the interaction/function described here (what it can be used for or how) may only be revealed if the participant explicitly asks what they can do, what something is for, or otherwise clearly asks for help — merely naming or describing an object is not enough to unlock its function.";
            case "real":
                return "A mixed-reality transition scene used after the immersive VR scenes.";
            case "endscene":
                return "The experiment completion scene; no further guided object interaction is required.";
            default:
                return "VR scene named " + (string.IsNullOrWhiteSpace(sceneName) ? "unknown" : sceneName.Trim()) + ".";
        }
    }

    private void LogTurnContextSent(string turnContext, bool isolatedUserTriggerContext = false)
    {
        if (CameraPoseSender.LatestRuntimeContextText.StartsWith("No CameraPoseSender", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[VRME] CameraPoseSender has not produced runtime context yet; sent immediate Unity head state fallback.");
        }

        Debug.Log("[VRME] Unity context sent. isolatedUserTriggerContext=" + isolatedUserTriggerContext +
            ", currentHeldObjects=" + BuildCurrentHeldObjectsSummary());
        Debug.Log("[VRME] Sent voice-turn context. chars=" + turnContext.Length + " preview=" + PreviewForLog(turnContext, 1800));
    }

    private string BuildCurrentHeldObjectsSummary()
    {
        InteractionTracker[] trackers = FindObjectsByType<InteractionTracker>(FindObjectsSortMode.None);
        var heldObjects = new List<string>();
        foreach (InteractionTracker tracker in trackers)
        {
            if (tracker == null || !tracker.isActiveAndEnabled || tracker.attentionOnlyTarget || IsSystemInteractionTracker(tracker))
            {
                continue;
            }

            tracker.RefreshCurrentHeldState();
            if (tracker.isCurrentlyHeld)
            {
                heldObjects.Add(tracker.ContextName);
            }
        }

        heldObjects.Sort(StringComparer.OrdinalIgnoreCase);
        return heldObjects.Count == 0 ? "none" : string.Join(", ", heldObjects);
    }

    private static bool IsSystemInteractionTracker(InteractionTracker tracker)
    {
        if (tracker == null)
        {
            return true;
        }

        // Semantic object trackers are attached to the actual Oculus HandGrab
        // node so their held state comes from real selection events. An explicit
        // displayName (for example "blue cube") makes that tracker conversational
        // even though its implementation GameObject has a system-looking name.
        if (!string.IsNullOrWhiteSpace(tracker.displayName))
        {
            return ContainsSystemTrackerIdentity(tracker.displayName.ToLowerInvariant());
        }

        return ContainsSystemTrackerIdentity(tracker.gameObject.name.ToLowerInvariant());
    }

    private static bool ContainsSystemTrackerIdentity(string identity)
    {
        return identity.Contains("[buildingblock] handgrab") ||
               identity.Contains("controllergrablocation") ||
               identity.Contains("controller interactor") ||
               identity.Contains("hand grab interactor") ||
               identity.Contains("ovrcamerarig") ||
               identity.Contains("tracking space");
    }

    private string BuildImmediateUnityStateContext()
    {
        using (var writer = new StringWriter())
        {
            writer.WriteLine("sampleUtc=" + DateTime.UtcNow.ToString("o"));
            writer.WriteLine("sceneName=" + SceneManager.GetActiveScene().name);
            writer.WriteLine("avatarCondition=" + PlayerData.avatarCondition);

            Transform player = ResolvePlayerTransform();
            if (player == null)
            {
                writer.WriteLine("headSource=none");
                writer.WriteLine("headPoseAvailable=False");
                return writer.ToString().TrimEnd();
            }

            writer.WriteLine("headSource=" + player.name);
            writer.WriteLine("headPoseAvailable=True");
            writer.WriteLine("head position=" + FormatVector(player.position));
            writer.WriteLine("head forward=" + FormatVector(player.forward));
            writer.WriteLine("head up=" + FormatVector(player.up));
            return writer.ToString().TrimEnd();
        }
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private void StartRecording()
    {
        if (isRecording)
        {
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[VRME] No microphone device found.");
            return;
        }

        string preferredDevice = ResolvePreferredMicrophoneDevice();
        if (string.IsNullOrWhiteSpace(activeMicrophoneDevice) ||
            !string.Equals(activeMicrophoneDevice, preferredDevice, StringComparison.Ordinal))
        {
            activeMicrophoneDevice = preferredDevice;
            ResetMicrophoneCapture("device list changed");
        }

        streamingPlayer?.Reset();
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        if (recordingClip != null)
        {
            Destroy(recordingClip);
            recordingClip = null;
        }

        // Pin the Oculus input by name. Passing null asks Windows for its
        // current default device, which can become stale during a scene/audio
        // device transition while its recording cursor still advances.
        if (Microphone.IsRecording(activeMicrophoneDevice))
        {
            Microphone.End(activeMicrophoneDevice);
        }
        recordingClip = Microphone.Start(activeMicrophoneDevice, false, maxRecordSeconds, sampleRate);
        if (recordingClip == null)
        {
            Debug.LogWarning("[VRME] Microphone.Start failed for device=" + activeMicrophoneDevice + ".");
            return;
        }

        isRecording = true;
        voiceTurnStartedAtUtc = DateTime.UtcNow;
        tutorialVoiceHeldAtStartKey = IsTutorialScene() ? GetSingleHeldTutorialObjectKey() : "";
        CameraPoseSender.BeginVoiceSampling();
        Debug.Log("[VRME] Recording started on " + activeMicrophoneDevice + ". Release " + GetRecordInputLabel() + " to send." + (isSending ? " Current reply is still finishing; this turn will queue." : ""));
    }

    private string GetRecordInputLabel()
    {
        if (enableControllerRecordButton)
        {
            return recordController + "/" + recordControllerButton;
        }

        return recordKey.ToString();
    }

    private async void StopRecordingAndSend()
    {
        if (!isRecording)
        {
            return;
        }

        int samplePosition = Microphone.GetPosition(activeMicrophoneDevice);
        Microphone.End(activeMicrophoneDevice);
        isRecording = false;
        CameraPoseSender.EndVoiceSampling();

        CameraPoseSender.ConsumeLatestVoiceContext(
            out string voiceAttentionSummary,
            out string voiceRuntimeContext);
        string tutorialHeldAtVoiceEndKey = IsTutorialScene() ? GetSingleHeldTutorialObjectKey() : "";
        string capturedTurnContext = BuildTurnContextString(
            voiceAttentionSummary,
            voiceRuntimeContext,
            hasTutorialVoiceSnapshot: IsTutorialScene(),
            tutorialHeldAtVoiceStartKey: tutorialVoiceHeldAtStartKey,
            tutorialHeldAtVoiceEndKey: tutorialHeldAtVoiceEndKey);
        tutorialVoiceHeldAtStartKey = "";
        InteractionTracker.ClearRecentEvents();
        Debug.Log("[VRME] Consumed and cleared the current User Trigger perception window.");

        if (recordingClip == null || samplePosition <= 0)
        {
            Debug.LogWarning("[VRME] Empty recording cursor/clip. Resetting microphone device=" +
                activeMicrophoneDevice + ".");
            ResetMicrophoneCapture("empty recording recovery");
            if (recordingClip != null)
            {
                Destroy(recordingClip);
                recordingClip = null;
            }
            _ = SendMicrophoneRetryPromptAsync();
            return;
        }

        float[] samples = new float[samplePosition * recordingClip.channels];
        recordingClip.GetData(samples, 0);
        if (!HasEncodablePcmSamples(samples))
        {
            Debug.LogWarning("[VRME] Silent/all-zero PCM recording discarded before sending. Resetting microphone device=" +
                activeMicrophoneDevice + "; press A and speak again.");
            ResetMicrophoneCapture("silent capture recovery");
            Destroy(recordingClip);
            recordingClip = null;
            _ = SendMicrophoneRetryPromptAsync();
            return;
        }

        byte[] wavBytes = EncodeWav(samples, recordingClip.channels, sampleRate);
        Destroy(recordingClip);
        recordingClip = null;
        await SendAudioAsync(wavBytes, capturedTurnContext);
    }

    private static bool HasEncodablePcmSamples(float[] samples)
    {
        if (samples == null || samples.Length == 0)
        {
            return false;
        }

        // Match the 16-bit conversion performed by EncodeWav. The previous
        // float epsilon admitted sub-PCM noise that became literal zero after
        // encoding, producing a several-second WAV containing no audio.
        const int requiredEncodableSamples = 8;
        int encodableSamples = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            short pcmValue = (short)Mathf.Clamp(
                samples[i] * short.MaxValue,
                short.MinValue,
                short.MaxValue);
            if (pcmValue != 0 && ++encodableSamples >= requiredEncodableSamples)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolvePreferredMicrophoneDevice()
    {
        string[] devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
        {
            return "";
        }

        foreach (string device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device) &&
                (device.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 device.IndexOf("Headset Microphone", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return device;
            }
        }

        return devices[0];
    }

    private void ResetMicrophoneCapture(string reason)
    {
        foreach (string device in Microphone.devices)
        {
            if (!string.IsNullOrWhiteSpace(device) && Microphone.IsRecording(device))
            {
                Microphone.End(device);
            }
        }

        // Stop a capture opened through the legacy null/default-device path.
        if (Microphone.IsRecording(null))
        {
            Microphone.End(null);
        }

        isRecording = false;
        Debug.Log("[VRME] Microphone capture reset. reason=" + reason +
            ", selectedDevice=" + (string.IsNullOrWhiteSpace(activeMicrophoneDevice) ? "none" : activeMicrophoneDevice));
    }

    private static void ResetPersistentConnectionForScene()
    {
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (persistentSocketSceneHandle == sceneHandle)
        {
            return;
        }

        PersistentWebSocket.Close();
        persistentSocketSceneHandle = sceneHandle;
        Debug.Log("[VRME] Cleared the previous scene WebSocket before opening a fresh connection for " +
            SceneManager.GetActiveScene().name + ".");
    }

    private async Task SendMicrophoneRetryPromptAsync()
    {
        const string prompt =
            "[SYSTEM_MICROPHONE_RETRY]\n" +
            "Say exactly this one short sentence and nothing else: " +
            "I didn't catch that. Please hold A and try again.\n" +
            "[/SYSTEM_MICROPHONE_RETRY]";

        // A participant can begin talking while the tail of the previous
        // reply is still finishing. Wait briefly instead of dropping the
        // recovery message because that send still owns the socket.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (lifetimeCancellation == null || lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!isSending)
            {
                bool sent = await SendTextPromptAsync(prompt, "microphone_retry");
                if (sent)
                {
                    Debug.Log("[VRME] Spoken microphone retry guidance delivered.");
                    return;
                }
            }

            await Task.Delay(250);
        }

        Debug.LogWarning("[VRME] Could not deliver spoken microphone retry guidance.");
    }

    private async Task RunAutoIntroAsync(float fallbackGraceSeconds = 0f)
    {
        bool isTutorial = string.Equals(
            SceneManager.GetActiveScene().name,
            "Tutorial_Interaction",
            StringComparison.OrdinalIgnoreCase);
        if (isTutorial)
        {
            await WaitForTutorialFirstUiAsync();
        }

        float tutorialDelay = isTutorial ? tutorialIntroDelayAfterFirstUi : 0f;
        if (tutorialDelay > 0f)
        {
            await Task.Delay(TimeSpan.FromSeconds(tutorialDelay));
        }

        // Tutorial starts shortly after its participant-ID UI closes. Formal
        // scenes have no minimum setup delay either now; the avatar-attention
        // gaze gate below is what actually paces the opening line.
        float minimumSceneAge = isTutorial
            ? 0f
            : Mathf.Max(0f, autoIntroDelaySeconds + Mathf.Max(0f, fallbackGraceSeconds));
        float remainingSceneDelay = Mathf.Max(0f, minimumSceneAge - (Time.realtimeSinceStartup - sceneStartedAtRealtime));
        Debug.Log("[VRME] Auto briefing fallback armed. minimumSceneAge=" + minimumSceneAge +
            ", remainingDelay=" + remainingSceneDelay +
            ", gazeGate=" + requireAvatarAttentionBeforeAutoIntro +
            ", gazeWindowSeconds=" + autoIntroAttentionWindowSeconds +
            ", requiredAvatarDwellSeconds=" + autoIntroRequiredAvatarAttentionSeconds +
            ", backendProactive=" + useBackendProactiveIntro +
            ", scene=" + SceneManager.GetActiveScene().name);
        if (remainingSceneDelay > 0f)
        {
            await Task.Delay(TimeSpan.FromSeconds(remainingSceneDelay));
        }

        string introPrompt = BuildAutoTaskBriefingPrompt();
        if (autoIntroSent || !isActiveAndEnabled || string.IsNullOrWhiteSpace(introPrompt))
        {
            Debug.Log("[VRME] Auto briefing fallback skipped. sent=" + autoIntroSent + ", active=" + isActiveAndEnabled + ", hasPrompt=" + !string.IsNullOrWhiteSpace(introPrompt));
            return;
        }

        // The opening line's content never depends on live gaze/interaction
        // data, so its LLM+TTS round trip can be fetched in the background
        // while the participant is still settling into the scene. Only the
        // audible playback is held back for the gaze gate below, so this only
        // hides latency; it never makes the avatar speak before the
        // participant is actually looking at it.
        bool gazeGateActive = requireAvatarAttentionBeforeAutoIntro;
        if (gazeGateActive)
        {
            autoIntroPlaybackHeld = true;
            Debug.Log("[VRME] Pre-fetching auto briefing audio in the background; playback held for the gaze gate.");
        }

        Task sendTask = SendAutoIntroRequestLoopAsync(introPrompt);

        bool attentionSatisfied = await WaitForAvatarAttentionBeforeAutoIntroAsync();
        if (!attentionSatisfied)
        {
            autoIntroPlaybackHeld = false;
            heldAutoIntroPlaybackActions.Clear();
            await sendTask;
            return;
        }

        if (gazeGateActive)
        {
            ReleaseHeldAutoIntroPlayback();
        }

        await sendTask;
    }

    private async Task SendAutoIntroRequestLoopAsync(string introPrompt)
    {
        bool waitingForConnectionLogged = false;
        int sendAttempt = 0;
        while (!autoIntroSent && isActiveAndEnabled &&
               lifetimeCancellation != null && !lifetimeCancellation.IsCancellationRequested)
        {
            websocket = PersistentWebSocket.Socket;
            cancellation = PersistentWebSocket.Cancellation;
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                if (!waitingForConnectionLogged)
                {
                    Debug.Log("[VRME] Auto briefing is waiting for the persistent backend connection.");
                    waitingForConnectionLogged = true;
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Mathf.Max(0.5f, reconnectDelaySeconds)),
                        lifetimeCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            waitingForConnectionLogged = false;
            sendAttempt++;
            Debug.Log("[VRME] Auto briefing fallback sending. scene=" + SceneManager.GetActiveScene().name + ", attempt=" + sendAttempt);
            bool sent = await SendTextPromptAsync(introPrompt, "auto_briefing");
            if (sent)
            {
                autoIntroSent = true;
                if (enableTaskHighlights && !taskHighlightsActivated && IsTaskHighlightRevealAllowedForCurrentScene())
                {
                    Debug.Log("[VRME] Activating task highlights after auto briefing completed.");
                    ActivateSceneTaskHighlights();
                }
                Debug.Log("[VRME] Auto briefing completed.");
                return;
            }

            Debug.LogWarning("[VRME] Auto briefing send failed; connection maintenance will reconnect before another send. attempt=" + sendAttempt);
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Mathf.Max(0.5f, reconnectDelaySeconds)),
                    lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void EnqueueOrHoldPlaybackAction(Action action, bool hold)
    {
        if (hold)
        {
            heldAutoIntroPlaybackActions.Add(action);
        }
        else
        {
            mainThreadActions.Enqueue(action);
        }
    }

    private void ReleaseHeldAutoIntroPlayback()
    {
        autoIntroPlaybackHeld = false;
        if (heldAutoIntroPlaybackActions.Count == 0)
        {
            return;
        }

        Debug.Log("[VRME] Gaze gate satisfied; releasing " + heldAutoIntroPlaybackActions.Count + " buffered auto-briefing playback action(s).");
        foreach (Action action in heldAutoIntroPlaybackActions)
        {
            mainThreadActions.Enqueue(action);
        }
        heldAutoIntroPlaybackActions.Clear();
    }

    private async Task<bool> WaitForAvatarAttentionBeforeAutoIntroAsync()
    {
        if (!requireAvatarAttentionBeforeAutoIntro)
        {
            return true;
        }

        float windowSeconds = Mathf.Max(0.5f, autoIntroAttentionWindowSeconds);
        float requiredSeconds = Mathf.Clamp(
            autoIntroRequiredAvatarAttentionSeconds,
            0.1f,
            windowSeconds);
        Debug.Log("[VRME] Auto briefing is waiting for avatar attention: " +
            requiredSeconds.ToString("0.0") + "s accumulated within the latest " +
            windowSeconds.ToString("0.0") + "s.");
        // No forced start here anymore: if the participant never looks, we never
        // speak the full briefing. We only ever nudge with a short "Hi, I'm here"
        // so the real introduction is never heard before they're actually
        // looking (which would be startling and easy to miss/mishear).
        // Tutorial gives the participant three seconds from the moment its
        // attention gate starts. Formal scenes retain their configured wait.
        float initialReminderDelay = IsTutorialScene()
            ? TutorialInitialAttentionReminderSeconds
            : Mathf.Max(0f, autoIntroMaximumAttentionWaitSeconds);
        float nextReminderAttemptAt = Time.realtimeSinceStartup + initialReminderDelay;

        // Deliberately does not also check !autoIntroSent here: that flag flips true
        // as soon as the parallel prefetch request finishes sending, which usually
        // happens well before the participant has actually looked over. Stopping this
        // wait on that flag was defeating the gaze gate entirely — the LLM+TTS round
        // trip would finish, autoIntroSent would flip, this loop would exit with
        // attentionSatisfied=false, and RunAutoIntroAsync would then clear the
        // already-buffered playback before it was ever released for real gaze.
        while (isActiveAndEnabled &&
               lifetimeCancellation != null && !lifetimeCancellation.IsCancellationRequested)
        {
            if (CameraPoseSender.TryGetRecentAvatarAttention(
                    windowSeconds,
                    requiredSeconds,
                    out float accumulatedSeconds,
                    out int hitSampleCount))
            {
                Debug.Log("[VRME] Avatar-attention gate satisfied. accumulatedDwellSeconds=" +
                    accumulatedSeconds.ToString("0.0") +
                    ", windowSeconds=" + windowSeconds.ToString("0.0") +
                    ", hitSamples=" + hitSampleCount + ".");
                if (IsTutorialScene())
                {
                    await WaitForCurrentReplyBeforeTutorialIntroAsync();
                }
                return true;
            }

            if (enableAutoIntroAttentionReminder &&
                Time.realtimeSinceStartup >= nextReminderAttemptAt)
            {
                Debug.Log("[VRME] Avatar-attention gate is still unmet; sending a short attention-getter and continuing to wait for gaze.");
                string reminderPrompt = IsTutorialScene()
                    ? "[SYSTEM_ATTENTION_REMINDER]\n" +
                      "[TUTORIAL_MOVEMENT_INVITATION]\n" +
                      "Say exactly this and nothing else: I'm over here. Please move around and come a little closer to me.\n" +
                      "[/SYSTEM_ATTENTION_REMINDER]"
                    : "[SYSTEM_ATTENTION_REMINDER]\n" +
                      "Say exactly this one short sentence and nothing else: Hi, I'm here.\n" +
                      "Do not introduce the task, mention highlights, or ask a question.\n" +
                      "[/SYSTEM_ATTENTION_REMINDER]";
                bool reminderSent = await SendTextPromptAsync(
                    reminderPrompt,
                    "attention_reminder");
                nextReminderAttemptAt = Time.realtimeSinceStartup + (reminderSent
                    ? Mathf.Max(10f, autoIntroAttentionReminderDelaySeconds)
                    : Mathf.Max(0.5f, reconnectDelaySeconds));
                continue;
            }

            await Task.Yield();
        }

        return false;
    }

    private async Task WaitForTutorialFirstUiAsync()
    {
        ToSetup[] setupScreens = FindObjectsByType<ToSetup>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (setupScreens.Length == 0)
        {
            return;
        }

        Debug.Log("[VRME] Tutorial briefing is waiting for the first participant-ID UI to close.");
        while (isActiveAndEnabled &&
               lifetimeCancellation != null && !lifetimeCancellation.IsCancellationRequested)
        {
            bool firstUiStillVisible = false;
            foreach (ToSetup setupScreen in setupScreens)
            {
                // SetupModule itself intentionally remains active because it owns
                // CameraPoseSender. Wait only for its participant-ID controls,
                // which ToSetup hides after a successful submission.
                if (setupScreen != null && setupScreen.IsParticipantInputVisible)
                {
                    firstUiStillVisible = true;
                    break;
                }
            }

            if (!firstUiStillVisible)
            {
                Debug.Log("[VRME] Tutorial first UI completed; avatar briefing may begin.");
                return;
            }

            await Task.Yield();
        }
    }

    // Fallback used whenever the serialized autoIntroPrompt field is blank (for
    // example after clearing an old Inspector override). Unity keeps whatever
    // value is serialized in the scene regardless of this class's field
    // initializer, so an empty override would otherwise silently skip the
    // entire opening turn instead of reverting to this text.
    private const string DefaultAutoIntroPrompt =
        "Greet the participant briefly in one short sentence, in whatever style fits the selected avatar condition, then ask them to describe in their own words " +
        "what they notice around them. Keep it to one short open question. Do not mention any task, objective, goal, " +
        "or specific interactive object.";

    private string BuildAutoTaskBriefingPrompt()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string taskObjective = GetSceneTaskObjective(sceneName);
        if (string.IsNullOrWhiteSpace(taskObjective))
        {
            // Wrapped so the backend can tag its reply source as "auto_briefing" (see
            // response_source in server_unity_vibevoice.py). Without this marker the
            // backend falls through to source="reply", which fails the client-side
            // source=="auto_briefing" check that gates playback behind the gaze gate —
            // the six formal scenes' opening line was never actually being held back.
            string greeting = string.IsNullOrWhiteSpace(autoIntroPrompt) ? DefaultAutoIntroPrompt : autoIntroPrompt;
            return "[SYSTEM_OPENING_GREETING]\n" + greeting + "\n[/SYSTEM_OPENING_GREETING]";
        }

        return
            "[SYSTEM_AUTO_TASK_BRIEFING]\n" +
            "Scene: " + sceneName + "\n" +
            "Task: " + taskObjective + "\n" +
            "Say only this task introduction in one or two short spoken sentences. Do not ask a reflection question.\n" +
            "[/SYSTEM_AUTO_TASK_BRIEFING]";
    }

    private static string GetSceneTaskObjective(string sceneName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(sceneName) ? "" : sceneName.Trim().ToLowerInvariant();
        switch (normalizedName)
        {
            case "tutorial_interaction":
                return "discover how the shapes respond by using the grip button to pick up any one of them, moving it around, and letting it go";
            // The six formal emotion scenes intentionally have no spoken task objective:
            // the interaction each scene affords is discoverable background knowledge
            // (see GetSceneDescription) that the avatar may only speak to reactively,
            // never announce. BuildAutoTaskBriefingPrompt() falls back to autoIntroPrompt
            // (greeting + open question) for any scene that returns "" here.
            default:
                return "";
        }
    }

    private string GetSingleHeldTutorialObjectKey()
    {
        string heldKey = "";
        int heldCount = 0;
        foreach (GameObject taskObject in activeGuidedTaskObjects)
        {
            InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
            if (tracker == null)
            {
                continue;
            }

            tracker.RefreshCurrentHeldState();
            if (!tracker.isCurrentlyHeld)
            {
                continue;
            }

            heldCount++;
            heldKey = tracker.ContextName;
        }

        return heldCount == 1 ? heldKey : "";
    }

    private InteractionTracker FindTutorialTrackerByKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        foreach (GameObject taskObject in activeGuidedTaskObjects)
        {
            InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
            if (tracker != null && string.Equals(tracker.ContextName, objectKey, StringComparison.OrdinalIgnoreCase))
            {
                return tracker;
            }
        }

        return null;
    }

    private void ApplyTutorialControlEvent(string actionName, string objectKey)
    {
        if (!IsTutorialScene() || string.IsNullOrWhiteSpace(actionName))
        {
            return;
        }

        switch (actionName.Trim().ToLowerInvariant())
        {
            case "initial_description_received":
                if (tutorialControlStage == TutorialControlStage.AwaitingInitialDescription)
                {
                    tutorialControlStage = TutorialControlStage.AwaitingFirstHeldDescription;
                }
                break;
            case "first_visual_description_received":
                if (tutorialControlStage == TutorialControlStage.AwaitingFirstHeldDescription &&
                    FindTutorialTrackerByKey(objectKey) != null)
                {
                    tutorialFirstObjectKey = objectKey;
                    tutorialControlStage = TutorialControlStage.AwaitingFirstShapeDescription;
                }
                break;
            case "first_shape_response_received":
                if (tutorialControlStage == TutorialControlStage.AwaitingFirstShapeDescription &&
                    string.Equals(objectKey, tutorialFirstObjectKey, StringComparison.OrdinalIgnoreCase))
                {
                    tutorialControlStage = TutorialControlStage.AwaitingFirstActionChoice;
                }
                break;
            case "first_action_choice_received":
                if (tutorialControlStage == TutorialControlStage.AwaitingFirstActionChoice &&
                    string.Equals(objectKey, tutorialFirstObjectKey, StringComparison.OrdinalIgnoreCase))
                {
                    InteractionTracker tracker = FindTutorialTrackerByKey(tutorialFirstObjectKey);
                    if (tracker != null)
                    {
                        tutorialFirstInteractionStartPosition = tracker.transform.position;
                        tutorialFirstInteractionArmedAtUtc = DateTime.UtcNow;
                        tutorialControlStage = TutorialControlStage.AwaitingFirstInteraction;
                    }
                }
                break;
            case "second_description_received":
                if (tutorialControlStage == TutorialControlStage.AwaitingSecondHeldDescription &&
                    FindTutorialTrackerByKey(objectKey) != null &&
                    !string.Equals(objectKey, tutorialFirstObjectKey, StringComparison.OrdinalIgnoreCase))
                {
                    CompleteTutorialControlledFlow();
                }
                break;
        }

        Debug.Log("[VRME] Tutorial control event applied. action=" + actionName +
            ", object=" + objectKey + ", stage=" + tutorialControlStage);
    }

    private void CheckTutorialControlledInteraction()
    {
        if (!IsTutorialScene() || tutorialControlStage != TutorialControlStage.AwaitingFirstInteraction)
        {
            return;
        }

        InteractionTracker tracker = FindTutorialTrackerByKey(tutorialFirstObjectKey);
        if (tracker == null)
        {
            return;
        }

        tracker.RefreshCurrentHeldState();
        bool releasedAfterPrompt = tracker.LastControllerReleaseUtc != DateTime.MinValue &&
            tracker.LastControllerReleaseUtc >= tutorialFirstInteractionArmedAtUtc;
        bool movedAfterPrompt = Vector3.Distance(tracker.transform.position, tutorialFirstInteractionStartPosition) >= 0.18f;
        if (!releasedAfterPrompt && !movedAfterPrompt)
        {
            return;
        }

        tutorialControlStage = TutorialControlStage.AwaitingSecondHeldDescription;
        Debug.Log("[VRME] Tutorial first-object interaction confirmed. released=" + releasedAfterPrompt +
            ", moved=" + movedAfterPrompt + ", object=" + tutorialFirstObjectKey);
        _ = SendTutorialStagePromptAsync("choose_second");
    }

    private void CompleteTutorialControlledFlow()
    {
        tutorialControlStage = TutorialControlStage.Complete;
        guidedTaskCompleted = true;
        guidedTaskActive = false;
        ClearGuidedTaskHighlights();

        SceneController[] controllers = FindObjectsByType<SceneController>(FindObjectsSortMode.None);
        foreach (SceneController controller in controllers)
        {
            if (controller != null)
            {
                controller.UnlockExitForTutorial();
            }
        }

        ShowTutorialExitHighlight();
        Debug.Log("[VRME] Tutorial controlled flow completed after two distinct held-object voice descriptions.");
    }

    private static bool IsTutorialScene()
    {
        return string.Equals(
            SceneManager.GetActiveScene().name,
            "Tutorial_Interaction",
            StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureTutorialTargets()
    {
        if (!IsTutorialScene())
        {
            return;
        }

        Transform viewer = Camera.main != null ? Camera.main.transform : ResolvePlayerTransform();
        Vector3 origin = viewer != null ? viewer.position : transform.position;
        Vector3 forward = viewer != null ? viewer.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 targetCenter = origin + forward * 3.2f;
        targetCenter = ProjectPointToWalkableGround(targetCenter, origin) + Vector3.up * 0.13f;
        float sideOffset = 0.72f;
        GameObject firstCube = FindExactSceneObject("BlueCube");
        GameObject secondCube = FindExactSceneObject("BlueCube (1)");
        if (firstCube != null && secondCube != null)
        {
            Vector3 cubeAxis = secondCube.transform.position - firstCube.transform.position;
            cubeAxis.y = 0f;
            if (cubeAxis.sqrMagnitude > 0.01f)
            {
                right = cubeAxis.normalized;
            }
            targetCenter = (firstCube.transform.position + secondCube.transform.position) * 0.5f;
        }
        Quaternion faceViewer = Quaternion.LookRotation(-forward, Vector3.up);

        CreateTutorialShapeTarget(
            "Tutorial Triangular Prism Target",
            targetCenter - right * sideOffset,
            faceViewer,
            CreateTriangularPrismMesh(),
            new Color(1f, 0.43f, 0.25f, 1f));
        CreateTutorialShapeTarget(
            "Tutorial Cylinder Target",
            targetCenter + right * sideOffset,
            faceViewer,
            CreateCylinderMesh(40),
            new Color(0.12f, 0.75f, 1f, 1f));
    }

    private static void CreateTutorialShapeTarget(
        string objectName,
        Vector3 position,
        Quaternion rotation,
        Mesh mesh,
        Color color)
    {
        GameObject existing = GameObject.Find(objectName);
        if (existing != null)
        {
            return;
        }

        GameObject target = new GameObject(objectName);
        target.transform.SetPositionAndRotation(position, rotation);
        target.transform.localScale = objectName.IndexOf("Cylinder", StringComparison.OrdinalIgnoreCase) >= 0
            ? Vector3.one * 0.22f
            : Vector3.one * 0.21f;

        MeshFilter filter = target.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = target.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        renderer.material = material;

        MeshCollider solidCollider = target.AddComponent<MeshCollider>();
        solidCollider.sharedMesh = mesh;
        solidCollider.convex = true;

        InteractionTracker tracker = target.AddComponent<InteractionTracker>();
        tracker.displayName = objectName.Replace("Tutorial ", "");
        tracker.attentionOnlyTarget = true;
        tracker.trackTriggerCollisions = false;
        Debug.Log("[Tutorial] Created " + objectName + " at " + position.ToString("F2") + ".");
    }

    private static Mesh CreateTriangularPrismMesh()
    {
        var mesh = new Mesh { name = "Tutorial Triangular Prism Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(-0.62f, -0.62f, -0.42f),
            new Vector3(0.62f, -0.62f, -0.42f),
            new Vector3(0f, 0.62f, -0.42f),
            new Vector3(-0.62f, -0.62f, 0.42f),
            new Vector3(0.62f, -0.62f, 0.42f),
            new Vector3(0f, 0.62f, 0.42f)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 3, 4, 5,
            0, 1, 4, 0, 4, 3,
            1, 2, 5, 1, 5, 4,
            2, 0, 3, 2, 3, 5
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateCylinderMesh(int segments)
    {
        segments = Mathf.Max(12, segments);
        var vertices = new Vector3[segments * 2 + 2];
        var triangles = new int[segments * 12];
        int frontCenter = segments * 2;
        int backCenter = frontCenter + 1;
        vertices[frontCenter] = new Vector3(0f, 0f, -0.45f);
        vertices[backCenter] = new Vector3(0f, 0f, 0.45f);
        for (int index = 0; index < segments; index++)
        {
            float radians = index * Mathf.PI * 2f / segments;
            float x = Mathf.Cos(radians) * 0.58f;
            float y = Mathf.Sin(radians) * 0.58f;
            vertices[index] = new Vector3(x, y, -0.45f);
            vertices[index + segments] = new Vector3(x, y, 0.45f);
            int next = (index + 1) % segments;
            int offset = index * 12;
            triangles[offset] = frontCenter;
            triangles[offset + 1] = next;
            triangles[offset + 2] = index;
            triangles[offset + 3] = backCenter;
            triangles[offset + 4] = index + segments;
            triangles[offset + 5] = next + segments;
            triangles[offset + 6] = index;
            triangles[offset + 7] = next;
            triangles[offset + 8] = next + segments;
            triangles[offset + 9] = index;
            triangles[offset + 10] = next + segments;
            triangles[offset + 11] = index + segments;
        }

        var mesh = new Mesh { name = "Tutorial Cylinder Mesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ActivateSceneTaskHighlights()
    {
        if (taskHighlightsActivated)
        {
            Debug.Log("[VRME] Task highlights already active. scene=" + SceneManager.GetActiveScene().name);
            return;
        }

        string sceneName = SceneManager.GetActiveScene().name;
        EnsureTutorialTargets();
        SceneTaskHighlightSpec spec = GetSceneTaskHighlightSpec(sceneName);
        if (spec == null)
        {
            Debug.Log("[VRME] No task highlight spec for scene=" + sceneName);
            return;
        }

        activeGuidedTaskSpec = spec;
        activeGuidedTaskObjects.Clear();
        activeGuidedTaskTargets.Clear();
        activeGuidedTaskMarkers.Clear();
        guidedTaskActive = spec.CompletionMode != GuidedTaskCompletionMode.None;
        guidedTaskCompleted = false;
        guidedTaskProgressSent = false;
        guidedTaskActivatedAtUtc = DateTime.UtcNow;
        taskHighlightsActivated = true;
        if (IsTutorialScene())
        {
            tutorialControlStage = TutorialControlStage.AwaitingInitialDescription;
            tutorialFirstObjectKey = "";
            tutorialFirstInteractionArmedAtUtc = DateTime.MinValue;
        }

        int objectCount = 0;
        foreach (string objectName in spec.ObjectNames)
        {
            GameObject taskObject = FindSceneObjectByNameHint(objectName);
            if (taskObject == null || activeGuidedTaskObjects.Contains(taskObject))
            {
                continue;
            }

            AddOutlineHighlight(taskObject, taskObjectHighlightColor, taskObjectOutlineWidth);
            activeGuidedTaskObjects.Add(taskObject);
            objectCount++;
        }

        int targetCount = 0;
        foreach (string targetName in spec.TargetNames)
        {
            GameObject targetObject = FindSceneObjectByNameHint(targetName);
            if (targetObject == null)
            {
                continue;
            }

            bool isTutorialScene = string.Equals(sceneName, "Tutorial_Interaction", StringComparison.OrdinalIgnoreCase);
            bool isPuppiesScene = string.Equals(sceneName, "Puppies", StringComparison.OrdinalIgnoreCase);
            bool isElephantScene = string.Equals(sceneName, "Elephant", StringComparison.OrdinalIgnoreCase);
            bool animalOutlineOnly = isPuppiesScene;
            if (!animalOutlineOnly && !isTutorialScene)
            {
                AddTargetMarker(
                    targetObject,
                    spec,
                    isElephantScene ? taskObjectHighlightColor : taskTargetHighlightColor);
            }
            if (isPuppiesScene || isElephantScene)
            {
                // An outline makes the selected animal unambiguous. The ground
                // disc alone is easy to miss underneath a moving animal.
                AddOutlineHighlight(targetObject, taskObjectHighlightColor, taskObjectOutlineWidth);
            }
            if (!activeGuidedTaskTargets.Contains(targetObject))
            {
                activeGuidedTaskTargets.Add(targetObject);
            }
            targetCount++;
        }

        if (targetCount == 0 && spec.UseRuntimeTargetFromPlayer)
        {
            GameObject runtimeTarget = CreateRuntimeGuidedTaskTarget(sceneName, spec);
            if (runtimeTarget != null)
            {
                AddTargetMarker(runtimeTarget, spec);
                activeGuidedTaskTargets.Add(runtimeTarget);
                targetCount++;
            }
        }

        if (string.Equals(sceneName, "Tunnel", StringComparison.OrdinalIgnoreCase))
        {
            HideGuidedTaskSurveyIfNeeded(sceneName);
        }
        if (string.Equals(sceneName, "Lake", StringComparison.OrdinalIgnoreCase))
        {
            SetLakeThrowTargetVisible(true);
        }

        Debug.Log("[VRME] Task highlights active. scene=" + sceneName + ", objects=" + objectCount + ", targets=" + targetCount);
        if (objectCount == 0)
        {
            Debug.LogWarning("[VRME] No task object highlight target found for scene=" + sceneName);
        }
        if (targetCount == 0 && spec.TargetNames.Length > 0)
        {
            Debug.LogWarning("[VRME] No task target marker found for scene=" + sceneName);
        }
    }

    private SceneTaskHighlightSpec GetSceneTaskHighlightSpec(string sceneName)
    {
        string normalizedName = string.IsNullOrWhiteSpace(sceneName) ? "" : sceneName.Trim().ToLowerInvariant();
        switch (normalizedName)
        {
            case "tutorial_interaction":
                return new SceneTaskHighlightSpec(
                    new[]
                    {
                        "BlueCube",
                        "BlueCube (1)",
                        "Tutorial Triangular Prism Target",
                        "Tutorial Cylinder Target"
                    },
                    Array.Empty<string>(),
                    "discoverable shape interaction",
                    GuidedTaskCompletionMode.TutorialControlledDialogue,
                    TaskMarkerPlacement.ObjectCenter,
                    0.8f,
                    0.9f);
            case "puppies":
                return new SceneTaskHighlightSpec(
                    new[] { "TennisBall" },
                    new[] { "VRME_FETCH_DOG" },
                    "Dog target",
                    GuidedTaskCompletionMode.DogFetchReturned,
                    TaskMarkerPlacement.Ground,
                    0.75f,
                    0.75f);
            case "elephant":
                return new SceneTaskHighlightSpec(
                    new[] { "Banana" },
                    new[] { "VRME_NEAREST_FEED_ELEPHANT" },
                    "Elephant target",
                    GuidedTaskCompletionMode.ElephantFed,
                    TaskMarkerPlacement.Ground,
                    0.75f,
                    0.75f);
            case "lake":
                return new SceneTaskHighlightSpec(
                    new[] { "Airplane", "Stone" },
                    Array.Empty<string>(),
                    "Lake target",
                    GuidedTaskCompletionMode.ObjectNearTarget,
                    TaskMarkerPlacement.Ground,
                    2.25f,
                    1.35f,
                    true,
                    4f);
            case "solitaryconfinement":
                return new SceneTaskHighlightSpec(
                    new[] { "Baseball", "Book", "Cup" },
                    new[] { "Door" },
                    "Prison door target",
                    GuidedTaskCompletionMode.ObjectNearTarget,
                    TaskMarkerPlacement.DoorSurface,
                    0.2f,
                    0.42f);
            case "tunnel":
                return new SceneTaskHighlightSpec(
                    new[] { "Flashlight", "HandTorch", "Torch" },
                    Array.Empty<string>(),
                    "Tunnel target",
                    GuidedTaskCompletionMode.PlayerAndObjectNearTarget,
                    TaskMarkerPlacement.Ground,
                    0.85f,
                    1.35f,
                    true,
                    3f);
            case "attic":
                return new SceneTaskHighlightSpec(
                    new[] { "Shield", "Shield01" },
                    Array.Empty<string>(),
                    "Safe position",
                    GuidedTaskCompletionMode.PlayerAndObjectNearTarget,
                    TaskMarkerPlacement.Ground,
                    0.85f,
                    1.1f,
                    true,
                    2.5f);
            default:
                return null;
        }
    }

    private GameObject CreateRuntimeGuidedTaskTarget(string sceneName, SceneTaskHighlightSpec spec)
    {
        Transform player = ResolvePlayerTransform();
        Vector3 basePosition = player != null ? player.position : transform.position;
        Vector3 forward = player != null ? player.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        Vector3 candidate;
        if (string.Equals(sceneName, "Lake", StringComparison.OrdinalIgnoreCase) &&
            TryGetLakeSurfaceTarget(basePosition, spec.RuntimeTargetForwardDistance, out Vector3 lakeTarget))
        {
            candidate = lakeTarget;
        }
        else if (string.Equals(sceneName, "Attic", StringComparison.OrdinalIgnoreCase) &&
            TryGetIndoorAtticTarget(basePosition, forward, spec.RuntimeTargetForwardDistance, out Vector3 atticTarget))
        {
            candidate = atticTarget;
        }
        else
        {
            candidate = basePosition + forward * Mathf.Max(0.5f, spec.RuntimeTargetForwardDistance);
            candidate = ProjectPointToWalkableGround(candidate, basePosition);
        }

        string runtimeTargetName = "VRME_RuntimeTaskTarget_" + sceneName;
        GameObject existingTarget = GameObject.Find(runtimeTargetName);
        if (existingTarget != null)
        {
            existingTarget.transform.position = candidate;
            return existingTarget;
        }

        GameObject runtimeTarget = new GameObject(runtimeTargetName);
        runtimeTarget.transform.position = candidate;
        Debug.Log("[VRME] Runtime task target created. scene=" + sceneName + ", position=" + candidate);
        return runtimeTarget;
    }

    private bool TryGetLakeSurfaceTarget(Vector3 playerPosition, float distance, out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;
        GameObject water = FindExactSceneObject("Water");
        if (water == null || !TryGetRendererBounds(water, out Bounds waterBounds))
        {
            return false;
        }

        Vector3 towardLakeCenter = waterBounds.center - playerPosition;
        towardLakeCenter.y = 0f;
        if (towardLakeCenter.sqrMagnitude < 0.01f)
        {
            towardLakeCenter = transform.forward;
            towardLakeCenter.y = 0f;
        }
        towardLakeCenter.Normalize();

        // The straight-ahead point from the spawn lies on the jetty. Search
        // diagonally into the lake and accept a point only when a downward ray
        // hits Water before it hits a bridge/deck collider.
        float searchDistance = Mathf.Max(5.5f, distance);
        float[] waterSearchAngles = { 60f, -60f, 75f, -75f, 45f, -45f };
        Vector3 candidate = Vector3.zero;
        bool foundOpenWater = false;
        foreach (float angle in waterSearchAngles)
        {
            Vector3 searchDirection = Quaternion.AngleAxis(angle, Vector3.up) * towardLakeCenter;
            Vector3 testPoint = playerPosition + searchDirection * searchDistance;
            testPoint = ClampLakePointInsideBounds(testPoint, waterBounds, 2.75f);
            if (!IsOpenWaterPoint(testPoint, water, waterBounds))
            {
                continue;
            }

            candidate = testPoint;
            foundOpenWater = true;
            break;
        }

        if (!foundOpenWater)
        {
            // Collider layouts can differ between editor and headset builds.
            // Keep the fallback laterally offset from the player-to-lake-centre
            // line, rather than falling back onto the jetty again.
            Vector3 lateral = Vector3.Cross(Vector3.up, towardLakeCenter).normalized;
            Vector3 firstSide = waterBounds.center + lateral * Mathf.Min(3.5f, waterBounds.extents.x * 0.35f);
            Vector3 secondSide = waterBounds.center - lateral * Mathf.Min(3.5f, waterBounds.extents.x * 0.35f);
            firstSide = ClampLakePointInsideBounds(firstSide, waterBounds, 2.75f);
            secondSide = ClampLakePointInsideBounds(secondSide, waterBounds, 2.75f);
            candidate = IsOpenWaterPoint(firstSide, water, waterBounds) ? firstSide : secondSide;
        }

        // The water shader displaces the visible surface above the static mesh
        // bounds. Ten centimetres was still swallowed by waves in-headset, so
        // keep the marker clearly above the highest rendered water geometry.
        candidate.y = waterBounds.max.y + 0.35f;
        targetPosition = candidate;
        return true;
    }

    private static Vector3 ClampLakePointInsideBounds(Vector3 point, Bounds waterBounds, float requestedInset)
    {
        float insetX = Mathf.Min(requestedInset, Mathf.Max(0.05f, waterBounds.extents.x - 0.05f));
        float insetZ = Mathf.Min(requestedInset, Mathf.Max(0.05f, waterBounds.extents.z - 0.05f));
        point.x = Mathf.Clamp(point.x, waterBounds.min.x + insetX, waterBounds.max.x - insetX);
        point.z = Mathf.Clamp(point.z, waterBounds.min.z + insetZ, waterBounds.max.z - insetZ);
        return point;
    }

    private static bool IsOpenWaterPoint(Vector3 point, GameObject water, Bounds waterBounds)
    {
        Vector3 rayOrigin = new Vector3(point.x, waterBounds.max.y + 4f, point.z);
        if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                Mathf.Max(8f, waterBounds.size.y + 8f),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide) ||
            hit.collider == null)
        {
            return false;
        }

        Transform hitTransform = hit.collider.transform;
        Transform waterTransform = water.transform;
        return hitTransform == waterTransform ||
            hitTransform.IsChildOf(waterTransform) ||
            waterTransform.IsChildOf(hitTransform) ||
            hit.collider.name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TryGetIndoorAtticTarget(
        Vector3 playerPosition,
        Vector3 playerForward,
        float distance,
        out Vector3 targetPosition)
    {
        targetPosition = Vector3.zero;
        GameObject ground = FindExactSceneObject("Ground");
        if (ground == null || !TryGetRendererBounds(ground, out Bounds floorBounds))
        {
            return false;
        }

        Vector3 candidate = playerPosition + playerForward * Mathf.Max(1f, distance);
        float edgeInset = 0.45f;
        candidate.x = Mathf.Clamp(candidate.x, floorBounds.min.x + edgeInset, floorBounds.max.x - edgeInset);
        candidate.z = Mathf.Clamp(candidate.z, floorBounds.min.z + edgeInset, floorBounds.max.z - edgeInset);
        candidate.y = floorBounds.max.y + 0.05f;
        targetPosition = candidate;
        return true;
    }

    private GameObject FindExactSceneObject(string objectName)
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (Transform sceneTransform in transforms)
        {
            if (sceneTransform != null && sceneTransform.gameObject.activeInHierarchy &&
                string.Equals(sceneTransform.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return sceneTransform.gameObject;
            }
        }

        return null;
    }

    private Vector3 ProjectPointToWalkableGround(Vector3 candidate, Vector3 basePosition)
    {
        float rayStartY = Mathf.Max(candidate.y, basePosition.y) + 0.75f;
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(candidate.x, rayStartY, candidate.z), Vector3.down, out hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            hit.normal.y > 0.2f)
        {
            return hit.point;
        }

        if (Physics.Raycast(basePosition + Vector3.up * 0.5f, Vector3.down, out hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            hit.normal.y > 0.2f)
        {
            candidate.y = hit.point.y;
            return candidate;
        }

        candidate.y = basePosition.y > 1f ? basePosition.y - 1.45f : basePosition.y;
        return candidate;
    }

    private GameObject FindSceneObjectByNameHint(string nameHint)
    {
        if (string.IsNullOrWhiteSpace(nameHint))
        {
            return null;
        }

        if (string.Equals(nameHint, "VRME_NEAREST_FEED_ELEPHANT", StringComparison.Ordinal))
        {
            return FindNearestFeedElephant();
        }
        if (string.Equals(nameHint, "VRME_FETCH_DOG", StringComparison.Ordinal))
        {
            return FindFetchDog();
        }

        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        GameObject firstContainsMatch = null;
        foreach (Transform sceneTransform in transforms)
        {
            if (sceneTransform == null || !sceneTransform.gameObject.activeInHierarchy)
            {
                continue;
            }

            string objectName = sceneTransform.name;
            if (string.Equals(objectName, nameHint, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveHighlightRoot(sceneTransform.gameObject);
            }

            if (firstContainsMatch == null &&
                objectName.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                firstContainsMatch = ResolveHighlightRoot(sceneTransform.gameObject);
            }
        }

        return firstContainsMatch;
    }

    private GameObject FindNearestFeedElephant()
    {
        FeedElephants[] feeders = FindObjectsByType<FeedElephants>(FindObjectsSortMode.None);
        Transform player = ResolvePlayerTransform();
        Vector3 reference = player != null ? player.position : transform.position;
        FeedElephants nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (FeedElephants feeder in feeders)
        {
            if (feeder == null || !feeder.isActiveAndEnabled)
            {
                continue;
            }

            GameObject elephantObject = feeder.elephant != null ? feeder.elephant : feeder.gameObject;
            float distance = (elephantObject.transform.position - reference).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = feeder;
            }
        }

        if (nearest == null)
        {
            return null;
        }

        GameObject result = nearest.elephant != null ? nearest.elephant : nearest.gameObject;
        Debug.Log("[VRME] Elephant highlight resolved to feedable target=" + result.name + ".");
        return result;
    }

    private GameObject FindFetchDog()
    {
        DogMotion2[] dogs = FindObjectsByType<DogMotion2>(FindObjectsSortMode.None);
        foreach (DogMotion2 dog in dogs)
        {
            if (dog == null || !dog.isActiveAndEnabled || !dog.canFetchBall)
            {
                continue;
            }

            Debug.Log("[VRME] Dog highlight resolved to fetch-capable target=" + dog.gameObject.name + ".");
            return dog.gameObject;
        }

        Debug.LogWarning("[VRME] No active DogMotion2 with canFetchBall=true was found; no dog will be highlighted.");
        return null;
    }

    private GameObject ResolveHighlightRoot(GameObject candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        Transform current = candidate.transform;
        while (current.parent != null)
        {
            if (current.GetComponent<InteractionTracker>() != null ||
                current.GetComponent<Rigidbody>() != null ||
                current.GetComponent<Collider>() != null)
            {
                return current.gameObject;
            }

            current = current.parent;
        }

        return candidate;
    }

    private void AddOutlineHighlight(GameObject target, Color color, float width)
    {
        if (target == null)
        {
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[VRME] Highlight target has no renderers: " + target.name);
            return;
        }

        Outline outline = target.GetComponent<Outline>();
        if (outline == null)
        {
            outline = target.AddComponent<Outline>();
        }

        outline.OutlineMode = Outline.Mode.OutlineAll;
        outline.OutlineColor = color;
        outline.OutlineWidth = width;
        outline.enabled = true;
    }

    private void ShowTutorialExitHighlight()
    {
        if (!IsTutorialScene())
        {
            return;
        }

        GameObject exitRoot = null;
        GameObject exitTarget = null;
        GameObject[] allSceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject candidate in allSceneObjects)
        {
            if (candidate == null || candidate.scene != SceneManager.GetActiveScene() ||
                !string.Equals(candidate.name, "Arrow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TeleportationEventListener listener = candidate.GetComponentInChildren<TeleportationEventListener>(true);
            if (listener != null)
            {
                exitRoot = candidate;
                exitTarget = listener.gameObject;
                break;
            }
        }

        if (exitRoot == null || exitTarget == null)
        {
            Debug.LogWarning("[VRME] Tutorial Exit root/teleport anchor was not found for highlighting.");
            return;
        }

        // The legacy Arrow root also owns the actual teleport anchor. Restore
        // that interaction without restoring its arrow graphic or text canvas.
        foreach (Transform child in exitRoot.transform)
        {
            bool containsExitTarget = child == exitTarget.transform || exitTarget.transform.IsChildOf(child);
            child.gameObject.SetActive(containsExitTarget);
        }
        exitRoot.SetActive(true);
        exitTarget.SetActive(true);

        AddOutlineHighlight(exitTarget, taskTargetHighlightColor, taskObjectOutlineWidth);
        if (tutorialExitHighlightMarker != null)
        {
            tutorialExitHighlightMarker.SetActive(true);
            return;
        }

        Bounds bounds;
        bool hasBounds = TryGetRendererBounds(exitTarget, out bounds);
        Vector3 center = hasBounds ? bounds.center : exitTarget.transform.position;
        float markerY = hasBounds ? bounds.min.y + 0.06f : center.y + 0.06f;

        tutorialExitHighlightMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tutorialExitHighlightMarker.name = "VRME_TutorialExitHighlight";
        tutorialExitHighlightMarker.transform.position = new Vector3(center.x, markerY, center.z);
        tutorialExitHighlightMarker.transform.localScale = new Vector3(0.65f, 0.035f, 0.65f);

        Collider markerCollider = tutorialExitHighlightMarker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer markerRenderer = tutorialExitHighlightMarker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            markerRenderer.material = CreateTaskHighlightMaterial(taskTargetHighlightColor);
        }

        GameObject markerLight = new GameObject("VRME_TutorialExitHighlight_Light");
        markerLight.transform.SetParent(tutorialExitHighlightMarker.transform, false);
        markerLight.transform.localPosition = Vector3.up * 0.5f;
        Light light = markerLight.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = taskTargetHighlightColor;
        light.intensity = 2.2f;
        light.range = 2.2f;
        light.shadows = LightShadows.None;

        Debug.Log("[VRME] Tutorial Exit teleport anchor enabled with highlight; legacy arrow and text remain hidden.");
    }

    private void AddTargetMarker(
        GameObject target,
        SceneTaskHighlightSpec spec,
        Color? markerColorOverride = null)
    {
        if (target == null)
        {
            return;
        }

        Bounds bounds;
        bool hasRendererBounds = TryGetRendererBounds(target, out bounds);
        if (!hasRendererBounds)
        {
            bounds = new Bounds(target.transform.position, new Vector3(0.1f, 0.02f, 0.1f));
        }

        string markerName = "VRME_TaskHighlight_" + target.name;
        if (GameObject.Find(markerName) != null)
        {
            return;
        }

        bool objectCenterMarker = spec.MarkerPlacement == TaskMarkerPlacement.ObjectCenter;
        bool doorSurfaceMarker = spec.MarkerPlacement == TaskMarkerPlacement.DoorSurface;
        GameObject marker = GameObject.CreatePrimitive(objectCenterMarker ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
        marker.name = markerName;
        float markerRadius = Mathf.Max(0.12f, spec.MarkerRadius);
        if (doorSurfaceMarker)
        {
            Vector3 surfaceNormal;
            marker.transform.position = GetDoorSurfaceMarkerPose(target, bounds, out surfaceNormal);
            marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
            marker.transform.localScale = new Vector3(markerRadius, 0.012f, markerRadius);
        }
        else if (objectCenterMarker)
        {
            marker.transform.position = bounds.center;
            marker.transform.localScale = Vector3.one * markerRadius;
        }
        else
        {
            // Keep Tunnel close to its walkable surface; other scenes retain the
            // larger offset needed to avoid floor/water depth fighting.
            bool isTunnelScene = string.Equals(
                SceneManager.GetActiveScene().name,
                "Tunnel",
                StringComparison.OrdinalIgnoreCase);
            float surfaceOffset = isTunnelScene ? 0.03f : 0.08f;
            float markerY = hasRendererBounds
                ? bounds.min.y + surfaceOffset
                : target.transform.position.y + surfaceOffset;
            marker.transform.position = new Vector3(bounds.center.x, markerY, bounds.center.z);
            marker.transform.localScale = new Vector3(markerRadius, 0.045f, markerRadius);
        }

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        Color markerColor = markerColorOverride ?? taskTargetHighlightColor;
        if (markerRenderer != null)
        {
            markerRenderer.material = CreateTaskHighlightMaterial(markerColor);
        }

        GameObject lightObject = new GameObject(markerName + "_Light");
        lightObject.transform.SetParent(marker.transform, false);
        lightObject.transform.localPosition = Vector3.up * 0.5f;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = markerColor;
        light.intensity = 2.2f;
        light.range = Mathf.Max(1.8f, markerRadius * 3f);
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
        activeGuidedTaskMarkers.Add(marker);
    }

    private Vector3 GetDoorSurfaceMarkerPosition(GameObject target, Bounds bounds)
    {
        Vector3 surfaceNormal;
        return GetDoorSurfaceMarkerPose(target, bounds, out surfaceNormal);
    }

    private Vector3 GetDoorSurfaceMarkerPose(GameObject target, Bounds bounds, out Vector3 surfaceNormal)
    {
        Vector3 markerPosition = bounds.center + Vector3.up * Mathf.Min(0.2f, bounds.size.y * 0.08f);
        Transform player = ResolvePlayerTransform();
        if (player != null)
        {
            Vector3 direction = markerPosition - player.position;
            if (direction.sqrMagnitude > 0.01f &&
                Physics.Raycast(
                    player.position,
                    direction.normalized,
                    out RaycastHit hit,
                    Mathf.Max(2f, direction.magnitude + 2f),
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                surfaceNormal = hit.normal.sqrMagnitude > 0.01f ? hit.normal.normalized : -direction.normalized;
                return hit.point + surfaceNormal * 0.035f;
            }
        }

        surfaceNormal = GetDoorSurfaceDirectionToPlayer(markerPosition);
        return markerPosition + surfaceNormal * 0.08f;
    }

    private Vector3 GetDoorSurfaceDirectionToPlayer(Vector3 markerPosition)
    {
        Transform player = ResolvePlayerTransform();
        if (player == null)
        {
            return transform.forward.sqrMagnitude > 0.01f ? transform.forward.normalized : Vector3.forward;
        }

        Vector3 directionToPlayer = player.position - markerPosition;
        directionToPlayer.y = 0f;
        if (directionToPlayer.sqrMagnitude < 0.01f)
        {
            return transform.forward.sqrMagnitude > 0.01f ? transform.forward.normalized : Vector3.forward;
        }

        return directionToPlayer.normalized;
    }

    private void CheckGuidedTaskCompletion()
    {
        if (!guidedTaskActive || guidedTaskCompleted || activeGuidedTaskSpec == null)
        {
            return;
        }

        if (activeGuidedTaskObjects.Count == 0)
        {
            return;
        }

        switch (activeGuidedTaskSpec.CompletionMode)
        {
            case GuidedTaskCompletionMode.ObjectUsedOnce:
                if (AnyTaskObjectUsedOnce())
                {
                    CompleteGuidedTask("object_used_once");
                }
                break;
            case GuidedTaskCompletionMode.ObjectNearTarget:
                if (activeGuidedTaskTargets.Count == 0)
                {
                    break;
                }
                if (AnyTaskObjectNearAnyTarget(activeGuidedTaskSpec.CompletionRadius, requirePriorUse: true))
                {
                    CompleteGuidedTask("object_near_target");
                }
                break;
            case GuidedTaskCompletionMode.PlayerAndObjectNearTarget:
                if (activeGuidedTaskTargets.Count == 0)
                {
                    break;
                }
                if (IsPlayerAndUsedObjectNearTarget(activeGuidedTaskSpec.CompletionRadius))
                {
                    CompleteGuidedTask("player_and_object_near_target");
                }
                break;
            case GuidedTaskCompletionMode.DogFetchReturned:
                if (HasHighlightedDogReturnedBall())
                {
                    CompleteGuidedTask("dog_fetch_returned_to_player");
                }
                break;
            case GuidedTaskCompletionMode.ElephantFed:
                if (HasHighlightedElephantBeenFed())
                {
                    CompleteGuidedTask("elephant_received_banana");
                }
                break;
            case GuidedTaskCompletionMode.TutorialControlledDialogue:
                // Tutorial completion is driven by the explicit dialogue state
                // machine and two distinct held-object voice checkpoints.
                break;
        }
    }

    private bool HasHighlightedDogReturnedBall()
    {
        DogMotion2[] dogs = FindObjectsByType<DogMotion2>(FindObjectsSortMode.None);
        foreach (DogMotion2 dog in dogs)
        {
            if (dog != null && dog.isActiveAndEnabled && dog.HasReturnedBallToPlayer &&
                IsAssociatedWithActiveGuidedTarget(dog.gameObject))
            {
                return true;
            }
        }

        return false;
    }

    private async Task WaitForCurrentReplyBeforeTutorialIntroAsync()
    {
        // Let queued main-thread audio-start actions run before checking the
        // player, then avoid cutting off a movement hint or help response.
        await Task.Delay(TimeSpan.FromSeconds(0.1));
        int attempts = 0;
        while (isActiveAndEnabled && attempts < 400 &&
               (isRecording || isSending || IsReplyPlaybackActive()))
        {
            attempts++;
            await Task.Delay(TimeSpan.FromSeconds(0.05));
        }
        await Task.Delay(TimeSpan.FromSeconds(0.15));
    }

    private bool HasHighlightedElephantBeenFed()
    {
        FeedElephants[] feeders = FindObjectsByType<FeedElephants>(FindObjectsSortMode.None);
        foreach (FeedElephants feeder in feeders)
        {
            GameObject elephantObject = feeder != null && feeder.elephant != null
                ? feeder.elephant
                : feeder != null ? feeder.gameObject : null;
            if (feeder != null && feeder.isActiveAndEnabled && feeder.HasFedOnce &&
                IsAssociatedWithActiveGuidedTarget(elephantObject))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAssociatedWithActiveGuidedTarget(GameObject candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Transform candidateTransform = candidate.transform;
        foreach (GameObject target in activeGuidedTaskTargets)
        {
            if (target == null)
            {
                continue;
            }

            Transform targetTransform = target.transform;
            if (candidate == target || candidateTransform.IsChildOf(targetTransform) ||
                targetTransform.IsChildOf(candidateTransform))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnyTaskObjectNearAnyTarget(float radius, bool requirePriorUse)
    {
        float sqrRadius = radius * radius;
        foreach (GameObject taskObject in activeGuidedTaskObjects)
        {
            if (taskObject == null)
            {
                continue;
            }

            InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
            if (tracker != null)
            {
                tracker.RefreshCurrentHeldState();
            }

            bool currentlyHeld = tracker != null && tracker.isCurrentlyHeld;
            if (requirePriorUse && !currentlyHeld && !WasTaskObjectUsed(taskObject))
            {
                continue;
            }

            // The tracker is attached to the actual rigid/grabbable object and
            // remains authoritative after release. Falling back to a parent scene
            // object's unchanged pose can prevent a valid Lake landing from
            // completing when the grabbable lives on a child object.
            Vector3 objectPosition = tracker != null ? tracker.transform.position : taskObject.transform.position;
            foreach (GameObject target in activeGuidedTaskTargets)
            {
                if (target == null)
                {
                    continue;
                }

                if ((objectPosition - GetTargetCompletionPoint(target)).sqrMagnitude <= sqrRadius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsPlayerAndUsedObjectNearTarget(float radius)
    {
        Transform player = ResolvePlayerTransform();
        if (player == null)
        {
            return false;
        }

        float sqrRadius = radius * radius;
        foreach (GameObject target in activeGuidedTaskTargets)
        {
            if (target == null)
            {
                continue;
            }

            Vector3 targetPoint = GetTargetCompletionPoint(target);
            Vector3 playerPosition = player.position;
            playerPosition.y = targetPoint.y;
            if ((playerPosition - targetPoint).sqrMagnitude > sqrRadius)
            {
                continue;
            }

            foreach (GameObject taskObject in activeGuidedTaskObjects)
            {
                if (taskObject == null)
                {
                    continue;
                }

                InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
                if (tracker != null)
                {
                    tracker.RefreshCurrentHeldState();
                }

                bool currentlyHeld = tracker != null && tracker.isCurrentlyHeld;
                if (!currentlyHeld && !WasTaskObjectUsed(taskObject))
                {
                    continue;
                }

                // A participant may pick up the task object before the delayed
                // avatar briefing makes the marker visible. Current selection is
                // authoritative and must not require releasing and grabbing again.
                // Both the participant and the carried/used object must actually
                // enter the highlighted area; merely keeping it somewhere near
                // the participant must not clear the task.
                Transform carriedTransform = tracker != null ? tracker.transform : taskObject.transform;
                Vector3 objectPosition = carriedTransform.position;
                objectPosition.y = targetPoint.y;
                if ((objectPosition - targetPoint).sqrMagnitude <= sqrRadius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool AnyTaskObjectUsedOnce()
    {
        foreach (GameObject taskObject in activeGuidedTaskObjects)
        {
            if (HasRecordedTaskObjectUse(taskObject))
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetTargetCompletionPoint(GameObject target)
    {
        Bounds bounds;
        if (TryGetRendererBounds(target, out bounds))
        {
            if (activeGuidedTaskSpec != null && activeGuidedTaskSpec.MarkerPlacement == TaskMarkerPlacement.DoorSurface)
            {
                return GetDoorSurfaceMarkerPosition(target, bounds);
            }

            return bounds.center;
        }

        return target.transform.position;
    }

    private bool HasRecordedTaskObjectUse(GameObject taskObject)
    {
        InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
        return tracker != null && (tracker.isUsed || tracker.wasGrabbedByController || tracker.wasCollisionUsed);
    }

    private bool WasTaskObjectUsed(GameObject taskObject)
    {
        InteractionTracker tracker = ResolveTaskObjectTracker(taskObject);
        if (tracker == null)
        {
            return false;
        }

        bool selectedAfterActivation = tracker.LastControllerGrabUtc != DateTime.MinValue &&
            tracker.LastControllerGrabUtc >= guidedTaskActivatedAtUtc;
        bool controllerContactAfterActivation = tracker.LastCollisionUtc != DateTime.MinValue &&
            tracker.LastCollisionUtc >= guidedTaskActivatedAtUtc &&
            IsControllerInteractionSource(tracker.LastCollisionSourceName);
        return selectedAfterActivation || controllerContactAfterActivation;
    }

    private static bool IsControllerInteractionSource(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return false;
        }

        return sourceName.IndexOf("ControllerGrabLocation", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sourceName.IndexOf("HandGrab", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sourceName.IndexOf("HandAnchor", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sourceName.IndexOf("ControllerInteractor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static InteractionTracker ResolveTaskObjectTracker(GameObject taskObject)
    {
        if (taskObject == null)
        {
            return null;
        }

        InteractionTracker tracker = taskObject.GetComponent<InteractionTracker>();
        return tracker != null ? tracker : taskObject.GetComponentInChildren<InteractionTracker>();
    }

    private void CompleteGuidedTask(string completionSource)
    {
        guidedTaskCompleted = true;
        guidedTaskActive = false;
        string sceneName = SceneManager.GetActiveScene().name;
        ClearGuidedTaskHighlights();
        Debug.Log("[VRME] Guided task completed. scene=" + sceneName + ", source=" + completionSource);
        Debug.Log("[VRME] Conversation and scene interaction remain active until the participant reaches Exit and presses B.");
        _ = SendStageCompleteAsync(sceneName, completionSource);
    }

    private void ClearGuidedTaskHighlights()
    {
        foreach (GameObject taskObject in activeGuidedTaskObjects)
        {
            if (taskObject == null)
            {
                continue;
            }

            Outline outline = taskObject.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
        }

        foreach (GameObject taskTarget in activeGuidedTaskTargets)
        {
            if (taskTarget == null)
            {
                continue;
            }

            Outline outline = taskTarget.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
        }

        foreach (GameObject marker in activeGuidedTaskMarkers)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }

        activeGuidedTaskMarkers.Clear();
        if (string.Equals(SceneManager.GetActiveScene().name, "Lake", StringComparison.OrdinalIgnoreCase))
        {
            SetLakeThrowTargetVisible(false);
        }
        Debug.Log("[VRME] Guided task highlights cleared. scene=" + SceneManager.GetActiveScene().name);
    }

    private void SetLakeThrowTargetVisible(bool visible)
    {
        Vector3 targetCenter = Vector3.zero;
        float completionRadius = 1.35f;
        if (activeGuidedTaskTargets.Count > 0 && activeGuidedTaskTargets[0] != null)
        {
            targetCenter = GetTargetCompletionPoint(activeGuidedTaskTargets[0]);
        }
        if (activeGuidedTaskSpec != null)
        {
            completionRadius = activeGuidedTaskSpec.CompletionRadius;
        }

        LakeThrowTargetVisualizer[] visualizers = FindObjectsByType<LakeThrowTargetVisualizer>(FindObjectsSortMode.None);
        foreach (LakeThrowTargetVisualizer visualizer in visualizers)
        {
            if (visualizer != null)
            {
                visualizer.SetTaskHighlightVisible(visible, targetCenter, completionRadius);
            }
        }
    }

    private void HideGuidedTaskSurveyIfNeeded(string sceneName)
    {
        TeleportationEventListener[] listeners = FindObjectsByType<TeleportationEventListener>(FindObjectsSortMode.None);
        if (listeners == null || listeners.Length == 0)
        {
            Debug.LogWarning("[VRME] No TeleportationEventListener found while hiding guided task survey. scene=" + sceneName);
            return;
        }

        foreach (TeleportationEventListener listener in listeners)
        {
            if (listener != null)
            {
                listener.HideQuestionnaireForGuidedTask();
            }
        }

        Debug.Log("[VRME] Guided task survey hidden until completion. scene=" + sceneName + ", listeners=" + listeners.Length);
    }

    private void TriggerGuidedTaskSurveyIfAvailable()
    {
        TeleportationEventListener[] listeners = FindObjectsByType<TeleportationEventListener>(FindObjectsSortMode.None);
        if (listeners == null || listeners.Length == 0)
        {
            Debug.LogWarning("[VRME] No TeleportationEventListener found for guided task survey completion.");
            return;
        }

        foreach (TeleportationEventListener listener in listeners)
        {
            if (listener == null)
            {
                continue;
            }

            listener.TriggerQuestionnaireFromGuidedTask();
            Debug.Log("[VRME] Triggered SAM via guided task completion.");
            return;
        }
    }

    private Material CreateTaskHighlightMaterial(Color highlightColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = "VRME_TaskHighlight_Material";
        material.color = new Color(highlightColor.r, highlightColor.g, highlightColor.b, 0.55f);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", highlightColor * 1.6f);
        }

        return material;
    }

    private sealed class SceneTaskHighlightSpec
    {
        public readonly string[] ObjectNames;
        public readonly string[] TargetNames;
        public readonly string TargetLabel;
        public readonly GuidedTaskCompletionMode CompletionMode;
        public readonly TaskMarkerPlacement MarkerPlacement;
        public readonly float MarkerRadius;
        public readonly float CompletionRadius;
        public readonly bool UseRuntimeTargetFromPlayer;
        public readonly float RuntimeTargetForwardDistance;

        public SceneTaskHighlightSpec(
            string[] objectNames,
            string[] targetNames,
            string targetLabel,
            GuidedTaskCompletionMode completionMode,
            TaskMarkerPlacement markerPlacement,
            float markerRadius,
            float completionRadius,
            bool useRuntimeTargetFromPlayer = false,
            float runtimeTargetForwardDistance = 4f)
        {
            ObjectNames = objectNames ?? Array.Empty<string>();
            TargetNames = targetNames ?? Array.Empty<string>();
            TargetLabel = targetLabel ?? "";
            CompletionMode = completionMode;
            MarkerPlacement = markerPlacement;
            MarkerRadius = markerRadius;
            CompletionRadius = completionRadius;
            UseRuntimeTargetFromPlayer = useRuntimeTargetFromPlayer;
            RuntimeTargetForwardDistance = runtimeTargetForwardDistance;
        }
    }

    private enum GuidedTaskCompletionMode
    {
        None,
        ObjectUsedOnce,
        ObjectNearTarget,
        PlayerAndObjectNearTarget,
        DogFetchReturned,
        ElephantFed,
        TutorialControlledDialogue
    }

    private enum TutorialControlStage
    {
        Inactive,
        AwaitingInitialDescription,
        AwaitingFirstHeldDescription,
        AwaitingFirstShapeDescription,
        AwaitingFirstActionChoice,
        AwaitingFirstInteraction,
        AwaitingSecondHeldDescription,
        Complete
    }

    private enum TaskMarkerPlacement
    {
        Ground,
        ObjectCenter,
        DoorSurface
    }

    private sealed class PendingAudioTurn
    {
        public readonly byte[] WavBytes;
        public readonly string TurnContext;

        public PendingAudioTurn(byte[] wavBytes, string turnContext)
        {
            WavBytes = wavBytes;
            TurnContext = turnContext;
        }
    }

    private async Task<bool> SendTextPromptAsync(string textPrompt, string sourceLabel = "text_prompt")
    {
        if (isSending)
        {
            Debug.LogWarning("[VRME] Text prompt skipped because another send is active. source=" + sourceLabel);
            return false;
        }

        isSending = true;
        try
        {
            if (isReceivingBackendProactiveIntro)
            {
                cancellation?.Cancel();
                ResetWebSocket();
                isReceivingBackendProactiveIntro = false;
            }

            await ConnectAsync();
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                Debug.LogWarning("[VRME] WebSocket is not open.");
                return false;
            }

            await SendConfigAsync();
            await SendTurnContextAsync();
            byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(textPrompt);
            await websocket.SendAsync(
                new ArraySegment<byte>(textBytes),
                WebSocketMessageType.Text,
                true,
                cancellation.Token);
            Debug.Log("[VRME] Sent text prompt. source=" + sourceLabel + ", chars=" + textPrompt.Length);

            Task<bool> receiveTask = ReceiveReplyAsync();
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(Mathf.Max(5f, textPromptReplyTimeoutSeconds)));
            Task completedTask = await Task.WhenAny(receiveTask, timeoutTask);
            if (completedTask != receiveTask)
            {
                Debug.LogWarning("[VRME] Text prompt reply timed out. source=" + sourceLabel + ", timeoutSeconds=" + textPromptReplyTimeoutSeconds);
                ResetWebSocket();
                return false;
            }

            bool receivedReply = await receiveTask;
            if (!receivedReply)
            {
                Debug.LogWarning("[VRME] Text prompt reached a stale closed WebSocket. The caller will reconnect and retry. source=" + sourceLabel);
            }
            return receivedReply;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Text prompt failed: " + ex.Message);
            ResetWebSocket();
            return false;
        }
        finally
        {
            isSending = false;
        }
    }

    private async Task SendStageProgressAsync(string sceneName, string objectContextName)
    {
        string prompt =
            "[SYSTEM_STAGE_PROGRESS]\n" +
            "Scene: " + sceneName + "\n" +
            "Object: " + objectContextName + "\n" +
            "Say only one short spoken sentence acknowledging the participant now has this object, then give " +
            "the exact next grounded step for this scene. Do not ask a question.\n" +
            "[/SYSTEM_STAGE_PROGRESS]";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!isSending)
            {
                bool sent = await SendTextPromptAsync(prompt, "stage_progress");
                if (sent)
                {
                    Debug.Log("[VRME] Stage-progress trigger sent. scene=" + sceneName + ", object=" + objectContextName);
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(0.5));
        }

        Debug.LogWarning("[VRME] Stage-progress trigger gave up after retries. scene=" + sceneName);
    }

    private async Task SendStageCompleteAsync(string sceneName, string completionSource)
    {
        string prompt =
            "[SYSTEM_STAGE_COMPLETE]\n" +
            "Scene: " + sceneName + "\n" +
            "Source: " + completionSource + "\n" +
            "Say only one short spoken sentence confirming the guided interaction is complete, then invite " +
            "optional free exploration. Do not ask a question.\n" +
            "[/SYSTEM_STAGE_COMPLETE]";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!isSending)
            {
                bool sent = await SendTextPromptAsync(prompt, "stage_complete");
                if (sent)
                {
                    Debug.Log("[VRME] Stage-complete trigger sent. scene=" + sceneName + ", source=" + completionSource);
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(0.5));
        }

        Debug.LogWarning("[VRME] Stage-complete trigger gave up after retries. scene=" + sceneName);
    }

    private async Task SendTutorialStagePromptAsync(string stageName)
    {
        string prompt =
            "[SYSTEM_TUTORIAL_STAGE]\n" +
            "Stage: " + stageName + "\n" +
            "Say only the fixed warm tutorial instruction for this stage.\n" +
            "[/SYSTEM_TUTORIAL_STAGE]";

        // audio_stream_end means the backend has finished sending PCM, not that
        // Unity has finished playing the buffered samples. Wait for both so this
        // automatic Tutorial transition cannot cut off the preceding reply.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!isSending && !IsReplyPlaybackActive())
            {
                await Task.Delay(TimeSpan.FromSeconds(0.45));
                if (!isSending && !IsReplyPlaybackActive())
                {
                    bool sent = await SendTextPromptAsync(prompt, "tutorial_stage");
                    if (sent)
                    {
                        Debug.Log("[VRME] Tutorial stage prompt sent after prior playback finished. stage=" + stageName);
                        return;
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(0.25));
        }

        Debug.LogWarning("[VRME] Tutorial stage prompt gave up after retries. stage=" + stageName);
    }

    private bool IsReplyPlaybackActive()
    {
        return (streamingPlayer != null && streamingPlayer.IsActive) ||
            (audioSource != null && audioSource.isPlaying);
    }

    private async Task SendAudioAsync(
        byte[] wavBytes,
        string capturedTurnContext,
        bool retryAfterStaleClose = true)
    {
        if (isSending)
        {
            pendingAudioTurns.Enqueue(new PendingAudioTurn(wavBytes, capturedTurnContext));
            Debug.Log("[VRME] Queued voice turn while previous reply is still active. pending=" + pendingAudioTurns.Count);
            return;
        }

        isSending = true;
        bool retryCurrentTurn = false;
        try
        {
            if (isReceivingBackendProactiveIntro)
            {
                cancellation?.Cancel();
                ResetWebSocket();
                isReceivingBackendProactiveIntro = false;
            }

            await ConnectAsync();
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                Debug.LogWarning("[VRME] WebSocket is not open.");
                return;
            }

            // The audio_turn payload below owns the only live context for this
            // User Trigger press. Do not also send scene-wide historical state.
            await SendConfigAsync(includeSceneRuntimeContext: false);

            string turnContext = capturedTurnContext ?? BuildTurnContextString();
            string wavBase64 = Convert.ToBase64String(wavBytes);
            string json = BuildTurnContextJson("audio_turn", turnContext).TrimEnd('}') +
                ",\"audioFormat\":\"wav\"" +
                ",\"audioBase64\":\"" + wavBase64 + "\"}";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);
            await websocket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Text,
                true,
                cancellation.Token);
            LogTurnContextSent(turnContext, isolatedUserTriggerContext: true);
            Debug.Log("[VRME] Sent audio_turn wav bytes: " + wavBytes.Length + ", base64Chars=" + wavBase64.Length);

            bool receivedReply = await ReceiveReplyAsync();
            if (!receivedReply && retryAfterStaleClose)
            {
                retryCurrentTurn = true;
                Debug.LogWarning("[VRME] Voice turn reached a stale closed WebSocket; reconnecting and retrying once.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Send failed: " + ex.Message);
            ResetWebSocket();
            if (retryAfterStaleClose)
            {
                retryCurrentTurn = true;
                Debug.LogWarning("[VRME] Voice transport failed before a reply; reconnecting and retrying once.");
            }
        }
        finally
        {
            isSending = false;
            if (retryCurrentTurn)
            {
                _ = SendAudioAsync(wavBytes, capturedTurnContext, false);
            }
            else if (pendingAudioTurns.TryDequeue(out PendingAudioTurn nextTurn))
            {
                Debug.Log("[VRME] Sending queued voice turn. remaining=" + pendingAudioTurns.Count);
                _ = SendAudioAsync(nextTurn.WavBytes, nextTurn.TurnContext);
            }
        }
    }

    private string BuildSceneContext()
    {
        if (!includeInteractionContext)
        {
            return "";
        }

        Transform player = ResolvePlayerTransform();
        Vector3 playerPosition = player != null ? player.position : Vector3.zero;
        InteractionTracker[] trackers = ResolveInteractionTrackers();

        using (var writer = new StringWriter())
        {
            var currentHeldObjects = new List<string>();
            writer.WriteLine("[STATIC_SCENE_DESCRIPTION]");
            writer.WriteLine(GetEffectiveScenePrompt());
            writer.WriteLine("[/STATIC_SCENE_DESCRIPTION]");
            writer.WriteLine("[INTERACTABLE_OBJECT_STATES]");
            writer.WriteLine("scene=" + SceneManager.GetActiveScene().name);
            if (trackers.Length == 0)
            {
                writer.WriteLine("none");
            }
            else
            {
                foreach (InteractionTracker tracker in trackers)
                {
                    if (tracker == null || !tracker.isActiveAndEnabled || IsSystemInteractionTracker(tracker)) continue;

                    tracker.RefreshCurrentHeldState();
                    Vector3 objectPosition = tracker.transform.position;
                    if (player != null && Vector3.Distance(playerPosition, objectPosition) > maxContextObjectDistance)
                    {
                        continue;
                    }

                    if (tracker.isCurrentlyHeld)
                    {
                        currentHeldObjects.Add(CleanSpokenObjectName(tracker.ContextName));
                    }

                    writer.WriteLine("- " + CleanSpokenObjectName(tracker.ContextName) + " | interactionState={" + tracker.InteractionStateSummary + "}");
                }
            }
            writer.WriteLine("[/INTERACTABLE_OBJECT_STATES]");

            writer.WriteLine("[CURRENT_HELD_OBJECTS]");
            if (currentHeldObjects.Count == 0)
            {
                writer.WriteLine("none");
            }
            else
            {
                foreach (string heldObject in currentHeldObjects)
                {
                    writer.WriteLine("- " + heldObject);
                }
            }
            writer.WriteLine("authority=Only objects listed above are currently in the participant's hand. Objects with everControllerGrabbed=true but currentHeld=false were handled before but are not currently held.");
            writer.WriteLine("[/CURRENT_HELD_OBJECTS]");

            writer.WriteLine("[INTERACTION_EVENTS]");
            writer.WriteLine(InteractionTracker.GetRecentEventsText(maxRecentInteractionEvents));
            writer.WriteLine("[/INTERACTION_EVENTS]");

            writer.WriteLine(BuildGuidedTaskContext(player, playerPosition));
            return writer.ToString();
        }
    }

    private string BuildCurrentTurnInteractionContext()
    {
        Transform player = ResolvePlayerTransform();
        Vector3 playerPosition = player != null ? player.position : Vector3.zero;
        InteractionTracker[] trackers = ResolveInteractionTrackers();
        var currentHeldObjects = new List<string>();
        var nearbyInteractables = new List<InteractionTracker>();

        foreach (InteractionTracker tracker in trackers)
        {
            if (tracker == null || !tracker.isActiveAndEnabled || tracker.attentionOnlyTarget || IsSystemInteractionTracker(tracker))
            {
                continue;
            }

            tracker.RefreshCurrentHeldState();
            if (player == null || Vector3.Distance(playerPosition, tracker.transform.position) <= maxContextObjectDistance)
            {
                nearbyInteractables.Add(tracker);
            }
            if (tracker.isCurrentlyHeld)
            {
                currentHeldObjects.Add(CleanSpokenObjectName(tracker.ContextName));
            }
        }

        if (player != null)
        {
            nearbyInteractables.Sort((left, right) =>
                Vector3.Distance(playerPosition, left.transform.position).CompareTo(
                    Vector3.Distance(playerPosition, right.transform.position)));
        }

        using (var writer = new StringWriter())
        {
            writer.WriteLine("[STATIC_SCENE_DESCRIPTION]");
            writer.WriteLine(GetEffectiveScenePrompt());
            writer.WriteLine("[/STATIC_SCENE_DESCRIPTION]");
            writer.WriteLine("[NEARBY_INTERACTABLE_OBJECTS]");
            if (nearbyInteractables.Count == 0)
            {
                writer.WriteLine("none");
            }
            else
            {
                int nearbyCount = Mathf.Min(Mathf.Max(1, maxDiscoveredSceneObjects), nearbyInteractables.Count);
                for (int i = 0; i < nearbyCount; i++)
                {
                    InteractionTracker tracker = nearbyInteractables[i];
                    string location = FormatGuidedTaskLocation(
                        CleanSpokenObjectName(tracker.ContextName),
                        tracker.transform.position,
                        player,
                        playerPosition);
                    writer.WriteLine("- " + location + " | currentHeld=" + tracker.isCurrentlyHeld);
                }
            }
            writer.WriteLine("authority=fresh availability, not proof of use");
            writer.WriteLine("[/NEARBY_INTERACTABLE_OBJECTS]");
            writer.WriteLine(BuildNearbyStaticSceneryContext(player, playerPosition));
            writer.WriteLine("[CURRENT_HELD_OBJECTS]");
            if (currentHeldObjects.Count == 0)
            {
                writer.WriteLine("none");
            }
            else
            {
                currentHeldObjects.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string heldObject in currentHeldObjects)
                {
                    writer.WriteLine("- " + heldObject);
                }
            }
            writer.WriteLine("authority=definitive list of what's held right now");
            writer.WriteLine("[/CURRENT_HELD_OBJECTS]");

            writer.WriteLine("[RECENT_CONTROLLER_EVENTS]");
            writer.WriteLine(InteractionTracker.GetRecentEventsTextSince(voiceTurnStartedAtUtc, maxRecentInteractionEvents));
            writer.WriteLine("authority=this-turn-only; CURRENT_HELD_OBJECTS overrides for current holding");
            writer.WriteLine("[/RECENT_CONTROLLER_EVENTS]");

            writer.WriteLine(BuildGuidedTaskContext(player, playerPosition));
            return writer.ToString().TrimEnd();
        }
    }

    // Surfaces non-interactive decorative props (renderer-only, never tracked as
    // an InteractionTracker) so the avatar has grounded, real conversation
    // material beyond the fixed interactable/task objects. Because every name
    // here is a real object with a real position, this widens what the avatar
    // can talk about without opening the door to inventing objects — the
    // accompanying instruction line still forbids suggesting interaction with
    // them, so it does not create new (imagined) affordances.
    private string BuildNearbyStaticSceneryContext(Transform player, Vector3 playerPosition)
    {
        using (var writer = new StringWriter())
        {
            writer.WriteLine("[NEARBY_STATIC_SCENERY]");
            writer.WriteLine("authority=decorative only, not interactable; never suggest grabbing/using/functions for these, name only for variety");

            if (!enableNearbyStaticSceneryContext)
            {
                writer.WriteLine("none (disabled)");
                writer.WriteLine("[/NEARBY_STATIC_SCENERY]");
                return writer.ToString().TrimEnd();
            }

            var seenObjects = new HashSet<GameObject>();
            var candidates = new List<KeyValuePair<GameObject, float>>();
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (Renderer objectRenderer in renderers)
            {
                if (objectRenderer == null || !objectRenderer.enabled || !objectRenderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                GameObject candidate = objectRenderer.gameObject;
                Rigidbody attachedBody = candidate.GetComponentInParent<Rigidbody>();
                if (attachedBody != null)
                {
                    candidate = attachedBody.gameObject;
                }

                if (candidate == null || seenObjects.Contains(candidate) ||
                    candidate.GetComponent<InteractionTracker>() != null ||
                    candidate.GetComponentInParent<InteractionTracker>() != null ||
                    HasInteractionLikeComponent(candidate))
                {
                    continue;
                }

                string candidateName = GetContextObjectName(candidate);
                if (IsIgnoredStaticSceneryObject(candidateName))
                {
                    continue;
                }

                float distance = player != null ? Vector3.Distance(playerPosition, candidate.transform.position) : 0f;
                if (player != null && distance > maxStaticSceneryDistance)
                {
                    continue;
                }

                seenObjects.Add(candidate);
                candidates.Add(new KeyValuePair<GameObject, float>(candidate, distance));
            }

            if (candidates.Count == 0)
            {
                writer.WriteLine("none");
            }
            else
            {
                candidates.Sort((left, right) => left.Value.CompareTo(right.Value));
                // Multi-mesh props (a lamp's cover/frame/mount as separate
                // renderers, for example) otherwise list the same physical
                // object several times. Collapse anything within ~0.4m of an
                // already-accepted spot down to a single mention.
                var acceptedPositions = new List<Vector3>();
                var accepted = new List<GameObject>();
                foreach (KeyValuePair<GameObject, float> candidatePair in candidates)
                {
                    if (accepted.Count >= Mathf.Max(1, maxStaticSceneryObjects))
                    {
                        break;
                    }

                    Vector3 position = candidatePair.Key.transform.position;
                    bool tooCloseToAccepted = false;
                    foreach (Vector3 acceptedPosition in acceptedPositions)
                    {
                        if (Vector3.Distance(position, acceptedPosition) < 0.4f)
                        {
                            tooCloseToAccepted = true;
                            break;
                        }
                    }

                    if (tooCloseToAccepted)
                    {
                        continue;
                    }

                    acceptedPositions.Add(position);
                    accepted.Add(candidatePair.Key);
                }

                foreach (GameObject candidate in accepted)
                {
                    string displayName = CleanSpokenObjectName(GetContextObjectName(candidate));
                    writer.WriteLine("- " + FormatGuidedTaskLocation(displayName, candidate.transform.position, player, playerPosition));
                }
            }

            writer.WriteLine("[/NEARBY_STATIC_SCENERY]");
            return writer.ToString().TrimEnd();
        }
    }

    // Static scenery reuses IsIgnoredContextObject's system-object denylist and
    // additionally screens out ground/wall/tiling clutter (for example the
    // repeated "Stone Floor prefab (225)/street" instances also seen in
    // NEARBY_INTERACTABLE_OBJECTS) so the list stays a short, curated set of
    // genuinely nameable props rather than a dump of level geometry.
    private static readonly string[] StaticSceneryIgnoredNameFragments =
    {
        "floor", "ground", "terrain", "street", "wall", "ceiling", "roof",
        "collider", "trigger", "spawn", "waypoint", "boundary", "occlusion",
        "navmesh", "reset point", "teleport", "exit", "safe position",
        "highlight", "outline", "marker", "target", "prefab (", "skybox",
        "post process", "volume", "reflection probe", "light probe", "poke",
        // The scripted antagonist (and his weapon) already has dedicated,
        // carefully worded handling via STATIC_SCENE_DESCRIPTION and
        // AtticSoundController; he must never be surfaced as casual
        // "conversation variety" filler alongside a lamp or a table.
        "gun", "beretta", "man_0", "man_1", "screaming"
    };

    // Strips trailing asset-catalog codes (for example "Table IKEA LERHAMN
    // N070421" -> "Table IKEA LERHAMN", "Radiator N110514 (1)" -> "Radiator"),
    // trailing instance numbers with no space (for example "Slant1" -> "Slant",
    // "Shield01" -> "Shield"), and splits camelCase compound asset names (for
    // example "LampFrame" -> "Lamp Frame"). Applied to every raw Unity object
    // name before it enters any context block, so the avatar never reads an
    // internal object/instance name out loud verbatim, whether it's decorative
    // scenery or a task-relevant interactable.
    private static string CleanSpokenObjectName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        string cleaned = System.Text.RegularExpressions.Regex.Replace(
            rawName,
            @"\s+[A-Za-z]{0,3}\d{4,}(\s*\(\d+\))?$",
            "");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*\(\d+\)$", "").Trim();
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?<=[A-Za-z])\d+$", "").Trim();
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "(?<=[a-z])(?=[A-Z])", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? rawName.Trim() : cleaned;
    }

    private static bool IsIgnoredStaticSceneryObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || IsIgnoredContextObject(objectName))
        {
            return true;
        }

        foreach (string fragment in StaticSceneryIgnoredNameFragments)
        {
            if (objectName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private string BuildGuidedTaskContext(Transform player, Vector3 playerPosition)
    {
        using (var writer = new StringWriter())
        {
            writer.WriteLine("[GUIDED_TASK_STATE]");
            string sceneName = SceneManager.GetActiveScene().name;
            writer.WriteLine("scene=" + sceneName);
            writer.WriteLine("status=" + (guidedTaskCompleted ? "completed" : (guidedTaskActive ? "active" : (taskHighlightsActivated ? "highlighted_without_completion_check" : "not_started"))));
            writer.WriteLine("objective=" + GetSceneTaskObjective(sceneName));
            writer.WriteLine("instruction_for_avatar=Background only, nothing is marked/highlighted; never say 'highlighted' or announce unprompted, only name if participant's words/actions lead there or as last-resort nudge. Only grounded actions (grab/carry/move/throw/place/bring toward target). No invented functions (open/read/activate/switch/transform) without proof. No invented objects/coordinates; use relative directions.");

            if (activeGuidedTaskObjects.Count == 0 && activeGuidedTaskTargets.Count == 0)
            {
                SceneTaskHighlightSpec plannedSpec = GetSceneTaskHighlightSpec(sceneName);
                if (plannedSpec == null)
                {
                    writer.WriteLine("highlightedObjects=none");
                    writer.WriteLine("highlightedTargets=none");
                    writer.WriteLine("[/GUIDED_TASK_STATE]");
                    return writer.ToString();
                }

                writer.WriteLine("plannedHighlights=intentionally_never_shown_this_is_quiet_background_knowledge_only");
                writer.WriteLine("plannedHighlightedObjectHints=" + string.Join(", ", Array.ConvertAll(plannedSpec.ObjectNames, CleanSpokenObjectName)));
                writer.WriteLine("plannedHighlightedTargetHints=" + (plannedSpec.TargetNames.Length > 0 ? string.Join(", ", Array.ConvertAll(plannedSpec.TargetNames, CleanSpokenObjectName)) : plannedSpec.TargetLabel));
                writer.WriteLine("plannedCompletionMode=" + plannedSpec.CompletionMode);
                writer.WriteLine("plannedHighlightedObjects:");
                var plannedObjects = new HashSet<GameObject>();
                foreach (string objectName in plannedSpec.ObjectNames)
                {
                    GameObject plannedObject = FindSceneObjectByNameHint(objectName);
                    if (plannedObject == null || plannedObjects.Contains(plannedObject))
                    {
                        continue;
                    }

                    plannedObjects.Add(plannedObject);
                    writer.WriteLine("- " + FormatGuidedTaskLocation(CleanSpokenObjectName(plannedObject.name), GetGuidedObjectPoint(plannedObject), player, playerPosition));
                }

                writer.WriteLine("plannedHighlightedTargets:");
                var plannedTargets = new HashSet<GameObject>();
                foreach (string targetName in plannedSpec.TargetNames)
                {
                    GameObject plannedTarget = FindSceneObjectByNameHint(targetName);
                    if (plannedTarget == null || plannedTargets.Contains(plannedTarget))
                    {
                        continue;
                    }

                    plannedTargets.Add(plannedTarget);
                    string label = !string.IsNullOrWhiteSpace(plannedSpec.TargetLabel) ? plannedSpec.TargetLabel : CleanSpokenObjectName(plannedTarget.name);
                    writer.WriteLine("- " + FormatGuidedTaskLocation(label, GetTargetCompletionPoint(plannedTarget), player, playerPosition));
                }

                if (plannedSpec.UseRuntimeTargetFromPlayer)
                {
                    writer.WriteLine("- " + plannedSpec.TargetLabel + " | will be placed on walkable ground ahead of the participant when the briefing begins");
                }

                AppendAnimalTaskState(writer, sceneName);
                writer.WriteLine("[/GUIDED_TASK_STATE]");
                return writer.ToString();
            }

            writer.WriteLine("highlightedObjects:");
            foreach (GameObject taskObject in activeGuidedTaskObjects)
            {
                if (taskObject == null)
                {
                    continue;
                }

                writer.WriteLine("- " + FormatGuidedTaskLocation(CleanSpokenObjectName(taskObject.name), GetGuidedObjectPoint(taskObject), player, playerPosition));
            }

            writer.WriteLine("highlightedTargets:");
            foreach (GameObject target in activeGuidedTaskTargets)
            {
                if (target == null)
                {
                    continue;
                }

                string label = activeGuidedTaskSpec != null && !string.IsNullOrWhiteSpace(activeGuidedTaskSpec.TargetLabel)
                    ? activeGuidedTaskSpec.TargetLabel
                    : CleanSpokenObjectName(target.name);
                writer.WriteLine("- " + FormatGuidedTaskLocation(label, GetTargetCompletionPoint(target), player, playerPosition));
            }

            AppendAnimalTaskState(writer, sceneName);
            writer.WriteLine("[/GUIDED_TASK_STATE]");
            return writer.ToString();
        }
    }

    private void AppendAnimalTaskState(StringWriter writer, string sceneName)
    {
        if (string.Equals(sceneName, "Puppies", StringComparison.OrdinalIgnoreCase))
        {
            bool caught = false;
            bool carrying = false;
            bool returned = false;
            string fetchState = "none";
            DogMotion2[] dogs = FindObjectsByType<DogMotion2>(FindObjectsSortMode.None);
            foreach (DogMotion2 dog in dogs)
            {
                if (dog == null || !dog.isActiveAndEnabled)
                {
                    continue;
                }

                caught = caught || dog.HasCaughtFetchedBall;
                carrying = carrying || dog.IsCarryingFetchedBall;
                returned = returned || dog.HasReturnedBallToPlayer;
                if (dog.IsCarryingFetchedBall || dog.HasReturnedBallToPlayer || fetchState == "none")
                {
                    fetchState = dog.FetchState;
                }
            }

            writer.WriteLine("dogFetchState=" + fetchState);
            writer.WriteLine("dogCaughtBall=" + caught);
            writer.WriteLine("dogCurrentlyCarryingBall=" + carrying);
            writer.WriteLine("dogReturnedBallToPlayer=" + returned);
            writer.WriteLine("animalStateAuthority=These values come directly from DogMotion2 and may be used as evidence for catching, carrying, or returning the tennis ball.");
        }
        else if (string.Equals(sceneName, "Elephant", StringComparison.OrdinalIgnoreCase))
        {
            bool eating = false;
            bool fed = false;
            FeedElephants[] feeders = FindObjectsByType<FeedElephants>(FindObjectsSortMode.None);
            foreach (FeedElephants feeder in feeders)
            {
                if (feeder == null || !feeder.isActiveAndEnabled)
                {
                    continue;
                }

                eating = eating || feeder.IsEating;
                fed = fed || feeder.HasFedOnce;
            }

            writer.WriteLine("elephantCurrentlyEating=" + eating);
            writer.WriteLine("elephantReceivedBanana=" + fed);
            writer.WriteLine("animalStateAuthority=These values come directly from FeedElephants and may be used as evidence that the elephant received the banana.");
        }
        else if (string.Equals(sceneName, "Attic", StringComparison.OrdinalIgnoreCase))
        {
            bool inFinalPosition = false;
            GunmanPresenceTracker[] gunmen = FindObjectsByType<GunmanPresenceTracker>(FindObjectsSortMode.None);
            foreach (GunmanPresenceTracker gunman in gunmen)
            {
                if (gunman == null || !gunman.isActiveAndEnabled)
                {
                    continue;
                }

                inFinalPosition = inFinalPosition || gunman.InFinalPosition;
            }

            writer.WriteLine("gunmanInFinalPosition=" + inFinalPosition);
            writer.WriteLine("animalStateAuthority=gunmanInFinalPosition comes directly from GunmanPresenceTracker; false means he is still walking in and has not arrived yet, true means he has reached his fixed spot and stays there facing the participant for the rest of the scene.");

            bool doorOpen = false;
            ExitDoorStateTracker[] doors = FindObjectsByType<ExitDoorStateTracker>(FindObjectsSortMode.None);
            foreach (ExitDoorStateTracker door in doors)
            {
                if (door == null || !door.isActiveAndEnabled)
                {
                    continue;
                }

                doorOpen = doorOpen || door.IsOpen;
            }

            writer.WriteLine("exitDoorOpen=" + doorOpen);
            writer.WriteLine("doorStateAuthority=exitDoorOpen comes directly from ExitDoorStateTracker; false means the exit door is still closed, true means it has been opened.");
        }
    }

    private Vector3 GetGuidedObjectPoint(GameObject taskObject)
    {
        Bounds bounds;
        if (taskObject != null && TryGetRendererBounds(taskObject, out bounds))
        {
            return bounds.center;
        }

        return taskObject != null ? taskObject.transform.position : Vector3.zero;
    }

    private string FormatGuidedTaskLocation(string label, Vector3 worldPoint, Transform player, Vector3 playerPosition)
    {
        if (player == null)
        {
            return label + " | position=" + FormatVector(worldPoint);
        }

        float distance = Vector3.Distance(playerPosition, worldPoint);
        return label + " | distance=" + distance.ToString("0.0") + "m | direction=" + GetRelativeDirectionLabel(player, worldPoint);
    }

    private static string GetRelativeDirectionLabel(Transform player, Vector3 worldPoint)
    {
        Vector3 toTarget = worldPoint - player.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.06f)
        {
            return "at the participant";
        }

        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        float angle = Vector3.SignedAngle(forward, toTarget.normalized, Vector3.up);
        float absAngle = Mathf.Abs(angle);
        string side = angle >= 0f ? "right" : "left";

        if (absAngle <= 20f)
        {
            return "straight ahead";
        }
        if (absAngle <= 65f)
        {
            return "front-" + side;
        }
        if (absAngle <= 115f)
        {
            return side;
        }
        if (absAngle <= 155f)
        {
            return "behind-" + side;
        }

        return "behind";
    }

    private string BuildClosestInteractionObjectSummary(InteractionTracker[] trackers, List<SceneContextObject> discoveredObjects, Vector3 playerPosition, bool hasPlayerPosition)
    {
        var summaries = new List<string>();
        if (hasPlayerPosition && trackers != null)
        {
            foreach (InteractionTracker tracker in trackers)
            {
                if (tracker == null || !tracker.isActiveAndEnabled || IsIgnoredContextObject(tracker.ContextName))
                {
                    continue;
                }

                float distance = Vector3.Distance(playerPosition, tracker.transform.position);
                if (distance <= maxContextObjectDistance)
                {
                    summaries.Add(tracker.ContextName + " " + distance.ToString("0.0") + "m interactionState={" + tracker.InteractionStateSummary + "}");
                }
            }
        }

        foreach (SceneContextObject sceneObject in discoveredObjects)
        {
            if (!sceneObject.hasDistance || sceneObject.distanceToPlayer <= maxContextObjectDistance)
            {
                summaries.Add(sceneObject.name + (sceneObject.hasDistance ? " " + sceneObject.distanceToPlayer.ToString("0.0") + "m" : ""));
            }
        }

        if (summaries.Count == 0)
        {
            return "none nearby";
        }

        int count = Mathf.Min(6, summaries.Count);
        return string.Join("; ", summaries.GetRange(0, count));
    }

    private static string OneLineContext(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        return value.Replace("\r", " ").Replace("\n", " | ");
    }

    private string BuildScriptedInteractionReferenceContext(Vector3 playerPosition, bool hasPlayerPosition)
    {
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        var seenObjects = new HashSet<GameObject>();
        using (var writer = new StringWriter())
        {
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                if (!IsInteractionLikeTypeName(type.Name))
                {
                    continue;
                }

                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo field in fields)
                {
                    if (ShouldSkipInteractionReferenceField(field.Name))
                    {
                        continue;
                    }

                    object value = field.GetValue(behaviour);
                    GameObject referencedObject = value as GameObject;
                    if (referencedObject == null)
                    {
                        Transform fieldTransform = value as Transform;
                        referencedObject = fieldTransform != null ? fieldTransform.gameObject : null;
                    }

                    if (referencedObject == null || !referencedObject.activeInHierarchy || seenObjects.Contains(referencedObject))
                    {
                        continue;
                    }

                    seenObjects.Add(referencedObject);
                    Transform referencedTransform = referencedObject.transform;
                    string distance = hasPlayerPosition
                        ? Vector3.Distance(playerPosition, referencedTransform.position).ToString("0.00")
                        : "unknown";

                    writer.WriteLine("- " + type.Name + "." + field.Name + "=" + referencedObject.name +
                        " | activeSelf=" + referencedObject.activeSelf +
                        " | position=" + FormatVector(referencedTransform.position) +
                        " | distanceToPlayer=" + distance);
                }
            }

            string result = writer.ToString().TrimEnd();
            return string.IsNullOrWhiteSpace(result) ? "none" : result;
        }
    }

    private static bool ShouldSkipInteractionReferenceField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        string[] skipped = { "anchor", "holdpoint", "audio", "sound", "clip", "animator", "arrow" };
        foreach (string skippedName in skipped)
        {
            if (fieldName.IndexOf(skippedName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private string BuildFlashlightContext(Vector3 playerPosition, bool hasPlayerPosition)
    {
        FlashlightOnGrab[] flashlights = FindObjectsByType<FlashlightOnGrab>(FindObjectsSortMode.None);
        if (flashlights.Length == 0)
        {
            return "none";
        }

        using (var writer = new StringWriter())
        {
            foreach (FlashlightOnGrab flashlightController in flashlights)
            {
                if (flashlightController == null || !flashlightController.isActiveAndEnabled)
                {
                    continue;
                }

                GameObject lightObject = flashlightController.flashlight;
                Vector3 controllerPosition = flashlightController.transform.position;
                string controllerDistance = hasPlayerPosition
                    ? Vector3.Distance(playerPosition, controllerPosition).ToString("0.00")
                    : "unknown";

                writer.Write("- flashlightController=" + flashlightController.gameObject.name);
                writer.Write(" | exists=True");
                writer.Write(" | controllerPosition=" + FormatVector(controllerPosition));
                writer.Write(" | controllerDistanceToPlayer=" + controllerDistance);

                if (lightObject != null)
                {
                    Transform lightTransform = lightObject.transform;
                    string lightDistance = hasPlayerPosition
                        ? Vector3.Distance(playerPosition, lightTransform.position).ToString("0.00")
                        : "unknown";

                    writer.Write(" | beamObject=" + lightObject.name);
                    writer.Write(" | beamActiveSelf=" + lightObject.activeSelf);
                    writer.Write(" | beamActiveInHierarchy=" + lightObject.activeInHierarchy);
                    writer.Write(" | beamPosition=" + FormatVector(lightTransform.position));
                    writer.Write(" | beamDistanceToPlayer=" + lightDistance);
                }
                else
                {
                    writer.Write(" | beamObject=missing");
                }

                writer.WriteLine();
            }

            string result = writer.ToString().TrimEnd();
            return string.IsNullOrWhiteSpace(result) ? "none" : result;
        }
    }

    private List<SceneContextObject> DiscoverSceneContextObjects(InteractionTracker[] trackers, Vector3 playerPosition, bool hasPlayerPosition)
    {
        var results = new List<SceneContextObject>();
        if (!autoDiscoverSceneObjectsForContext || maxDiscoveredSceneObjects <= 0)
        {
            return results;
        }

        string[] hints = ParseSceneObjectHints();
        if (hints.Length == 0)
        {
            return results;
        }

        var trackerObjects = new HashSet<GameObject>();
        if (trackers != null)
        {
            foreach (InteractionTracker tracker in trackers)
            {
                if (tracker != null)
                {
                    trackerObjects.Add(tracker.gameObject);
                }
            }
        }

        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        var seenObjects = new HashSet<GameObject>();
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
            if (candidate == null || trackerObjects.Contains(candidate) || seenObjects.Contains(candidate))
            {
                continue;
            }

            string candidateName = GetContextObjectName(candidate);
            if (IsIgnoredContextObject(candidateName) || !IsLikelyContextObject(candidate, candidateName, hints))
            {
                continue;
            }

            Vector3 position = collider.bounds.center;
            float distance = hasPlayerPosition ? Vector3.Distance(playerPosition, position) : 0f;
            if (hasPlayerPosition && distance > maxContextObjectDistance)
            {
                continue;
            }

            seenObjects.Add(candidate);
            results.Add(new SceneContextObject
            {
                name = candidateName,
                position = position,
                hasDistance = hasPlayerPosition,
                distanceToPlayer = distance
            });

            if (results.Count >= maxDiscoveredSceneObjects)
            {
                break;
            }
        }

        return results;
    }

    private int AttachTrackersToSceneObjects()
    {
        string[] hints = ParseSceneObjectHints();
        if (hints.Length == 0)
        {
            return 0;
        }

        int attachedCount = 0;
        var seenObjects = new HashSet<GameObject>();
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                continue;
            }

            GameObject candidate = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
            if (candidate == null || seenObjects.Contains(candidate) || candidate.GetComponent<InteractionTracker>() != null)
            {
                continue;
            }

            string candidateName = GetContextObjectName(candidate);
            if (IsIgnoredContextObject(candidateName) || !IsLikelyContextObject(candidate, candidateName, hints))
            {
                continue;
            }

            InteractionTracker tracker = candidate.AddComponent<InteractionTracker>();
            tracker.displayName = candidateName;
            seenObjects.Add(candidate);
            attachedCount++;
        }

        return attachedCount;
    }

    private int AttachAvatarAttentionTrackers()
    {
        string[] hints = ParseCommaSeparatedHints(avatarObjectNameHints);
        if (hints.Length == 0)
        {
            return 0;
        }

        int attachedCount = 0;
        var seenObjects = new HashSet<GameObject>();
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || !behaviour.isActiveAndEnabled)
            {
                continue;
            }

            GameObject candidate = ResolveAvatarAttentionRoot(behaviour.gameObject, hints);
            if (candidate == null || seenObjects.Contains(candidate))
            {
                continue;
            }

            seenObjects.Add(candidate);
            InteractionTracker tracker = candidate.GetComponent<InteractionTracker>();
            if (tracker == null)
            {
                tracker = candidate.AddComponent<InteractionTracker>();
                attachedCount++;
            }

            tracker.displayName = "Avatar/Social Agent";
            tracker.attentionOnlyTarget = true;
            tracker.trackTriggerCollisions = false;
            EnsureAvatarAttentionCollider(candidate);
        }

        return attachedCount;
    }

    private void NormalizeLakeInteractionTrackers()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, "Lake", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        InteractionTracker[] trackers = FindObjectsByType<InteractionTracker>(FindObjectsSortMode.None);
        int airplanes = 0;
        int stones = 0;
        int excluded = 0;
        foreach (InteractionTracker tracker in trackers)
        {
            if (tracker == null)
            {
                continue;
            }

            if (IsInNamedHierarchy(tracker.transform, "Airplane1", "Airplane2"))
            {
                tracker.displayName = "paper airplane";
                airplanes++;
                continue;
            }

            if (IsInNamedHierarchy(tracker.transform, "Stone1", "Stone2"))
            {
                tracker.displayName = "stone";
                stones++;
                continue;
            }

            // Lake dialogue is intentionally restricted to its two task-object
            // categories. Water/splash/telescope helpers must never become a
            // spoken gaze or interaction target.
            if (!tracker.attentionOnlyTarget && !IsSystemInteractionTracker(tracker))
            {
                tracker.enabled = false;
                excluded++;
            }
        }

        Debug.Log("[VRME] Lake interaction context normalized. airplaneTrackers=" + airplanes +
            ", stoneTrackers=" + stones + ", excludedTrackers=" + excluded + ".");
    }

    // Runtime-attached (like AttachTrackersToSceneObjects above) instead of placed in
    // the scene file, so the intruder's prefab instance in Attic.unity never needs to
    // be hand-edited when this tracking behavior changes.
    private void AttachGunmanPresenceTracker()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, "Attic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // AtticSoundController keeps the intruder GameObject SetActive(false) for the
        // first several seconds of the scene (see additionalManDelaySeconds), so this
        // must search inactive objects too or it will run before he ever exists to find.
        foreach (Animator candidate in FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate == null || candidate.runtimeAnimatorController == null ||
                !string.Equals(candidate.runtimeAnimatorController.name, "GunManController", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate.GetComponent<GunmanPresenceTracker>() == null)
            {
                candidate.gameObject.AddComponent<GunmanPresenceTracker>();
                Debug.Log("[VRME] Attached GunmanPresenceTracker to " + candidate.gameObject.name + ".");
            }

            return;
        }

        Debug.LogWarning("[VRME] Attic scene has no Animator using GunManController; gunman presence cannot be tracked.");
    }

    // OpenDoorOnCharacter already holds a direct reference to the door's Animator
    // (set true via its "openDoor" bool once the intruder walks through the trigger),
    // so reuse that wiring instead of guessing which Animator belongs to the door.
    private void AttachExitDoorStateTracker()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, "Attic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (OpenDoorOnCharacter doorTrigger in FindObjectsByType<OpenDoorOnCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Animator doorAnimator = doorTrigger != null ? doorTrigger.myAnimatorController : null;
            if (doorAnimator == null)
            {
                continue;
            }

            if (doorAnimator.GetComponent<ExitDoorStateTracker>() == null)
            {
                doorAnimator.gameObject.AddComponent<ExitDoorStateTracker>();
                Debug.Log("[VRME] Attached ExitDoorStateTracker to " + doorAnimator.gameObject.name + ".");
            }

            return;
        }

        Debug.LogWarning("[VRME] Attic scene has no OpenDoorOnCharacter with a wired Animator; exit door state cannot be tracked.");
    }

    private static bool IsInNamedHierarchy(Transform item, params string[] objectNames)
    {
        Transform current = item;
        while (current != null)
        {
            foreach (string objectName in objectNames)
            {
                if (string.Equals(current.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }

    private GameObject ResolveAvatarAttentionRoot(GameObject sourceObject, string[] hints)
    {
        if (sourceObject == null)
        {
            return null;
        }

        if (IsAvatarAttentionCandidate(sourceObject, hints))
        {
            return sourceObject;
        }

        Transform current = sourceObject.transform.parent;
        while (current != null)
        {
            if (IsAvatarAttentionCandidate(current.gameObject, hints))
            {
                return current.gameObject;
            }

            current = current.parent;
        }

        return null;
    }

    private bool IsAvatarAttentionCandidate(GameObject candidate, string[] hints)
    {
        if (candidate == null || !candidate.activeInHierarchy)
        {
            return false;
        }

        // The conversational avatar prefab owns this client component, so this is
        // a stronger identity signal than broad names such as "Man" or "Character".
        if (candidate.GetComponent<VrmeAtticClient>() != null)
        {
            return true;
        }

        if (candidate.GetComponent<CameraPoseSender>() != null)
        {
            return false;
        }

        string candidateName = candidate.name;
        if (MatchesSceneObjectHint(candidateName, hints))
        {
            return true;
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

    private void EnsureAvatarAttentionCollider(GameObject avatarRoot)
    {
        if (avatarRoot == null || avatarRoot.GetComponent<Collider>() != null)
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

    private string[] ParseSceneObjectHints()
    {
        return ParseCommaSeparatedHints(sceneObjectNameHints);
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

    private static bool MatchesSceneObjectHint(string objectName, string[] hints)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        foreach (string hint in hints)
        {
            if (objectName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyContextObject(GameObject candidate, string candidateName, string[] hints)
    {
        if (MatchesSceneObjectHint(candidateName, hints))
        {
            return true;
        }

        if (HasInteractionLikeComponent(candidate))
        {
            return true;
        }

        Transform parent = candidate != null ? candidate.transform.parent : null;
        return parent != null && MatchesSceneObjectHint(parent.name, hints);
    }

    private static bool HasInteractionLikeComponent(GameObject candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Component[] components = candidate.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null)
            {
                continue;
            }

            string typeName = component.GetType().Name;
            if (IsInteractionLikeTypeName(typeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInteractionLikeTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        string[] interactionTypes =
        {
            "GrabInteractable",
            "PokeInteractable",
            "RayInteractable",
            "Pointable",
            "FlashlightOnGrab",
            "HapticOnGrab",
            "Banana",
            "FeedElephants",
            "ElephantAnimationTrigger",
            "HapticOnTouchElephant",
            "HapticsOnTouchDog",
            "DogMotion",
            "TennisBall",
            "TelescopeTrigger",
            "UIButtonTrigger",
            "DoorInteraction",
            "DoorEvents",
            "AtticSoundController",
            "OpenDoorOnCharacter",
            "WaterSplashEffect"
        };

        foreach (string interactionType in interactionTypes)
        {
            if (typeName.IndexOf(interactionType, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIgnoredContextObject(string objectName)
    {
        string[] ignored = { "controller", "camera", "player", "avatar", "canvas", "ovr", "xr", "tracking", "runtime marker", "slider", "survey", "sam", "ui" };
        foreach (string ignoredName in ignored)
        {
            if (objectName.IndexOf(ignoredName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetContextObjectName(GameObject candidate)
    {
        if (candidate == null)
        {
            return "unknown";
        }

        Transform parent = candidate.transform.parent;
        if (parent != null && MatchesParentContextName(parent.name))
        {
            return parent.name + "/" + candidate.name;
        }

        return candidate.name;
    }

    private static bool MatchesParentContextName(string parentName)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            return false;
        }

        string[] usefulParentNames = { "HandTorch", "Flashlight", "Torch", "Airplane", "Plane", "Stone", "Rock", "Banana", "Fruit", "Elephant", "Dog", "Puppy", "Ball", "Baseball", "Book", "Cup", "Telescope", "ManScreaming", "Man", "Gun", "Door", "Sign", "Bar", "Handle", "Light", "Exit" };
        foreach (string name in usefulParentNames)
        {
            if (parentName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private InteractionTracker[] ResolveInteractionTrackers()
    {
        if (keyInteractables != null && keyInteractables.Length > 0)
        {
            return keyInteractables;
        }

        if (!autoFindInteractionTrackers)
        {
            return Array.Empty<InteractionTracker>();
        }

        return FindObjectsByType<InteractionTracker>(FindObjectsSortMode.None);
    }

    private Transform ResolvePlayerTransform()
    {
        if (playerTransform != null)
        {
            return playerTransform;
        }

        GameObject centerEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
        if (centerEyeAnchor != null && centerEyeAnchor.activeInHierarchy)
        {
            return centerEyeAnchor.transform;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform;
        }

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera != null && camera.isActiveAndEnabled)
            {
                return camera.transform;
            }
        }

        return null;
    }

    private static string PreviewForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        string oneLine = value.Replace("\r", " ").Replace("\n", " | ");
        if (oneLine.Length <= maxLength)
        {
            return oneLine;
        }

        return oneLine.Substring(0, maxLength) + "...";
    }

    private struct SceneContextObject
    {
        public string name;
        public Vector3 position;
        public bool hasDistance;
        public float distanceToPlayer;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.00},{value.y:0.00},{value.z:0.00})";
    }

    private async Task<bool> ReceiveReplyAsync()
    {
        byte[] buffer = new byte[8192];
        bool receivingStream = false;
        bool holdingCurrentStream = false;

        while (true)
        {
            WebSocketReceiveResult result;
            byte[] payload;
            using (var stream = new MemoryStream())
            {
                do
                {
                    result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.LogWarning("[VRME] WebSocket closed after this reply. The next voice turn will reconnect automatically.");
                        ResetWebSocket();
                        return false;
                    }

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                payload = stream.ToArray();
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string text = System.Text.Encoding.UTF8.GetString(payload);
                if (text.Contains("\"condition_config\""))
                {
                    string backendCondition = ExtractJsonString(text, "avatarCondition", "observer");
                    mainThreadActions.Enqueue(() => ApplyBackendCondition(backendCondition));
                    Debug.Log("[VRME] Backend condition received: " + backendCondition);
                    continue;
                }

                if (string.Equals(
                    ExtractJsonString(text, "type", ""),
                    "tutorial_control",
                    StringComparison.OrdinalIgnoreCase))
                {
                    string actionName = ExtractJsonString(text, "action", "");
                    string objectKey = ExtractJsonString(text, "objectKey", "");
                    mainThreadActions.Enqueue(() => ApplyTutorialControlEvent(actionName, objectKey));
                    Debug.Log("[VRME] Tutorial control event received. action=" + actionName + ", object=" + objectKey);
                    continue;
                }

                if (text.Contains("\"tutorial_exit_highlight\""))
                {
                    mainThreadActions.Enqueue(ShowTutorialExitHighlight);
                    Debug.Log("[VRME] Tutorial Exit highlight requested by the backend.");
                    continue;
                }

                if (text.Contains("\"avatar_reply_start\""))
                {
                    string source = ExtractJsonString(text, "source", "");
                    Debug.Log("[VRME] Avatar reply started. source=" + source);
                    continue;
                }

                if (text.Contains("\"audio_stream_start\""))
                {
                    receivingStream = true;
                    string source = ExtractJsonString(text, "source", "");
                    holdingCurrentStream = autoIntroPlaybackHeld &&
                        string.Equals(source, "auto_briefing", StringComparison.OrdinalIgnoreCase);
                    int streamSampleRate = ExtractJsonInt(text, "sampleRate", 24000);
                    int streamChannels = ExtractJsonInt(text, "channels", 1);
                    EnqueueOrHoldPlaybackAction(() => BeginPcmStream(streamSampleRate, streamChannels), holdingCurrentStream);
                    if (ShouldActivateHighlightsForReplySource(source))
                    {
                        EnqueueOrHoldPlaybackAction(() => ActivateSceneTaskHighlightsFromAudioStart(source), holdingCurrentStream);
                    }
                    Debug.Log("[VRME] Audio stream started. source=" + source + ", sampleRate=" + streamSampleRate + ", channels=" + streamChannels +
                        (holdingCurrentStream ? " (playback held for gaze gate)" : ""));
                    continue;
                }

                if (text.Contains("\"audio_stream_end\""))
                {
                    EnqueueOrHoldPlaybackAction(EndPcmStream, holdingCurrentStream);
                    Debug.Log("[VRME] Audio stream ended. WebSocket remains available for the next voice turn if the server keeps it open.");
                    return true;
                }

                if (string.Equals(
                    ExtractJsonString(text, "type", ""),
                    "voice_turn_end",
                    StringComparison.OrdinalIgnoreCase))
                {
                    string reason = ExtractJsonString(text, "reason", "unspecified");
                    Debug.LogWarning("[VRME] Voice turn completed without a spoken reply. reason=" + reason);
                    return true;
                }

                if (text.Contains("\"audio_stream_error\""))
                {
                    Debug.LogWarning("[VRME] Audio stream error: " + text);
                    return false;
                }

                Debug.Log("[VRME] Text reply: " + text);
                return true;
            }

            if (receivingStream)
            {
                byte[] pcmChunk = payload;
                EnqueueOrHoldPlaybackAction(() => AppendPcmStreamChunk(pcmChunk), holdingCurrentStream);
                continue;
            }

            mainThreadActions.Enqueue(() => PlayWav(payload));
            return true;
        }
    }

    private static int ExtractJsonInt(string json, string key, int fallback)
    {
        string marker = "\"" + key + "\":";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return fallback;
        }

        start += marker.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start]))
        {
            start++;
        }

        int end = start;
        while (end < json.Length && char.IsDigit(json[end]))
        {
            end++;
        }

        return int.TryParse(json.Substring(start, end - start), out int value) ? value : fallback;
    }

    private static string ExtractJsonString(string json, string key, string fallback)
    {
        string marker = "\"" + key + "\":";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return fallback;
        }

        start += marker.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start]))
        {
            start++;
        }

        if (start >= json.Length || json[start] != '"')
        {
            return fallback;
        }

        start++;
        int end = json.IndexOf('"', start);
        return end > start ? json.Substring(start, end - start) : fallback;
    }

    private bool ShouldActivateHighlightsForReplySource(string source)
    {
        return enableTaskHighlights &&
            !taskHighlightsActivated &&
            IsTaskHighlightRevealAllowedForCurrentScene() &&
            (string.Equals(source, "proactive_guide", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(source, "auto_briefing", StringComparison.OrdinalIgnoreCase));
    }

    // Task discovery is meant to stay conversational and incidental (see
    // GetSceneDescription / BuildAutoTaskBriefingPrompt): the avatar must never
    // single out which object matters. Ordinary object affordance is already
    // handled separately by the distance-based HightlightObject outline, so this
    // legacy "point at the task object" reveal is disabled for every scene.
    private static bool IsTaskHighlightRevealAllowedForCurrentScene()
    {
        // Formal scenes preserve free discovery. The tutorial deliberately
        // reveals its practice objects after the avatar introduces the task.
        return IsTutorialScene();
    }

    private void ActivateSceneTaskHighlightsFromAudioStart(string source)
    {
        if (!ShouldActivateHighlightsForReplySource(source))
        {
            return;
        }

        autoIntroSent = true;
        Debug.Log("[VRME] Activating task highlights at avatar audio start. source=" + source);
        ActivateSceneTaskHighlights();
    }

    private void ApplyBackendCondition(string backendCondition)
    {
        string normalized = NormalizeAvatarConditionForBackend(backendCondition);
        PlayerData.avatarCondition = normalized;
        AvatarDominanceBehaviorController behavior = GetComponent<AvatarDominanceBehaviorController>();
        if (behavior != null)
        {
            behavior.SetConditionFromBackend(normalized);
        }
    }

    private static string NormalizeAvatarConditionForBackend(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
        if (normalized == "warm" || normalized == "warm_avatar" || normalized == "warm-companion" ||
            normalized == "warm_companion" || normalized == "supportive" || normalized == "supportive_companion" ||
            normalized == "companion" || normalized == "emotional")
        {
            return "warm";
        }

        if (normalized == "cold" || normalized == "cold_avatar" || normalized == "cold-observer" ||
            normalized == "cold_observer" || normalized == "distant")
        {
            return "cold";
        }

        if (normalized == "dominant" || normalized == "dominance" || normalized == "dom")
        {
            return "dom";
        }

        if (normalized == "submissive" || normalized == "submission" || normalized == "sub")
        {
            return "sub";
        }

        if (normalized == "observer" || normalized == "detached" || normalized == "detached_observer" ||
            normalized == "baseline")
        {
            return "observer";
        }

        if (normalized == "context_aware" || normalized == "context-aware" ||
            normalized == "context_aware_guide" || normalized == "context-aware-guide" ||
            normalized == "context" || normalized == "guide" || normalized == "informational" ||
            normalized == "appraisal")
        {
            return "context_aware";
        }

        return "backend";
    }

    private void BeginPcmStream(int streamSampleRate, int channels)
    {
        streamingPlayer.Begin(audioSource, streamSampleRate, channels, replyGain, streamMaxSeconds, streamStartBufferSeconds);
    }

    private void AppendPcmStreamChunk(byte[] pcmBytes)
    {
        streamingPlayer.AppendPcm16(pcmBytes);
    }

    private void EndPcmStream()
    {
        streamingPlayer.Finish();
    }

    private void PlayWav(byte[] wavBytes)
    {
        try
        {
            streamingPlayer?.Reset();
            AudioClip clip = DecodeWav(wavBytes, replyGain);
            audioSource.clip = clip;
            audioSource.loop = false;
            audioSource.Play();
            Debug.Log("[VRME] Playing reply wav bytes: " + wavBytes.Length);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Could not play reply wav. " + ex.Message);
        }
    }

    private sealed class StreamingPcmPlayer
    {
        private readonly object lockObject = new object();
        private readonly System.Collections.Generic.Queue<float> samples = new System.Collections.Generic.Queue<float>();
        private AudioSource source;
        private bool active;
        private bool finished;
        private bool playbackStarted;
        private float finishedAt = -1f;
        private float drainedAt = -1f;
        private bool hasPendingByte;
        private byte pendingByte;
        private int channels = 1;
        private float gain = 1f;
        private float startBufferSeconds = 0.18f;
        private int sampleRate = 24000;

        public bool IsActive => active;

        public void Begin(AudioSource audioSource, int hz, int channelCount, float outputGain, int maxSeconds, float bufferSeconds)
        {
            Reset();
            source = audioSource;
            sampleRate = Mathf.Max(8000, hz);
            channels = Mathf.Max(1, channelCount);
            gain = outputGain;
            startBufferSeconds = Mathf.Max(0.05f, bufferSeconds);
            int lengthSamples = sampleRate * Mathf.Max(5, maxSeconds);
            AudioClip clip = AudioClip.Create("VRME Streaming Reply", lengthSamples, channels, sampleRate, true, OnAudioRead);
            source.clip = clip;
            source.loop = true;
            active = true;
            finished = false;
            playbackStarted = false;
            Debug.Log("[VRME] Streaming PCM clip ready.");
        }

        public void AppendPcm16(byte[] pcmBytes)
        {
            if (!active || pcmBytes == null || pcmBytes.Length == 0)
            {
                return;
            }

            lock (lockObject)
            {
                int i = 0;
                if (hasPendingByte)
                {
                    short value = (short)(pendingByte | (pcmBytes[0] << 8));
                    samples.Enqueue(Mathf.Clamp((value / 32768f) * gain, -1f, 1f));
                    hasPendingByte = false;
                    i = 1;
                }

                for (; i + 1 < pcmBytes.Length; i += 2)
                {
                    short value = BitConverter.ToInt16(pcmBytes, i);
                    samples.Enqueue(Mathf.Clamp((value / 32768f) * gain, -1f, 1f));
                }

                if (i < pcmBytes.Length)
                {
                    pendingByte = pcmBytes[i];
                    hasPendingByte = true;
                }
            }
        }

        public void Finish()
        {
            finished = true;
            finishedAt = Time.realtimeSinceStartup;
            drainedAt = -1f;
        }

        public void Update()
        {
            if (!active || source == null)
            {
                return;
            }

            if (!playbackStarted)
            {
                int queued;
                lock (lockObject)
                {
                    queued = samples.Count;
                }

                if (queued >= Mathf.CeilToInt(sampleRate * channels * startBufferSeconds) || finished)
                {
                    source.Play();
                    playbackStarted = true;
                    Debug.Log("[VRME] Streaming PCM playback started. queuedSamples=" + queued);
                }
            }

            if (finished && playbackStarted)
            {
                int queued;
                lock (lockObject)
                {
                    queued = samples.Count;
                }

                if (queued == 0)
                {
                    if (drainedAt < 0f)
                    {
                        drainedAt = Time.realtimeSinceStartup;
                    }

                    if (Time.realtimeSinceStartup - drainedAt > 0.75f)
                    {
                        Reset();
                        Debug.Log("[VRME] Streaming PCM playback finished.");
                    }
                }
                else
                {
                    drainedAt = -1f;
                }
            }
        }

        public void Reset()
        {
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }

            lock (lockObject)
            {
                samples.Clear();
            }

            if (source != null)
            {
                source.loop = false;
                source.clip = null;
            }

            active = false;
            finished = false;
            playbackStarted = false;
            finishedAt = -1f;
            drainedAt = -1f;
            hasPendingByte = false;
            pendingByte = 0;
        }

        private void OnAudioRead(float[] data)
        {
            lock (lockObject)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = samples.Count > 0 ? samples.Dequeue() : 0f;
                }
            }
        }
    }

    private static byte[] EncodeWav(float[] samples, int channels, int hz)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            int byteCount = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + byteCount);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(hz);
            writer.Write(hz * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(byteCount);

            foreach (float sample in samples)
            {
                short value = (short)Mathf.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
                writer.Write(value);
            }

            return stream.ToArray();
        }
    }

    private static AudioClip DecodeWav(byte[] wavBytes, float gain)
    {
        using (var reader = new BinaryReader(new MemoryStream(wavBytes)))
        {
            string riff = new string(reader.ReadChars(4));
            if (riff != "RIFF")
            {
                throw new InvalidDataException("Missing RIFF header.");
            }

            reader.ReadInt32();
            string wave = new string(reader.ReadChars(4));
            if (wave != "WAVE")
            {
                throw new InvalidDataException("Missing WAVE header.");
            }

            short channels = 1;
            int hz = 16000;
            short bitsPerSample = 16;
            byte[] data = null;

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                string chunkId = new string(reader.ReadChars(4));
                int chunkSize = reader.ReadInt32();

                if (chunkId == "fmt ")
                {
                    short audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    hz = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();
                    if (chunkSize > 16)
                    {
                        reader.ReadBytes(chunkSize - 16);
                    }
                    if (audioFormat != 1 || bitsPerSample != 16)
                    {
                        throw new InvalidDataException("Only 16-bit PCM wav is supported.");
                    }
                }
                else if (chunkId == "data")
                {
                    data = reader.ReadBytes(chunkSize);
                    break;
                }
                else
                {
                    reader.ReadBytes(chunkSize);
                }
            }

            if (data == null)
            {
                throw new InvalidDataException("Missing wav data chunk.");
            }

            int sampleCount = data.Length / 2;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short value = BitConverter.ToInt16(data, i * 2);
                samples[i] = Mathf.Clamp((value / 32768f) * gain, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("VRME Reply", sampleCount / channels, channels, hz, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

    private static class PersistentWebSocket
    {
        private static readonly object StateGate = new object();
        private static readonly SemaphoreSlim ConnectGate = new SemaphoreSlim(1, 1);
        private static ClientWebSocket socket;
        private static CancellationTokenSource cancellationSource;
        private static string connectedUrl = "";

        public static ClientWebSocket Socket
        {
            get
            {
                lock (StateGate)
                {
                    return socket;
                }
            }
        }

        public static CancellationTokenSource Cancellation
        {
            get
            {
                lock (StateGate)
                {
                    return cancellationSource;
                }
            }
        }

        public static async Task ConnectAsync(string url, float timeoutSeconds)
        {
            await ConnectGate.WaitAsync();
            try
            {
                lock (StateGate)
                {
                    if (socket != null && socket.State == WebSocketState.Open &&
                        string.Equals(connectedUrl, url, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log("[VRME] Reusing persistent WebSocket for scene=" + SceneManager.GetActiveScene().name);
                        return;
                    }

                    DisposeLocked();
                    cancellationSource = new CancellationTokenSource();
                    socket = new ClientWebSocket();
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                    connectedUrl = url;
                }

                ClientWebSocket connectingSocket;
                CancellationToken persistentToken;
                lock (StateGate)
                {
                    connectingSocket = socket;
                    persistentToken = cancellationSource.Token;
                }

                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Mathf.Max(0.5f, timeoutSeconds))))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(persistentToken, timeout.Token))
                {
                    await connectingSocket.ConnectAsync(new Uri(url), linked.Token);
                }

                Debug.Log("[VRME] Opened persistent WebSocket to " + url);
            }
            catch
            {
                lock (StateGate)
                {
                    DisposeLocked();
                }
                throw;
            }
            finally
            {
                ConnectGate.Release();
            }
        }

        public static void Reset(ClientWebSocket expectedSocket)
        {
            lock (StateGate)
            {
                if (expectedSocket == null || socket != expectedSocket)
                {
                    return;
                }

                DisposeLocked();
            }
        }

        public static void Close()
        {
            lock (StateGate)
            {
                DisposeLocked();
            }
        }

        private static void DisposeLocked()
        {
            try
            {
                cancellationSource?.Cancel();
                socket?.Dispose();
            }
            catch
            {
                // Ignore cleanup races; the next voice turn can create a fresh connection.
            }
            finally
            {
                socket = null;
                cancellationSource?.Dispose();
                cancellationSource = null;
                connectedUrl = "";
            }
        }
    }

    private void OnApplicationQuit()
    {
        PersistentWebSocket.Close();
    }

    private void OnDestroy()
    {
        if (isRecording || (!string.IsNullOrWhiteSpace(activeMicrophoneDevice) &&
            Microphone.IsRecording(activeMicrophoneDevice)))
        {
            ResetMicrophoneCapture("scene client destroyed");
            CameraPoseSender.EndVoiceSampling();
        }

        if (recordingClip != null)
        {
            Destroy(recordingClip);
            recordingClip = null;
        }

        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();
        lifetimeCancellation = null;
        // Scene changes replace the avatar GameObject but deliberately keep the
        // shared WebSocket alive. The next scene sends a fresh config/prompt on
        // the same connection. Only application quit or a real socket failure
        // closes it.
        websocket = null;
        cancellation = null;
        Debug.Log("[VRME] Scene client destroyed; persistent WebSocket retained for the next scene.");
    }
}
