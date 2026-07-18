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
    public string sceneObjectNameHints = "HandTorch,Flashlight,Torch,Shield,Shield01,Airplane,Plane,Stone,Rock,Banana,Fruit,Elephant,Dog,Puppy,Ball,Baseball,Book,Cup,Telescope,ManScreaming,Man,Gun,Sign,Door,Handle,Bar,Key,Switch,Button,Lever,Panel,Light,Exit";
    [Tooltip("Comma-separated object/script name hints used to mark the conversational avatar as a social attention target.")]
    public string avatarObjectNameHints = "Rocketbox,ReadyPlayerMe,DigitalHuman,SocialAgent,CompanionAvatar";
    [Range(1, 40)] public int maxDiscoveredSceneObjects = 20;
    [Range(1f, 100f)] public float maxContextObjectDistance = 25f;
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
    [Range(0f, 60f)] public float autoIntroDelaySeconds = 10f;
    [Tooltip("Tutorial-only delay after the participant has completed the first UI.")]
    [Range(0f, 5f)] public float tutorialIntroDelayAfterFirstUi = 0.75f;
    [Range(5f, 120f)] public float textPromptReplyTimeoutSeconds = 60f;
    [TextArea(3, 8)] public string autoIntroPrompt =
        "Please give the participant a brief task briefing for the current VR scene. State the avatar's purpose and the single interaction task they should try before free exploration.";
    public bool enableTaskHighlights = true;
    [Tooltip("Send scene context with the initial config so backend proactive guidance can be context-aware without showing highlights early.")]
    public bool sendSceneContextWithConfig = true;
    public Color taskObjectHighlightColor = new Color(1f, 0.82f, 0.1f, 1f);
    public Color taskTargetHighlightColor = new Color(0.1f, 0.95f, 1f, 1f);
    [Range(1f, 10f)] public float taskObjectOutlineWidth = 6f;

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly ConcurrentQueue<byte[]> pendingAudioTurns = new ConcurrentQueue<byte[]>();
    private ClientWebSocket websocket;
    private CancellationTokenSource cancellation;
    private AudioSource audioSource;
    private StreamingPcmPlayer streamingPlayer;
    private AudioClip recordingClip;
    private bool isRecording;
    private DateTime voiceTurnStartedAtUtc = DateTime.MinValue;
    private bool isSending;
    private bool isReceivingBackendProactiveIntro;
    private bool recordKeyWasDown;
    private bool autoIntroSent;
    private bool taskHighlightsActivated;
    private bool guidedTaskActive;
    private bool guidedTaskCompleted;
    private CancellationTokenSource lifetimeCancellation;
    private SceneTaskHighlightSpec activeGuidedTaskSpec;
    private readonly List<GameObject> activeGuidedTaskObjects = new List<GameObject>();
    private readonly List<GameObject> activeGuidedTaskTargets = new List<GameObject>();
    private readonly List<GameObject> activeGuidedTaskMarkers = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPersistentConnectionForPlaySession()
    {
        PersistentWebSocket.Close();
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
        lifetimeCancellation = new CancellationTokenSource();
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
            ", sessionId=" + PlayerData.sessionId +
            ", avatarCondition=" + PlayerData.avatarCondition);

        if (autoAttachInteractionTrackersToSceneObjects)
        {
            int attachedCount = AttachTrackersToSceneObjects();
            Debug.Log("[VRME] Auto-attached InteractionTracker to " + attachedCount + " scene objects.");
        }

        NormalizeLakeInteractionTrackers();

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

        if (!useBackendProactiveIntro && enableTaskHighlights && string.Equals(SceneManager.GetActiveScene().name, "Tunnel", StringComparison.OrdinalIgnoreCase))
        {
            _ = ActivateSceneTaskHighlightsAfterDelayAsync(1f);
        }
    }

    private async Task ActivateSceneTaskHighlightsAfterDelayAsync(float delaySeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, delaySeconds)));
        if (isActiveAndEnabled && enableTaskHighlights && !taskHighlightsActivated)
        {
            ActivateSceneTaskHighlights();
        }
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
        CheckGuidedTaskCompletion();
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

    private async Task SendConfigAsync()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            return;
        }

        string sceneContext = sendSceneContextWithConfig ? BuildSceneContext() : (sendLiveContextOnlyForVoiceTurn ? "" : BuildSceneContext());
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

    private string BuildTurnContextString()
    {
        using (var writer = new StringWriter())
        {
            string currentHeldObjects = BuildCurrentHeldObjectsSummary();
            writer.WriteLine("[UNITY_CONTEXT_SUMMARY]");
            writer.WriteLine(CameraPoseSender.LatestVoiceAttentionSummary);
            writer.WriteLine("currentHeldObjects=" + currentHeldObjects);
            writer.WriteLine("authority=This short summary is generated at voice release and should be treated as the freshest Unity state for gaze attention and held objects.");
            writer.WriteLine("[/UNITY_CONTEXT_SUMMARY]");

            writer.WriteLine("[IMMEDIATE_UNITY_STATE]");
            writer.WriteLine(BuildImmediateUnityStateContext());
            writer.WriteLine("[/IMMEDIATE_UNITY_STATE]");

            writer.WriteLine("[LIVE_USER_OBSERVATIONS]");
            writer.WriteLine(CameraPoseSender.LatestVoiceRuntimeContextText);
            writer.WriteLine("[/LIVE_USER_OBSERVATIONS]");

            if (includeInteractionContext)
            {
                writer.WriteLine(BuildCurrentTurnInteractionContext());
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
                return "A VR interaction tutorial. The participant can practice grabbing and throwing the blue cubes, speak to the nearby avatar by holding the right-controller A button, and finish at the marked Exit.";
            case "lake":
                return "A lakeside VR scene. The guided interaction is to pick up either the highlighted airplane or the highlighted stone and throw it toward the highlighted target area by the lake.";
            case "attic":
                return "An attic VR scene. The guided interaction is to use the highlighted shield and move behind the highlighted safe position.";
            case "puppies":
                return "A VR scene with puppies. The guided interaction is to pick up the highlighted tennis ball and throw it toward the highlighted puppy.";
            case "solitaryconfinement":
                return "A solitary-confinement VR scene. The guided interaction is to use one of the highlighted movable cell objects and move or throw it toward the highlighted door target.";
            case "tunnel":
                return "A dark tunnel VR scene. The guided interaction is to take the highlighted flashlight and move with it toward the highlighted position in the tunnel.";
            case "elephant":
                return "A VR scene with an elephant. The guided interaction is to pick up the highlighted banana and throw it toward the highlighted elephant.";
            case "real":
                return "A mixed-reality transition scene used after the immersive VR scenes.";
            case "endscene":
                return "The experiment completion scene; no further guided object interaction is required.";
            default:
                return "VR scene named " + (string.IsNullOrWhiteSpace(sceneName) ? "unknown" : sceneName.Trim()) + ".";
        }
    }

    private void LogTurnContextSent(string turnContext)
    {
        if (CameraPoseSender.LatestRuntimeContextText.StartsWith("No CameraPoseSender", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[VRME] CameraPoseSender has not produced runtime context yet; sent immediate Unity head state fallback.");
        }

        Debug.Log("[VRME] Unity context summary: " + CameraPoseSender.LatestVoiceAttentionSummary +
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

        string identity = (tracker.ContextName + " " + tracker.gameObject.name).ToLowerInvariant();
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

        streamingPlayer?.Reset();
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        recordingClip = Microphone.Start(null, false, maxRecordSeconds, sampleRate);
        isRecording = true;
        voiceTurnStartedAtUtc = DateTime.UtcNow;
        CameraPoseSender.BeginVoiceSampling();
        Debug.Log("[VRME] Recording started. Release " + GetRecordInputLabel() + " to send." + (isSending ? " Current reply is still finishing; this turn will queue." : ""));
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

        int samplePosition = Microphone.GetPosition(null);
        Microphone.End(null);
        isRecording = false;
        CameraPoseSender.EndVoiceSampling();

        if (recordingClip == null || samplePosition <= 0)
        {
            Debug.LogWarning("[VRME] Empty recording.");
            return;
        }

        float[] samples = new float[samplePosition * recordingClip.channels];
        recordingClip.GetData(samples, 0);
        byte[] wavBytes = EncodeWav(samples, recordingClip.channels, sampleRate);
        await SendAudioAsync(wavBytes);
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

        float sceneDelay = isTutorial ? tutorialIntroDelayAfterFirstUi : autoIntroDelaySeconds;
        float delaySeconds = Mathf.Max(0f, sceneDelay + Mathf.Max(0f, fallbackGraceSeconds));
        Debug.Log("[VRME] Auto briefing fallback armed. delaySeconds=" + delaySeconds +
            ", backendProactive=" + useBackendProactiveIntro +
            ", scene=" + SceneManager.GetActiveScene().name);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        string introPrompt = BuildAutoTaskBriefingPrompt();
        if (autoIntroSent || !isActiveAndEnabled || string.IsNullOrWhiteSpace(introPrompt))
        {
            Debug.Log("[VRME] Auto briefing fallback skipped. sent=" + autoIntroSent + ", active=" + isActiveAndEnabled + ", hasPrompt=" + !string.IsNullOrWhiteSpace(introPrompt));
            return;
        }

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
                if (enableTaskHighlights && !taskHighlightsActivated)
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
                if (setupScreen != null && setupScreen.gameObject.activeInHierarchy)
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

    private string BuildAutoTaskBriefingPrompt()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string taskObjective = GetSceneTaskObjective(sceneName);
        if (string.IsNullOrWhiteSpace(taskObjective))
        {
            return autoIntroPrompt;
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
                return "Hold the right-controller A button to speak with the nearby avatar, then practice grabbing and throwing a blue cube. To finish, use the controller thumbstick to move to the marked Exit position; do not physically walk there.";
            case "puppies":
                return "Use the highlighted tennis ball and throw it toward the highlighted puppy.";
            case "elephant":
                return "Use the highlighted banana and throw it toward the highlighted elephant.";
            case "lake":
                return "Use either the highlighted airplane or the highlighted stone and throw it toward the highlighted lake target area.";
            case "solitaryconfinement":
                return "Use the highlighted prison object and move or throw it toward the highlighted target area.";
            case "tunnel":
                return "Use the highlighted flashlight and move toward the highlighted tunnel position.";
            case "attic":
                return "Use the highlighted shield and move behind the highlighted safe position.";
            default:
                return "";
        }
    }

    private void ActivateSceneTaskHighlights()
    {
        if (taskHighlightsActivated)
        {
            Debug.Log("[VRME] Task highlights already active. scene=" + SceneManager.GetActiveScene().name);
            return;
        }

        string sceneName = SceneManager.GetActiveScene().name;
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
        taskHighlightsActivated = true;

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

            AddTargetMarker(targetObject, spec);
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
            case "puppies":
                return new SceneTaskHighlightSpec(
                    new[] { "TennisBall", "BallPoint" },
                    new[] { "Dog", "Puppy" },
                    "Dog target",
                    GuidedTaskCompletionMode.DogFetchReturned,
                    TaskMarkerPlacement.Ground,
                    0.75f,
                    0.75f);
            case "elephant":
                return new SceneTaskHighlightSpec(
                    new[] { "banana", "Banana" },
                    new[] { "Elephant" },
                    "Elephant target",
                    GuidedTaskCompletionMode.ElephantFed,
                    TaskMarkerPlacement.Ground,
                    0.75f,
                    0.75f);
            case "lake":
                return new SceneTaskHighlightSpec(
                    new[] { "Airplane", "Stone" },
                    new[] { "Target" },
                    "Lake target",
                    GuidedTaskCompletionMode.ObjectNearTarget,
                    TaskMarkerPlacement.Ground,
                    2.25f,
                    2.4f);
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
                    0.55f,
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

        Vector3 candidate = basePosition + forward * Mathf.Max(0.5f, spec.RuntimeTargetForwardDistance);
        candidate = ProjectPointToWalkableGround(candidate, basePosition);

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

    private void AddTargetMarker(GameObject target, SceneTaskHighlightSpec spec)
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
            float markerY = hasRendererBounds ? bounds.min.y + 0.03f : target.transform.position.y + 0.06f;
            marker.transform.position = new Vector3(bounds.center.x, markerY, bounds.center.z);
            marker.transform.localScale = new Vector3(markerRadius, 0.035f, markerRadius);
        }

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            markerRenderer.material = CreateTaskHighlightMaterial();
        }

        GameObject lightObject = new GameObject(markerName + "_Light");
        lightObject.transform.SetParent(marker.transform, false);
        lightObject.transform.localPosition = Vector3.up * 0.5f;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = taskTargetHighlightColor;
        light.intensity = 1.3f;
        light.range = Mathf.Max(1.2f, markerRadius * 2.2f);
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
                if (HasAnyDogReturnedBall())
                {
                    CompleteGuidedTask("dog_fetch_returned_to_player");
                }
                break;
            case GuidedTaskCompletionMode.ElephantFed:
                if (HasAnyElephantBeenFed())
                {
                    CompleteGuidedTask("elephant_received_banana");
                }
                break;
        }
    }

    private bool HasAnyDogReturnedBall()
    {
        DogMotion2[] dogs = FindObjectsByType<DogMotion2>(FindObjectsSortMode.None);
        foreach (DogMotion2 dog in dogs)
        {
            if (dog != null && dog.isActiveAndEnabled && dog.HasReturnedBallToPlayer)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyElephantBeenFed()
    {
        FeedElephants[] feeders = FindObjectsByType<FeedElephants>(FindObjectsSortMode.None);
        foreach (FeedElephants feeder in feeders)
        {
            if (feeder != null && feeder.isActiveAndEnabled && feeder.HasFedOnce)
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
            if (taskObject == null || (requirePriorUse && !WasTaskObjectUsed(taskObject)))
            {
                continue;
            }

            Vector3 objectPosition = taskObject.transform.position;
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
                if (taskObject == null || !WasTaskObjectUsed(taskObject))
                {
                    continue;
                }

                if ((taskObject.transform.position - player.position).sqrMagnitude <= 1.6f * 1.6f)
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
        InteractionTracker tracker = taskObject != null ? taskObject.GetComponent<InteractionTracker>() : null;
        if (tracker == null && taskObject != null)
        {
            tracker = taskObject.GetComponentInChildren<InteractionTracker>();
        }

        return tracker != null && (tracker.isUsed || tracker.wasGrabbedByController || tracker.wasCollisionUsed);
    }

    private bool WasTaskObjectUsed(GameObject taskObject)
    {
        InteractionTracker tracker = taskObject.GetComponent<InteractionTracker>();
        if (tracker == null)
        {
            tracker = taskObject.GetComponentInChildren<InteractionTracker>();
        }

        return tracker == null || tracker.isUsed || tracker.wasGrabbedByController || tracker.wasCollisionUsed;
    }

    private void CompleteGuidedTask(string completionSource)
    {
        guidedTaskCompleted = true;
        guidedTaskActive = false;
        ClearGuidedTaskHighlights();
        Debug.Log("[VRME] Guided task completed. scene=" + SceneManager.GetActiveScene().name + ", source=" + completionSource);
        Debug.Log("[VRME] Conversation and scene interaction remain active until the participant enters the Exit trigger.");
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

        foreach (GameObject marker in activeGuidedTaskMarkers)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }

        activeGuidedTaskMarkers.Clear();
        Debug.Log("[VRME] Guided task highlights cleared. scene=" + SceneManager.GetActiveScene().name);
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

    private Material CreateTaskHighlightMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = "VRME_TaskHighlight_Material";
        material.color = new Color(taskTargetHighlightColor.r, taskTargetHighlightColor.g, taskTargetHighlightColor.b, 0.55f);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", taskTargetHighlightColor * 1.6f);
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
        ElephantFed
    }

    private enum TaskMarkerPlacement
    {
        Ground,
        ObjectCenter,
        DoorSurface
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

    private async Task SendAudioAsync(byte[] wavBytes, bool retryAfterStaleClose = true)
    {
        if (isSending)
        {
            pendingAudioTurns.Enqueue(wavBytes);
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

            await SendConfigAsync();

            string turnContext = BuildTurnContextString();
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
            LogTurnContextSent(turnContext);
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
                _ = SendAudioAsync(wavBytes, false);
            }
            else if (pendingAudioTurns.TryDequeue(out byte[] nextWavBytes))
            {
                Debug.Log("[VRME] Sending queued voice turn. remaining=" + pendingAudioTurns.Count);
                _ = SendAudioAsync(nextWavBytes);
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
                        currentHeldObjects.Add(tracker.ContextName);
                    }

                    writer.WriteLine("- " + tracker.ContextName + " | interactionState={" + tracker.InteractionStateSummary + "}");
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

        foreach (InteractionTracker tracker in trackers)
        {
            if (tracker == null || !tracker.isActiveAndEnabled || tracker.attentionOnlyTarget || IsSystemInteractionTracker(tracker))
            {
                continue;
            }

            tracker.RefreshCurrentHeldState();
            if (tracker.isCurrentlyHeld)
            {
                currentHeldObjects.Add(tracker.ContextName);
            }
        }

        using (var writer = new StringWriter())
        {
            writer.WriteLine("[STATIC_SCENE_DESCRIPTION]");
            writer.WriteLine(GetEffectiveScenePrompt());
            writer.WriteLine("[/STATIC_SCENE_DESCRIPTION]");
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
            writer.WriteLine("authority=Only objects listed above are currently in the participant's hand at this voice trigger.");
            writer.WriteLine("[/CURRENT_HELD_OBJECTS]");

            writer.WriteLine("[RECENT_CONTROLLER_EVENTS]");
            writer.WriteLine(InteractionTracker.GetRecentEventsTextSince(voiceTurnStartedAtUtc, maxRecentInteractionEvents));
            writer.WriteLine("authority=These controller events occurred during the current voice-trigger window only. A grab event does not prove the object is still held; CURRENT_HELD_OBJECTS is authoritative for current holding.");
            writer.WriteLine("[/RECENT_CONTROLLER_EVENTS]");

            writer.WriteLine(BuildGuidedTaskContext(player, playerPosition));
            return writer.ToString().TrimEnd();
        }
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
            writer.WriteLine("instruction_for_avatar=If the participant says they cannot find the object or target, guide them using the highlighted object and target directions below. Suggest only physically grounded actions: grab, carry, move, throw, place, or bring the highlighted object toward the highlighted target. Do not invent object-specific functions such as opening, reading, activating, switching on, transforming, or triggering hidden mechanisms unless an explicit interaction event/state proves that ability. Do not invent unseen objects or exact coordinates in speech; describe relative directions naturally.");

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

                writer.WriteLine("plannedHighlights=not_visible_until_avatar_briefing_begins");
                writer.WriteLine("plannedHighlightedObjectHints=" + string.Join(", ", plannedSpec.ObjectNames));
                writer.WriteLine("plannedHighlightedTargetHints=" + (plannedSpec.TargetNames.Length > 0 ? string.Join(", ", plannedSpec.TargetNames) : plannedSpec.TargetLabel));
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
                    writer.WriteLine("- " + FormatGuidedTaskLocation(plannedObject.name, GetGuidedObjectPoint(plannedObject), player, playerPosition));
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
                    string label = !string.IsNullOrWhiteSpace(plannedSpec.TargetLabel) ? plannedSpec.TargetLabel : plannedTarget.name;
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

                writer.WriteLine("- " + FormatGuidedTaskLocation(taskObject.name, GetGuidedObjectPoint(taskObject), player, playerPosition));
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
                    : target.name;
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

                if (text.Contains("\"avatar_reply_start\""))
                {
                    string source = ExtractJsonString(text, "source", "");
                    if (ShouldActivateHighlightsForReplySource(source))
                    {
                        mainThreadActions.Enqueue(() => ActivateSceneTaskHighlightsFromReplyStart(source));
                    }
                    Debug.Log("[VRME] Avatar reply started. source=" + source);
                    continue;
                }

                if (text.Contains("\"audio_stream_start\""))
                {
                    receivingStream = true;
                    string source = ExtractJsonString(text, "source", "");
                    int streamSampleRate = ExtractJsonInt(text, "sampleRate", 24000);
                    int streamChannels = ExtractJsonInt(text, "channels", 1);
                    if (ShouldActivateHighlightsForReplySource(source))
                    {
                        mainThreadActions.Enqueue(() => ActivateSceneTaskHighlightsFromReplyStart(source));
                    }
                    mainThreadActions.Enqueue(() => BeginPcmStream(streamSampleRate, streamChannels));
                    Debug.Log("[VRME] Audio stream started. source=" + source + ", sampleRate=" + streamSampleRate + ", channels=" + streamChannels);
                    continue;
                }

                if (text.Contains("\"audio_stream_end\""))
                {
                    mainThreadActions.Enqueue(EndPcmStream);
                    Debug.Log("[VRME] Audio stream ended. WebSocket remains available for the next voice turn if the server keeps it open.");
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
                mainThreadActions.Enqueue(() => AppendPcmStreamChunk(pcmChunk));
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
            (string.Equals(source, "proactive_guide", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(source, "auto_briefing", StringComparison.OrdinalIgnoreCase));
    }

    private void ActivateSceneTaskHighlightsFromReplyStart(string source)
    {
        if (!ShouldActivateHighlightsForReplySource(source))
        {
            return;
        }

        autoIntroSent = true;
        Debug.Log("[VRME] Activating task highlights at avatar reply start. source=" + source);
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
