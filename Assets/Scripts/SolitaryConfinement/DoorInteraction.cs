using UnityEngine;
using System.Collections;
using Oculus.Haptics;
public class DoorInteraction : MonoBehaviour
{
    public enum HandSide
    {
        Left = 0,
        Right = 1
    }

    public HapticClip clip;
    public Transform leftAnchor;
    public Transform rightAnchor;
    public bool isLooping = false; 
    private HapticClipPlayer leftPlayer;
    private HapticClipPlayer rightPlayer;
    public AudioSource audioSource;
    public AudioClip knockSound;
    public GameObject Baseball;
    public GameObject Book;
    public GameObject Cup;
    private void Start()
    {
        EnsurePlayers();
    }

    private void EnsurePlayers()
    {
        // Allow external scripts to trigger door feedback even if Start() hasn't run yet.
        if (leftPlayer == null) leftPlayer = new HapticClipPlayer(clip);
        if (rightPlayer == null) rightPlayer = new HapticClipPlayer(clip);

        leftPlayer.isLooping = isLooping;
        rightPlayer.isLooping = isLooping;
    }

    /// <summary>
    /// Trigger the same feedback as if a controller hand touched the door trigger.
    /// Useful for replay-driven interactions (e.g., BodyPosePlayer hand touches).
    /// </summary>
    public void TriggerByHand(HandSide side)
    {
        EnsurePlayers();

        if (side == HandSide.Left)
        {
            leftPlayer.Play(Controller.Left);
            Debug.Log("Left controller touched.");
        }
        else
        {
            rightPlayer.Play(Controller.Right);
            Debug.Log("Right controller touched.");
        }

        PlayAudio();
    }

    /// <summary>Trigger only the audio (same as thrown objects in OnTriggerEnter).</summary>
    public void TriggerByObject()
    {
        PlayAudio();
    }

    private void OnTriggerEnter(Collider other)
    {
        Transform t = other.transform;

        if (leftAnchor != null && t.IsChildOf(leftAnchor))
        {
            TriggerByHand(HandSide.Left);
        }
        else if (rightAnchor != null && t.IsChildOf(rightAnchor))
        {
            TriggerByHand(HandSide.Right);
        }
        // Check if the colliding object in the toThrow array
        else if (other.gameObject == Baseball || other.gameObject == Book || other.gameObject == Cup)
        {
            TriggerByObject();
        }
    }

    private void PlayAudio()
    {
        if (audioSource != null && knockSound != null)
        {
            audioSource.PlayOneShot(knockSound);
        }
    }
}
