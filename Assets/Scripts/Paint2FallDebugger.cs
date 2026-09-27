using UnityEngine;

[DisallowMultipleComponent]
public sealed class Paint2FallDebugger : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float reportInterval = 0.5f;
    [SerializeField, Min(0.01f)] private float fallingThreshold = 0.1f;

    private Rigidbody playerBody;
    private float previousVerticalVelocity;
    private float measuredVerticalAcceleration;
    private float nextReportTime;
    private bool hasPreviousSample;
    private bool wasFalling;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeDebugger()
    {
        if (FindObjectOfType<Paint2FallDebugger>(true) != null)
            return;

        GameObject debuggerObject = new GameObject("Paint2FallDebugger_Runtime");
        DontDestroyOnLoad(debuggerObject);
        debuggerObject.AddComponent<Paint2FallDebugger>();
        Debug.Log("[Paint2FallDebug] Monitor created. Waiting for PlayerPhysics to become active.");
    }

    private void FixedUpdate()
    {
        if (playerBody == null)
        {
            TryFindPlayerBody();
            return;
        }

        if (!playerBody.gameObject.activeInHierarchy)
        {
            ResetSamples();
            return;
        }

        float verticalVelocity = playerBody.velocity.y;

        if (hasPreviousSample)
            measuredVerticalAcceleration =
                (verticalVelocity - previousVerticalVelocity) / Time.fixedDeltaTime;

        bool isFalling = verticalVelocity < -fallingThreshold;

        if (isFalling && (!wasFalling || Time.unscaledTime >= nextReportTime))
        {
            Report(verticalVelocity);
            nextReportTime = Time.unscaledTime + reportInterval;
        }

        previousVerticalVelocity = verticalVelocity;
        hasPreviousSample = true;
        wasFalling = isFalling;
    }

    private void TryFindPlayerBody()
    {
        foreach (Rigidbody candidate in Resources.FindObjectsOfTypeAll<Rigidbody>())
        {
            if (!candidate.gameObject.scene.IsValid() || candidate.name != "PlayerPhysics")
                continue;

            playerBody = candidate;
            ResetSamples();
            Debug.Log(
                $"[Paint2FallDebug] Monitoring '{GetPath(candidate.transform)}'. " +
                $"UseGravity={candidate.useGravity}, Drag={candidate.drag:F3}, " +
                $"ProjectGravityY={Physics.gravity.y:F3}, FixedDeltaTime={Time.fixedDeltaTime:F4}.",
                candidate);
            return;
        }
    }

    private void Report(float verticalVelocity)
    {
        float expectedAcceleration = playerBody.useGravity ? Physics.gravity.y : 0f;
        float accelerationError = measuredVerticalAcceleration - expectedAcceleration;
        bool accelerationMatchesGravity =
            Mathf.Abs(accelerationError) <= Mathf.Max(0.5f, Mathf.Abs(expectedAcceleration) * 0.1f);

        Collider playerCollider = playerBody.GetComponent<Collider>();
        float groundDistance = float.PositiveInfinity;
        string groundName = "none";

        if (playerCollider != null)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                playerCollider.bounds.center,
                Vector3.down,
                1000f,
                ~0,
                QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == playerCollider || hit.transform.IsChildOf(playerBody.transform))
                    continue;

                float distanceFromBottom = Mathf.Max(0f, playerCollider.bounds.min.y - hit.point.y);
                if (distanceFromBottom >= groundDistance)
                    continue;

                groundDistance = distanceFromBottom;
                groundName = GetPath(hit.transform);
            }
        }

        string distanceText = float.IsPositiveInfinity(groundDistance)
            ? "no collider detected"
            : $"{groundDistance:F3}";

        Debug.Log(
            "[Paint2FallDebug] FALL SAMPLE\n" +
            $"Position Y: {playerBody.position.y:F3}\n" +
            $"Vertical velocity: {verticalVelocity:F3} units/s\n" +
            $"Measured acceleration: {measuredVerticalAcceleration:F3} units/s^2\n" +
            $"Expected gravity acceleration: {expectedAcceleration:F3} units/s^2\n" +
            $"Acceleration error: {accelerationError:F3}\n" +
            $"Matches normal project gravity: {accelerationMatchesGravity}\n" +
            $"Use Gravity: {playerBody.useGravity}, Drag: {playerBody.drag:F3}, " +
            $"Is Kinematic: {playerBody.isKinematic}\n" +
            $"Time Scale: {Time.timeScale:F3}, Fixed Delta Time: {Time.fixedDeltaTime:F4}\n" +
            $"Ground distance: {distanceText}, Ground collider: {groundName}",
            playerBody);
    }

    private void ResetSamples()
    {
        hasPreviousSample = false;
        wasFalling = false;
        measuredVerticalAcceleration = 0f;
        nextReportTime = 0f;
    }

    private static string GetPath(Transform target)
    {
        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }

        return path;
    }
}
