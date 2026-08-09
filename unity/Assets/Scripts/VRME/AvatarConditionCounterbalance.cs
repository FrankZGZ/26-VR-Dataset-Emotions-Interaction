using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Assigns warm/cold per experimental scene from one of 18 counterbalanced
/// 3-warm/3-cold patterns. There are C(6,3)=20 ways to split the six scenes
/// into 3 warm + 3 cold; this excludes the two fully-blocked splits
/// ({Attic,Elephant,Lake}=warm / {Attic,Elephant,Lake}=cold, i.e. WWWCCC and
/// CCCWWW by scene identity), which have only one warm&lt;-&gt;cold transition
/// and are the least informative pattern for testing prior-exposure/order
/// effects. Every scene index is still warm in exactly 9 of the 18 remaining
/// patterns, so the per-scene warm/cold split stays exactly balanced (54
/// participants = 3 full cycles through all 18 patterns -&gt; each scene is
/// warm for exactly 27 participants and cold for the other 27).
///
/// Pattern is selected from the numeric participantId (cycling every 18), so
/// recruiting participants with consecutive IDs keeps the design balanced
/// even if the final N stops short of a full multiple of 18.
///
/// NOTE ON SCENE-ORDER INDEPENDENCE: scene *presentation order* is assigned
/// separately in LatinSquare.cs / MySceneLoader.cs via participantId % 6.
/// Because 6 divides 18, participants sharing an order row (id, id+6, id+12)
/// always land on the same 3 of these 18 patterns, fixed and evenly spaced by
/// construction — this file alone cannot make presentation order and warm/cold
/// pattern fully orthogonal across all 6x18 combinations. If that matters for
/// the analysis, the order assignment in LatinSquare.cs/MySceneLoader.cs would
/// need to be driven from the same combined (order, pattern) table instead of
/// participantId % 6 computed in isolation.
/// </summary>
public static class AvatarConditionCounterbalance
{
    private static readonly string[] ExperimentalScenes =
    {
        "Attic", "Elephant", "Lake", "Puppies", "SolitaryConfinement", "Tunnel"
    };

    // All C(6,3)=20 three-warm/three-cold splits over ExperimentalScenes,
    // excluding the two fully-blocked splits {0,1,2} and {3,4,5}. Each value
    // is an index into ExperimentalScenes; scenes not listed are cold.
    private static readonly int[][] WarmSceneIndexPatterns =
    {
        new[] { 0, 1, 3 }, new[] { 0, 1, 4 }, new[] { 0, 1, 5 },
        new[] { 0, 2, 3 }, new[] { 0, 2, 4 }, new[] { 0, 2, 5 },
        new[] { 0, 3, 4 }, new[] { 0, 3, 5 }, new[] { 0, 4, 5 },
        new[] { 1, 2, 3 }, new[] { 1, 2, 4 }, new[] { 1, 2, 5 },
        new[] { 1, 3, 4 }, new[] { 1, 3, 5 }, new[] { 1, 4, 5 },
        new[] { 2, 3, 4 }, new[] { 2, 3, 5 }, new[] { 2, 4, 5 },
    };

    [Serializable]
    private sealed class AssignmentRecord
    {
        public string participantId;
        public string loginId;
        public string sessionId;
        public string scheme;
        public int patternIndex;
        public string generatedAtUtc;
        public List<SceneAssignment> assignments = new List<SceneAssignment>();
    }

    [Serializable]
    private sealed class SceneAssignment
    {
        public string sceneName;
        public string avatarCondition;
    }

    public static bool ApplyForActiveScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        if (string.Equals(sceneName, "Tutorial_Interaction", StringComparison.OrdinalIgnoreCase))
        {
            // Every participant receives the same concise, supportive tutorial.
            // The six formal scenes still receive their counterbalanced condition below.
            PlayerData.avatarCondition = "warm";
            Debug.Log("[Counterbalance] Tutorial condition fixed to warm.");
            return true;
        }

        int sceneIndex = Array.FindIndex(ExperimentalScenes,
            scene => string.Equals(scene, sceneName, StringComparison.OrdinalIgnoreCase));
        if (sceneIndex < 0)
        {
            return false;
        }

        int patternIndex = ParticipantPatternIndex(PlayerData.participantId, PlayerData.loginId);
        bool isWarm = Array.IndexOf(WarmSceneIndexPatterns[patternIndex], sceneIndex) >= 0;
        PlayerData.avatarCondition = isWarm ? "warm" : "cold";
        PlayerData.counterbalanceScheme = "warm-cold-18pattern-v1";
        WriteAssignmentRecord(patternIndex);
        Debug.Log("[Counterbalance] scene=" + sceneName +
            ", condition=" + PlayerData.avatarCondition +
            ", patternIndex=" + patternIndex + " of " + WarmSceneIndexPatterns.Length + ".");
        return true;
    }

    private static int ParticipantPatternIndex(string participantId, string loginId)
    {
        string identity = !string.IsNullOrWhiteSpace(participantId) && participantId != "0"
            ? participantId.Trim()
            : (string.IsNullOrWhiteSpace(loginId) ? "0" : loginId.Trim());
        if (long.TryParse(identity, out long numericId))
        {
            return (int)(Math.Abs(numericId) % WarmSceneIndexPatterns.Length);
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in identity)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)(hash % (uint)WarmSceneIndexPatterns.Length);
        }
    }

    private static void WriteAssignmentRecord(int patternIndex)
    {
        var record = new AssignmentRecord
        {
            participantId = PlayerData.participantId,
            loginId = PlayerData.loginId,
            sessionId = PlayerData.sessionId,
            scheme = PlayerData.counterbalanceScheme,
            patternIndex = patternIndex,
            generatedAtUtc = DateTime.UtcNow.ToString("o")
        };

        int[] warmIndices = WarmSceneIndexPatterns[patternIndex];
        for (int index = 0; index < ExperimentalScenes.Length; index++)
        {
            record.assignments.Add(new SceneAssignment
            {
                sceneName = ExperimentalScenes[index],
                avatarCondition = Array.IndexOf(warmIndices, index) >= 0 ? "warm" : "cold"
            });
        }

        string directory = Path.Combine(Application.persistentDataPath,
            "CounterbalanceData", SafePathPart(PlayerData.participantId));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            "AvatarConditionAssignment_" + SafePathPart(PlayerData.sessionId) + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(record, true));
    }

    private static string SafePathPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }
}
