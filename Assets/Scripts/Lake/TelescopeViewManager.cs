using UnityEngine;

public class TelescopeViewManager : MonoBehaviour
{
    public Camera xrCamera;
    public Camera telescopeCamera;
    public GameObject telescopeOverlayCanvas;

    public void EnterTelescopeView()
    {
        xrCamera.enabled = true; // 保持XR摄像头开着
        telescopeCamera.enabled = true; // 望远镜摄像头渲染到纹理
        telescopeOverlayCanvas.SetActive(true); // 打开RawImage覆盖
    }

    public void ExitTelescopeView()
    {
        telescopeOverlayCanvas.SetActive(false);
        telescopeCamera.enabled = false;
    }
}