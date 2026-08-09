using System.Collections;
using System.Collections.Generic;
using UnityEngine;


// Global settings.
public static class StudySettings
{
    public static string serverURL;
    public static string loginURL;
    public static string studyDataURL;
    public static int sceneTime; 
}

// Participant data and status.
public static class PlayerData
{
    public static string participantId { get; set; }
    public static string loginId { get; set; }
    public static string sessionId { get; set; }
    public static string avatarCondition { get; set; }
    public static string[] sceneSequence { get; set; }
    public static int currentSceneIndex { get; set; }
    public static string counterbalanceScheme { get; set; }

    // static contrsuctor.
    static PlayerData()
    {
        participantId = "0";
        // Only affects AvatarConditionCounterbalance's default pattern index when a
        // scene is entered directly without going through Login (real participants
        // get a real loginId/participantId from Login, which overrides this). "0"
        // resolves to WarmSceneIndexPatterns[0] (Attic/Elephant/Puppies warm,
        // Lake/SolitaryConfinement/Tunnel cold) for the no-login default.
        loginId = "0";
        sessionId = System.Guid.NewGuid().ToString("N");
        avatarCondition = "unset";
        counterbalanceScheme = "warm-cold-18pattern-v1";
        sceneSequence = new string[1] { "Test" };
        currentSceneIndex = 0;
    }
}

// Network models for JSON body.
public class LoginModel
{
    public string loginId;
    public long timestampUtcUnixMs;
}

// Finish study model.
public class FinishStudyModel
{
    public string participantId;
    public string loginId;
    public string sessionId;
    public string avatarCondition;
    public long timestampUtcUnixMs;
}

public class LoginResult
{
    public bool result;
    public string message;
    public string[] sequence;
    public string participantId;
}

public class EmotionSurveySingle
{
    public string participantId;
    public string loginId;
    public string sessionId;
    public string avatarCondition;
    public long timestampUtcUnixMs;
    public string sceneName;
    public int sceneIndex;
    public int sceneSequenceLength;
    public string surveyVersion;
    public float valenceValue;
    public float arousalValue;
    public float dominanceValue;
    public float socialPresenceValue;
    public float autonomyValue;
    public float agencyValue;
    public float helpfulnessValue;
    public float trustValue;
    public float intrusivenessValue;
    public float warmthValue;
    public float competenceValue;
    public float contextAwarenessValue;
    public float guidanceClarityValue;
    public float attentionAccuracyValue;
    public float conversationNaturalnessValue;
    // RoSAS-SF (Fraune et al., 2025, Int'l J of Social Robotics): validated
    // 6-item short form of the Robotic Social Attributes Scale. Two items per
    // subscale: warmth (compassionate, social), competence (competent,
    // reliable), discomfort (scary, awkward).
    public float rosasCompassionateValue;
    public float rosasSocialValue;
    public float rosasCompetentValue;
    public float rosasReliableValue;
    public float rosasScaryValue;
    public float rosasAwkwardValue;
}


public class GlobalVariables : MonoBehaviour
{
    // UI for global settings.
    public string serverURL;
    public int sceneTime;

    // Start is called before the first frame update
    void Start()
    {
        // Bypass variables to global storage.
        StudySettings.serverURL = this.serverURL;
        StudySettings.loginURL = (this.serverURL + "/login/").Replace("//", "/").Replace(":/", "://");
        StudySettings.studyDataURL = (this.serverURL + "/data/").Replace("//", "/").Replace(":/", "://");
        StudySettings.sceneTime = this.sceneTime;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
