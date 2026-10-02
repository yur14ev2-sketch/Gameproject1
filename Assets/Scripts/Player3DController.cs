using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class Player3DController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 20f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpHeight = 3f;
    [SerializeField, Min(1f)] private float jumpGravityMultiplier = 3f;
    [SerializeField, Min(0f)] private float airControlAcceleration = 8f;

    [Header("Collision Feel")]
    [SerializeField, Range(0f, 1f)] private float surfaceFriction = 0.1f;

    [Header("Mouse Look")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 2.5f;
    [SerializeField] private float minimumPitch = -25f;
    [SerializeField] private float maximumPitch = 65f;

    [Header("Camera")]
    [SerializeField, Min(0.1f)] private float cameraDistance = 8f;
    [SerializeField] private float cameraHeight = 2f;

    private Rigidbody body;
    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();
    private readonly Dictionary<Collider, Vector3> wallContacts = new Dictionary<Collider, Vector3>();
    private Vector2 moveInput;
    private bool jumpRequested;
    private float yaw;
    private float pitch = 15f;
    private PhysicMaterial playerMaterial;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        yaw = transform.eulerAngles.y;

        playerMaterial = new PhysicMaterial("Player 3D Low Friction")
        {
            dynamicFriction = surfaceFriction,
            staticFriction = surfaceFriction,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };

        foreach (Collider playerCollider in GetComponentsInChildren<Collider>(true))
            playerCollider.sharedMaterial = playerMaterial;

        if (cameraTransform == null)
        {
            Debug.LogError("Player 3D Controller requires a camera reference.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        LockCursor();
    }

    private void Update()
    {
        moveInput = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);

        if (Input.GetKeyDown(KeyCode.Space))
            jumpRequested = true;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
        }
        else if (Input.GetMouseButtonDown(0))
        {
            LockCursor();
        }
    }

    private void FixedUpdate()
    {
        bool grounded = groundContacts.Count > 0;
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 desiredDirection = yawRotation * new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 velocity = body.velocity;

        if (grounded)
        {
            velocity.x = desiredDirection.x * moveSpeed;
            velocity.z = desiredDirection.z * moveSpeed;
        }
        else if (moveInput.sqrMagnitude > 0.001f)
        {
            Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 desiredHorizontalVelocity = desiredDirection * moveSpeed;
            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                desiredHorizontalVelocity,
                airControlAcceleration * Time.fixedDeltaTime);
            velocity.x = horizontalVelocity.x;
            velocity.z = horizontalVelocity.z;
        }

        RemoveVelocityIntoWalls(ref velocity);

        if (jumpRequested && grounded)
        {
            float jumpGravity = Mathf.Abs(Physics.gravity.y) * jumpGravityMultiplier;
            velocity.y = Mathf.Sqrt(2f * jumpGravity * jumpHeight);
            groundContacts.Clear();
            grounded = false;
        }

        if (!grounded)
            velocity += Physics.gravity * ((jumpGravityMultiplier - 1f) * Time.fixedDeltaTime);

        body.velocity = velocity;
        jumpRequested = false;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
            return;

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = transform.position + Vector3.up * cameraHeight;
        Vector3 cameraPosition = focusPoint - orbitRotation * Vector3.forward * cameraDistance;
        cameraTransform.SetPositionAndRotation(cameraPosition, orbitRotation);
    }

    private void OnDisable()
    {
        moveInput = Vector2.zero;
        jumpRequested = false;
        groundContacts.Clear();
        wallContacts.Clear();

        if (body != null)
        {
            Vector3 velocity = body.velocity;
            velocity.x = 0f;
            velocity.z = 0f;
            body.velocity = velocity;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        groundContacts.Remove(collision.collider);
        wallContacts.Remove(collision.collider);
    }

    private void UpdateGroundContact(Collision collision)
    {
        bool isGround = false;
        Vector3 strongestWallNormal = Vector3.zero;
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;
            if (normal.y > 0.5f)
            {
                isGround = true;
            }

            if (normal.y <= 0.5f)
            {
                Vector3 horizontalNormal = Vector3.ProjectOnPlane(normal, Vector3.up);
                if (horizontalNormal.sqrMagnitude > strongestWallNormal.sqrMagnitude)
                    strongestWallNormal = horizontalNormal;
            }
        }

        if (isGround)
            groundContacts.Add(collision.collider);
        else
            groundContacts.Remove(collision.collider);

        if (strongestWallNormal.sqrMagnitude > 0.01f)
            wallContacts[collision.collider] = strongestWallNormal.normalized;
        else
            wallContacts.Remove(collision.collider);
    }

    private void RemoveVelocityIntoWalls(ref Vector3 velocity)
    {
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        foreach (Vector3 wallNormal in wallContacts.Values)
        {
            float intoWallSpeed = Vector3.Dot(horizontalVelocity, wallNormal);
            if (intoWallSpeed < 0f)
                horizontalVelocity -= wallNormal * intoWallSpeed;
        }

        velocity.x = horizontalVelocity.x;
        velocity.z = horizontalVelocity.z;
    }

    private void OnDestroy()
    {
        if (playerMaterial != null)
            Destroy(playerMaterial);
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
