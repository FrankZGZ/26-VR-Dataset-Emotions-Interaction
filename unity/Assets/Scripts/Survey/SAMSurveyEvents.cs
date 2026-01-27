using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.IO; // Add for file operations

public class SAMSurveyEvents : MonoBehaviour
{
    // Variables
    // public string serverURL; // Commented out as we won't be using a server

    // Components 
    public ToggleGroup valenceButtons;
    public ToggleGroup arousalButtons;
    public ToggleGroup dominanceButtons;
    public Button submitButton;
    public Text debugInfo;
   // GameObjects to control after submission
    public GameObject[] disableAfterSubmit; 
    public GameObject[] enableAfterSubmit;

    // Input checker.
    // private bool valenceChanged = false;
    // private bool arousalChanged = false;
    // private bool dominanceChanged = false;

    // Start is called before the first frame update
    void Start()
    {
        // Debug info.
        Debug.Log("[Debug] Survey start");

        // Add listener to submit button.
        submitButton.onClick.AddListener(delegate { StartCoroutine(ButtonClickEvent()); });

        // Hide loading circle.
        // loadingCircle.SetActive(false);
        
        // Create directory if it doesn't exist (add participantId folder)
        string participantId = PlayerData.participantId;
        string directoryPath = Path.Combine(Application.persistentDataPath, "SurveyData", participantId);
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            Debug.Log("[Debug] Created directory: " + directoryPath);
        }
    }

    // Save survey data to a local file
    private bool SaveSurveyDataToFile(string jsonData, string participantId, string sceneName, string timeStr)
    {
        try
        {
            // Create filename with time string for uniqueness
            string fileName = $"SAMSurvey_{participantId}_{sceneName}_{timeStr}.json";
            string directoryPath = Path.Combine(Application.persistentDataPath, "SurveyData", participantId);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            string filePath = Path.Combine(directoryPath, fileName);
            
            // Write data to file
            File.WriteAllText(filePath, jsonData);
            
            Debug.Log($"[Debug] Survey data saved to: {filePath}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Error] Failed to save survey data: {e.Message}");
            return false;
        }
    }

    // Invoked when submit button clicked.
    private IEnumerator ButtonClickEvent()
    {
        // Check if answered. 
        if ((valenceButtons.AnyTogglesOn() == false) 
            || (arousalButtons.AnyTogglesOn() == false) 
            || (dominanceButtons.AnyTogglesOn() == false))
        {
            debugInfo.text = "Please answer the questions.";
            yield break;
        }

        // Construct json file. 
        var surveySingle = new EmotionSurveySingle();
        surveySingle.participantId = PlayerData.participantId;
        surveySingle.loginId = PlayerData.loginId;
        surveySingle.timestampUtcUnixMs = System.DateTimeOffset.Now.ToUnixTimeMilliseconds();
        surveySingle.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        surveySingle.valenceValue = valenceButtons.GetComponent<RadioButtonEvents>().selectedToggleValue;
        surveySingle.arousalValue = arousalButtons.GetComponent<RadioButtonEvents>().selectedToggleValue;
        surveySingle.dominanceValue = dominanceButtons.GetComponent<RadioButtonEvents>().selectedToggleValue;

        // Serialization. 
        var surveyDataSingle = JsonUtility.ToJson(surveySingle);

        // Update UI.
        submitButton.interactable = false;
        submitButton.GetComponentInChildren<Text>().text = "Submitting ...";
        // loadingCircle.SetActive(true);

        // Save data to local file
        string timeStr = System.DateTime.Now.ToString("dd-MMM_HH-mm-ss");
        bool saveSuccess = SaveSurveyDataToFile(surveyDataSingle, surveySingle.participantId, surveySingle.sceneName, timeStr);
        
        // Short delay for visual feedback
        yield return new WaitForSeconds(0.5f);
        
        if (saveSuccess)
        {
            submitButton.interactable = false;
            submitButton.GetComponentInChildren<Text>().text = "Submitted";
            Debug.Log("[Debug] Data saved successfully.");
            
            // Disable any GameObjects
            if (disableAfterSubmit != null)
            {
                foreach (GameObject obj in disableAfterSubmit)
                {
                    if (obj != null)
                    {
                        obj.SetActive(false);
                        Debug.Log("[Debug] Disabled: " + obj.name);
                    }
                }
            }
            
            // Enable any GameObjects
            if (enableAfterSubmit != null)
            {
                foreach (GameObject obj in enableAfterSubmit)
                {
                    if (obj != null)
                    {
                        obj.SetActive(true);
                        Debug.Log("[Debug] Enabled: " + obj.name);
                    }
                }
            }

            // refresh the gameobject posistion to the centereyeanchor
    
            GameObject centerEyeAnchor = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
            if (centerEyeAnchor != null)
            {
    
                Transform currentTransform = this.transform;
                currentTransform.position = centerEyeAnchor.transform.position;

            }
            /* Scene switching code commented out for now
            if(PlayerData.currentSceneIndex < PlayerData.sceneSequence.Length - 1)
            {
                // Load next scene.
                PlayerData.currentSceneIndex++;
                SceneManager.LoadScene(sceneName: PlayerData.sceneSequence[PlayerData.currentSceneIndex]);
            }
            else
            {
                // End of study.
                SceneManager.LoadScene("EndScene");
            }
            */
        }
        else
        {
            submitButton.interactable = true;
            submitButton.GetComponentInChildren<Text>().text = "Submit";
            Debug.Log("[Debug] Error saving data!");
            debugInfo.text = "[Error] Could not save survey data.";
        }

        /* Original server code commented out
        // Posting data.
        Debug.Log("[Debug] Posting survey result...");
        using (var postRequest = new UnityWebRequest())
        {
            postRequest.url = serverURL; // PostUri is a string containing the url
            postRequest.method = "POST";
            postRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(surveyDataSingle)); // postData is Json file as a string
            postRequest.downloadHandler = new DownloadHandlerBuffer();
            postRequest.SetRequestHeader("Content-Type", "application/json");
            yield return postRequest.SendWebRequest();

            if(postRequest.result == UnityWebRequest.Result.ConnectionError)
            {
                submitButton.interactable = true;
                submitButton.GetComponentInChildren<Text>().text = "Submit";
                Debug.Log("[Debug] Connection error!");
                debugInfo.text = "[Error] Please check your Internet connection.";
            }
            else if(postRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                submitButton.interactable = true;
                submitButton.GetComponentInChildren<Text>().text = "Submit";
                Debug.Log("[Debug] Protocol error!");
                debugInfo.text = "[Error] Please check your Internet connection.";
            }
            else
            {
                submitButton.interactable = false;
                submitButton.GetComponentInChildren<Text>().text = "Submitted";
                Debug.Log("[Debug] Post request completed.");

                if(PlayerData.currentSceneIndex < PlayerData.sceneSequence.Length - 1)
                {
                    // Load next scene.
                    PlayerData.currentSceneIndex++;
                    SceneManager.LoadScene(sceneName: PlayerData.sceneSequence[PlayerData.currentSceneIndex]);
                }
                else
                {
                    // End of study.
                    SceneManager.LoadScene("EndScene");
                }
            }

            // Update UI.
            // loadingCircle.SetActive(false);
        }
        */
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
