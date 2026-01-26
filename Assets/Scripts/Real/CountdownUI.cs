using UnityEngine;
using TMPro; 

public class CountdownUI : MonoBehaviour
{
    public GameObject countdownPanel;   
    public TMP_Text countdownText;      
    public float waitTime = 10f;        
    public float countdownDuration = 5f; 

    void Start()
    {
        countdownPanel.SetActive(false);
        StartCoroutine(ShowCountdown());
    }

    System.Collections.IEnumerator ShowCountdown()
    {
        yield return new WaitForSeconds(waitTime);

        countdownPanel.SetActive(true);

        for (int i = (int)countdownDuration; i > 0; i--)
        {
            countdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

    }
}
