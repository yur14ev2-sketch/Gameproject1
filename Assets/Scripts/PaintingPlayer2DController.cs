using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PaintingPlayer2DController : MonoBehaviour
{
    [Header("2D Controls")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(0f)] private float jumpHeight = 2f;

    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();
    private readonly Dictionary<Collider, Vector3> contactNormals = new Dictionary<Collider, Vector3>();
    private Rigidbody body;
    private Vector3 movementAxis = Vector3.right;
    private float horizontalInput;
    private bool jumpRequested;
    private bool controlEnabled;
    private PhysicMaterial noFrictionMaterial;
    private float blockedTimer;
    private float nextBlockedLogTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        noFrictionMaterial = new PhysicMaterial("2D Player No Friction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };

        foreach (Collider playerCollider in GetComponentsInChildren<Collider>(true))
            playerCollider.sharedMaterial = noFrictionMaterial;

        SetControlEnabled(false, null);
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!controlEnabled)
            return;

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
        if (!controlEnabled || body.isKinematic)
            return;

        Vector3 velocity = body.velocity;
        float measuredHorizontalSpeed = Mathf.Abs(Vector3.Dot(velocity, movementAxis));
        float verticalVelocity = velocity.y;
        velocity = movementAxis * (horizontalInput * moveSpeed);
        velocity.y = verticalVelocity;

        if (jumpRequested && groundContacts.Count > 0)
        {
            velocity.y = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            groundContacts.Clear();
        }

        body.velocity = velocity;
        jumpRequested = false;

        if (Mathf.Abs(horizontalInput) > 0.5f && measuredHorizontalSpeed < 0.1f)
        {
            blockedTimer += Time.fixedDeltaTime;
            if (blockedTimer >= 0.35f && Time.time >= nextBlockedLogTime)
            {
                LogBlockedState(measuredHorizontalSpeed);
                nextBlockedLogTime = Time.time + 1f;
            }
        }
        else
        {
            blockedTimer = 0f;
        }
    }

    public void SetControlEnabled(bool enabled, Camera paintingCamera)
    {
        if (body == null)
            body = GetComponent<Rigidbody>();

        controlEnabled = enabled;
        horizontalInput = 0f;
        jumpRequested = false;
        groundContacts.Clear();
        contactNormals.Clear();
        blockedTimer = 0f;

        if (enabled && paintingCamera != null)
        {
            movementAxis = paintingCamera.transform.right;
            movementAxis.y = 0f;
            movementAxis.Normalize();

            body.constraints = RigidbodyConstraints.FreezeRotation |
                               (Mathf.Abs(movementAxis.x) >= Mathf.Abs(movementAxis.z)
                                   ? RigidbodyConstraints.FreezePositionZ
                                   : RigidbodyConstraints.FreezePositionX);
        }

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = !enabled;
        body.useGravity = enabled;
    }

    private void OnCollisionEnter(Collision collision) => EvaluateGround(collision);
    private void OnCollisionStay(Collision collision) => EvaluateGround(collision);

    private void OnCollisionExit(Collision collision)
    {
        groundContacts.Remove(collision.collider);
        contactNormals.Remove(collision.collider);
    }

    private void EvaluateGround(Collision collision)
    {
        bool grounded = false;
        Vector3 strongestNormal = Vector3.zero;
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;
            if (Mathf.Abs(Vector3.Dot(normal, movementAxis)) > Mathf.Abs(Vector3.Dot(strongestNormal, movementAxis)))
                strongestNormal = normal;

            if (normal.y > 0.5f)
            {
                grounded = true;
            }
        }

        contactNormals[collision.collider] = strongestNormal;

        if (grounded)
            groundContacts.Add(collision.collider);
        else
            groundContacts.Remove(collision.collider);
    }

    private void LogBlockedState(float horizontalSpeed)
    {
        string contacts = "none";
        foreach (KeyValuePair<Collider, Vector3> contact in contactNormals)
        {
            string item = $"{contact.Key.name} normal={contact.Value} bounds={contact.Key.bounds}";
            contacts = contacts == "none" ? item : contacts + " | " + item;
        }

        Debug.LogWarning(
            $"[Player2D Stuck] player={name}, position={transform.position}, input={horizontalInput:F1}, " +
            $"velocity={body.velocity}, horizontalSpeed={horizontalSpeed:F3}, grounded={groundContacts.Count > 0}, " +
            $"contacts={contacts}",
            this);
    }

    private void OnDestroy()
    {
        if (noFrictionMaterial != null)
            Destroy(noFrictionMaterial);
    }
}
