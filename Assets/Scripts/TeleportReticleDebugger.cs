using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using Oculus.Interaction.DistanceReticles;
using System.Reflection;

public class TeleportReticleDebugger : MonoBehaviour
{
    public TeleportReticleDrawer reticleDrawer;
    public TeleportInteractor interactor;

    void Update()
    {
        if (reticleDrawer == null || interactor == null)
            return;

        if (interactor.State == InteractorState.Hover &&
            interactor.Interactable != null &&
            interactor.Interactable.AllowTeleport &&
            interactor.ArcEnd.Point != Vector3.zero)
        {
            ReticleDataTeleport data = new ReticleDataTeleport();
            MethodInfo align = typeof(TeleportReticleDrawer).GetMethod("Align", BindingFlags.NonPublic | BindingFlags.Instance);
            align?.Invoke(reticleDrawer, new object[] { data });
        }
        else
        {
            MethodInfo hide = typeof(TeleportReticleDrawer).GetMethod("Hide", BindingFlags.NonPublic | BindingFlags.Instance);
            hide?.Invoke(reticleDrawer, null);
        }
    }
}
