using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class PushableExhibit : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform movableExhibit;
    [SerializeField] private BoxCollider pushArea;

    [Header("Push Feel")]
    [SerializeField, Min(0.1f)] private float mass = 8f;
    [SerializeField, Min(0f)] private float movementResistance = 6f;
    [SerializeField, Min(0f)] private float maximumSpeed = 8f;

    private Rigidbody exhibitBody;
    private Collider[] exhibitColliders;
    private Vector3 movementAxis;
    private Vector3 lockedPerpendicularPosition;
    private float lockedWorldY;
    private Quaternion lockedWorldRotation;
    private float minimumTravel;
    private float maximumTravel;
    private float currentTravel;

    private void Awake()
    {
        if (movableExhibit == null)
            movableExhibit = transform.Find("exhibit");

        if (movableExhibit == null || pushArea == null)
        {
            Debug.LogError("Pushable Exhibit needs an exhibit child and a Push Area reference.", this);
            enabled = false;
            return;
        }

        exhibitBody = GetComponent<Rigidbody>();
        if (exhibitBody == null)
            exhibitBody = gameObject.AddComponent<Rigidbody>();

        exhibitColliders = GetComponentsInChildren<Collider>(true);
        UpdateMovementAxis();

        exhibitBody.mass = mass;
        exhibitBody.drag = movementResistance;
        exhibitBody.angularDrag = movementResistance;
        exhibitBody.interpolation = RigidbodyInterpolation.Interpolate;
        exhibitBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        exhibitBody.constraints = RigidbodyConstraints.FreezePositionY |
                                  RigidbodyConstraints.FreezeRotation;

        lockedWorldY = transform.position.y;
        lockedWorldRotation = transform.rotation;
        lockedPerpendicularPosition = exhibitBody.position -
                                      movementAxis * Vector3.Dot(exhibitBody.position, movementAxis);
        UpdateTravelLimits();
    }

    private void FixedUpdate()
    {
        if (exhibitBody == null || pushArea == null)
            return;

        UpdateMovementAxis();
        UpdateTravelLimits();

        Bounds areaBounds = pushArea.bounds;
        Bounds exhibitBounds = CalculateExhibitBounds();
        float bodyTravel = Vector3.Dot(exhibitBody.position, movementAxis);
        float boundsCenterTravel = Vector3.Dot(exhibitBounds.center, movementAxis);
        float projectedRadius = Mathf.Abs(movementAxis.x) * exhibitBounds.extents.x +
                                Mathf.Abs(movementAxis.y) * exhibitBounds.extents.y +
                                Mathf.Abs(movementAxis.z) * exhibitBounds.extents.z;
        float leftOffset = boundsCenterTravel - projectedRadius - bodyTravel;
        float rightOffset = boundsCenterTravel + projectedRadius - bodyTravel;

        float areaCenterTravel = Vector3.Dot(areaBounds.center, movementAxis);
        float areaRadius = GetAreaHalfLength();
        minimumTravel = areaCenterTravel - areaRadius - leftOffset;
        maximumTravel = areaCenterTravel + areaRadius - rightOffset;
        if (minimumTravel > maximumTravel)
            minimumTravel = maximumTravel = areaCenterTravel;

        currentTravel = Mathf.Clamp(bodyTravel, minimumTravel, maximumTravel);
        float axisVelocity = Vector3.Dot(exhibitBody.velocity, movementAxis);
        if ((currentTravel <= minimumTravel + 0.001f && axisVelocity < 0f) ||
            (currentTravel >= maximumTravel - 0.001f && axisVelocity > 0f))
        {
            axisVelocity = 0f;
        }

        Vector3 position = lockedPerpendicularPosition + movementAxis * currentTravel;
        position.y = lockedWorldY;
        exhibitBody.MoveRotation(lockedWorldRotation);
        exhibitBody.MovePosition(position);

        axisVelocity = Mathf.Clamp(axisVelocity, -maximumSpeed, maximumSpeed);
        exhibitBody.velocity = movementAxis * axisVelocity;
    }

    public bool CanMove(Vector3 worldDirection)
    {
        if (!enabled || pushArea == null)
            return false;

        float requestedDirection = Vector3.Dot(worldDirection, movementAxis);
        if (Mathf.Abs(requestedDirection) < 0.001f)
            return false;

        UpdateTravelLimits();
        return requestedDirection > 0f
            ? currentTravel < maximumTravel - 0.01f
            : currentTravel > minimumTravel + 0.01f;
    }

    private void UpdateMovementAxis()
    {
        Vector3 scale = pushArea.transform.lossyScale;
        movementAxis = Mathf.Abs(scale.x * pushArea.size.x) >= Mathf.Abs(scale.z * pushArea.size.z)
            ? pushArea.transform.right
            : pushArea.transform.forward;
        movementAxis = Vector3.ProjectOnPlane(movementAxis, Vector3.up).normalized;
        if (movementAxis.sqrMagnitude < 0.99f)
            movementAxis = Vector3.right;
    }

    private void UpdateTravelLimits()
    {
        if (exhibitBody == null || pushArea == null)
            return;

        Bounds exhibitBounds = CalculateExhibitBounds();
        float bodyTravel = Vector3.Dot(exhibitBody.position, movementAxis);
        float boundsCenterTravel = Vector3.Dot(exhibitBounds.center, movementAxis);
        float projectedRadius = Mathf.Abs(movementAxis.x) * exhibitBounds.extents.x +
                                Mathf.Abs(movementAxis.y) * exhibitBounds.extents.y +
                                Mathf.Abs(movementAxis.z) * exhibitBounds.extents.z;
        float areaCenterTravel = Vector3.Dot(pushArea.bounds.center, movementAxis);
        float areaRadius = GetAreaHalfLength();

        minimumTravel = areaCenterTravel - areaRadius -
                        (boundsCenterTravel - projectedRadius - bodyTravel);
        maximumTravel = areaCenterTravel + areaRadius -
                        (boundsCenterTravel + projectedRadius - bodyTravel);
        currentTravel = bodyTravel;
    }

    private float GetAreaHalfLength()
    {
        Vector3 scale = pushArea.transform.lossyScale;
        return Mathf.Abs(scale.x * pushArea.size.x) >= Mathf.Abs(scale.z * pushArea.size.z)
            ? Mathf.Abs(scale.x * pushArea.size.x) * 0.5f
            : Mathf.Abs(scale.z * pushArea.size.z) * 0.5f;
    }

    private Bounds CalculateExhibitBounds()
    {
        Bounds result = new Bounds(exhibitBody.position, Vector3.zero);
        bool foundCollider = false;

        foreach (Collider childCollider in exhibitColliders)
        {
            if (childCollider == null || !childCollider.enabled || childCollider.isTrigger)
                continue;

            if (!foundCollider)
            {
                result = childCollider.bounds;
                foundCollider = true;
            }
            else
            {
                result.Encapsulate(childCollider.bounds);
            }
        }

        return result;
    }

    private void OnValidate()
    {
        mass = Mathf.Max(0.1f, mass);
        movementResistance = Mathf.Max(0f, movementResistance);
        maximumSpeed = Mathf.Max(0f, maximumSpeed);
    }
}
