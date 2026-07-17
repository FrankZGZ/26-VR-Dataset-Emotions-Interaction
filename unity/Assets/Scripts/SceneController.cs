using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;

public class SceneController : MonoBehaviour
{

    // Variables.
    public float countdownTimerSeconds = -1;
    private const float MinimumExitDelaySeconds = 60f;
    // public GameObject surveyCanvas;
    public GameObject doorObject;
    // If it is tutorial scene.
    public bool skipSurvey = false;
    [Tooltip("List of all interactable objects whose usage status needs to be tracked.")]
    public List<InteractionTracker> requiredInteractables;

    // Door animation.
    private Animator doorAnimator;

    // Arrow.
    public GameObject arrowObject;


    // Scene status.
    private bool sceneConditionsMet = false;
    public bool SceneConditionsMet => sceneConditionsMet;

    // Start is called before the first frame update
    void Start()
    {
        // Skip in tutorial scene.
        if(skipSurvey == false)
        {
            // Use global settings.
            if(this.countdownTimerSeconds < 0)
            {
                this.countdownTimerSeconds = StudySettings.sceneTime;
            }

            // Scene/global overrides must never open an experiment exit before
            // one full minute. Keep the tutorial and survey's own short timers.
            string activeSceneName = SceneManager.GetActiveScene().name;
            bool isExperimentScene = activeSceneName != "Tutorial" && activeSceneName != "EmotionSurvey";
            if (isExperimentScene)
            {
                this.countdownTimerSeconds = Mathf.Max(this.countdownTimerSeconds, MinimumExitDelaySeconds);
            }

            // Get door animator.
            if (this.doorObject != null)
            {
                this.doorAnimator = this.doorObject.GetComponent<Animator>();
            }
        }
    }

    // Update is called once per frame
    void Update()
    {   
        // Keep original early-return behavior unless we're in replay camera mode (we still want the timer/arrow).
        if(sceneConditionsMet == true || skipSurvey == true)
        {
            return;
        }

        // Count down.
        countdownTimerSeconds -= Time.deltaTime;
        // Debug.Log(string.Format("[Timer] {0}", countdownTimerSeconds));

        // Time up.
        if (countdownTimerSeconds <= 0.0f)
        {
            bool allItemsUsed = CheckIfAllInteractablesAreUsed();
            if (allItemsUsed)
            {
                // Conditions met.
                sceneConditionsMet = true;
                
                // Check if door animator exists before using it
                if (this.doorAnimator != null)
                {
                    this.doorAnimator.SetBool("openExitDoor", true);
                }
                
                // Always show arrow when time is up
                if (this.arrowObject != null)
                {
                    this.arrowObject.SetActive(true);
                }

                Debug.Log("Scene conditions met, opening the door.");
            }   
        }
    }

    private bool CheckIfAllInteractablesAreUsed()
    {
        if (requiredInteractables == null || requiredInteractables.Count == 0)
        {
            return true;
        }
        
        // Check if the isUsed flag of each object in the list is true
        return requiredInteractables.All(interactable => interactable != null && interactable.isUsed);
    }

    // Start emotion survey.
    public void StartEmotionSurvey()
    {
        if (skipSurvey == true)
        {

        }
        else
        {
            // Set camera pose data.
            gameObject.GetComponent<CameraPoseSender>().EndSessionAndSendData();

            // Load emotion survey scene.
            SceneManager.LoadScene("EmotionSurvey");
        }
    }
}
