using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]

public class TennisBall : MonoBehaviour
{
    [Header("Basic Physical Parameters")]
    public float mass = 0.057f; // tennis ball standard mass
    public float radius = 0.033f; // tennis ball standard radius
    public float bounciness = 0.1f; // 更低弹性
    public float friction = 1.0f;   // 更高摩擦

    private Rigidbody rb;
    private PhysicsMaterial physicsMaterial;
    
    // for the dog to catch the ball
    public static TennisBall lastThrownBall;
    public bool hasBeenThrown = false;
    
    //private bool wasHeld = false;
    private float stillTime = 0f;
    public float stillThreshold = 2f; // 静止2秒后重置

    void Awake()
    {
        // set the rigidbody parameters
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // create and apply the physics material
        physicsMaterial = new PhysicsMaterial("TennisBallMaterial");
        physicsMaterial.bounciness = bounciness;
        physicsMaterial.dynamicFriction = friction;
        physicsMaterial.staticFriction = friction;
        physicsMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        physicsMaterial.frictionCombine = PhysicsMaterialCombine.Average;

        // set the collider
        SphereCollider collider = GetComponent<SphereCollider>();
        collider.radius = radius;
        collider.material = physicsMaterial;
    }

    void Update()
    {
        // 如果球的速度大于某个阈值，且之前是静止的，认为被扔出
        if (!hasBeenThrown && rb.linearVelocity.magnitude > 1.5f)
        {
            hasBeenThrown = true;
            lastThrownBall = this;
            Debug.Log("TennisBall thrown!");
        }

        // 静止检测
        if (rb.linearVelocity.magnitude < 0.1f)
        {
            stillTime += Time.deltaTime;
            if (stillTime > stillThreshold)
            {
                hasBeenThrown = false;
                stillTime = 0f;
                Debug.Log("TennisBall reset after being still.");

                // 强制让球完全静止
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            stillTime = 0f;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // 不再在落地时赋值 lastThrownBall
        // if (hasBeenThrown && collision.gameObject.CompareTag("Ground"))
        // {
        //     lastThrownBall = this;
        //     Debug.Log("TennisBall landed after being thrown!");
        // }
    }
}
