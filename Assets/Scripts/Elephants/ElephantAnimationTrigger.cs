using UnityEngine;
using System.Collections;

public class ElephantAnimationTrigger : MonoBehaviour
{
    public Animator animator;
    public AudioSource audioSource;
    public AudioClip trumpetSound;
    public float playSoundDelay = 1.0f; // Delay before playing the sound
    public float cooldownTime = 4.0f; // Cooldown time between triggers

    private float lastTriggerTime = -Mathf.Infinity; // Time when the animation was last triggered

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        // Reset the trigger to ensure a clean state on start
        animator.ResetTrigger("PlayTrumpet");
    }

    public void PlayAnimation()
    {
        if (animator == null) return;

        // Check if cooldown is active
        if (Time.time - lastTriggerTime < cooldownTime)
        {
            Debug.Log("Cooldown active, cannot trigger yet.");
            return;
        }

        lastTriggerTime = Time.time; // Update the last trigger time

        // Set the trigger to start the animation
        animator.SetTrigger("PlayTrumpet");
        Debug.Log("SetTrigger: PlayTrumpet");

        // Notify the manager to slow down other elephants
        ElephantsAnimationManager.Instance.OnElephantAnimationStart(this);

        // Start the coroutine to handle sound and animation end
        StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        // Wait for the specified delay before playing the sound
        yield return new WaitForSeconds(playSoundDelay);

        // Play the sound if AudioSource and AudioClip are assigned
        if (audioSource != null && trumpetSound != null)
        {
            audioSource.PlayOneShot(trumpetSound);
            Debug.Log($"Playing trumpet sound after {playSoundDelay} seconds delay!");
        }

        // Wait for the current animation to finish (get current animation length)
        yield return new WaitForSeconds(GetCurrentAnimationLength());

        // Animation finished, notify the manager to restore speed for other elephants
        ElephantsAnimationManager.Instance.OnElephantAnimationEnd(this);
    }

    private float GetCurrentAnimationLength()
    {
        if (animator == null) return 2.0f; // fallback duration if animator is null
        var state = animator.GetCurrentAnimatorStateInfo(0); // Get info about the current state on layer 0
        // Return the state's length if it's valid, otherwise return fallback
        return state.length > 0 ? state.length : 2.0f;
    }

    public void StopAnimation(bool hardStop = true)
    {
        if (animator != null)
        {
            if (hardStop)
            {
                // Completely disable the animator
                animator.enabled = false;
            }
            else
            {
                // Just pause the animation
                animator.speed = 0f;
            }
            Debug.Log("[Animation] Animation stopped.");
        }
    }

    public void Resume()
    {
        if (animator != null)
        {
            // Re-enable and set speed back to normal
            animator.enabled = true;
            animator.speed = 1f;
            Debug.Log("[Animation] Animator resumed.");
        }
    }
}