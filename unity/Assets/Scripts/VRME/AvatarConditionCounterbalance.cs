using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Assigns warm/cold deterministically for the six experimental scenes.
/// Adjacent participant numbers receive complementary assignments, so every
/// participant experiences three warm and three cold scenes and every scene is
/// balanced across each participant pair. Tutorial and transition scenes are excluded.
/// </summary>
public static class AvatarConditionCounterbalance
{
    private static readonly string[] ExperimentalScenes =
    {
        "Attic", "Elephant", "Lake", "Puppies", "SolitaryConfinement", "Tunnel"
    };

    [Serializable]
    private sealed class AssignmentRecord
    {
        public string participantId;
        public string loginId;
        public string sessionId;
        public string scheme;
        public int participantParity;
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
        int sceneIndex = Array.FindIndex(ExperimentalScenes,
            scene => string.Equals(scene, sceneName, StringComparison.OrdinalIgnoreCase));
        if (sceneIndex < 0)
        {
            return false;
        }

        int parity = ParticipantParity(PlayerData.participantId, PlayerData.loginId);
        PlayerData.avatarCondition = ((sceneIndex + parity) % 2 == 0) ? "warm" : "cold";
        PlayerData.counterbalanceScheme = "warm-cold-v1";
        WriteAssignmentRecord(parity);
        Debug.Log("[Counterbalance] scene=" + sceneName +
            ", condition=" + PlayerData.avatarCondition +
            ", participantParity=" + parity + ".");
        return true;
    }

    private static int ParticipantParity(string participantId, string loginId)
    {
        string identity = !string.IsNullOrWhiteSpace(participantId) && participantId != "0"
            ? participantId.Trim()
            : (string.IsNullOrWhiteSpace(loginId) ? "0" : loginId.Trim());
        if (long.TryParse(identity, out long numericId))
        {
            return (int)(Math.Abs(numericId) % 2L);
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in identity)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)(hash & 1u);
        }
    }

    private static void WriteAssignmentRecord(int parity)
    {
        var record = new AssignmentRecord
        {
            participantId = PlayerData.participantId,
            loginId = PlayerData.loginId,
            sessionId = PlayerData.sessionId,
            scheme = PlayerData.counterbalanceScheme,
            participantParity = parity,
            generatedAtUtc = DateTime.UtcNow.ToString("o")
        };
        for (int index = 0; index < ExperimentalScenes.Length; index++)
        {
            record.assignments.Add(new SceneAssignment
            {
                sceneName = ExperimentalScenes[index],
                avatarCondition = ((index + parity) % 2 == 0) ? "warm" : "cold"
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
