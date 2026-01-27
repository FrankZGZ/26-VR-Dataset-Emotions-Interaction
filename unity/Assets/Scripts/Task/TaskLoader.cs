using UnityEngine;
using System.Collections.Generic;

public class TaskLoader : MonoBehaviour
{
    [Tooltip("The list of task GameObjects to be loaded randomly.")]
    public List<GameObject> taskObjects;

    [Tooltip("SceneLoader")]
    public GameObject sceneLoader;

    private List<GameObject> availableTasks;

    void Awake()
    {
        if (sceneLoader == null)
        {
            Debug.LogError("The 'Scene Loader' GameObject is not assigned in the Inspector. Please assign the GameObject that has the SceneLoader script.");
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnEnable()
    {
        // Initialize the random number generator with a time-based seed.
        // This ensures a different random sequence each time the game is run.
        Random.InitState((int)System.DateTime.Now.Ticks);

        // Always initialize to avoid null reference if called externally.
        availableTasks = new List<GameObject>();

        if (taskObjects != null && taskObjects.Count > 0)
        {
            // Initialize the list of available tasks.
            availableTasks.AddRange(taskObjects);

            // Ensure all tasks are initially inactive.
            foreach (var task in taskObjects)
            {
                if (task != null)
                {
                    task.SetActive(false);
                }
            }

            // Activate the first random task.
            ActivateNextRandomTask();
            return;
        }

        Debug.LogWarning("The task list (taskObjects) on TaskLoader is empty or not assigned. Treating as completed and loading next scene.");
        LoadNextScene();
    }

    /// <summary>
    /// Activates the next random task from the list.
    /// This method can be called externally when a task is completed to load the next one.
    /// </summary>
    public void ActivateNextRandomTask()
    {
        if (availableTasks == null)
        {
            availableTasks = (taskObjects != null) ? new List<GameObject>(taskObjects) : new List<GameObject>();
        }

        if (availableTasks.Count > 0)
        {
            int randomIndex = Random.Range(0, availableTasks.Count);
            GameObject taskToActivate = availableTasks[randomIndex];
            
            // Activate the selected task.
            if (taskToActivate != null)
            {
                taskToActivate.SetActive(true);
            }
            
            Debug.Log($"Activated task: {taskToActivate.name}");

            // Remove the task from the available list to ensure it's loaded only once.
            availableTasks.RemoveAt(randomIndex);
        }
        else
        {
            Debug.Log("All tasks have been loaded.");
            LoadNextScene();
        }
    }

    private void LoadNextScene()
    {
        // When all tasks are completed, call the method on SceneLoader.
        if (sceneLoader != null)
        {
            // Call the LoadNextScene method on any script attached to the sceneLoader GameObject.
            // Make sure the method name string here exactly matches the method in your script.
            sceneLoader.SendMessage("LoadNextScene", SendMessageOptions.DontRequireReceiver);
            return;
        }

        Debug.LogError("Cannot call scene loader because the reference is not set.");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
