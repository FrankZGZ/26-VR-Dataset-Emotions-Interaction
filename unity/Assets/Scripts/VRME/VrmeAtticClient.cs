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
    public string serverUrl = "ws://localhost:8080/";
    public KeyCode recordKey = KeyCode.V;
    public int sampleRate = 16000;
    public int maxRecordSeconds = 12;
    public bool autoConnectOnStart = true;
    public float connectTimeoutSeconds = 3f;
    public bool showRuntimeMarker = true;
    public Color markerColor = new Color(0.1f, 0.6f, 1f, 1f);
    [TextArea(3, 8)] public string scenePrompt = "";
    public bool includeInteractionContext = true;
    public bool autoFindInteractionTrackers = true;
    public InteractionTracker[] keyInteractables = Array.Empty<InteractionTracker>();
    public bool autoAttachInteractionTrackersToSceneObjects = true;
    public bool autoDiscoverSceneObjectsForContext = true;
    [Tooltip("Comma-separated object name hints used when InteractionTracker components are not present.")]
    public string sceneObjectNameHints = "HandTorch,Flashlight,Torch,Airplane,Plane,Stone,Rock,Banana,Fruit,Elephant,Dog,Puppy,Ball,Baseball,Book,Cup,Telescope,ManScreaming,Man,Gun,Sign,Door,Handle,Bar,Key,Switch,Button,Lever,Panel,Light,Exit";
    [Range(1, 40)] public int maxDiscoveredSceneObjects = 20;
    [Range(1f, 100f)] public float maxContextObjectDistance = 25f;
    public Transform playerTransform;
    public int maxRecentInteractionEvents = 5;
    public AudioSource playbackAudioSource;
    [Range(0.1f, 5f)] public float replyGain = 2.2f;
    public bool streamReplyAudio = true;
    [Range(0.05f, 1f)] public float streamStartBufferSeconds = 0.18f;
    [Range(5, 120)] public int streamMaxSeconds = 45;
    public bool autoIntroOnStart = false;
    [Range(0f, 60f)] public float autoIntroDelaySeconds = 10f;
    [TextArea(3, 8)] public string autoIntroPrompt =
        "Please introduce this VR scene to the participant. Describe the current environment, explain which interaction states or constraints are relevant, and state the task they should focus on. Keep it calm, clear, and complete without ending abruptly.";

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private ClientWebSocket websocket;
    private CancellationTokenSource cancellation;
    private Task connectTask;
    private AudioSource audioSource;
    private StreamingPcmPlayer streamingPlayer;
    private AudioClip recordingClip;
    private bool isRecording;
    private bool isSending;
    private bool recordKeyWasDown;
    private bool autoIntroSent;

    private void Start()
    {
        audioSource = playbackAudioSource != null ? playbackAudioSource : GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        streamingPlayer = new StreamingPcmPlayer();

        Debug.Log("[VRME] Client started. recordKey=" + recordKey + ", microphones=" + Microphone.devices.Length);

        if (autoAttachInteractionTrackersToSceneObjects)
        {
            int attachedCount = AttachTrackersToSceneObjects();
            Debug.Log("[VRME] Auto-attached InteractionTracker to " + attachedCount + " scene objects.");
        }

        if (showRuntimeMarker)
        {
            CreateRuntimeMarker();
        }

        if (autoConnectOnStart)
        {
            _ = ConnectAsync();
        }

        if (autoIntroOnStart)
        {
            _ = RunAutoIntroAsync();
        }
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        bool recordKeyIsDown = Input.GetKey(recordKey);
        if (recordKeyIsDown && !recordKeyWasDown)
        {
            StartRecording();
        }

        if (!recordKeyIsDown && recordKeyWasDown)
        {
            StopRecordingAndSend();
        }

        recordKeyWasDown = recordKeyIsDown;
        streamingPlayer?.Update();
    }

    private void OnGUI()
    {
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

    private Task ConnectAsync()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            return Task.CompletedTask;
        }

        if (connectTask != null && !connectTask.IsCompleted)
        {
            return connectTask;
        }

        connectTask = ConnectInternalAsync();
        return connectTask;
    }

    private async Task ConnectInternalAsync()
    {
        cancellation?.Cancel();
        cancellation = new CancellationTokenSource();
        websocket?.Dispose();
        websocket = new ClientWebSocket();

        try
        {
            float timeoutSeconds = Mathf.Max(0.5f, connectTimeoutSeconds);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, timeout.Token))
            {
                await websocket.ConnectAsync(new Uri(serverUrl), linked.Token);
            }
            Debug.Log("[VRME] Connected to " + serverUrl);
            await SendConfigAsync();
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

    private async Task SendConfigAsync()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            return;
        }

        string sceneContext = BuildSceneContext();
        string json =
            "{\"type\":\"config\",\"streamReplyAudio\":" + (streamReplyAudio ? "true" : "false") +
            ",\"scenePrompt\":\"" + EscapeJson(scenePrompt) +
            "\",\"sceneContext\":\"" + EscapeJson(sceneContext) + "\"}";
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);
        await websocket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            true,
            cancellation.Token);
        Debug.Log("[VRME] Sent scene config. contextChars=" + sceneContext.Length + " preview=" + PreviewForLog(sceneContext, 1400));
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
        if (isRecording || isSending)
        {
            return;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[VRME] No microphone device found.");
            return;
        }

        recordingClip = Microphone.Start(null, false, maxRecordSeconds, sampleRate);
        isRecording = true;
        CameraPoseSender.BeginVoiceSampling();
        Debug.Log("[VRME] Recording started. Release " + recordKey + " to send.");
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

    private async Task RunAutoIntroAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, autoIntroDelaySeconds)));
        if (autoIntroSent || !isActiveAndEnabled || string.IsNullOrWhiteSpace(autoIntroPrompt))
        {
            return;
        }

        autoIntroSent = true;
        await SendTextPromptAsync(autoIntroPrompt);
    }

    private async Task SendTextPromptAsync(string textPrompt)
    {
        if (isSending)
        {
            return;
        }

        isSending = true;
        try
        {
            await ConnectAsync();
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                Debug.LogWarning("[VRME] WebSocket is not open.");
                return;
            }

            await SendConfigAsync();
            byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(textPrompt);
            await websocket.SendAsync(
                new ArraySegment<byte>(textBytes),
                WebSocketMessageType.Text,
                true,
                cancellation.Token);
            Debug.Log("[VRME] Sent text prompt: " + textPrompt);

            await ReceiveReplyAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Text prompt failed: " + ex.Message);
        }
        finally
        {
            isSending = false;
        }
    }

    private async Task SendAudioAsync(byte[] wavBytes)
    {
        if (isSending)
        {
            return;
        }

        isSending = true;
        try
        {
            await ConnectAsync();
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                Debug.LogWarning("[VRME] WebSocket is not open.");
                return;
            }

            await SendConfigAsync();

            await websocket.SendAsync(
                new ArraySegment<byte>(wavBytes),
                WebSocketMessageType.Binary,
                true,
                cancellation.Token);
            Debug.Log("[VRME] Sent wav bytes: " + wavBytes.Length);

            await ReceiveReplyAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VRME] Send failed: " + ex.Message);
        }
        finally
        {
            isSending = false;
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
        List<SceneContextObject> discoveredObjects = DiscoverSceneContextObjects(trackers, playerPosition, player != null);

        using (var writer = new StringWriter())
        {
            writer.WriteLine("Scene: " + SceneManager.GetActiveScene().name);
            writer.WriteLine("Latest user head and gaze state:");
            writer.WriteLine(CameraPoseSender.LatestRuntimeContextText);

            string flashlightContext = BuildFlashlightContext(playerPosition, player != null);
            writer.WriteLine("High-priority scene cues:");
            writer.WriteLine("- Use the gaze/head state above first; if hitName names an object, treat it as the user's current attention target.");
            writer.WriteLine("- If attendedObjects lists an object with attendedLongEnough=True, treat it as an important object the user looked at during this voice turn even if the latest hit is false.");
            writer.WriteLine("- Flashlight: " + OneLineContext(flashlightContext));
            writer.WriteLine("- Closest interaction objects: " + BuildClosestInteractionObjectSummary(trackers, discoveredObjects, playerPosition, player != null));

            if (player != null)
            {
                writer.WriteLine("Player position: " + FormatVector(playerPosition));
            }
            else
            {
                writer.WriteLine("Player position: unknown");
            }

            writer.WriteLine("Key interactable objects:");
            if (trackers.Length == 0)
            {
                writer.WriteLine("- none with InteractionTracker components");
            }
            else
            {
                foreach (InteractionTracker tracker in trackers)
                {
                    if (tracker == null || !tracker.isActiveAndEnabled) continue;

                    Vector3 objectPosition = tracker.transform.position;
                    if (player != null && Vector3.Distance(playerPosition, objectPosition) > maxContextObjectDistance)
                    {
                        continue;
                    }

                    string distance = player != null
                        ? Vector3.Distance(playerPosition, objectPosition).ToString("0.00")
                        : "unknown";
                    writer.WriteLine("- " + tracker.ContextName + " | used=" + tracker.isUsed + " | position=" + FormatVector(objectPosition) + " | distanceToPlayer=" + distance);
                }
            }

            writer.WriteLine("Nearby scene objects found by name/collider:");
            if (discoveredObjects.Count == 0)
            {
                writer.WriteLine("- none");
            }
            else
            {
                foreach (SceneContextObject sceneObject in discoveredObjects)
                {
                    string distance = sceneObject.hasDistance ? sceneObject.distanceToPlayer.ToString("0.00") : "unknown";
                    writer.WriteLine("- " + sceneObject.name + " | position=" + FormatVector(sceneObject.position) + " | distanceToPlayer=" + distance);
                }
            }

            writer.WriteLine("Flashlight state:");
            writer.WriteLine(flashlightContext);

            writer.WriteLine("Scripted interaction references:");
            writer.WriteLine(BuildScriptedInteractionReferenceContext(playerPosition, player != null));

            writer.WriteLine("Recent interaction events:");
            writer.WriteLine(InteractionTracker.GetRecentEventsText(maxRecentInteractionEvents));
            return writer.ToString();
        }
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
                    summaries.Add(tracker.ContextName + " " + distance.ToString("0.0") + "m used=" + tracker.isUsed);
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

    private string[] ParseSceneObjectHints()
    {
        if (string.IsNullOrWhiteSpace(sceneObjectNameHints))
        {
            return Array.Empty<string>();
        }

        string[] rawHints = sceneObjectNameHints.Split(',');
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

    private async Task ReceiveReplyAsync()
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
                        Debug.LogWarning("[VRME] Server closed the WebSocket.");
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                payload = stream.ToArray();
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string text = System.Text.Encoding.UTF8.GetString(payload);
                if (text.Contains("\"audio_stream_start\""))
                {
                    receivingStream = true;
                    int streamSampleRate = ExtractJsonInt(text, "sampleRate", 24000);
                    int streamChannels = ExtractJsonInt(text, "channels", 1);
                    mainThreadActions.Enqueue(() => BeginPcmStream(streamSampleRate, streamChannels));
                    Debug.Log("[VRME] Audio stream started. sampleRate=" + streamSampleRate + ", channels=" + streamChannels);
                    continue;
                }

                if (text.Contains("\"audio_stream_end\""))
                {
                    mainThreadActions.Enqueue(EndPcmStream);
                    Debug.Log("[VRME] Audio stream ended.");
                    return;
                }

                if (text.Contains("\"audio_stream_error\""))
                {
                    Debug.LogWarning("[VRME] Audio stream error: " + text);
                    return;
                }

                Debug.Log("[VRME] Text reply: " + text);
                return;
            }

            if (receivingStream)
            {
                byte[] pcmChunk = payload;
                mainThreadActions.Enqueue(() => AppendPcmStreamChunk(pcmChunk));
                continue;
            }

            mainThreadActions.Enqueue(() => PlayWav(payload));
            return;
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

    private async void OnDestroy()
    {
        try
        {
            cancellation?.Cancel();
            if (websocket != null && websocket.State == WebSocketState.Open)
            {
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Scene closed", CancellationToken.None);
            }
        }
        catch
        {
            // Ignore shutdown races while Unity unloads the scene.
        }
        finally
        {
            websocket?.Dispose();
            cancellation?.Dispose();
        }
    }
}
