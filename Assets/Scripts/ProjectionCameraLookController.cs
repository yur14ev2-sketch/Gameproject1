using UnityEngine;

[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public sealed class ProjectionCameraLookController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameModeManager gameModeManager;

    [Header("Look Controls")]
    [SerializeField, Min(0f)] private float rotationSpeed = 45f;
    [SerializeField, Range(0f, 180f)] private float horizontalLimit = 60f;
    [SerializeField, Range(0f, 89f)] private float upwardLimit = 45f;
    [SerializeField, Range(0f, 89f)] private float downwardLimit = 35f;
    [SerializeField] private bool resetOnEnter = true;

    private Quaternion centerLocalRotation;
    private float yawOffset;
    private float pitchOffset;
    private bool viewActive;

    private void Awake()
    {
        ResolveManager();
        centerLocalRotation = transform.localRotation;
    }

    private void OnEnable()
    {
        ResolveManager();
        viewActive = false;
    }

    private void Update()
    {
        bool shouldBeActive = gameModeManager != null && gameModeManager.IsProjectionViewActive;
        if (!shouldBeActive)
        {
            viewActive = false;
            return;
        }

        if (!viewActive)
            BeginViewControl();

        float horizontalInput = 0f;
        float verticalInput = 0f;
        if (Input.GetKey(KeyCode.LeftArrow))
            horizontalInput -= 1f;
        if (Input.GetKey(KeyCode.RightArrow))
            horizontalInput += 1f;
        if (Input.GetKey(KeyCode.DownArrow))
            verticalInput -= 1f;
        if (Input.GetKey(KeyCode.UpArrow))
            verticalInput += 1f;

        yawOffset += horizontalInput * rotationSpeed * Time.deltaTime;
        pitchOffset -= verticalInput * rotationSpeed * Time.deltaTime;
        yawOffset = Mathf.Clamp(yawOffset, -horizontalLimit, horizontalLimit);
        pitchOffset = Mathf.Clamp(pitchOffset, -upwardLimit, downwardLimit);

        transform.localRotation = centerLocalRotation * Quaternion.Euler(pitchOffset, yawOffset, 0f);
    }

    private void BeginViewControl()
    {
        viewActive = true;
        if (resetOnEnter)
        {
            yawOffset = 0f;
            pitchOffset = 0f;
            transform.localRotation = centerLocalRotation;
        }

    }

    private void OnDisable()
    {
        viewActive = false;
    }

    private void ResolveManager()
    {
        if (gameModeManager == null)
            gameModeManager = FindObjectOfType<GameModeManager>(true);
    }

    private void OnValidate()
    {
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        horizontalLimit = Mathf.Clamp(horizontalLimit, 0f, 180f);
        upwardLimit = Mathf.Clamp(upwardLimit, 0f, 89f);
        downwardLimit = Mathf.Clamp(downwardLimit, 0f, 89f);
    }
}
