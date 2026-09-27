using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class Player3DController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 20f;

    [Header("Mouse Look")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 2.5f;
    [SerializeField] private float minimumPitch = -25f;
    [SerializeField] private float maximumPitch = 65f;

    [Header("Camera")]
    [SerializeField, Min(0.1f)] private float cameraDistance = 8f;
    [SerializeField] private float cameraHeight = 2f;

    private Rigidbody body;
    private Vector2 moveInput;
    private float yaw;
    private float pitch = 15f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        yaw = transform.eulerAngles.y;

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
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 desiredDirection = yawRotation * new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 velocity = body.velocity;
        velocity.x = desiredDirection.x * moveSpeed;
        velocity.z = desiredDirection.z * moveSpeed;
        body.velocity = velocity;
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

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
