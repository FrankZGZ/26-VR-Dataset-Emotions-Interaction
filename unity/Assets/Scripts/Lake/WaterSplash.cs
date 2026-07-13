using UnityEngine;

public class WaterSplash : MonoBehaviour
{
    public GameObject splashPrefab; // 拖进去你的水花Prefab
    public float spawnHeightOffset = 0.01f; // 生成时比水面略高一点
    public AudioClip splashSound;   // 拖进去水花声音
    public float audioVolume = 0.8f; // 音量

    [Header("Reusable throw object")]
    [Tooltip("Seconds after entering the water before returning to the original position.")]
    public float respawnDelay = 2f;
    [Tooltip("Fallback return time after a throw misses the water.")]
    public float respawnAfterThrowDelay = 6f;

    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Rigidbody body;
    private bool initialIsKinematic;
    private Renderer[] renderers;
    private Collider[] colliders;
    private bool isRespawning;
    private bool hasBeenThrown;
    private float thrownAt;

    private void Awake()
    {
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        body = GetComponent<Rigidbody>();
        initialIsKinematic = body != null && body.isKinematic;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    private void Update()
    {
        if (isRespawning || body == null)
        {
            return;
        }

        float localDistance = Vector3.Distance(transform.localPosition, initialLocalPosition);
        if (!hasBeenThrown && body.linearVelocity.magnitude > 1.2f && localDistance > 0.25f)
        {
            hasBeenThrown = true;
            thrownAt = Time.time;
        }

        if (hasBeenThrown && Time.time - thrownAt >= Mathf.Max(respawnDelay, respawnAfterThrowDelay))
        {
            StartCoroutine(RespawnAtOrigin());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isRespawning && other != null && other.name.Contains("SplashLayer")) // 根据你的水面名字判断
        {
            Vector3 splashPosition = transform.position;
            splashPosition.y = other.transform.position.y + spawnHeightOffset;

            GameObject splash = Instantiate(splashPrefab, splashPosition, Quaternion.Euler(90f, 0f, 0f));

            // 播放音效
            AudioSource.PlayClipAtPoint(splashSound, splashPosition, audioVolume);

            StartCoroutine(RespawnAtOrigin());
        }
    }

    private System.Collections.IEnumerator RespawnAtOrigin()
    {
        isRespawning = true;

        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }

        SetThrowObjectVisible(false);
        yield return new WaitForSeconds(Mathf.Max(0.1f, respawnDelay));

        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;

        PaperAirplaneFlight airplane = GetComponent<PaperAirplaneFlight>();
        if (airplane != null)
        {
            airplane.ResetForRespawn();
        }

        if (body != null)
        {
            body.isKinematic = initialIsKinematic;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.WakeUp();
        }

        SetThrowObjectVisible(true);
        hasBeenThrown = false;
        isRespawning = false;
        Debug.Log($"[LakeRespawn] {gameObject.name} returned to its original position.");
    }

    private void SetThrowObjectVisible(bool visible)
    {
        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
            {
                itemRenderer.enabled = visible;
            }
        }

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider != null)
            {
                itemCollider.enabled = visible;
            }
        }
    }
}
