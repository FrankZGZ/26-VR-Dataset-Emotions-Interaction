using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TriggerStates : MonoBehaviour
{
    public string triggerObjectName;

    private Animator animator;

    public GameObject facingTowardObject;
    public bool isFollow = false;

    public float speed = 2;

    public bool constrainY = true;

    private void Awake()
    {
        if (GetComponent<Animator>() != null)
        {
            animator = GetComponent<Animator>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        //Debug.Log(other.gameObject.name);
        if (other.gameObject.name == triggerObjectName)
        {
            if (animator != null)
                animator.SetBool("playShooting", true);
            isFollow = true;
        }
    }


    void Update()
    {
        if (!isFollow) return;

        Transform targetTransform = GetFacingTargetTransform();
        if (targetTransform == null) return;

        Vector3 v = targetTransform.position - this.gameObject.transform.position;
        if (constrainY) v.y = 0.0f;
        if (v.sqrMagnitude < 0.000001f) return;

        Quaternion q = Quaternion.LookRotation(v);
        this.gameObject.transform.rotation = Quaternion.Slerp(this.gameObject.transform.rotation, q, speed * Time.deltaTime);
        }

    private Transform GetFacingTargetTransform()
    {
        if (facingTowardObject != null) return facingTowardObject.transform;
        if (Camera.main != null) return Camera.main.transform;

        return null;
    }

}
