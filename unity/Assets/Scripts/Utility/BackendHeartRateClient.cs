using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Polls the avatar backend's Polar H10 bridge without sharing its voice WebSocket.</summary>
public sealed class BackendHeartRateClient : MonoBehaviour
{
    [Serializable]
    private sealed class HeartRateResponse
    {
        public bool available;
        public float bpm;
        public string source;
        public string timestampUtc;
        public float ageSeconds;
    }

    public static bool Available { get; private set; }
    public static float Bpm { get; private set; }
    public static string Source { get; private set; } = "Polar H10 backend not started";
    public static string SensorTimestampUtc { get; private set; }

    private static string endpoint = "http://127.0.0.1:8080/heart-rate";
    private static BackendHeartRateClient instance;

    public static void EnsureRunning(string voiceWebSocketUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(voiceWebSocketUrl))
        {
            string baseUrl = voiceWebSocketUrl.Trim().TrimEnd('/');
            if (baseUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                baseUrl = "https://" + baseUrl.Substring(6);
            else if (baseUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                baseUrl = "http://" + baseUrl.Substring(5);
            endpoint = baseUrl + "/heart-rate";
        }
        if (instance != null) return;
        GameObject host = new GameObject("Backend Heart Rate Client");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<BackendHeartRateClient>();
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
        StartCoroutine(Poll());
    }

    private IEnumerator Poll()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
            {
                request.timeout = 3;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    HeartRateResponse response = JsonUtility.FromJson<HeartRateResponse>(request.downloadHandler.text);
                    Available = response != null && response.available && response.ageSeconds <= 5f;
                    Bpm = Available ? response.bpm : 0f;
                    Source = response != null && !string.IsNullOrWhiteSpace(response.source)
                        ? response.source
                        : "Polar H10 backend unavailable";
                    SensorTimestampUtc = response != null ? response.timestampUtc : null;
                }
                else
                {
                    Available = false;
                    Bpm = 0f;
                    Source = "Avatar backend heart-rate endpoint unavailable";
                }
            }
            yield return wait;
        }
    }
}
