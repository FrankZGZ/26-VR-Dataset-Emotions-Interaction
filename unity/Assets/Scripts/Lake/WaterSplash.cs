using UnityEngine;

public class WaterSplash : MonoBehaviour
{
    public GameObject splashPrefab; // 拖进去你的水花Prefab
    public float spawnHeightOffset = 0.01f; // 生成时比水面略高一点
    public AudioClip splashSound;   // 拖进去水花声音
    public float audioVolume = 0.8f; // 音量

    private void OnTriggerEnter(Collider other)
    {
        if (other != null && other.name.Contains("SplashLayer")) // 根据你的水面名字判断
        {
            Vector3 splashPosition = transform.position;
            splashPosition.y = other.transform.position.y + spawnHeightOffset;

            GameObject splash = Instantiate(splashPrefab, splashPosition, Quaternion.Euler(90f, 0f, 0f));

            // 播放音效
            AudioSource.PlayClipAtPoint(splashSound, splashPosition, audioVolume);
        }
    }
}
