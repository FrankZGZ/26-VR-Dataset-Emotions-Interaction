using UnityEngine;

public class ProceduralAvatarIdle : MonoBehaviour
{
    public Transform rigRoot;
    public AudioSource voiceAudioSource;
    [Range(0f, 2f)] public float intensity = 0.65f;
    [Range(0.1f, 3f)] public float speed = 0.85f;
    public bool letAnimatorDriveBody = true;
    [Range(0f, 2f)] public float speakingIntensity = 1.0f;
    [Range(0f, 2f)] public float gestureIntensity = 0.75f;
    [Range(0.05f, 1f)] public float speakingSmoothing = 0.18f;

    private Transform head;
    private Transform spine;
    private Transform leftUpperArm;
    private Transform rightUpperArm;
    private Transform leftForearm;
    private Transform rightForearm;
    private Transform leftHand;
    private Transform rightHand;
    private Animator animator;
    private Vector3 baseLocalPosition;
    private Quaternion headBaseRotation = Quaternion.identity;
    private Quaternion spineBaseRotation = Quaternion.identity;
    private Quaternion leftArmBaseRotation = Quaternion.identity;
    private Quaternion rightArmBaseRotation = Quaternion.identity;
    private Quaternion leftForearmBaseRotation = Quaternion.identity;
    private Quaternion rightForearmBaseRotation = Quaternion.identity;
    private Quaternion leftHandBaseRotation = Quaternion.identity;
    private Quaternion rightHandBaseRotation = Quaternion.identity;
    private readonly float[] audioSamples = new float[256];
    private float speakingWeight;

    private void Awake()
    {
        if (rigRoot == null)
        {
            rigRoot = transform;
        }

        if (voiceAudioSource == null)
        {
            voiceAudioSource = GetComponentInChildren<AudioSource>();
        }
        animator = rigRoot.GetComponentInChildren<Animator>();

        baseLocalPosition = transform.localPosition;
        head = FindByName(rigRoot, "head");
        spine = FindByName(rigRoot, "spine2") ?? FindByName(rigRoot, "spine1") ?? FindByName(rigRoot, "spine");
        leftUpperArm = FindByName(rigRoot, "l upperarm") ?? FindByName(rigRoot, "leftupperarm");
        rightUpperArm = FindByName(rigRoot, "r upperarm") ?? FindByName(rigRoot, "rightupperarm");
        leftForearm = FindByName(rigRoot, "l forearm") ?? FindByName(rigRoot, "leftforearm") ?? FindByName(rigRoot, "l lowerarm");
        rightForearm = FindByName(rigRoot, "r forearm") ?? FindByName(rigRoot, "rightforearm") ?? FindByName(rigRoot, "r lowerarm");
        leftHand = FindByName(rigRoot, "l hand") ?? FindByName(rigRoot, "lefthand");
        rightHand = FindByName(rigRoot, "r hand") ?? FindByName(rigRoot, "righthand");

        if (head != null) headBaseRotation = head.localRotation;
        if (spine != null) spineBaseRotation = spine.localRotation;
        if (leftUpperArm != null) leftArmBaseRotation = leftUpperArm.localRotation;
        if (rightUpperArm != null) rightArmBaseRotation = rightUpperArm.localRotation;
        if (leftForearm != null) leftForearmBaseRotation = leftForearm.localRotation;
        if (rightForearm != null) rightForearmBaseRotation = rightForearm.localRotation;
        if (leftHand != null) leftHandBaseRotation = leftHand.localRotation;
        if (rightHand != null) rightHandBaseRotation = rightHand.localRotation;
    }

