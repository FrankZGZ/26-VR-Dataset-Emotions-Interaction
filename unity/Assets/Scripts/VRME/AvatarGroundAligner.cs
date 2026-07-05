using System.Collections;
using UnityEngine;

/// <summary>
/// Corrects the foot/root offset difference between the former ReadyPlayerMe
/// avatar and the Rocketbox replacement. Alignment runs once and remains fixed.
/// </summary>
public class AvatarGroundAligner : MonoBehaviour
{
    [Range(0.1f, 3f)] public float rayStartAboveFeet = 0.75f;
    [Range(1f, 10f)] public float rayDistance = 4f;
    [Range(0.01f, 2f)] public float maximumCorrection = 1.25f;

    private IEnumerator Start()
    {
        // Let animated skinned-mesh bounds settle before measuring the feet.
        yield return null;
        yield return new WaitForEndOfFrame();
        AlignFeetToGround();
    }

    private void AlignFeetToGround()
    {
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

        Vector3 origin = new Vector3(
            avatarBounds.center.x,
            avatarBounds.min.y + rayStartAboveFeet,
            avatarBounds.center.z);

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            rayDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float bestGroundY = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.point.y <= avatarBounds.min.y + rayStartAboveFeet &&
                hit.point.y > bestGroundY)
            {
                bestGroundY = hit.point.y;
            }
        }

        if (float.IsNegativeInfinity(bestGroundY))
        {
            Debug.LogWarning("[VRME] Avatar ground alignment skipped: no ground collider below the feet.");
            return;
        }

        float correction = bestGroundY - avatarBounds.min.y;
        if (Mathf.Abs(correction) > maximumCorrection)
        {
            Debug.LogWarning("[VRME] Avatar ground alignment rejected excessive correction: " + correction);
            return;
        }

        transform.position += Vector3.up * correction;
        Debug.Log("[VRME] Avatar feet aligned to ground. correctionY=" + correction.ToString("0.000"));
    }
}
