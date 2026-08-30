using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.3f;
    [SerializeField, Min(0.01f)] private float jumpSpeed = 8f;

    private Rigidbody body;
    private float horizontalInput;
    private bool jumpRequested;
    private bool isGrounded;
    private bool isRising;
    private bool ceilingHitRequested;
    private float jumpStartY;
    private PhysicMaterial noFrictionMaterial;
    private CoinSystem coinSystem;
    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezePositionZ |
                           RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.useGravity = false;
        coinSystem = FindObjectOfType<CoinSystem>();

        noFrictionMaterial = new PhysicMaterial("Player No Friction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
        GetComponent<BoxCollider>().sharedMaterial = noFrictionMaterial;
    }

    private void Update()
    {
        horizontalInput = 0f;

        if (Input.GetKey(KeyCode.A))
            horizontalInput -= 1f;

        if (Input.GetKey(KeyCode.D))
            horizontalInput += 1f;

        if (Input.GetKeyDown(KeyCode.Space))
            jumpRequested = true;
    }

    private void FixedUpdate()
    {
        Vector3 velocity = body.velocity;
        velocity.x = horizontalInput * moveSpeed;

        if (jumpRequested && isGrounded)
        {
            jumpStartY = body.position.y;
            isRising = true;
            ceilingHitRequested = false;
            isGrounded = false;
            groundContacts.Clear();
        }

        if (isGrounded)
        {
            velocity.y = 0f;
        }
        else if (isRising)
        {
            if (TryHitQuestionBlock(out Collider questionBlock))
            {
                coinSystem?.TryPopCoin(questionBlock);
                velocity.y = -jumpSpeed;
                isRising = false;
                ceilingHitRequested = false;
            }
            else if (ceilingHitRequested)
            {
                velocity.y = -jumpSpeed;
                isRising = false;
                ceilingHitRequested = false;
            }
            else if (body.position.y >= jumpStartY + jumpHeight)
            {
                Vector3 position = body.position;
                position.y = jumpStartY + jumpHeight;
                body.position = position;
                velocity.y = -jumpSpeed;
                isRising = false;
            }
            else
            {
                velocity.y = jumpSpeed;
            }
        }
        else
        {
            velocity.y = -jumpSpeed;
        }

        body.velocity = velocity;
        jumpRequested = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        EvaluateCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        EvaluateCollision(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        groundContacts.Remove(collision.collider);
        isGrounded = groundContacts.Count > 0;
    }

    private void EvaluateCollision(Collision collision)
    {
        bool hasGroundContact = false;

        for (int i = 0; i < collision.contactCount; i++)
        {
            float normalY = collision.GetContact(i).normal.y;

            if (!isRising && normalY > 0.5f)
                hasGroundContact = true;

            if (isRising && normalY < -0.5f)
            {
                ceilingHitRequested = true;
            }
        }

        if (hasGroundContact)
            groundContacts.Add(collision.collider);
        else
            groundContacts.Remove(collision.collider);

        isGrounded = groundContacts.Count > 0;
    }

    private bool TryHitQuestionBlock(out Collider selectedBlock)
    {
        selectedBlock = null;

        Collider playerCollider = GetComponent<Collider>();
        Bounds playerBounds = playerCollider.bounds;
        float checkDistance = jumpSpeed * Time.fixedDeltaTime + 0.05f;
        Vector3 halfExtents = playerBounds.extents;
        halfExtents.x *= 0.9f;
        halfExtents.y *= 0.9f;

        RaycastHit[] hits = Physics.BoxCastAll(
            playerBounds.center,
            halfExtents,
            Vector3.up,
            Quaternion.identity,
            checkDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float closestHorizontalDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;

            if (hitCollider == playerCollider ||
                hitCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            string normalizedName = hitCollider.name.Replace(" ", "").ToLowerInvariant();

            if (!normalizedName.StartsWith("questionblock"))
                continue;

            float horizontalDistance = Mathf.Abs(
                hitCollider.bounds.center.x - playerBounds.center.x);

            if (horizontalDistance < closestHorizontalDistance)
            {
                closestHorizontalDistance = horizontalDistance;
                selectedBlock = hitCollider;
            }
        }

        return selectedBlock != null;
    }

    private void OnDestroy()
    {
        if (noFrictionMaterial != null)
            Destroy(noFrictionMaterial);
    }
}
