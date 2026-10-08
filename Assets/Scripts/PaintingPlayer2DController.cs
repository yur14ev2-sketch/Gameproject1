using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PaintingPlayer2DController : MonoBehaviour
{
    [Header("2D Controls")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(0f)] private float jumpHeight = 2f;

    [Header("Visual Depth")]
    [Tooltip("Moves only the red model toward the painting camera. Physics stays on the projection plane.")]
    [SerializeField, Min(0f)] private float visualDepthOffset = 0.06f;

    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();
    private readonly Dictionary<Collider, Vector3> contactNormals = new Dictionary<Collider, Vector3>();
    private Rigidbody body;
    private Transform visualRoot;
    private Vector3 movementAxis = Vector3.right;
    private float horizontalInput;
    private bool jumpRequested;
    private bool isRising;
    private bool ceilingHitRequested;
    private bool controlEnabled;
    private PhysicMaterial noFrictionMaterial;
    private float blockedTimer;
    private float nextBlockedLogTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        visualRoot = transform.Find("Visual");
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

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
            isRising = true;
            ceilingHitRequested = false;
            groundContacts.Clear();
        }

        if (isRising)
        {
            if (TryHitQuestionBlock(out Collider questionBlock))
            {
                TryActivateQuestionBlock(questionBlock);
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
            else if (velocity.y <= 0f)
            {
                isRising = false;
            }
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
        isRising = false;
        ceilingHitRequested = false;

        if (enabled && paintingCamera != null)
        {
            movementAxis = paintingCamera.transform.right;
            movementAxis.y = 0f;
            movementAxis.Normalize();

            body.constraints = RigidbodyConstraints.FreezeRotation |
                               (Mathf.Abs(movementAxis.x) >= Mathf.Abs(movementAxis.z)
                                   ? RigidbodyConstraints.FreezePositionZ
                                   : RigidbodyConstraints.FreezePositionX);

            SetVisualDepth(paintingCamera);
        }
        else if (visualRoot != null)
        {
            visualRoot.localPosition = Vector3.zero;
        }

        if (enabled)
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            RegisterWithCoinSystems();
        }
        else
        {
            UnregisterFromCoinSystems();
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.useGravity = false;
            body.isKinematic = true;
        }
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

            if (isRising && normal.y < -0.5f)
                ceilingHitRequested = true;
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
        UnregisterFromCoinSystems();

        if (noFrictionMaterial != null)
            Destroy(noFrictionMaterial);
    }

    private void SetVisualDepth(Camera paintingCamera)
    {
        if (visualRoot == null)
            visualRoot = transform.Find("Visual");
        if (visualRoot == null || paintingCamera == null)
            return;

        // The projection mesh and the physical player intentionally share one
        // collision plane. Only the artwork is pulled toward the camera so the
        // black silhouette cannot visually cover the red player.
        visualRoot.position = transform.position -
                              paintingCamera.transform.forward.normalized * visualDepthOffset;
    }

    private bool TryHitQuestionBlock(out Collider selectedBlock)
    {
        selectedBlock = null;
        Collider playerCollider = GetComponent<Collider>();

        if (playerCollider == null)
            return false;

        Bounds bounds = playerCollider.bounds;
        Vector3 halfExtents = bounds.extents;
        halfExtents.x = Mathf.Max(0.01f, halfExtents.x * 0.8f);
        halfExtents.z = Mathf.Max(0.01f, halfExtents.z * 0.8f);
        halfExtents.y = 0.02f;

        Vector3 origin = new Vector3(
            bounds.center.x,
            bounds.max.y - 0.01f,
            bounds.center.z);
        float castDistance = Mathf.Max(0.12f, body.velocity.y * Time.fixedDeltaTime + 0.08f);
        RaycastHit[] hits = Physics.BoxCastAll(
            origin,
            halfExtents,
            Vector3.up,
            Quaternion.identity,
            castDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;

            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                continue;

            string normalizedName =
                hitCollider.name.Replace(" ", "").ToLowerInvariant();

            if (!normalizedName.StartsWith("questionblock"))
                continue;

            if (hits[i].distance < nearestDistance)
            {
                nearestDistance = hits[i].distance;
                selectedBlock = hitCollider;
            }
        }

        return selectedBlock != null;
    }

    private void TryActivateQuestionBlock(Collider questionBlock)
    {
        CoinSystem[] coinSystems = FindObjectsOfType<CoinSystem>(true);

        for (int i = 0; i < coinSystems.Length; i++)
        {
            if (coinSystems[i].TryPopCoin(questionBlock))
                return;
        }
    }

    private void RegisterWithCoinSystems()
    {
        Collider playerCollider = GetComponent<Collider>();

        if (playerCollider == null)
            return;

        CoinSystem[] coinSystems = FindObjectsOfType<CoinSystem>(true);

        for (int i = 0; i < coinSystems.Length; i++)
            coinSystems[i].RegisterPlayer(playerCollider);
    }

    private void UnregisterFromCoinSystems()
    {
        Collider playerCollider = GetComponent<Collider>();

        if (playerCollider == null)
            return;

        CoinSystem[] coinSystems = FindObjectsOfType<CoinSystem>(true);

        for (int i = 0; i < coinSystems.Length; i++)
            coinSystems[i].UnregisterPlayer(playerCollider);
    }
}
