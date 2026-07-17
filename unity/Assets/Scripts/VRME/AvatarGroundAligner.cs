using System.Collections;
using UnityEngine;

/// <summary>
/// Corrects the foot/root offset difference between the former ReadyPlayerMe
/// avatar and the Rocketbox replacement. Alignment runs once and remains fixed.
/// </summary>
public class AvatarGroundAligner : MonoBehaviour
{
    [Range(0.1f, 4f)] public float rayStartAboveFeet = 2.5f;
    [Range(1f, 10f)] public float rayDistance = 6f;
    [Range(0.01f, 2f)] public float maximumCorrection = 1.25f;

    private IEnumerator Start()
    {
        // Let animated skinned-mesh bounds settle before measuring the feet.
        yield return null;
        yield return new WaitForEndOfFrame();
        AlignFeetToGround();
    }

    public void AlignFeetToGround()
    {
        // Include the prefab's non-skinned renderers. They provide the stable
        // authored foot bound; animated SkinnedMeshRenderer bounds alone can
        // extend roughly a metre below the visible feet in the prison idle pose.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds avatarBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            avatarBounds.Encapsulate(renderers[i].bounds);
        }

        float bestGroundY;
        bool usedParticipantFloor = false;
        if (!TryResolveGroundY(avatarBounds.center, avatarBounds.min.y, out bestGroundY))
        {
            OVRCameraRig[] rigs = Object.FindObjectsByType<OVRCameraRig>(FindObjectsSortMode.None);
            bool foundParticipantFloor = false;
            foreach (OVRCameraRig rig in rigs)
            {
                Transform eye = rig != null ? rig.centerEyeAnchor : null;
                if (eye != null && TryResolveGroundY(eye.position, eye.position.y - 1.62f, out bestGroundY))
                {
                    foundParticipantFloor = true;
                    usedParticipantFloor = true;
                    break;
                }
            }

            if (!foundParticipantFloor)
            {
                Debug.LogWarning("[VRME] Avatar ground alignment skipped: no ground collider below the avatar or participant.");
                return;
            }
        }

        float correction = bestGroundY - avatarBounds.min.y;
        if (Mathf.Abs(correction) > maximumCorrection)
        {
            Debug.LogWarning("[VRME] Avatar ground alignment rejected excessive correction: " + correction);
            return;
        }

        transform.position += Vector3.up * correction;
        Debug.Log("[VRME] Avatar feet aligned to global floor. groundY=" + bestGroundY.ToString("0.000") +
            ", correctionY=" + correction.ToString("0.000") +
            ", source=" + (usedParticipantFloor ? "participant_floor_fallback" : "avatar_floor"));
    }

    private bool TryResolveGroundY(Vector3 horizontalPosition, float expectedFeetY, out float groundY)
    {
        Vector3 origin = new Vector3(
            horizontalPosition.x,
            expectedFeetY + rayStartAboveFeet,
            horizontalPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            rayDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        groundY = 0f;
        float bestScore = float.PositiveInfinity;
        bool foundNamedFloor = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.normal.y < 0.65f ||
                hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            bool namedFloor = LooksLikeFloor(hit.transform);
            float score = Mathf.Abs(hit.point.y - expectedFeetY);
            if ((namedFloor && !foundNamedFloor) ||
                (namedFloor == foundNamedFloor && score < bestScore))
            {
                foundNamedFloor = namedFloor;
                bestScore = score;
                groundY = hit.point.y;
            }
        }

        return !float.IsPositiveInfinity(bestScore);
    }

    private static bool LooksLikeFloor(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            string objectName = current.name;
            if (current.CompareTag("Ground") ||
                objectName.IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("ground", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
