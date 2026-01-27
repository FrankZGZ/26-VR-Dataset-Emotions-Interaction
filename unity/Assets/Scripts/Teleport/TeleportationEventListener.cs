using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;
public class TeleportationEventListener : MonoBehaviour
{
    public GameObject locomotionSystem;
    public GameObject player;
    public GameObject sceneController;
    public GameObject samTask;
    public GameObject leftTaskRay;
    public GameObject rightTaskRay;
    public OVRCameraRig ovrCameraRig;     
    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider teleportationProvider;
    public Vector3 teleportTargetPosition = Vector3.zero; 
    public float teleportTargetYRotation = 0f; 
    public GameObject[] disableAfterTeleport;
    public GameObject[] destroyAfterTeleport;
    public GameObject[] refreshAfterTeleport;

    private void Start()
    {
        teleportationProvider = locomotionSystem.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();

        if (teleportationProvider != null)
        {
            teleportationProvider.endLocomotion += OnEndLocomotion;
            Debug.Log("Teleportation provider setup succeeded!");
        }
        else
        {
            Debug.Log("Teleportation provider not found!");
        }
    }

    private void OnDestroy()
    {
        if (teleportationProvider != null)
        {
            teleportationProvider.endLocomotion -= OnEndLocomotion;
        }
    }

    private void OnEndLocomotion(LocomotionSystem system)
    {
        // This method is called when teleportation (or any other locomotion) ends.
        Debug.Log(string.Format("Teleported to {0:s}", player.transform.position.ToString()));
        // Insert any additional logic you want to happen after teleporting here.
    }

    // Triggered with colliders. 
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Collider Event Triggered!");
        if (other.CompareTag("Player"))
        {
            // Save camera pose data before ending the scene
            CameraPoseSender cameraPoseSender = FindObjectOfType<CameraPoseSender>();
            if (cameraPoseSender != null)
            {
                cameraPoseSender.EndSessionAndSendData();
                Debug.Log("Camera pose data saved before starting SAM survey!");
            }
            else
            {
                Debug.LogWarning("CameraPoseSender not found in the scene!");
            }

            // Triggered when the XR player's collider enters the teleportation pad collider
            HandleTeleportPadInteraction();
            // Teleport to origin.
            TeleportToOrigin();
        }
    }

    
    private void HandleTeleportPadInteraction()
    {
        // The XR player interacts with the teleportation pad.
        Debug.Log("Interacted with Teleport Pad!");

        //  End the scene and start emotion survey.
        // sceneController.GetComponent<SceneController>().StartEmotionSurvey();

        // Activate the SAMTask object.
        if (samTask != null)
        {
            samTask.SetActive(true);
            Debug.Log("SAMTask GameObject activated");
        }
        else
        {
            Debug.LogError("samTask reference is null! Please assign it in the Inspector.");
        }
        
        // Activate the task rays.
        if (leftTaskRay != null)  leftTaskRay.SetActive(true);
        if (rightTaskRay != null) rightTaskRay.SetActive(true);

        // Disable the disableAfterTeleport objects.
        if (disableAfterTeleport != null)
            {
                foreach (GameObject obj in disableAfterTeleport)
                {
                    if (obj != null)
                    {
                        obj.SetActive(false);
                    }
                }
            }

        // Destroy the destroyAfterTeleport objects.
        if (destroyAfterTeleport != null)
        {
            foreach (GameObject obj in destroyAfterTeleport)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
        }

        // Refresh the refreshAfterTeleport objects.
        if (refreshAfterTeleport != null)
        {
            foreach (GameObject obj in refreshAfterTeleport)
            {
                if (obj != null)
                {
                Animator animator = obj.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }
                }
            }
        }
        
        
    }
    private void TeleportToOrigin()
    {
        // 位置
        ovrCameraRig.transform.position = teleportTargetPosition;

        // 朝向
        ovrCameraRig.transform.rotation = Quaternion.Euler(0, teleportTargetYRotation, 0);
    }

}
