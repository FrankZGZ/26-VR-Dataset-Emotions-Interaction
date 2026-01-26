using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class TeleporationManager : MonoBehaviour
{

    [SerializeField] private InputActionAsset actionAsset;
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor;
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider provider;
    private InputAction _thumbstick;
    private bool _isActive;

    // Start is called before the first frame update
    void Start()
    {
        rayInteractor.enabled = false;

        var activate = actionAsset.FindActionMap("XRI LeftHand Locomotion").FindAction("Teleport Mode Activate");
        activate.Enable();
        activate.performed += OnTeleportActivate;

        var cancel = actionAsset.FindActionMap("XRI LeftHand Locomotion").FindAction("Teleport Mode Cancel");
        cancel.Enable();
        cancel.performed += OnTeleportCancel;

        _thumbstick = actionAsset.FindActionMap("XRI LeftHand Locomotion").FindAction("Move");
        _thumbstick.Enable();


    }

    // Update is called once per frame
    void Update()
    {
        if(_isActive)
        {
            return;
        }
        
        if(_thumbstick.triggered)
        {
            return;
        }

        if(!rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            rayInteractor.enabled = false;
            _isActive = false;
            return;
        }

        UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest request = new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest()
        {
            destinationPosition = hit.point,
            // destinationRotation = ?
        };

        provider.QueueTeleportRequest(request);
        _isActive = false;
    }

    private void OnTeleportActivate(InputAction.CallbackContext context)
    {
        rayInteractor.enabled = true;
        _isActive = true;
    }

    private void OnTeleportCancel(InputAction.CallbackContext context)
    {
        rayInteractor.enabled = false;
        _isActive = false;
    }
}
