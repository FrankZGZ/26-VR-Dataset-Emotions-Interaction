using UnityEngine;

[RequireComponent(typeof(Animator))]
public class DogAnimationTrigger : MonoBehaviour
{

    public DogMotion2 dogMotion2;  
    public float interactionCooldown = 4.0f;
    private float lastTriggerTime = -Mathf.Infinity;

    void Awake()
    {
        if (dogMotion2 == null)
        {
            dogMotion2 = GetComponent<DogMotion2>();
            if (dogMotion2 == null)
            {
                // Log an error if the DogMotion2 script cannot be found.
                // This script requires DogMotion2 to function correctly.
                Debug.LogError("DogAnimationTrigger could not find the required DogMotion2 script on this GameObject!", this);
            }
        }
    }

    public void PlayAnimation()
    {
        if (dogMotion2 == null)
            return;

        // If the dog is preparing or interacting, do not trigger
        if (dogMotion2.IsBusy)
        {
            Debug.Log("Dog is busy, ignoring trigger.");
            return;
        }

        // Check cooldown
        if (Time.time - lastTriggerTime < interactionCooldown)
        {
            Debug.Log("Triggered too quickly, please try again later.");
            return;
        }

        lastTriggerTime = Time.time;
        Debug.Log("DogAnimationTrigger: Starting interaction");
        dogMotion2.StartInteraction();
    }

   public void StopAnimation(bool hardStop = true)
    {
        if (dogMotion2 == null) return;

        Debug.Log("DogAnimationTrigger: Ending interaction");
        dogMotion2.EndInteraction();
    }
}


