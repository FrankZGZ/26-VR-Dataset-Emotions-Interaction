using UnityEngine;


public class CustomTeleportationProvider : UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider
{
    private void OnEnable()
    {

    }

    private void OnDisable()
    {
        
    }

    private void OnEndLocomotion(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider provider)
    {
        // This method is called when teleportation ends.
        Debug.Log("Teleportation has ended!");
        // Insert any additional logic you want to happen after teleporting here.
    }
}
