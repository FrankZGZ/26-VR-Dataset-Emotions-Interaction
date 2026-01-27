using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.IO; // Add for file operations

public class CameraPoseSender : MonoBehaviour
{
    // Serializable class representing each recorded pose
    [System.Serializable]
    public class CameraPose
    {
        public Vector3 position;
        public Quaternion orientation;
        public string timestamp;
    }

    // Serializable class representing all the data to be sent to the server
    [System.Serializable]
    public class DataToSend
    {
        public string loginId;
        public string participantId;
        public string sceneName;
        public List<CameraPose> cameraPoses = new List<CameraPose>();
    }

    public GameObject cameraRig; // Reference to the camera or XR rig
    public GameObject backupTriggerObject; // If this object is active and data hasn't been saved, trigger saving.

    private List<CameraPose> bufferedPoses = new List<CameraPose>();
    private float timeSinceLastRecord = 0.0f; // Timer for recording poses
    private const float recordInterval = 0.1f; // Time interval for recording poses
    private string localDirectoryPath;
    private bool isSessionEnded = false; // Flag to prevent multiple saves

    private void Start()
    {
        string participantId = PlayerData.participantId;
        localDirectoryPath = Path.Combine(Application.persistentDataPath, "CameraPoseData", participantId);
        if (!Directory.Exists(localDirectoryPath))
        {
            Directory.CreateDirectory(localDirectoryPath);
            Debug.Log("[Debug] Created directory: " + localDirectoryPath);
        }
    }

    private void Update()
    {
        // Update timers
        timeSinceLastRecord += Time.deltaTime;

        // Record camera pose if the record interval has passed
        if (timeSinceLastRecord >= recordInterval)
        {
            timeSinceLastRecord = 0.0f;
            RecordCameraPose();
        }

        // Backup Trigger Logic: If data hasn't been saved yet and the backup object becomes active, save the data.
        if (!isSessionEnded && backupTriggerObject != null && backupTriggerObject.activeInHierarchy)
        {
            Debug.Log("[Debug] Backup trigger object is active. Saving camera pose data as a fallback.");
            EndSessionAndSendData();
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
                cameraPoses = new List<CameraPose>(bufferedPoses) // Use a copy of the list
            };

            string jsonData = JsonUtility.ToJson(data);
            string timeStr = System.DateTime.Now.ToString("dd-MMM_HH-mm-ss");
            
            // Save all data to a single file
            SaveCameraPoseDataToFile(jsonData, data.participantId, data.sceneName, timeStr);
            
            bufferedPoses.Clear(); // Clear the buffer after saving
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
                cameraPoses = bufferedPoses
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
