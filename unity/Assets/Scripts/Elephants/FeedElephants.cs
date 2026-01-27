using UnityEngine;
using System.Collections;
public class FeedElephants : MonoBehaviour
{
    public GameObject elephant;
    public GameObject fruit;
    public GameObject ArrowPoint;
    public Animator animator;
    public Transform noseHoldPoint;
    public AudioSource audioSource;
    public AudioClip EatSound;
    public float playSoundDelay = 6.0f; // Delay before playing the sound
    public ElephantAnimationTrigger animationTrigger; // let FeedElephants know who it is
    
    public float destroyFruitDelay = 2.0f; // In seconds, time to hide the fruit
    public float slowDuration = 8.0f; // In seconds, total time for the eating sequence and fruit reset

    private bool hasFed = false; 
    private Vector3 fruitInitialPosition;
    private Quaternion fruitInitialRotation;
    private Transform fruitInitialParent;

    private void Start()
    {
        if (fruit != null)
        {
            fruitInitialPosition = fruit.transform.position;
            fruitInitialRotation = fruit.transform.rotation;
            fruitInitialParent = fruit.transform.parent;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasFed) return; // has fed and cannot feed again

        Debug.Log("Fruit OnTriggerEnter");
        Transform t = other.transform;


        if (other.gameObject == fruit)
        {
            hasFed = true; // has fed

            // move fruit to noseHoldPoint
            fruit.transform.SetParent(noseHoldPoint);
            fruit.transform.localPosition = Vector3.zero;
            fruit.transform.localRotation = Quaternion.identity;

            // Set Rigidbody to Kinematic to avoid weird physics when attached
            Rigidbody fruitRb = fruit.GetComponent<Rigidbody>();
            if (fruitRb != null)
            {
                fruitRb.isKinematic = true;
            }

            // play elephant eating animation
            animator.SetTrigger("PlayEating");


            // notify Manager to slow down other elephants
            if (animationTrigger != null)
                ElephantsAnimationManager.Instance.OnElephantAnimationStart(animationTrigger);

            // play eating sound after delay
            if (audioSource != null && EatSound != null)
            {
                StartCoroutine(PlaySoundAfterDelay(playSoundDelay));
            }

            // Hide and then reset the fruit instead of destroying it
            StartCoroutine(ResetFruitAfterDelay());
            // slowDuration 
            StartCoroutine(RestoreSpeedAfterDelay(slowDuration));
        }

    }

    private IEnumerator ResetFruitAfterDelay()
    {
        // Wait until the fruit should disappear
        yield return new WaitForSeconds(destroyFruitDelay);

        // Hide the fruit by disabling its renderer
        Renderer fruitRenderer = fruit.GetComponent<Renderer>();
        if (fruitRenderer != null)
        {
            fruitRenderer.enabled = false;
        }

        // Wait for the remainder of the animation duration
        yield return new WaitForSeconds(slowDuration - destroyFruitDelay);

        // Reset fruit to its original state
        if (fruit != null)
        {
            fruit.transform.SetParent(fruitInitialParent);
            fruit.transform.position = fruitInitialPosition;
            fruit.transform.rotation = fruitInitialRotation;
            if (fruitRenderer != null)
            {
                fruitRenderer.enabled = true; // Make it visible again
            }

            // Restore physics state but disable gravity as requested
            Rigidbody fruitRb = fruit.GetComponent<Rigidbody>();
            if (fruitRb != null)
            {
                fruitRb.isKinematic = false;
                fruitRb.useGravity = false;
                fruitRb.linearVelocity = Vector3.zero;
                fruitRb.angularVelocity = Vector3.zero;
            }
            // show arrow point
            ArrowPoint.SetActive(true);
        }

        // Allow this elephant to eat again
        hasFed = false;
    }

    private IEnumerator DestroyFruitAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(fruit);
    }

    private IEnumerator PlaySoundAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (audioSource != null && EatSound != null)
        {
            audioSource.PlayOneShot(EatSound);
        }
    }
    private IEnumerator RestoreSpeedAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (animationTrigger != null)
            ElephantsAnimationManager.Instance.OnElephantAnimationEnd(animationTrigger);
    }
}
