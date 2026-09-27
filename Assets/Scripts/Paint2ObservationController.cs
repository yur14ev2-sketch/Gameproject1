using UnityEngine;

[DisallowMultipleComponent]
public sealed class Paint2ObservationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private Transform viewPoint;
    [SerializeField] private Transform interactionPoint;

    [Header("Interaction")]
    [SerializeField] private KeyCode toggleKey = KeyCode.E;
    [SerializeField, Min(0f)] private float interactionDistance = 75f;

    [Header("Camera Transition")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.35f;

    public bool IsObserving { get; private set; }

    private Vector3 normalLocalPosition;
    private Quaternion normalLocalRotation;
    private Vector3 transitionStartPosition;
    private Quaternion transitionStartRotation;
    private float transitionProgress;
    private bool isTransitioning;

    private void Awake()
    {
        normalLocalPosition = transform.localPosition;
        normalLocalRotation = transform.localRotation;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (IsObserving)
                SetObservation(false);
            else if (CanEnterObservation())
                SetObservation(true);
        }

        if (isTransitioning)
            UpdateTransition();
    }

    private bool CanEnterObservation()
    {
        if (playerMovement == null || interactionPoint == null)
            return false;

        return Vector3.Distance(playerMovement.transform.position, interactionPoint.position)
               <= interactionDistance;
    }

    private void SetObservation(bool observing)
    {
        if (viewPoint == null || playerMovement == null)
        {
            Debug.LogError("Paint 2 observation references are incomplete.", this);
            return;
        }

        IsObserving = observing;
        playerMovement.SetInputEnabled(!observing);
        transitionStartPosition = transform.position;
        transitionStartRotation = transform.rotation;
        transitionProgress = 0f;
        isTransitioning = true;

        if (transitionDuration <= 0f)
            CompleteTransition();
    }

    private void UpdateTransition()
    {
        transitionProgress += Time.unscaledDeltaTime / Mathf.Max(transitionDuration, 0.0001f);
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transitionProgress));
        GetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation);

        transform.SetPositionAndRotation(
            Vector3.Lerp(transitionStartPosition, targetPosition, t),
            Quaternion.Slerp(transitionStartRotation, targetRotation, t));

        if (transitionProgress >= 1f)
            CompleteTransition();
    }

    private void CompleteTransition()
    {
        isTransitioning = false;

        if (IsObserving)
        {
            transform.SetPositionAndRotation(viewPoint.position, viewPoint.rotation);
        }
        else
        {
            transform.localPosition = normalLocalPosition;
            transform.localRotation = normalLocalRotation;
        }
    }

    private void GetTargetPose(out Vector3 position, out Quaternion rotation)
    {
        if (IsObserving)
        {
            position = viewPoint.position;
            rotation = viewPoint.rotation;
            return;
        }

        Transform cameraParent = transform.parent;
        position = cameraParent != null
            ? cameraParent.TransformPoint(normalLocalPosition)
            : normalLocalPosition;
        rotation = cameraParent != null
            ? cameraParent.rotation * normalLocalRotation
            : normalLocalRotation;
    }

    private void OnDisable()
    {
        if (!IsObserving)
            return;

        IsObserving = false;
        isTransitioning = false;
        playerMovement?.SetInputEnabled(true);
        transform.localPosition = normalLocalPosition;
        transform.localRotation = normalLocalRotation;
    }
}
