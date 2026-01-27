using UnityEngine;

public class TelescopeFollowHead : MonoBehaviour
{
    public Transform headToFollow; // XR Rig 的 CenterEyeAnchor
    public Camera telescopeCamera;
    public float lockedFOV = 25f;

    void LateUpdate()
    {
        if (headToFollow != null)
        {
            // 固定位置，仅旋转
            transform.rotation = headToFollow.rotation;

            // 强制锁定 FOV（防止 XR 系统改回来）
            if (telescopeCamera != null && telescopeCamera.fieldOfView != lockedFOV)
            {
                telescopeCamera.fieldOfView = lockedFOV;
            }
        }
    }
}
