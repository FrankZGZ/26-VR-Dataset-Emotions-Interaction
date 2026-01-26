using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
public class EndScenes : MonoBehaviour
{
    public Button submitButton;
    void Start()
    {
        // Button listener.
        submitButton.onClick.AddListener(delegate { SubmitButtonClickEvent(); });
    }

    void SubmitButtonClickEvent()
    {
        EndEditorPlay();
    }

    void EndEditorPlay()
    {
        // End editor play.
        UnityEditor.EditorApplication.isPlaying = false;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
