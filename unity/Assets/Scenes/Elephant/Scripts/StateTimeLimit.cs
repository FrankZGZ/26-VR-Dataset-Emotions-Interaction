using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 保持原始脚本名称不变
public class StateTimeLimit : MonoBehaviour
{
    // 行走和闲置的持续时间
    public float walkDuration = 20f;
    public float idleDuration = 15f;
    
    // 内部变量
    private Animator animator;
    private string currentState = "Walk"; // 当前状态
    private float stateTimer = 0f;        // 状态计时器
    
    void Start()
    {
        // 获取Animator组件
        animator = GetComponent<Animator>();
        
        if (animator == null)
        {
            Debug.LogError("找不到Animator组件！请确保大象对象上有Animator组件。");
            return;
        }
        
        // 强制播放走路动画
        PlayAnimation("Walk");
        Debug.Log("大象开始行走");
    }
    
    void Update()
    {
        // 增加计时器
        stateTimer += Time.deltaTime;
        
        // 根据当前状态和时间控制动画切换
        if (currentState == "Walk" && stateTimer >= walkDuration)
        {
            // 行走时间到，切换到闲置
            PlayAnimation("Idle");
            stateTimer = 0f;
            currentState = "Idle";
            Debug.Log($"大象停下休息，将闲置 {idleDuration} 秒");
        }
        else if (currentState == "Idle" && stateTimer >= idleDuration)
        {
            // 闲置时间到，切换到行走
            PlayAnimation("Walk");
            stateTimer = 0f;
            currentState = "Walk";
            Debug.Log($"大象开始行走，将行走 {walkDuration} 秒");
        }
    }
    
    // 辅助方法：播放指定的动画状态
    void PlayAnimation(string stateName)
    {
        // 直接使用Play方法强制播放动画状态
        animator.Play(stateName, 0, 0f);
    }
    
    // 显示状态信息（帮助调试）
    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 20), $"当前状态: {currentState}");
        GUI.Label(new Rect(10, 30, 300, 20), $"已持续: {stateTimer:F1} 秒");
        GUI.Label(new Rect(10, 50, 300, 20), $"目标时间: {(currentState == "Walk" ? walkDuration : idleDuration)} 秒");
    }
}