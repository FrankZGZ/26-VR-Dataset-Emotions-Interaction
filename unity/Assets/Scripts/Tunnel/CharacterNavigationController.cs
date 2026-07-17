using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class CharacterNavigationController : MonoBehaviour
{
    //[ReadOnly] public string destination;
    [ReadOnly] public Vector3 destinationVec;
    Vector3 lastPosition;
    public bool reachedDestination;
    public float minDistance = 1.5f;
    public float rotationSpeed;
    public float minSpeed, maxSpeed;
    public float movementSpeed;
    public float movementSpeedAnimationFactor = 1f;
    public Animator animator;
    public DogMotion dogMotion;
    public DogMotion2 dogMotion2;

    // =====================
    // Added: Dog avoidance feature
    // Dogs will avoid each other when too close
    [Header("Dog Avoidance Settings")]
    public float avoidDistance = 0.7f; // Minimum distance to keep from other dogs
    public float avoidStrength = 1.0f; // How strongly to avoid other dogs
    // =====================

    Vector3 velocity;

    [ReadOnly] public float destinationDistance = 0.0f;

    private void Start()
    {
        movementSpeed = Random.Range(minSpeed, maxSpeed);
    }

    private void Update()
    {
        if (animator == null)
        {
            return;
        }

        int stateHashCached = Animator.StringToHash("Base Layer.Walking");
        if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == stateHashCached) { 
            if (transform.position != destinationVec)
            {
                Vector3 destinationDirection = destinationVec - transform.position;
                destinationDirection.y = 0;

                // =====================
                // Added: Avoidance logic for other dogs
                // Calculate a repulsion vector if other dogs are too close
                Vector3 avoidVector = Vector3.zero;
                CharacterNavigationController[] allDogs = FindObjectsOfType<CharacterNavigationController>();
                foreach (var otherDog in allDogs)
                {
                    if (otherDog == this) continue;
                    float dist = Vector3.Distance(transform.position, otherDog.transform.position);
                    if (dist < avoidDistance && dist > 0.01f)
                    {
                        // The closer the other dog, the stronger the repulsion
                        Vector3 away = (transform.position - otherDog.transform.position).normalized;
                        avoidVector += away * (avoidStrength * (avoidDistance - dist) / avoidDistance);
                    }
                }
                // Add the avoidance vector to the destination direction
                destinationDirection += avoidVector;
                // =====================

                destinationDistance = destinationDirection.magnitude;

                if (destinationDistance >= minDistance)
                {
                    reachedDestination = false;
                    Quaternion targetRotation = Quaternion.LookRotation(destinationDirection);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                    transform.Translate(Vector3.forward * movementSpeed * Time.deltaTime);
                }
                else
                {
                    reachedDestination = true;
                    animator.SetBool("playIdle", true);
                    animator.SetBool("playWalking", true);
                    // animator.SetFloat("speed", movementSpeed * movementSpeedAnimationFactor);
                    if (dogMotion != null)
                    {
                        dogMotion.tiggerDoneWalking();
                    }
                    if (dogMotion2 != null)
                    {
                        dogMotion2.tiggerDoneWalking();
                    }
                }

                velocity = (transform.position - lastPosition) / Time.deltaTime;
                velocity.y = 0;
                var velocityMagnitude = velocity.magnitude;
                velocity = velocity.normalized;
                var fwdDotProduct = Vector3.Dot(transform.forward, velocity);
                var rightDotProduct = Vector3.Dot(transform.right, velocity);
            }
        }
    }

    public void SetDestination(Vector3 destination)
    {
        this.destinationVec = destination;
        reachedDestination = false;
    }

    void OnCollisionEnter(Collision collision)
    {
//        Debug.Log(collision.gameObject.name);
        if (collision.gameObject.name == "WalkingArea")
            return;

        reachedDestination = true;
        animator.SetBool("playIdle", true);
        animator.SetBool("playWalking", true);
        // animator.SetFloat("speed", movementSpeed * movementSpeedAnimationFactor);
        if (dogMotion != null)
        {
            dogMotion.tiggerDoneWalking();
        }
        if (dogMotion2 != null)
        {
            dogMotion2.tiggerDoneWalking();
        }
    }
}
