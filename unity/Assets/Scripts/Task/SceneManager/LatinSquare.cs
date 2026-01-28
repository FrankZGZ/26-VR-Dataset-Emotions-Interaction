using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

//this script is used to generate and save the Latin square sequence to a JSON file and a text file
//the JSON file is used to store the Latin square sequence for each participant
//the text file is used to store all the Latin square sequences for all participants
//the Latin square sequence is a list of integers that represent the sequence of the Latin square

public class LatinSquare : MonoBehaviour
{
    public int participantCount = 84;        
    public int latinSquareSize = 6;          
    public string outputFileName = "LatinSquare.json";
    public string outputTxtName = "AllLatinSquares.txt";

    // fixed 6×6 Balanced Latin Square from https://www.math.uni-hamburg.de/home/m-schmidt/latin-squares.html
    private static readonly int[][] baseLatinSquare = new int[][]
    {
        new int[] {1, 2, 6, 3, 5, 4},
        new int[] {2, 3, 1, 4, 6, 5},
        new int[] {3, 4, 2, 5, 1, 6},
        new int[] {4, 5, 3, 6, 2, 1},
        new int[] {5, 6, 4, 1, 3, 2},
        new int[] {6, 1, 5, 2, 4, 3}
    };

    // Start is called before the first frame update
    void Start()
    {
        GenerateAndSaveLatinSquares();
    }

    public void GenerateAndSaveLatinSquares()
    {
        var allSequences = new List<List<int>>();

        // Assign fixed Latin square to participants
        for (int pid = 0; pid < participantCount; pid++)
        {
            int rowIndex = pid % latinSquareSize;
            allSequences.Add(baseLatinSquare[rowIndex].ToList());
        }

        WriteSequencesToJson(allSequences);
        WriteAllLatinSquaresToTxt(allSequences);
        Debug.Log($"[LatinSquare] Assigned fixed Latin square to {participantCount} participants.");
    }

    // Write the sequences to a JSON file
    private void WriteSequencesToJson(List<List<int>> allSequences)
    {
        string filePath = Path.Combine(Application.persistentDataPath, outputFileName);
        using (var sw = new StreamWriter(filePath, false))
        {
            sw.WriteLine("[");
            for (int i = 0; i < allSequences.Count; i++)
            {
                string line = "  [" + string.Join(", ", allSequences[i]) + "]";
                if (i < allSequences.Count - 1) line += ",";
                sw.WriteLine(line);
            }
            sw.WriteLine("]");
        }
        Debug.Log($"[LatinSquare] LatinSquare.json written to: {filePath}");
    }

    // Write all Latin squares to a text file
    private void WriteAllLatinSquaresToTxt(List<List<int>> allSequences)
    {
        string logPath = Path.Combine(Application.persistentDataPath, outputTxtName);
        List<string> lines = new List<string>();
        for (int pid = 0; pid < allSequences.Count; pid++)
        {
            var rowList = allSequences[pid];
            string line = $"participantId: {pid}, sequence: {string.Join(", ", rowList)}";
            lines.Add(line);
        }
        File.WriteAllLines(logPath, lines);
        Debug.Log($"[LatinSquare] Write {allSequences.Count} participantId's Latin squares to: {logPath}");
    }
}
