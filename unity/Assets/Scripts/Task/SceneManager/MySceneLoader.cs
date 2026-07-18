using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Linq;

[System.Serializable]
public class LatinSquareLogEntry
{
    public string timestamp;
    public int participantId;
    public int row;
    public List<int> sequence;
}

[System.Serializable]
public class LatinSquareLogEntryList
{
    public List<LatinSquareLogEntry> entries;
}

public class MySceneLoader : MonoBehaviour
{
    [System.Serializable]
    private class LatinSquareFileFormat
    {
        public List<List<int>> sequences;
    }

    public int realSceneBuildIndex;
    public int endSceneBuildIndex; 
    
    // Sequential scene switching variables
    
    private int currentSequentialIndex = 0; 
    private static int persistentSequentialIndex = 0; // Persist across scenes
    private static bool flowSessionInitialized = false;
    private static bool directSceneResumeMode = false;
    private static List<int> directRecoverySequence = null;
    private List<int> sequentialScenes; 
    private bool isLoadingRealScene = false;
    
    // Real scene auto-switching variables
    public const float RealSceneWaitTimeSeconds = 20f;
    private Coroutine autoSwitchCoroutine; // Auto-switch coroutine reference

    private const int totalParticipants = 85;
    private const string latinSquareFileName = "LatinSquare.json";
    private static List<List<int>> allSequences = null;
    private static bool hasLoggedLatinSquare = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPlaySessionFlow()
    {
        persistentSequentialIndex = 0;
        flowSessionInitialized = false;
        directSceneResumeMode = false;
        directRecoverySequence = null;
        hasLoggedLatinSquare = false;
    }

    void Start()
    {
        ResolveSpecialSceneBuildIndices();

        // Read the Latin square sequence from the JSON file
        if (allSequences == null)
        {
            string filePath = Path.Combine(Application.persistentDataPath, latinSquareFileName);
            if (File.Exists(filePath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(filePath);
                    // Clean up the JSON content by removing unnecessary whitespace
                    jsonContent = jsonContent.Replace("\n", "").Replace("\r", "").Replace(" ", "").Replace("\t", "");
                    
                    // Manual parsing instead of JsonUtility since it has trouble with top-level arrays
                    allSequences = new List<List<int>>();
                    
                    // Remove the outer brackets [ ]
                    if (jsonContent.StartsWith("[") && jsonContent.EndsWith("]"))
                    {
                        jsonContent = jsonContent.Substring(1, jsonContent.Length - 2);
                        
                        // Split by ],[ to get individual arrays
                        string[] arrayStrings = jsonContent.Split(new string[] { "],[" }, System.StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string arrayStr in arrayStrings)
                        {
                            // Clean up any remaining brackets
                            string cleanStr = arrayStr.Replace("[", "").Replace("]", "");
                            
                            // Split by comma to get individual numbers
                            string[] numberStrings = cleanStr.Split(',');
                            
                            List<int> sequence = new List<int>();
                            foreach (string numStr in numberStrings)
                            {
                                if (int.TryParse(numStr.Trim(), out int num))
                                {
                                    sequence.Add(num);
                                }
                            }
                            
                            if (sequence.Count > 0)
                            {
                                allSequences.Add(sequence);
                            }
                        }
                        
                        Debug.Log($"[LatinSquare] Successfully parsed {allSequences.Count} sequences from {latinSquareFileName}");

                    }
                    else
                    {
                        Debug.LogError($"[LatinSquare] JSON content doesn't start with '[' and end with ']'. Content: {jsonContent.Substring(0, Mathf.Min(jsonContent.Length, 100))}");
                        allSequences = new List<List<int>>(); // Initialize to empty list
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[LatinSquare] Exception while reading/parsing {latinSquareFileName}: {e.ToString()}");
                    allSequences = new List<List<int>>(); // Initialize to empty list
                }
            }
            else
            {
                Debug.LogError($"[LatinSquare] {latinSquareFileName} not found at {filePath}!");
                allSequences = new List<List<int>>(); // Initialize to empty list
            }
        }

        int currentSceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
        string currentSceneName = SceneManager.GetActiveScene().name;
        List<int> canonicalExperimentScenes = GetCanonicalExperimentScenes();

        // Keep the participant/Latin-square path when the run starts from the
        // tutorial. When Play starts in any experiment scene, rotate the full
        // six-scene cycle so that scene becomes the first one.
        if (currentSceneName == "Tutorial_Interaction")
        {
            flowSessionInitialized = true;
            directSceneResumeMode = false;
            persistentSequentialIndex = 0;
        }
        else if (currentSceneBuildIndex == realSceneBuildIndex)
        {
            if (!flowSessionInitialized)
            {
                flowSessionInitialized = true;
                directSceneResumeMode = false;
                persistentSequentialIndex = 0;
            }
        }
        else if (canonicalExperimentScenes.Contains(currentSceneBuildIndex) && !flowSessionInitialized)
        {
            flowSessionInitialized = true;
            directSceneResumeMode = true;
            persistentSequentialIndex = 0;
            directRecoverySequence = RotateSequenceToStartAt(canonicalExperimentScenes, currentSceneBuildIndex);
            PlayerData.sceneSequence = directRecoverySequence
                .Select(index => Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(index)))
                .ToArray();
            PlayerData.currentSceneIndex = 0;
            Debug.Log($"[SceneFlow] Direct six-scene run started from {currentSceneName}; cycle={string.Join(",", PlayerData.sceneSequence)}.");
        }

        int pid = 0;
        int.TryParse(PlayerData.participantId, out pid);
        if (pid < allSequences.Count)
        {
            var rowList = allSequences[pid];
            if (!hasLoggedLatinSquare)
            {
                Debug.Log($"[LatinSquare] pid={pid}, rowList={string.Join(",", rowList)}");
                Debug.Log($"[LatinSquare] allSequences[pid]={string.Join(",", allSequences[pid])}");
                LogLatinSquareToJsonFile(pid, pid, rowList);
                hasLoggedLatinSquare = true;
            }
            sequentialScenes = directSceneResumeMode
                ? new List<int>(directRecoverySequence ?? canonicalExperimentScenes)
                : new List<int>(rowList);
        }
        else
        {
            Debug.LogError($"[LatinSquare] pid={pid} is out of range, allSequences.Count={allSequences.Count}");
            sequentialScenes = directSceneResumeMode
                ? new List<int>(directRecoverySequence ?? canonicalExperimentScenes)
                : new List<int>(canonicalExperimentScenes);
        }

        bool hasCompleteSixSceneSequence =
            sequentialScenes.Count == canonicalExperimentScenes.Count &&
            sequentialScenes.Distinct().Count() == canonicalExperimentScenes.Count &&
            sequentialScenes.All(canonicalExperimentScenes.Contains);
        if (!hasCompleteSixSceneSequence)
        {
            Debug.LogWarning("[SceneFlow] The supplied sequence is not a complete six-scene cycle; using the canonical six scenes instead.");
            sequentialScenes = directSceneResumeMode
                ? new List<int>(directRecoverySequence ?? canonicalExperimentScenes)
                : new List<int>(canonicalExperimentScenes);
        }

        PlayerData.sceneSequence = sequentialScenes
            .Select(index => Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(index)))
            .ToArray();
        
