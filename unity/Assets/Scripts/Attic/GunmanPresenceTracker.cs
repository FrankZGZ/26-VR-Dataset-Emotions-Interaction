using UnityEngine;

/// <summary>
/// Exposes whether the Attic scene's intruder has finished walking to his scripted
/// final position. He is moved there by WaypointNavigator/WaypointCharacterController,
/// which sets the Animator's "playWalking" bool false once he reaches his end waypoint
/// and never repositions him again afterward (he only turns to face the participant).
/// Tracking that transition here lets the voice backend answer live questions about
/// him ("is he here", "what is he doing") with real evidence instead of guessing.
/// </summary>
public class GunmanPresenceTracker : MonoBehaviour
{
    public bool InFinalPosition { get; private set; }

    private Animator animator;
    private bool hasStartedWalking;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (animator == null || InFinalPosition)
        {
            return;
        }

        bool isWalking = animator.GetBool("playWalking");
        if (isWalking)
        {
            hasStartedWalking = true;
            return;
        }

        if (hasStartedWalking)
        {
            InFinalPosition = true;
            Debug.Log("[GunmanPresenceTracker] Intruder reached final position.");
        }
    }
}
