using UnityEngine;
using System.Collections;

public class MoveToCameraFrontOnEnable : MonoBehaviour
{
    public Transform centerEyeAnchor;
    public Vector3 offset = new Vector3(0, 0, 0.02f);
    public float followSpeed = 5.0f;  

    void LateUpdate()
    {
        if (centerEyeAnchor != null)
        {
            Vector3 targetPosition = centerEyeAnchor.position + centerEyeAnchor.forward * offset.z
                                                           + centerEyeAnchor.up * offset.y
                                                           + centerEyeAnchor.right * offset.x;

            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);

            Quaternion targetRotation = Quaternion.LookRotation(centerEyeAnchor.forward, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * followSpeed);
        }
    }
}
