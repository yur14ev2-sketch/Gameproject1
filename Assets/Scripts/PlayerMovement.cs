using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class PlayerMovement : MonoBehaviour
{
    private enum MovementPlane
    {
        XY,
        ZY
    }

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.3f;
    [SerializeField] private MovementPlane movementPlane = MovementPlane.XY;

    private Rigidbody body;
    private float horizontalInput;
    private bool jumpRequested;
    private bool isGrounded;
    private bool isRising;
    private bool ceilingHitRequested;
    private PhysicMaterial noFrictionMaterial;
    private CoinSystem coinSystem;
    private bool inputEnabled = true;
    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation |
                           (movementPlane == MovementPlane.XY
                               ? RigidbodyConstraints.FreezePositionZ
                               : RigidbodyConstraints.FreezePositionX);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.useGravity = true;
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

        if (!inputEnabled)
        {
            jumpRequested = false;
            return;
        }

        if (Input.GetKey(KeyCode.A))
            horizontalInput -= 1f;

        if (Input.GetKey(KeyCode.D))
            horizontalInput += 1f;

        if (Input.GetKeyDown(KeyCode.Space))
            jumpRequested = true;
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        horizontalInput = 0f;
        jumpRequested = false;

        if (!enabled && body != null)
            body.velocity = Vector3.zero;
    }

    public void ConfigureForCamera(Camera targetCamera)
    {
        if (targetCamera == null)
            return;

        Vector3 cameraRight = targetCamera.transform.right;
        movementPlane = Mathf.Abs(cameraRight.x) >= Mathf.Abs(cameraRight.z)
            ? MovementPlane.XY
            : MovementPlane.ZY;

        if (body == null)
            body = GetComponent<Rigidbody>();

        body.constraints = RigidbodyConstraints.FreezeRotation |
                           (movementPlane == MovementPlane.XY
                               ? RigidbodyConstraints.FreezePositionZ
                               : RigidbodyConstraints.FreezePositionX);
    }

    private void FixedUpdate()
    {
        Vector3 velocity = body.velocity;

        if (movementPlane == MovementPlane.XY)
            velocity.x = horizontalInput * moveSpeed;
        else
            velocity.z = horizontalInput * moveSpeed;

        if (jumpRequested && isGrounded)
        {
            float gravity = Mathf.Abs(Physics.gravity.y);
            velocity.y = Mathf.Sqrt(2f * gravity * jumpHeight);
            isRising = true;
            ceilingHitRequested = false;
            isGrounded = false;
            groundContacts.Clear();
        }

        if (isRising)
        {
            if (TryHitQuestionBlock(out Collider questionBlock))
            {
                coinSystem?.TryPopCoin(questionBlock);
                velocity.y = Mathf.Min(velocity.y, 0f);
                isRising = false;
                ceilingHitRequested = false;
            }
            else if (ceilingHitRequested)
            {
                velocity.y = Mathf.Min(velocity.y, 0f);
                isRising = false;
                ceilingHitRequested = false;
            }
        }

        if (velocity.y <= 0f)
            isRising = false;

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
        float checkDistance = Mathf.Max(0f, body.velocity.y) * Time.fixedDeltaTime + 0.05f;
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

            float horizontalDistance = movementPlane == MovementPlane.XY
                ? Mathf.Abs(hitCollider.bounds.center.x - playerBounds.center.x)
                : Mathf.Abs(hitCollider.bounds.center.z - playerBounds.center.z);

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
