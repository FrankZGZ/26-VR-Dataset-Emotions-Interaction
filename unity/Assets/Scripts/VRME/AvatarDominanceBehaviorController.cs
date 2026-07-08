using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the avatar near the participant and applies a lightweight
/// dominance/submission nonverbal manipulation. The manipulation is deliberately
/// simple: dominant faces and looks at the participant more directly; submissive
/// turns slightly away and averts gaze periodically.
/// </summary>
public class AvatarDominanceBehaviorController : MonoBehaviour
{
    [Header("Position only")]
    public float preferredDistance = 2.65f;
    public float minimumDistance = 2.5f;
    public float maximumDistance = 2.8f;

    [Header("Safe placement")]
    public float bodyRadius = 0.32f;
    public float bodyHeight = 1.75f;
    public float groundRayHeight = 2.5f;
    public float groundRayDistance = 5f;
    public float maximumFloorDifference = 0.75f;
    public LayerMask placementMask = ~0;

    [Header("Nonverbal dominance cues")]
    public bool enableNonverbalCues = true;
    [Tooltip("Disabled by default because the authored idle/talking animation also controls the head. Enable only if the avatar has no other head animation.")]
    public bool enableHeadGazeCues = false;
    [Tooltip("Dominant agent faces the participant directly; submissive agent keeps this many degrees of body aversion.")]
    public float submissiveBodyAvertedDegrees = 18f;
    [Tooltip("How quickly the avatar body rotates toward the dominance-condition facing target.")]
    public float bodyFacingLerpSpeed = 3.5f;
    [Range(0f, 1f)] public float dominantHeadGazeWeight = 0.20f;
    [Range(0f, 1f)] public float submissiveDirectGazeWeight = 0.12f;
    [Range(0f, 1f)] public float submissiveAvertedGazeWeight = 0.06f;
    public float headGazeLerpSpeed = 1.6f;
    [Tooltip("Submissive gaze cycle length. It briefly returns to the participant, then averts again.")]
    public float submissiveGazeCycleSeconds = 3.2f;
    public float submissiveDirectGazeSeconds = 0.85f;
    public float submissiveGazeSideOffset = 0.55f;
    public float submissiveGazeDownOffset = 0.24f;

    [Header("Nonverbal posture cues")]
    public bool enablePostureCues = true;
    [Tooltip("Small local spine pitch applied after the authored animation. Keep this subtle to avoid rig artifacts.")]
    public float dominantSpinePitchDegrees = -2f;
    public float submissiveSpinePitchDegrees = 3f;
    [Tooltip("Small local spine roll applied after the authored animation for a slightly less assertive silhouette.")]
    public float submissiveSpineRollDegrees = 2.5f;

    [Header("Condition animation routing")]
    [Tooltip("Sets Animator parameters from the current avatar condition. This lets an Animator Controller or Mixamo states react without changing the LLM/server code.")]
    public bool driveAnimatorConditionParameters = true;
    [Tooltip("Current controller fallback: dominant/submissive select different existing idle variants via IdleAnimIdx2.")]
    public bool useExistingIdleIndexFallback = true;
    public string existingIdleIndexParameter = "IdleAnimIdx2";
    public int dominantExistingIdleIndex = 0;
    public int submissiveExistingIdleIndex = 2;
    public string existingSpeakIndexParameter = "SpeakAnimIdx2";
    public int dominantExistingSpeakIndex = 0;
    public int submissiveExistingSpeakIndex = 2;
    public string dominanceFloatParameter = "DominanceLevel";
    public string dominantBoolParameter = "IsDominant";
    public string submissiveBoolParameter = "IsSubmissive";
    public string conditionIntParameter = "DominanceCondition";
    public string gestureTriggerParameter = "GestureTrigger";
    [Tooltip("Optional. Enable after adding Mixamo states to the Animator Controller.")]
    public bool crossFadeConditionStates = false;
    public int conditionAnimationLayer = 0;
    public float conditionCrossFadeSeconds = 0.25f;
    public string dominantIdleStateName = "Confident Idle";
    public string submissiveIdleStateName = "Nervous Idle";
    public string dominantGestureStateName = "Pointing";
    public string submissiveGestureStateName = "Shrugging";
    [Tooltip("If the optional gesture states exist, play one short condition-specific gesture when the backend condition changes.")]
    public bool playGestureOnConditionChange = true;

