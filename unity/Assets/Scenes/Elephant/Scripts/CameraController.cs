using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    // 移动速度
    public float moveSpeed = 10f;
    // 鼠标灵敏度
    public float mouseSensitivity = 3f;
    // 上下看的限制角度
    public float upperLookLimit = 80f;
    public float lowerLookLimit = 80f;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // 锁定并隐藏鼠标光标
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // 记录初始旋转角度
        rotationY = transform.eulerAngles.y;
        rotationX = transform.eulerAngles.x;
    }

    void Update()
    {
        // 处理摄像机旋转
        HandleRotation();
        
        // 处理摄像机移动
        HandleMovement();
        
        // 按ESC键解锁鼠标
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void HandleRotation()
    {
        // 获取鼠标输入
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // 计算上下旋转并限制角度
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -upperLookLimit, lowerLookLimit);
        
        // 计算左右旋转
        rotationY += mouseX;
        
        // 应用旋转
        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0);
    }

    private void HandleMovement()
    {
        // 获取键盘输入
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        // 创建移动向量
        Vector3 forward = transform.forward * vertical;
        Vector3 right = transform.right * horizontal;
        
        // 组合移动向量并应用移动
        Vector3 movement = forward + right;
        
        // 如果按住Shift键，加速移动
        if (Input.GetKey(KeyCode.LeftShift))
        {
            movement *= 2.5f;
        }
        
        // 应用移动（乘以时间和速度）
        transform.position += movement * moveSpeed * Time.deltaTime;
        
        // 上下移动（可选，使用Q和E键）
        if (Input.GetKey(KeyCode.E))
        {
            transform.position += Vector3.up * moveSpeed * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.Q))
        {
            transform.position += Vector3.down * moveSpeed * Time.deltaTime;
        }
    }
}