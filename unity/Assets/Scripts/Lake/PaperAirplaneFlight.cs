using UnityEngine;

public class PaperAirplaneFlight : MonoBehaviour
{
    [Range(0, 9.81f)]
    public float buoyancy = 8f;

    [Header("Head Correction")]
    public Vector3 forwardAxisCorrection = new Vector3(-90f, 90f, 0f);
    public GameObject splashObject;
    [Header("Trail Settings")]
    public GameObject trailObject;  // Reference to the Trail parent object
    
    private Transform paperAirplane;
    private Rigidbody _rigidbody;

    private bool isFlying = false;
    private bool isFallingInWater = false;  // Track if it's falling in water instead of permanent flag
    private TrailRenderer[] _trail;
    
    // Time to allow flying again after velocity drops
    private float resetTimer = 0f;
    private float resetDelay = 2f; // Wait 2 seconds before allowing flight again

    private void Awake()
    {
        paperAirplane = GetComponent<Transform>();
        _rigidbody = GetComponent<Rigidbody>();
        _trail = GetComponentsInChildren<TrailRenderer>();
        
        // Disable trail GameObject at start
        if (trailObject != null)
        {
            trailObject.SetActive(false);
        }
        
        // Also ensure trail renderers aren't emitting
        foreach (var trail in _trail)
        {
            trail.emitting = false;
        }
    }

    private void FixedUpdate()
    {
        Vector3 velocity = _rigidbody.linearVelocity;
        
        // If it's falling in water, keep trails off
        if (isFallingInWater)
        {
            EnsureTrailsOff();
            
            // If velocity is very low, allow reset
            if (velocity.magnitude < 0.5f)
            {
                resetTimer += Time.fixedDeltaTime;
                if (resetTimer >= resetDelay)
                {
                    isFallingInWater = false;
                    resetTimer = 0f;
                }
            }
            else
            {
                resetTimer = 0f; // Reset timer if still moving
            }
            
            return;
        }

        // Can start flying again if not falling in water
        if (!isFlying && velocity.magnitude > 2.5f)
        {
            isFlying = true;
            
            // Enable trail GameObject when flying
            if (trailObject != null)
            {
                trailObject.SetActive(true);
            }
            
            foreach (var trail in _trail)
            {
                trail.emitting = true;
            }
        }

        if (!isFlying)
            return;

        // apply buoyancy force
        _rigidbody.AddForce(Vector3.up * buoyancy, ForceMode.Force);

        // adjust the orientation of the plane
        if (velocity.sqrMagnitude > 0.3f)
        {
            Quaternion rawRotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            Quaternion correction = Quaternion.Euler(forwardAxisCorrection);
            paperAirplane.rotation = rawRotation * correction;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Check for splash object specifically
        if (isFlying && collision.gameObject == splashObject)
        {
            StopFlying();
            isFallingInWater = true;  // Mark that it hit water
        }
        // Also stop flying for any other collision
        else if (isFlying)
        {
            Debug.Log("Collided with: " + collision.gameObject.name);
            StopFlying();
            // Don't set isFallingInWater for normal collisions
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Check if collision is with SplashLayer
        if (isFlying && other.gameObject == splashObject)
        {
            Debug.Log("TriggerEnter SplashLayer (by GameObject)");
            StopFlying();
            isFallingInWater = true;  // Mark that it hit water
        }
    }

    private void StopFlying()
    {
        Debug.Log("StopFlying() called");
        isFlying = false;
        EnsureTrailsOff();
        
        // Optionally reduce velocity to make it stop more quickly
        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = _rigidbody.linearVelocity * 0.5f;
        }
    }
    
    private void EnsureTrailsOff()
    {
        // Disable the trail GameObject
        if (trailObject != null)
        {
            trailObject.SetActive(false);
        }
        
        // Ensure all trails are stopped and cleared
        if (_trail != null)
        {
            foreach (var trail in _trail)
            {
                if (trail != null)
                {
                    trail.emitting = false;
                    trail.Clear();
                }
            }
        }
    }

    public void ResetForRespawn()
    {
        isFlying = false;
        isFallingInWater = false;
        resetTimer = 0f;
        EnsureTrailsOff();
    }
}
