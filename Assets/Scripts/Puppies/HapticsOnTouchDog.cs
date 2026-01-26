using UnityEngine;
using Oculus.Haptics;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class HapticsOnTouchDog : MonoBehaviour
{
    public HapticClip clip;
    public Transform leftAnchor;
    public Transform rightAnchor;

    public bool isLooping = false; 

    private HapticClipPlayer leftPlayer;
    private HapticClipPlayer rightPlayer;

    public DogAnimationTrigger animationTriggerScript;

    private void Start()
    {
        leftPlayer = new HapticClipPlayer(clip);
        rightPlayer = new HapticClipPlayer(clip);

        leftPlayer.isLooping = isLooping;
        rightPlayer.isLooping = isLooping;
    }

    private void OnTriggerEnter(Collider other)
    {
        Transform t = other.transform;

        if (leftAnchor != null && t.IsChildOf(leftAnchor))
        {
            leftPlayer.Play(Controller.Left);
            Debug.Log("Left controller touched.");
            animationTriggerScript.PlayAnimation(); 
            //animationTriggerScript.StopAnimation();
            //StartCoroutine(ResumeAndPlay());
        }
        else if (rightAnchor != null && t.IsChildOf(rightAnchor))
        {
            rightPlayer.Play(Controller.Right);
            Debug.Log("Right controller touched.");
            animationTriggerScript.PlayAnimation(); 
            //animationTriggerScript.StopAnimation();
            //StartCoroutine(ResumeAndPlay());
        }
    }

    /*private IEnumerator ResumeAndPlay()
    {
        animationTriggerScript.Resume();        
        yield return new WaitForSeconds(0.05f); 
        animationTriggerScript.PlayAnimation(); 
    } */

    private void OnTriggerExit(Collider other)
    {
        Transform t = other.transform;

        if (leftAnchor != null && t.IsChildOf(leftAnchor))
        {
            leftPlayer.Stop();
            Debug.Log("Left controller exited.");
            animationTriggerScript.StopAnimation();
        }
        else if (rightAnchor != null && t.IsChildOf(rightAnchor))
        {
            rightPlayer.Stop();
            Debug.Log("Right controller exited.");
            animationTriggerScript.StopAnimation();
        }
    }

    private void OnDestroy()
    {
        leftPlayer?.Dispose();
        rightPlayer?.Dispose();
    }
}
