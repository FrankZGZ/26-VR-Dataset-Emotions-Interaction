using UnityEngine;

/// <summary>
/// Exposes the Attic exit door's open/closed state, driven by OpenDoorOnCharacter's
/// "openDoor" Animator bool (set true once the intruder walks through the trigger
/// that wires to this door's Animator). Lets the voice backend answer live questions
/// about the door without hallucinating.
/// </summary>
public class ExitDoorStateTracker : MonoBehaviour
{
    public bool IsOpen { get; private set; }

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (animator == null)
        {
            return;
        }

        IsOpen = animator.GetBool("openDoor");
    }
}