        // Check if current scene is the real scene
        if (currentSceneBuildIndex == realSceneBuildIndex)
        {
            // If current is real scene, use persistent index
            isLoadingRealScene = true;
            currentSequentialIndex = persistentSequentialIndex;
            
            autoSwitchCoroutine = StartCoroutine(AutoSwitchAfterDelay());
        }
        else
        {
            // If current is not real scene, find its position in the sequence.
            isLoadingRealScene = false;
            bool currentSceneFoundInSequence = false;
            for (int i = 0; i < sequentialScenes.Count; i++)
            {
                if (sequentialScenes[i] == currentSceneBuildIndex)
                {
                    currentSequentialIndex = i + 1; // Set to next scene index
                    persistentSequentialIndex = currentSequentialIndex; // Save to static variable
                    PlayerData.currentSceneIndex = i;
                    currentSceneFoundInSequence = true;
                    Debug.Log($"[SceneFlow] {currentSceneName} is scene {i + 1}/{sequentialScenes.Count}; next sequence index={currentSequentialIndex}.");
                    break;
                }
            }

            if (!currentSceneFoundInSequence &&
                currentSceneBuildIndex != endSceneBuildIndex &&
                currentSceneName != "Tutorial_Interaction")
            {
                Debug.LogWarning($"[SceneFlow] {currentSceneName} is not in the active sequence; retaining index {persistentSequentialIndex}.");
                currentSequentialIndex = persistentSequentialIndex;
            }
        }
    }
    
    public void SetInitialIndex(int index)
    {
        // Comment out original random scene management
        // RandomSceneManager.currentIndex = index;
        
        // Set initial index for sequential scenes
        currentSequentialIndex = index;
    }

    public void LoadNextScene()
    {
        // Stop previous auto-switch coroutine if exists
        if (autoSwitchCoroutine != null)
        {
            StopCoroutine(autoSwitchCoroutine);
            autoSwitchCoroutine = null;
        }
        
        // Comment out original random scene switching logic
        // LoadBySceneNum(RandomSceneManager.getSceneNum());
        // RandomSceneManager.currentIndex += 1;
        
        // New sequential scene switching logic
        if (isLoadingRealScene)
        {
            // Switch from real scene to next sequential scene
            if (currentSequentialIndex < sequentialScenes.Count)
            {
                int nextSceneIndex = sequentialScenes[currentSequentialIndex];
                Debug.Log($"Loading from real scene: sequentialScenes[{currentSequentialIndex}] = {nextSceneIndex}");
                LoadBySceneNum(nextSceneIndex);
                currentSequentialIndex++;
                persistentSequentialIndex = currentSequentialIndex; // Keep persistent index updated
                isLoadingRealScene = false;
            }
            else
            {
                // All scenes completed, load end scene
                Debug.Log("Sequence completed, loading end scene");
                LoadBySceneNum(endSceneBuildIndex);
                isLoadingRealScene = false;
            }
        }
        else
        {
            // When switching from a regular scene, check if it was the last one.
            if (currentSequentialIndex >= sequentialScenes.Count)
            {
                // If the sequence is complete, go directly to the end scene.
                Debug.Log("Sequence completed, loading end scene");
                LoadBySceneNum(endSceneBuildIndex);
            }
            else
            {
                // Otherwise, switch from the regular scene to the real scene.
                isLoadingRealScene = true;
                Debug.Log($"Loading real scene: {realSceneBuildIndex}");
                LoadBySceneNum(realSceneBuildIndex);
            }
        }
    }

    public void LoadBySceneNum(int sceneNumber)
    {
        StartCoroutine(LoadAsyncScene(sceneNumber));
    }

    private List<int> GetCanonicalExperimentScenes()
    {
        var result = new List<int>();
        for (int buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (sceneName == "Tutorial_Interaction" ||
                buildIndex == realSceneBuildIndex ||
                buildIndex == endSceneBuildIndex)
            {
                continue;
            }

            result.Add(buildIndex);
        }

        return result;
    }

    private void ResolveSpecialSceneBuildIndices()
    {
        int resolvedRealIndex = FindBuildIndexBySceneName("Real");
        int resolvedEndIndex = FindBuildIndexBySceneName("EndScene");

        if (resolvedRealIndex >= 0)
        {
            realSceneBuildIndex = resolvedRealIndex;
        }

        if (resolvedEndIndex >= 0)
        {
            endSceneBuildIndex = resolvedEndIndex;
        }

        if (resolvedRealIndex < 0 || resolvedEndIndex < 0)
        {
            Debug.LogError("[SceneFlow] Real and EndScene must both be enabled in Build Settings.");
        }
    }

    private int FindBuildIndexBySceneName(string targetSceneName)
    {
        for (int buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (Path.GetFileNameWithoutExtension(scenePath) == targetSceneName)
            {
                return buildIndex;
            }
        }

        return -1;
    }

    private List<int> RotateSequenceToStartAt(List<int> source, int firstSceneBuildIndex)
    {
        var rotated = new List<int>(source.Count);
        int startIndex = source.IndexOf(firstSceneBuildIndex);
        if (startIndex < 0)
        {
            return new List<int>(source);
        }

        for (int offset = 0; offset < source.Count; offset++)
        {
            rotated.Add(source[(startIndex + offset) % source.Count]);
        }

        return rotated;
    }

    private IEnumerator LoadAsyncScene(int sceneNumber)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneNumber);

        // Wait until the asynchronous scene fully loads
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
        
        // If loaded scene is real scene, start auto-switch timer
        if (sceneNumber == realSceneBuildIndex && isLoadingRealScene)
        {
            autoSwitchCoroutine = StartCoroutine(AutoSwitchAfterDelay());
        }
    }
    
    // Real is a fixed 20-second baseline, independent of stale scene overrides.
    private IEnumerator AutoSwitchAfterDelay()
    {
        Debug.Log($"[SceneFlow] Real baseline started; advancing in {RealSceneWaitTimeSeconds:0.##} seconds.");
        yield return new WaitForSecondsRealtime(RealSceneWaitTimeSeconds);
        Debug.Log("[SceneFlow] Real baseline complete; loading next scene.");
        LoadNextScene();
    }

    private void LogLatinSquareToJsonFile(int participantId, int row, List<int> sequence)
    {
        var entry = new LatinSquareLogEntry
        {
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            participantId = participantId,
            row = row,
            sequence = sequence
        };

        string logPath = Path.Combine(Application.persistentDataPath, "LatinSquareLog.json");
        List<LatinSquareLogEntry> allEntries = new List<LatinSquareLogEntry>();
        if (File.Exists(logPath))
        {
            string json = File.ReadAllText(logPath);
            if (!string.IsNullOrWhiteSpace(json))
            {
                allEntries = JsonUtility.FromJson<LatinSquareLogEntryList>("{\"entries\":" + json + "}").entries;
            }
        }
        allEntries.Add(entry);

        string newJson = JsonUtility.ToJson(new LatinSquareLogEntryList { entries = allEntries }, true);
        int startIdx = newJson.IndexOf('[');
        int endIdx = newJson.LastIndexOf(']');
        if (startIdx >= 0 && endIdx > startIdx)
        {
            newJson = newJson.Substring(startIdx, endIdx - startIdx + 1);
        }
        File.WriteAllText(logPath, newJson);

        Debug.Log($"[LatinSquare] {JsonUtility.ToJson(entry)}");
    }
}
