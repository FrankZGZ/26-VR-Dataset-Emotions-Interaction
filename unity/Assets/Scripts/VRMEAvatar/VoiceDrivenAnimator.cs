using UnityEngine;

public class VoiceDrivenAnimator : MonoBehaviour
{
    public Animator animator;
    public AudioSource voiceAudioSource;
    public string speakingBool = "IsSpeaking";
    public string speakIndexParameter = "SpeakAnimIdx2";
    public string idleIndexParameter = "IdleAnimIdx2";
    public int speakingAnimationIndex = 3;
    public int idleAnimationIndex = 0;
    [Range(0.01f, 0.5f)] public float activationThreshold = 0.025f;
    [Range(0.05f, 1f)] public float releaseDelay = 0.35f;

    private readonly float[] samples = new float[256];
    private float lastVoiceTime = -999f;
    private bool isSpeaking;
    private int speakingBoolHash;
    private int speakIndexHash;
    private int idleIndexHash;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (voiceAudioSource == null)
        {
            voiceAudioSource = GetComponentInChildren<AudioSource>();
        }

        speakingBoolHash = Animator.StringToHash(speakingBool);
        speakIndexHash = Animator.StringToHash(speakIndexParameter);
        idleIndexHash = Animator.StringToHash(idleIndexParameter);

        if (animator != null)
        {
            animator.SetInteger(idleIndexHash, idleAnimationIndex);
            animator.SetInteger(speakIndexHash, speakingAnimationIndex);
            animator.SetBool(speakingBoolHash, false);
        }
    }

    private void Update()
    {
        if (animator == null || voiceAudioSource == null)
        {
            return;
        }

        if (voiceAudioSource.isPlaying && GetVoiceLevel() >= activationThreshold)
        {
            lastVoiceTime = Time.time;
        }

        bool shouldSpeak = Time.time - lastVoiceTime <= releaseDelay;
        if (shouldSpeak == isSpeaking)
        {
            return;
        }

        isSpeaking = shouldSpeak;
        animator.SetInteger(idleIndexHash, idleAnimationIndex);
        animator.SetInteger(speakIndexHash, speakingAnimationIndex);
        animator.SetBool(speakingBoolHash, isSpeaking);
        Debug.Log("[VRME] Animator speaking=" + isSpeaking);
    }

    private float GetVoiceLevel()
    {
        voiceAudioSource.GetOutputData(samples, 0);
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Mathf.Sqrt(sum / samples.Length);
    }
}
