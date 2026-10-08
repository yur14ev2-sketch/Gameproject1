using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ProjectionCollisionContactProbe : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float groundSampleInterval = 0.5f;
    [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.35f;

    private Rigidbody body;
    private Collider playerCollider;
    private GameModeManager gameModeManager;
    private float nextGroundSampleTime;
    private float nextStayLogTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        gameModeManager = FindObjectOfType<GameModeManager>();
        Debug.Log(
            $"[ProjectionCollisionDebug] PROBE_READY player={name} rigidbody={(body != null)} " +
            $"collider={(playerCollider != null ? playerCollider.GetType().Name : "MISSING")}",
            this);
    }

    private void FixedUpdate()
    {
        if (gameModeManager == null || !gameModeManager.IsInsidePainting ||
            Time.time < nextGroundSampleTime || playerCollider == null)
            return;

        nextGroundSampleTime = Time.time + groundSampleInterval;
        Bounds bounds = playerCollider.bounds;
        Vector3 origin = bounds.center;
        float distance = bounds.extents.y + groundProbeDistance;
        bool hitSomething = Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        Debug.Log(
            $"[ProjectionCollisionDebug] GROUND_PROBE player={name} pos={transform.position} " +
            $"velocity={(body != null ? body.velocity.ToString() : "NONE")} " +
            $"hit={hitSomething} hitObject={(hitSomething ? GetPath(hit.collider.transform) : "NONE")} " +
            $"hitCollider={(hitSomething ? hit.collider.GetType().Name : "NONE")} " +
            $"projectionHit={(hitSomething && IsProjectionCollider(hit.collider))} " +
            $"distance={(hitSomething ? hit.distance.ToString("F5") : "NONE")} " +
            $"normal={(hitSomething ? hit.normal.ToString() : "NONE")}",
            this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        LogCollision("CONTACT_ENTER", collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (Time.time < nextStayLogTime)
            return;
        nextStayLogTime = Time.time + groundSampleInterval;
        LogCollision("CONTACT_STAY", collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        Debug.Log(
            $"[ProjectionCollisionDebug] CONTACT_EXIT player={name} " +
            $"other={GetPath(collision.collider.transform)} " +
            $"projection={IsProjectionCollider(collision.collider)}",
            this);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.LogWarning(
            $"[ProjectionCollisionDebug] TRIGGER_ENTER player={name} other={GetPath(other.transform)} " +
            $"projection={IsProjectionCollider(other)}",
            this);
    }

    private void LogCollision(string eventName, Collision collision)
    {
        float strongestGroundNormal = -1f;
        for (int i = 0; i < collision.contactCount; i++)
            strongestGroundNormal = Mathf.Max(strongestGroundNormal, collision.GetContact(i).normal.y);

        Debug.Log(
            $"[ProjectionCollisionDebug] {eventName} player={name} " +
            $"other={GetPath(collision.collider.transform)} type={collision.collider.GetType().Name} " +
            $"projection={IsProjectionCollider(collision.collider)} contacts={collision.contactCount} " +
            $"maxNormalY={strongestGroundNormal:F5} impulse={collision.impulse}",
            this);
    }

    private static bool IsProjectionCollider(Collider collider)
    {
        if (collider == null)
            return false;
        if (collider.name.StartsWith("ProjectionCollision_"))
            return true;
        return GetPath(collider.transform).Contains("/ProjectedObjects/");
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
