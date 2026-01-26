using UnityEngine;
using System.Collections.Generic;

public class ElephantsAnimationManager : MonoBehaviour
{
    public static ElephantsAnimationManager Instance { get; private set; }

    public List<ElephantAnimationTrigger> elephants = new List<ElephantAnimationTrigger>();

    [Header("Speed Settings")]
    public float slowedSpeed = 0.3f;
    public float normalSpeed = 1.0f;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void OnElephantAnimationStart(ElephantAnimationTrigger caller)
    {
        foreach (var elephant in elephants)
        {
            if (elephant != null && elephant != caller)
            {
                if (elephant.animator != null)
                    elephant.animator.speed = slowedSpeed;
            }
        }
    }

    public void OnElephantAnimationEnd(ElephantAnimationTrigger caller)
    {
        foreach (var elephant in elephants)
        {
            if (elephant != null)
            {
                if (elephant.animator != null)
                    elephant.animator.speed = normalSpeed;
            }
        }
    }
}