    private void LateUpdate()
    {
        float t = Time.time * speed;
        float voiceLevel = GetVoiceLevel();
        float targetSpeakingWeight = voiceAudioSource != null && voiceAudioSource.isPlaying
            ? Mathf.Clamp01(0.18f + voiceLevel * 16f)
            : 0f;
        speakingWeight = Mathf.Lerp(
            speakingWeight,
            targetSpeakingWeight,
            1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, speakingSmoothing)));

        float amount = intensity * (1f + speakingWeight * 0.45f);
        float speakingAmount = speakingWeight * speakingIntensity;
        float gestureAmount = speakingAmount * gestureIntensity;
        float phrasePulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 1.65f + 0.35f)), 3f);
        float alternatePulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 1.45f + 2.2f)), 3f);

        transform.localPosition = baseLocalPosition + new Vector3(0f, Mathf.Sin(t * 2.0f) * 0.012f * amount, 0f);
        if (letAnimatorDriveBody && animator != null && animator.runtimeAnimatorController != null)
        {
            return;
        }

        if (spine != null)
        {
            float breathe = Mathf.Sin(t * 2.0f) * 1.3f * amount;
            float sway = Mathf.Sin(t * 0.63f) * 1.1f * amount;
            float engagedLean = -2.5f * speakingAmount;
            float conversationalSway = Mathf.Sin(t * 2.1f + 0.4f) * 1.4f * speakingAmount;
            spine.localRotation = spineBaseRotation * Quaternion.Euler(
                breathe + engagedLean,
                sway + conversationalSway,
                -sway * 0.45f + Mathf.Sin(t * 1.7f) * 0.8f * speakingAmount);
        }

        if (head != null)
        {
            float nod = Mathf.Sin(t * 0.92f + 0.7f) * 2.0f * amount;
            float turn = Mathf.Sin(t * 0.47f + 1.4f) * 2.8f * amount;
            float tilt = Mathf.Sin(t * 0.71f) * 1.2f * amount;
            float speechNod = (Mathf.Sin(t * 5.8f) * 1.2f + phrasePulse * 3.4f) * speakingAmount;
            float speechTurn = Mathf.Sin(t * 2.3f + 1.1f) * 1.4f * speakingAmount;
            float speechTilt = Mathf.Sin(t * 2.7f + 0.6f) * 0.9f * speakingAmount;
            head.localRotation = headBaseRotation * Quaternion.Euler(
                nod + speechNod,
                turn + speechTurn,
                tilt + speechTilt);
        }

        if (leftUpperArm != null)
        {
            leftUpperArm.localRotation = leftArmBaseRotation * Quaternion.Euler(
                Mathf.Sin(t * 0.76f) * 1.4f * amount - phrasePulse * 5.5f * gestureAmount,
                Mathf.Sin(t * 1.3f + 0.2f) * 2.0f * gestureAmount,
                -phrasePulse * 3.5f * gestureAmount);
        }

        if (rightUpperArm != null)
        {
            rightUpperArm.localRotation = rightArmBaseRotation * Quaternion.Euler(
                Mathf.Sin(t * 0.73f + 1.7f) * 1.4f * amount - alternatePulse * 5.0f * gestureAmount,
                Mathf.Sin(t * 1.25f + 1.8f) * 2.0f * gestureAmount,
                alternatePulse * 3.2f * gestureAmount);
        }

        if (leftForearm != null)
        {
            leftForearm.localRotation = leftForearmBaseRotation * Quaternion.Euler(
                phrasePulse * 8.0f * gestureAmount,
                0f,
                Mathf.Sin(t * 2.4f) * 1.8f * gestureAmount);
        }

        if (rightForearm != null)
        {
            rightForearm.localRotation = rightForearmBaseRotation * Quaternion.Euler(
                alternatePulse * 7.0f * gestureAmount,
                0f,
                Mathf.Sin(t * 2.2f + 1.1f) * 1.8f * gestureAmount);
        }

        if (leftHand != null)
        {
            leftHand.localRotation = leftHandBaseRotation * Quaternion.Euler(
                0f,
                Mathf.Sin(t * 3.3f + 0.4f) * 3.0f * gestureAmount,
                phrasePulse * 5.0f * gestureAmount);
        }

        if (rightHand != null)
        {
            rightHand.localRotation = rightHandBaseRotation * Quaternion.Euler(
                0f,
                Mathf.Sin(t * 3.1f + 1.5f) * 3.0f * gestureAmount,
                -alternatePulse * 4.5f * gestureAmount);
        }
    }

    private float GetVoiceLevel()
    {
        if (voiceAudioSource == null || !voiceAudioSource.isPlaying)
        {
            return 0f;
        }

        voiceAudioSource.GetOutputData(audioSamples, 0);
        float sum = 0f;
        for (int i = 0; i < audioSamples.Length; i++)
        {
            sum += audioSamples[i] * audioSamples[i];
        }

        return Mathf.Sqrt(sum / audioSamples.Length);
    }

    private static Transform FindByName(Transform root, string token)
    {
        string normalizedToken = Normalize(token);
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (Normalize(child.name).Contains(normalizedToken))
            {
                return child;
            }
        }

        return null;
    }

    private static string Normalize(string value)
    {
        return value.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }
}
