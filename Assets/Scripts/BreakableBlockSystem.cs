using UnityEngine;

[DisallowMultipleComponent]
public sealed class BreakableBlockSystem : MonoBehaviour
{
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
        PlayerMovement player = collision.gameObject.GetComponentInParent<PlayerMovement>();

        if (player == null || collision.relativeVelocity.y <= 0f)
            return;

        Collider playerCollider = player.GetComponent<Collider>();

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            Collider blockCollider = GetSystemCollider(contact);

            if (blockCollider == null)
                continue;

            bool playerIsBelowBlock =
                playerCollider.bounds.center.y < blockCollider.bounds.center.y;
            bool hitBlockUnderside =
                contact.point.y <= blockCollider.bounds.center.y;

            if (!playerIsBelowBlock || !hitBlockUnderside)
                continue;

            GameObject block = blockCollider.gameObject;
            block.SetActive(false);
            Destroy(block);
            return;
        }
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
