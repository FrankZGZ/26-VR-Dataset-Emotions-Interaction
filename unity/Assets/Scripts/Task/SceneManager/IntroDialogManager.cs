using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class IntroDialogManager : MonoBehaviour
{
    public void LoadNextDialog(GameObject gameObject)
    {
        gameObject.SetActive(true);
    }

    public void DeactiveCurrentDialog(GameObject gameObject)
    {
        gameObject.SetActive(false);
    }

    public void SetLeftControllerActive(GameObject gameObject)
    {
        gameObject.SetActive(true);
    }

    public void SetRightControllerActive(GameObject gameObject)
    {
        gameObject.SetActive(true);
    }

    public void SetLeftControllerDeactive(GameObject gameObject)
    {
        gameObject.SetActive(false);
    }

    public void SetRightControllerDeactive(GameObject gameObject)
    {
        gameObject.SetActive(false);
    }
}