    private Transform participantHead;
    private Animator animator;
    private Transform headBone;
    private Transform chestBone;
    private Transform spineBone;
    private float gazePhaseOffset;
    private readonly Dictionary<int, AnimatorControllerParameterType> animatorParameterTypes =
        new Dictionary<int, AnimatorControllerParameterType>();
    private string lastAnimatorCondition = "";

    private IEnumerator Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        }
        CacheAnimatorParameters();
        gazePhaseOffset = Random.Range(0f, Mathf.Max(0.01f, submissiveGazeCycleSeconds));

        for (int i = 0; i < 60; i++)
        {
            participantHead = FindParticipantHead();
            if (participantHead != null && Mathf.Abs(participantHead.position.y) > 0.3f)
            {
                break;
            }
            yield return null;
        }

        if (participantHead != null)
        {
            PlaceNearParticipant(participantHead);
        }
    }

    private void LateUpdate()
    {
        if (!enableNonverbalCues)
        {
            return;
        }

        string condition = NormalizeCondition(PlayerData.avatarCondition);
        if (condition != "dom" && condition != "sub")
        {
            return;
        }

        if (participantHead == null)
        {
            participantHead = FindParticipantHead();
            if (participantHead == null)
            {
                return;
            }
        }

        ApplyAnimatorCondition(condition);
        ApplyBodyFacing(condition);
        if (enableHeadGazeCues)
        {
            ApplyHeadGaze(condition);
        }
        ApplyPosture(condition);
    }

    private void PlaceNearParticipant(Transform participantHead)
    {
        Vector3 authoredDirection = transform.position - participantHead.position;
        authoredDirection.y = 0f;
        float authoredDistance = authoredDirection.magnitude;
        if (authoredDistance < 0.1f)
        {
            authoredDirection = participantHead.forward;
            authoredDirection.y = 0f;
        }
        authoredDirection.Normalize();

        // Preserve a correctly authored location whenever it is already in range.
        if (authoredDistance >= minimumDistance && authoredDistance <= maximumDistance &&
            TryResolveGround(transform.position, participantHead, out Vector3 authoredGround) &&
            IsSafe(authoredGround, participantHead))
        {
            transform.position = authoredGround;
            return;
        }

        float[] distances = { preferredDistance, maximumDistance, minimumDistance };
        float[] angles = { 0f, 12f, -12f, 24f, -24f, 36f, -36f, 50f, -50f, 70f, -70f, 90f, -90f };
        foreach (float distance in distances)
        {
            foreach (float angle in angles)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * authoredDirection;
                Vector3 candidate = participantHead.position + direction * distance;
                if (!TryResolveGround(candidate, participantHead, out Vector3 groundedCandidate))
                {
                    continue;
                }

                if (!IsSafe(groundedCandidate, participantHead))
                {
                    continue;
                }

                transform.position = groundedCandidate;
                Debug.Log("[VRME] Avatar safely placed " + distance.ToString("0.00") +
                    "m from participant at " + groundedCandidate);
                return;
            }
        }

        Debug.LogWarning("[VRME] No safe 2.5-2.8m avatar position found; keeping the scene-authored position.");
    }

    private bool TryResolveGround(
        Vector3 horizontalCandidate,
        Transform participantHead,
        out Vector3 groundedCandidate)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            horizontalCandidate + Vector3.up * groundRayHeight,
            Vector3.down,
            groundRayDistance,
            placementMask,
            QueryTriggerInteraction.Ignore);

        float expectedFloorY = participantHead.position.y - 1.65f;
        float bestScore = float.PositiveInfinity;
        groundedCandidate = default;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform) || hit.normal.y < 0.65f)
            {
                continue;
            }

            float floorDifference = Mathf.Abs(hit.point.y - expectedFloorY);
            if (floorDifference > maximumFloorDifference || floorDifference >= bestScore)
            {
                continue;
            }

            bestScore = floorDifference;
            groundedCandidate = new Vector3(horizontalCandidate.x, hit.point.y, horizontalCandidate.z);
        }

        return !float.IsPositiveInfinity(bestScore);
    }

    private bool IsSafe(Vector3 candidate, Transform participantHead)
    {
        Vector3 bottom = candidate + Vector3.up * (bodyRadius + 0.06f);
        Vector3 top = candidate + Vector3.up * (bodyHeight - bodyRadius);
        foreach (Collider overlap in Physics.OverlapCapsule(
            bottom,
            top,
            bodyRadius,
            placementMask,
            QueryTriggerInteraction.Ignore))
        {
            if (overlap != null && overlap.transform != transform && !overlap.transform.IsChildOf(transform))
            {
                return false;
            }
        }

        Vector3 target = candidate + Vector3.up * 1.35f;
        Vector3 ray = target - participantHead.position;
        foreach (RaycastHit hit in Physics.RaycastAll(
            participantHead.position,
            ray.normalized,
            ray.magnitude,
            placementMask,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform) || hit.distance < 0.25f)
            {
                continue;
            }

            if (hit.distance < ray.magnitude - 0.15f)
            {
                return false;
            }
        }

        return true;
    }

    public void SetConditionFromBackend(string backendCondition)
    {
        PlayerData.avatarCondition = NormalizeCondition(backendCondition);
        Debug.Log("[VRME] Avatar nonverbal condition set to " + PlayerData.avatarCondition);
    }

    private void ApplyBodyFacing(string condition)
    {
        Vector3 toParticipant = participantHead.position - transform.position;
        toParticipant.y = 0f;
        if (toParticipant.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toParticipant.normalized, Vector3.up);
        if (condition == "sub")
        {
            float side = Mathf.Sin(gazePhaseOffset * 1.37f) >= 0f ? 1f : -1f;
            targetRotation *= Quaternion.Euler(0f, submissiveBodyAvertedDegrees * side, 0f);
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-bodyFacingLerpSpeed * Time.deltaTime));
    }

    private void ApplyHeadGaze(string condition)
    {
        if (headBone == null)
        {
            return;
        }

        Vector3 target = participantHead.position;
        float weight = dominantHeadGazeWeight;
        if (condition == "sub")
        {
            float cycle = Mathf.Repeat(Time.time + gazePhaseOffset, Mathf.Max(0.01f, submissiveGazeCycleSeconds));
            bool directGlance = cycle < submissiveDirectGazeSeconds;
            weight = directGlance ? submissiveDirectGazeWeight : submissiveAvertedGazeWeight;
            if (!directGlance)
            {
                float side = Mathf.Sin((Time.time + gazePhaseOffset) * 1.9f) >= 0f ? 1f : -1f;
                target += participantHead.right * submissiveGazeSideOffset * side;
                target += Vector3.down * submissiveGazeDownOffset;
            }
        }

        Vector3 lookDirection = target - headBone.position;
        if (lookDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        Quaternion weightedRotation = Quaternion.Slerp(headBone.rotation, targetRotation, weight);
        headBone.rotation = Quaternion.Slerp(
            headBone.rotation,
            weightedRotation,
            1f - Mathf.Exp(-headGazeLerpSpeed * Time.deltaTime));
    }

    private void ApplyPosture(string condition)
    {
        if (!enablePostureCues || animator == null || !animator.enabled)
        {
            return;
        }

        Transform postureBone = chestBone != null ? chestBone : spineBone;
        if (postureBone == null)
        {
            return;
        }

        if (condition == "dom")
        {
            postureBone.localRotation *= Quaternion.Euler(dominantSpinePitchDegrees, 0f, 0f);
        }
        else if (condition == "sub")
        {
            float side = Mathf.Sin(gazePhaseOffset * 1.37f) >= 0f ? 1f : -1f;
            postureBone.localRotation *= Quaternion.Euler(
                submissiveSpinePitchDegrees,
                0f,
                submissiveSpineRollDegrees * side);
        }
    }

    private void CacheAnimatorParameters()
    {
        animatorParameterTypes.Clear();
        if (animator == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            animatorParameterTypes[parameter.nameHash] = parameter.type;
        }
    }

    private void ApplyAnimatorCondition(string condition)
    {
        if (!driveAnimatorConditionParameters || animator == null || !animator.enabled)
        {
            return;
        }

        bool isDominant = condition == "dom";
        bool isSubmissive = condition == "sub";
        bool changed = condition != lastAnimatorCondition;

        SafeSetFloat(dominanceFloatParameter, isDominant ? 1f : 0f);
        SafeSetBool(dominantBoolParameter, isDominant);
        SafeSetBool(submissiveBoolParameter, isSubmissive);
        SafeSetInt(conditionIntParameter, isDominant ? 1 : isSubmissive ? -1 : 0);

        if (useExistingIdleIndexFallback)
        {
            SafeSetInt(existingIdleIndexParameter, isDominant ? dominantExistingIdleIndex : submissiveExistingIdleIndex);
            SafeSetInt(existingSpeakIndexParameter, isDominant ? dominantExistingSpeakIndex : submissiveExistingSpeakIndex);
        }

        if (changed)
        {
            SafeSetTrigger(gestureTriggerParameter);

            if (crossFadeConditionStates)
            {
                string idleState = isDominant ? dominantIdleStateName : submissiveIdleStateName;
                TryCrossFadeState(idleState);

                if (playGestureOnConditionChange)
                {
                    string gestureState = isDominant ? dominantGestureStateName : submissiveGestureStateName;
                    TryCrossFadeState(gestureState);
                }
            }

            lastAnimatorCondition = condition;
        }
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        return animatorParameterTypes.TryGetValue(Animator.StringToHash(parameterName), out AnimatorControllerParameterType actualType) &&
            actualType == expectedType;
    }

    private void SafeSetFloat(string parameterName, float value)
    {
        if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Float))
        {
            animator.SetFloat(parameterName, value);
        }
    }

    private void SafeSetBool(string parameterName, bool value)
    {
        if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Bool))
        {
            animator.SetBool(parameterName, value);
        }
    }

    private void SafeSetInt(string parameterName, int value)
    {
        if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Int))
        {
            animator.SetInteger(parameterName, value);
        }
    }

    private void SafeSetTrigger(string parameterName)
    {
        if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(parameterName);
        }
    }

    private bool TryCrossFadeState(string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int layer = Mathf.Clamp(conditionAnimationLayer, 0, Mathf.Max(0, animator.layerCount - 1));
        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(layer, stateHash))
        {
            return false;
        }

        animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, conditionCrossFadeSeconds), layer);
        return true;
    }

    public static string NormalizeCondition(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
        if (normalized == "dominant" || normalized == "dominance" || normalized == "dom")
        {
            return "dom";
        }
        if (normalized == "submissive" || normalized == "submission" || normalized == "sub")
        {
            return "sub";
        }
        return "observer";
    }

    private static Transform FindParticipantHead()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.isActiveAndEnabled)
        {
            return mainCamera.transform;
        }

        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (camera != null && camera.isActiveAndEnabled)
            {
                return camera.transform;
            }
        }
        return null;
    }
}
