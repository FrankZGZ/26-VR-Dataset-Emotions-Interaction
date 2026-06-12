using UnityEngine;

public class FaceCameraOnYAxis : MonoBehaviour
{
    public Transform target;
    public float turnSpeed = 720f;
    public float yawOffsetDegrees = 0f;

    void LateUpdate()
    {
        if (target == null)
        {
            target = FindCameraTarget();
            if (target == null) return;
        }

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        targetRotation *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    private static Transform FindCameraTarget()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform;
        }

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera != null && camera.isActiveAndEnabled)
            {
                return camera.transform;
            }
        }

        return null;
    }
}
