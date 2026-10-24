using UnityEngine;

[DisallowMultipleComponent]
public sealed class BreakableBlockSystem : MonoBehaviour
{
    [Header("Break Detection")]
    [SerializeField, Min(0.01f)] private float minimumUpwardSpeed = 0.1f;
    [SerializeField, Min(0f)] private float undersideTolerance = 0.03f;

    private void Awake()
    {
        Rigidbody systemBody = GetComponent<Rigidbody>();

        if (systemBody == null)
            systemBody = gameObject.AddComponent<Rigidbody>();

        systemBody.isKinematic = true;
        systemBody.useGravity = false;
        systemBody.constraints = RigidbodyConstraints.FreezeAll;
    }

    private void OnCollisionEnter(Collision collision)
    {
        PlayerMovement legacyPlayer = collision.gameObject.GetComponentInParent<PlayerMovement>();
        PaintingPlayer2DController paintingPlayer =
            collision.gameObject.GetComponentInParent<PaintingPlayer2DController>();

        if (legacyPlayer == null && paintingPlayer == null)
            return;

        Component player = paintingPlayer != null ? paintingPlayer : legacyPlayer;
        Collider playerCollider = player.GetComponent<Collider>();
        Rigidbody playerBody = player.GetComponent<Rigidbody>();

        if (playerCollider == null || playerBody == null)
            return;

        // Unity may already have reduced the rigidbody velocity by the time
        // OnCollisionEnter runs, so retain the pre-solve impact speed too.
        float verticalImpactSpeed = Mathf.Max(
            playerBody.velocity.y,
            Mathf.Abs(collision.relativeVelocity.y));

        if (verticalImpactSpeed < minimumUpwardSpeed)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            Collider blockCollider = GetSystemCollider(contact);

            if (blockCollider == null)
                continue;

            GameObject managedBlock = GetDirectChildBlock(blockCollider.transform);
            if (managedBlock == null)
                continue;

            Bounds playerBounds = playerCollider.bounds;
            Bounds blockBounds = blockCollider.bounds;
            bool playerIsBelowBlock =
                playerBounds.center.y < blockBounds.center.y &&
                playerBounds.max.y <= blockBounds.center.y;
            bool hitBlockUnderside =
                contact.point.y <= blockBounds.min.y + undersideTolerance;

            if (!playerIsBelowBlock || !hitBlockUnderside)
                continue;

            // Disable immediately so this collision cannot be reported again
            // before Unity performs the end-of-frame destruction.
            managedBlock.SetActive(false);
            Destroy(managedBlock);
            return;
        }
    }

    private GameObject GetDirectChildBlock(Transform hitTransform)
    {
        if (hitTransform == null || !hitTransform.IsChildOf(transform))
            return null;

        Transform current = hitTransform;
        while (current.parent != null && current.parent != transform)
            current = current.parent;

        return current.parent == transform ? current.gameObject : null;
    }

    private Collider GetSystemCollider(ContactPoint contact)
    {
        if (contact.thisCollider != null &&
            contact.thisCollider.transform.IsChildOf(transform))
        {
            return contact.thisCollider;
        }

        if (contact.otherCollider != null &&
            contact.otherCollider.transform.IsChildOf(transform))
        {
            return contact.otherCollider;
        }

        return null;
    }
}
