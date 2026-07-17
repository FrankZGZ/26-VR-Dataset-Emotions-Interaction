using System;
using System.Reflection;
using UnityEngine;

public class DogMotion2 : MonoBehaviour
{
    public Transform lookAtTarget;      // Drag in the main camera (or player)
    public GameObject walkingArea;
    public Animator animator;

    // Time calculation (used for random logic)
    private readonly DateTime epochStart = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);
    private double lastChange = 0;
    private double now;
    public long cooldown = 0;

    // Interaction control
    private bool isInteracting = false;
    private bool isPreparingInteraction = false;
    private Quaternion targetRotation;
    private float rotationThreshold = 1f;
    public float lookSpeed = 2f;

    // -- Added: Sit down after long period of inactivity --
    [Tooltip("If inactive for more than this many seconds, the dog will sit indefinitely")]
    public float inactivityThreshold = 15f;
    private float lastInteractionTime;  // Measured using Time.time
    
    // Navigation controller reference
    private CharacterNavigationController cnc;
    
    public bool IsBusy => isPreparingInteraction || isInteracting;

    // Interaction control of chasing the ball
    public GameObject ball;
    public Transform mouthPosition; // Position of the dog mouth to catch the ball
    [Tooltip("Horizontal dog-to-ball distance at which the dog catches the fetched ball.")]
    public float catchBallDistance = 0.9f;
    [Tooltip("Horizontal distance from the player at which the dog drops the fetched ball.")]
    public float returnDropDistance = 0.3f;
    [Tooltip("Straight-line speed while carrying the ball back to the player.")]
    public float returnMoveSpeed = 1.5f;
    [Tooltip("Seconds the ball is allowed to fly before the dog starts chasing it.")]
    public float fetchStartDelay = 0.35f;
    private enum DogState { Normal, ChasingBall, ReturningBall }
    private DogState dogState = DogState.Normal;
    private Vector3 fetchTarget;

    public bool HasCaughtFetchedBall { get; private set; }
    public bool HasReturnedBallToPlayer { get; private set; }
    public bool IsCarryingFetchedBall => dogState == DogState.ReturningBall && ball != null;
    public string FetchState => dogState.ToString();

    public bool canFetchBall = false;

    private bool isPreparingFetch = false;
    private GameObject fetchBallTarget = null;

    // record the current walking animation state, avoid setting it repeatedly
    private bool isWalkingAnimPlaying = false;

    void Start()
    {
        now = (DateTime.UtcNow - epochStart).TotalMilliseconds;
        lastChange = now - 1;
        lastInteractionTime = Time.time;

        // Initialize navigation controller reference
        cnc = GetComponent<CharacterNavigationController>();
        if (cnc != null)
            cnc.minDistance = 0.5f;

        // Force start in Idle state
        animator.Play("Idle", 0, 0f);
        animator.SetBool("playIdle", true);

        // Ignore collision between dog and ball
        if (ball != null)
        {
            Collider dogCol = GetComponent<Collider>();
            Collider ballCol = ball.GetComponent<Collider>();
            if (dogCol != null && ballCol != null)
            {
                Physics.IgnoreCollision(dogCol, ballCol);
            }
        }
    }

    void Update()
    {
        now = (DateTime.UtcNow - epochStart).TotalMilliseconds;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        int hash = st.fullPathHash;

        // ====== Prevent the dog from repeatedly sitting down/getting up, reset parameters ======
        if (hash == Animator.StringToHash("Base Layer.Sitting Down"))
        {
            animator.SetBool("playSittingDown", false);
        }
        if (hash == Animator.StringToHash("Base Layer.Getting Up"))
        {
            animator.SetBool("playGettingUp", false);
        }

        // ====== 1. Ball logic - highest priority ======
        // Check if a new ball has been thrown
        if (canFetchBall && dogState == DogState.Normal
            && TennisBall.lastThrownBall != null && !isPreparingFetch
            && Time.time - TennisBall.lastThrownBall.fetchThrownAt >= Mathf.Max(0f, fetchStartDelay))
        {
            fetchBallTarget = TennisBall.lastThrownBall.gameObject;
            if (fetchBallTarget != null)
            {
                Debug.Log("Ball throw detected. Start chasing immediately!");
                // A real throw has priority over petting/highlight interactions. These
                // flags can otherwise remain set while the dog turns toward the user,
                // causing the fetch event to sit unconsumed indefinitely.
                isPreparingInteraction = false;
                isInteracting = false;
                ClearAllAnimationFlags();
                animator.SetBool("playWalking", true);
                animator.speed = 1.0f;
                ball = fetchBallTarget;
                dogState = DogState.ChasingBall;
                HasCaughtFetchedBall = false;
                HasReturnedBallToPlayer = false;
                isWalkingAnimPlaying = true;
                SetMovementSpeed(1.3f);
                if (cnc != null)
                {
                    cnc.enabled = false;
                }
                isPreparingFetch = false;
                fetchBallTarget = null;
            }
            return;
        }

        // Chasing ball state
        if (dogState == DogState.ChasingBall && ball != null)
        {
            Debug.Log("Chasing the ball...");

            // Only set animation when entering state or if animation is not playing
            if (!isWalkingAnimPlaying || !animator.GetCurrentAnimatorStateInfo(0).IsName("Walking"))
            {
                Debug.Log("Chasing: Setting walking animation and speed");
                ClearAllAnimationFlags();
                animator.SetBool("playWalking", true);
                animator.speed = 1.0f;
                SetMovementSpeed(1.3f);
                isWalkingAnimPlaying = true;
            }

            // Use horizontal distance because the dog's root and the tennis ball
            // sit at different heights. A 3D threshold can leave the dog circling
            // the ball forever even when its mouth is already beside it.
            float horizontalBallDistance = HorizontalDistance(transform.position, ball.transform.position);
            float catchDistance = Mathf.Max(0.35f, catchBallDistance);
            if (horizontalBallDistance > catchDistance)
            {
                // Fetch movement must not depend on the Animator reaching an
                // exact state name. Move directly toward the live ball target.
                Vector3 toBall = ball.transform.position - transform.position;
                toBall.y = 0f;
                if (toBall.sqrMagnitude > 0.000001f)
                {
                    Vector3 direction = toBall.normalized;
                    Quaternion targetFacing = Quaternion.LookRotation(direction, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        targetFacing,
                        rotationThreshold * 180f * Time.deltaTime);
                    float remainingTravel = Mathf.Max(0f, horizontalBallDistance - catchDistance);
                    float step = Mathf.Min(1.3f * Time.deltaTime, remainingTravel);
                    transform.position += direction * step;
                }
            }
            if (horizontalBallDistance <= catchDistance)
            {
                Debug.Log("Caught the ball! Start returning");
                TennisBall caughtTennisBall = ball.GetComponent<TennisBall>();
                if (caughtTennisBall != null)
                {
                    caughtTennisBall.BeginDogCarry();
                }
                Rigidbody caughtBallBody = ball.GetComponent<Rigidbody>();
                if (caughtBallBody != null)
                {
                    if (!caughtBallBody.isKinematic)
                    {
                        caughtBallBody.linearVelocity = Vector3.zero;
                        caughtBallBody.angularVelocity = Vector3.zero;
                    }
                    caughtBallBody.isKinematic = true;
                }
                ball.transform.position = mouthPosition != null
                    ? mouthPosition.position
                    : transform.position + transform.forward * 0.8f + Vector3.up * 0.2f;
                HasCaughtFetchedBall = true;
                InteractionTracker caughtTracker = ball.GetComponent<InteractionTracker>();
                if (caughtTracker != null)
                {
                    caughtTracker.RecordExternalEvent("dog_fetch:caught", gameObject.name);
                }
                dogState = DogState.ReturningBall;
                isWalkingAnimPlaying = false; // Reset for returning state

                // Return movement is driven directly below. The normal navigation
                // controller only moves in an exact Walking animator state and also
                // adds dog avoidance, both of which can stall or bend this fetch path.
                if (cnc != null)
                {
                    cnc.enabled = false;
                }

                // =================================================================
                // Added: Set initial speed for ReturningBall state immediately
                Debug.Log("Setting initial returning speed");
                ClearAllAnimationFlags(); // Clear flags before setting new animation/speed
                animator.SetBool("playWalking", true);
                animator.speed = 1.0f; // Consistent animation speed for returning
                SetMovementSpeed(1.5f);  // <--- ADJUSTED: Reduced return speed
                isWalkingAnimPlaying = true; // Mark as playing
                // =================================================================

                // Immediately start returning to the player
                if (lookAtTarget != null && cnc != null)
                {
                    Vector3 playerPos = lookAtTarget.position;
                    playerPos.y = transform.position.y;
                    cnc.SetDestination(playerPos);
                    Debug.Log("Start returning to the player");
                }
            }
            return;
        }

        // Returning ball state
        if (dogState == DogState.ReturningBall && ball != null)
        {
            Debug.Log("Returning the ball...");

            // Ball follows in front of the dog's mouth
            Vector3 mouthPos = mouthPosition != null ? mouthPosition.position : (transform.position + transform.forward * 0.8f + Vector3.up * 0.2f);
            ball.transform.position = mouthPos;
            Rigidbody carriedBallBody = ball.GetComponent<Rigidbody>();
            if (carriedBallBody != null)
            {
                carriedBallBody.isKinematic = true;
            }

            // XZ-plane distance: sqrt((dogX-playerX)^2 + (dogZ-playerZ)^2).
            float distanceToPlayer = lookAtTarget != null ? HorizontalDistance(transform.position, lookAtTarget.position) : 999f;
            Debug.Log("Horizontal distance to player: " + distanceToPlayer);
            float stopDistance = Mathf.Max(0.05f, returnDropDistance);
            if (distanceToPlayer <= stopDistance)
            {
                Debug.Log("Returned to player, drop the ball");

                // Drop the ball
                GameObject returnedBall = ball;
                Rigidbody returnedBallBody = returnedBall.GetComponent<Rigidbody>();
                if (returnedBallBody != null)
                {
                    returnedBallBody.isKinematic = false;
                }
                HasReturnedBallToPlayer = true;
                InteractionTracker returnedTracker = returnedBall.GetComponent<InteractionTracker>();
                if (returnedTracker != null)
                {
                    returnedTracker.RecordExternalEvent("dog_fetch:returned_to_player", gameObject.name);
                }
                TennisBall returnedTennisBall = returnedBall.GetComponent<TennisBall>();
                if (returnedTennisBall != null)
                {
                    Vector3 playerPosition = lookAtTarget != null
                        ? lookAtTarget.position
                        : transform.position - transform.forward;
                    returnedTennisBall.ReturnToPlayerPickupPoint(playerPosition, transform.position);
                }
                else
                {
                    returnedBall.transform.position = transform.position + transform.forward * 0.7f + Vector3.up * 0.1f;
                }
                ball = null;
                dogState = DogState.Normal;
                isWalkingAnimPlaying = false; // Reset for normal state
                if (cnc != null)
                {
                    cnc.enabled = true;
                }

                // Restore to normal state
                animator.speed = 1f;
                SetMovementSpeed(1f);

                // Reset animation
                ClearAllAnimationFlags();
                animator.ResetTrigger("playFetchBall"); // End fetchBall trigger, go back to Idle
                animator.SetBool("playIdle", true);
                lastChange = now;
                cooldown = 1000; // Short rest

                // Navigate to the player's side
                if (lookAtTarget != null && cnc != null)
                {
                    cnc.SetDestination(lookAtTarget.position);
                }
            }
            else
            {
                // Only set animation when entering state or if animation is not playing
                if (!isWalkingAnimPlaying || !animator.GetCurrentAnimatorStateInfo(0).IsName("Walking"))
                {
                    Debug.Log("Returning: Setting walking animation and speed");
                    ClearAllAnimationFlags();
                    animator.SetBool("playWalking", true);
                    animator.speed = 1.0f; // Consistent animation speed for returning
                    SetMovementSpeed(1.5f); // <--- ADJUSTED: Reduced return speed
                    isWalkingAnimPlaying = true;
                }

                // Move straight toward the player's current XZ position. Move no
                // farther than the remaining distance so the dog stops at the
                // configured radius instead of overshooting and oscillating.
                if (lookAtTarget != null)
                {
                    Vector3 toPlayer = lookAtTarget.position - transform.position;
                    toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude > 0.000001f)
                    {
                        Vector3 direction = toPlayer.normalized;
                        Quaternion targetFacing = Quaternion.LookRotation(direction, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(
                            transform.rotation,
                            targetFacing,
                            rotationThreshold * 180f * Time.deltaTime);

                        float remainingTravel = Mathf.Max(0f, distanceToPlayer - stopDistance);
                        float step = Mathf.Min(Mathf.Max(0.1f, returnMoveSpeed) * Time.deltaTime, remainingTravel);
                        transform.position += direction * step;
                    }
                }
            }
            return;
        }

        // ====== 2. Interaction logic ======
        // If interaction ended but still in interaction animation state, force back to Idle
        if (!isInteracting && st.IsTag("Interaction"))
        {
            animator.Play("Idle", 0, 0f);
            animator.SetBool("playIdle", true);
            lastChange = now;
            cooldown = 0;
            return;
        }

        // Preparing for interaction (turn to face the player)
        if (isPreparingInteraction)
        {
            Debug.Log("Preparing interaction, turning...");
            if (Quaternion.Angle(transform.rotation, targetRotation) > rotationThreshold)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    lookSpeed * Time.deltaTime * 100f
                );
                return;
            }
            else
            {
                isPreparingInteraction = false;
                isInteracting = true;
                animator.SetBool("playInteraction", true);
                return;
            }
        }

        // Interacting
        if (isInteracting)
        {
            Debug.Log("Interacting...");
            if (!st.IsTag("Interaction") && st.normalizedTime >= 1f)
                EndInteraction();
            return;
        }

        // ====== 3. Long inactivity check ======
        bool isIdleOrWalk = hash == Animator.StringToHash("Base Layer.Idle")
                          || hash == Animator.StringToHash("Base Layer.Walking");
        if (Time.time - lastInteractionTime > inactivityThreshold && isIdleOrWalk)
        {
            animator.SetBool("playSittingDown", true);
            cooldown = long.MaxValue;
            return;
        }

        // ====== 4. Random behavior logic ======
        if (now - lastChange >= cooldown)
        {
            Debug.Log("Performing random behavior");
            ClearAllAnimationFlags();

            float r = UnityEngine.Random.value;

            if (hash == Animator.StringToHash("Base Layer.Sitting Down"))
            {
                animator.SetBool("playSitting", true);
                cooldown = (long)UnityEngine.Random.Range(1000f, 5000f);
            }
            else if (hash == Animator.StringToHash("Base Layer.Sitting"))
            {
                animator.SetBool("playGettingUp", true);
                cooldown = 300;
                animator.SetFloat("timeUp", cooldown);
            }
            else if (hash == Animator.StringToHash("Base Layer.Getting Up"))
            {
                animator.SetBool("playIdle", true);
                cooldown = (long)UnityEngine.Random.Range(500f, 5000f);
            }
            else if (isIdleOrWalk)
            {
                if (r > 0.5f)
                {
                    animator.SetBool("playWalking", true);
                    cooldown = (long)UnityEngine.Random.Range(2000f, 5000f);
                    Vector3 dest = GetPointInSpawnArea();
                    cnc?.SetDestination(dest);
                }
                else if (r > 0.2f)
                {
                    animator.SetBool("playSittingDown", true);
                    cooldown = 200;
                    animator.SetFloat("timeDown", cooldown);
                }
                else
                {
                    animator.SetBool("playIdle", true);
                    cooldown = (long)UnityEngine.Random.Range(500f, 5000f);
                }
            }

            lastChange = now;
        }

        // ...在狗状态变为非walking时，重置isWalkingAnimPlaying...
        if (dogState != DogState.ChasingBall && dogState != DogState.ReturningBall && isWalkingAnimPlaying)
        {
            Debug.Log("Not chasing or returning, resetting isWalkingAnimPlaying");
            isWalkingAnimPlaying = false;
        }
    }

    // Helper method to clear all animation flags
    private void ClearAllAnimationFlags()
    {
        animator.SetBool("playSitting", false);
        animator.SetBool("playSittingDown", false);
        animator.SetBool("playGettingUp", false);
        animator.SetBool("playIdle", false);
        animator.SetBool("playWalking", false);
        animator.SetBool("playInteraction", false);
    }

    // Callback when navigation ends
    public void tiggerDoneWalking()
    {
        cooldown = 0;
    }

    private Vector3 GetPointInSpawnArea()
    {
        Vector3 c = walkingArea.transform.position;
        Vector3 h = walkingArea.transform.localScale * 0.5f;
        return new Vector3(
            UnityEngine.Random.Range(c.x - h.x, c.x + h.x),
            c.y,
            UnityEngine.Random.Range(c.z - h.z, c.z + h.z)
        );
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Triggered by user interaction
    public void StartInteraction()
    {
        // If the dog is chasing or returning the ball, do not allow interaction
        if (isInteracting || isPreparingInteraction || dogState == DogState.ChasingBall || dogState == DogState.ReturningBall) return;
        Debug.Log("StartInteraction called");
        isPreparingInteraction = true;
        lastChange = now;
        cooldown = long.MaxValue;

        // Record interaction time
        lastInteractionTime = Time.time;

        // Clear all animation flags
        ClearAllAnimationFlags();

        // -- Added: Stop current navigation to prevent drift --
        if (cnc != null)
            cnc.SetDestination(transform.position);

        // Calculate target orientation
        if (lookAtTarget != null)
        {
            Vector3 dir = lookAtTarget.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                targetRotation = Quaternion.LookRotation(dir);
        }
    }

    // End interaction and resume random behavior
    public void EndInteraction()
    {
        animator.SetBool("playInteraction", false);
        isInteracting = false;
        cooldown = 0;
        lastChange = now;
        lastInteractionTime = Time.time;
    }

    // General speed control method
    private void SetMovementSpeed(float speed)
    {
        // 1. Try NavMeshAgent
        var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null)
            navAgent.speed = speed;
            
        // 2. Try CharacterNavigationController's various possible properties
        if (cnc != null)
        {
            // Try common speed property names
            var type = cnc.GetType();
            
            // Try speed field
            var speedField = type.GetField("speed");
            if (speedField != null && speedField.FieldType == typeof(float))
                speedField.SetValue(cnc, speed);
                
            // Try Speed property
            var speedProperty = type.GetProperty("Speed");
            if (speedProperty != null && speedProperty.CanWrite)
                speedProperty.SetValue(cnc, speed);
                
            // Try moveSpeed field
            var moveSpeedField = type.GetField("moveSpeed");
            if (moveSpeedField != null && moveSpeedField.FieldType == typeof(float))
                moveSpeedField.SetValue(cnc, speed);
                
            // Try walkSpeed field
            var walkSpeedField = type.GetField("walkSpeed");
            if (walkSpeedField != null && walkSpeedField.FieldType == typeof(float))
                walkSpeedField.SetValue(cnc, speed);

            // Try movementSpeed field (for CharacterNavigationController)
            var movementSpeedField = type.GetField("movementSpeed");
            if (movementSpeedField != null && movementSpeedField.FieldType == typeof(float))
                movementSpeedField.SetValue(cnc, speed);
        }
    }
}
