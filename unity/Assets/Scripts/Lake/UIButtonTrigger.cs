using UnityEngine;
using UnityEngine.Events;

public class UIButtonTrigger : MonoBehaviour
{
    public UnityEvent onTrigger;

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[UIButtonTrigger] Trigger entered by: {other.name}");

        if (other.name.Contains("Right") || other.CompareTag("RightHand"))
        {
            Debug.Log("[UIButtonTrigger] Right hand detected � invoking event.");
            onTrigger.Invoke();
        }
        else
        {
            Debug.Log("[UIButtonTrigger] Not triggered by expected object (RightHand).");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[UIButtonTrigger] Trigger exited by: {other.name}");
    }
}