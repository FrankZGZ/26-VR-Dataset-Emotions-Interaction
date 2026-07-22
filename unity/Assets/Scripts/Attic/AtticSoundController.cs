using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AtticSoundController : MonoBehaviour
{
    [Tooltip("Additional time before the armed man and his scream appear.")]
    public float additionalManDelaySeconds = 10f;

    // Start walking time.
    public float startWalkingTime;
    public GameObject man;

    // Set up audio source and timer.
    public GameObject manCreaming;
    public float manCreamingTime;

    private bool manActivated;
    private bool screamActivated;

    // Gun shot time.
    // public float gunshotTime;
    // public GameObject gunShoting;

    // Start is called before the first frame update
    void Start()
    {
        float additionalDelay = Mathf.Max(0f, additionalManDelaySeconds);
        startWalkingTime = Mathf.Max(0f, startWalkingTime) + additionalDelay;
        manCreamingTime = Mathf.Max(0f, manCreamingTime) + additionalDelay;

        if (man != null)
        {
            man.SetActive(false);
        }
        if (manCreaming != null)
        {
            manCreaming.SetActive(false);
        }

        Debug.Log("[Attic] Armed man delayed by an additional " + additionalDelay.ToString("0.0") + " seconds.");
    }

    // Update is called once per frame
    void Update()
    {
        // Count time down
        startWalkingTime -= Time.deltaTime;
        manCreamingTime -= Time.deltaTime;
        // gunshotTime -= Time.deltaTime;

        // Start walking.
        if(!manActivated && startWalkingTime <= 0.0f)
        {
            manActivated = true;
            if (man != null)
            {
                man.SetActive(true);
            }
        }

        // Play creaming sound.
        if(!screamActivated && manCreamingTime <= 0.0f)
        {
            screamActivated = true;
            if (manCreaming != null)
            {
                manCreaming.SetActive(true);
            }
        }

        // Play gunshot sound.
        // if(gunshotTime <= 0.0f)
        // {
        //     gunShoting.SetActive(true);
        // }

    }
}
