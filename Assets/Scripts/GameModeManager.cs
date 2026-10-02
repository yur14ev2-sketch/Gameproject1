using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameModeManager : MonoBehaviour
{
    [System.Serializable]
    private sealed class PaintingEntry
    {
        public string paintingName;
        public Transform interactionPoint;
        public GameObject playerSystem;
        public Camera paintingCamera;
        public Transform playerSpawnPoint;
    }

    [Header("Player Systems")]
    [SerializeField] private GameObject roomPlayerSystem;
    [SerializeField] private Transform roomPlayer;
    [SerializeField] private Camera roomCamera;
    [Tooltip("The single editable 2D physics player shown in the Hierarchy.")]
    [SerializeField] private PlayerMovement paintingPlayer;
    [SerializeField] private PaintingEntry[] paintings;

    [Header("Painting Interaction")]
    [SerializeField] private KeyCode interactionKey = KeyCode.E;
    [SerializeField, Min(0f)] private float interactionDistance = 75f;

    public bool IsInsidePainting { get; private set; }
    public Camera RoomCamera => roomCamera;
    private PaintingEntry activePainting;

    private void Awake()
    {
        ResolveRoomCamera();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
            ApplyRoomMode();
    }

    public void ForceRoomMode()
    {
        ApplyRoomMode();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(interactionKey))
            return;

        if (IsInsidePainting)
        {
            Debug.Log($"[GameMode] E detected: leaving {activePainting?.paintingName} and returning to the 3D room.", this);
            ApplyRoomMode();
            return;
        }

        PaintingEntry nearestPainting = FindNearestPainting();
        if (nearestPainting != null)
        {
            float distance = GetPlanarDistance(nearestPainting.interactionPoint);
            Debug.Log($"[GameMode] E detected: entering {nearestPainting.paintingName}. Planar distance = {distance:F1}.", this);
            ApplyPaintingMode(nearestPainting);
        }
        else
        {
            Debug.Log($"[GameMode] E detected outside every painting interaction range. Allowed = {interactionDistance:F1}.", this);
        }
    }

    private PaintingEntry FindNearestPainting()
    {
        if (roomPlayer == null || paintings == null)
            return null;

        PaintingEntry nearest = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (PaintingEntry painting in paintings)
        {
            if (painting == null || painting.interactionPoint == null || painting.paintingCamera == null)
                continue;

            float distance = GetPlanarDistance(painting.interactionPoint);
            if (distance <= interactionDistance && distance < nearestDistance)
            {
                nearest = painting;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private float GetPlanarDistance(Transform point)
    {
        if (roomPlayer == null || point == null)
            return float.PositiveInfinity;

        Vector3 offset = roomPlayer.position - point.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void ApplyRoomMode()
    {
        ResolveRoomCamera();
        IsInsidePainting = false;
        activePainting = null;

        if (roomPlayerSystem != null)
            roomPlayerSystem.SetActive(true);

        SetAllPaintingSystemsActive(false);
        SetAllPaintingPlayersActive(false);
        SetAllNewPaintingPlayersState(false, null);

        if (paintingPlayer != null)
            paintingPlayer.gameObject.SetActive(false);

        SetExclusiveCamera(roomCamera);

        Debug.Log("[GameMode] Room mode enforced: 3D player active, every 2D painting player inactive.", this);
    }

    private void ApplyPaintingMode(PaintingEntry painting)
    {
        ResolveRoomCamera();
        IsInsidePainting = true;
        activePainting = painting;

        if (roomPlayerSystem != null)
            roomPlayerSystem.SetActive(false);

        SetAllPaintingSystemsActive(false);
        SetAllPaintingPlayersActive(false);
        if (painting.playerSystem != null)
            painting.playerSystem.SetActive(true);

        PlayerMovement selectedPlayer = paintingPlayer != null
            ? paintingPlayer
            : ResolvePaintingPlayer(painting);
        if (selectedPlayer != null)
        {
            ResetPaintingPlayer(painting, selectedPlayer);
            selectedPlayer.ConfigureForCamera(painting.paintingCamera);
            selectedPlayer.gameObject.SetActive(true);
        }

        if (painting.paintingCamera != null)
            painting.paintingCamera.gameObject.SetActive(true);

        SetExclusiveCamera(painting.paintingCamera);

        SetPaintingPlayersControl(painting, true);

        if (selectedPlayer != null)
            LogPaintingPlayerState(painting, selectedPlayer);
    }

    private static void SetPaintingPlayersControl(PaintingEntry painting, bool enabled)
    {
        if (painting?.interactionPoint == null)
            return;

        Transform paintingRoot = painting.interactionPoint.parent;
        if (paintingRoot == null)
            return;

        foreach (PaintingPlayer2DController player in paintingRoot.GetComponentsInChildren<PaintingPlayer2DController>(true))
        {
            if (enabled)
            {
                player.gameObject.SetActive(true);

                if (painting.playerSpawnPoint != null)
                {
                    Rigidbody body = player.GetComponent<Rigidbody>();
                    player.transform.SetPositionAndRotation(
                        painting.playerSpawnPoint.position,
                        painting.playerSpawnPoint.rotation);

                    if (body != null)
                    {
                        body.position = painting.playerSpawnPoint.position;
                        body.rotation = painting.playerSpawnPoint.rotation;
                    }
                }
            }

            player.SetControlEnabled(enabled, painting.paintingCamera);

            if (!enabled)
                player.gameObject.SetActive(false);
        }
    }

    private static void SetAllNewPaintingPlayersState(bool enabled, Camera paintingCamera)
    {
        foreach (PaintingPlayer2DController player in Resources.FindObjectsOfTypeAll<PaintingPlayer2DController>())
        {
            if (player.gameObject.scene.IsValid())
            {
                if (enabled)
                    player.gameObject.SetActive(true);

                player.SetControlEnabled(enabled, paintingCamera);

                if (!enabled)
                    player.gameObject.SetActive(false);
            }
        }
    }

    private void LogPaintingPlayerState(PaintingEntry painting, PlayerMovement player)
    {
        if (player == null)
        {
            Debug.LogError($"[GameMode] {painting.paintingName}: no PlayerMovement exists under the assigned player system.", this);
            return;
        }

        Renderer visual = player.GetComponentInChildren<Renderer>(true);
        Vector3 viewport = painting.paintingCamera != null
            ? painting.paintingCamera.WorldToViewportPoint(visual != null ? visual.bounds.center : player.transform.position)
            : new Vector3(float.NaN, float.NaN, float.NaN);

        Debug.Log(
            $"[GameMode] {painting.paintingName} player check: " +
            $"active={player.gameObject.activeInHierarchy}, position={player.transform.position}, " +
            $"lossyScale={player.transform.lossyScale}, renderer={(visual != null && visual.enabled)}, " +
            $"viewport={viewport}.",
            player);
    }

    private PlayerMovement ResolvePaintingPlayer(PaintingEntry painting)
    {
        if (painting.playerSystem == null)
            return null;

        PlayerMovement movement = painting.playerSystem.GetComponentInChildren<PlayerMovement>(true);
        if (movement != null || painting.playerSpawnPoint == null)
            return movement;

        float nearestDistance = float.PositiveInfinity;
        foreach (PlayerMovement candidate in Resources.FindObjectsOfTypeAll<PlayerMovement>())
        {
            if (!candidate.gameObject.scene.IsValid())
                continue;

            float distance = (candidate.transform.position - painting.playerSpawnPoint.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                movement = candidate;
            }
        }

        if (movement != null)
        {
            movement.transform.SetParent(painting.playerSystem.transform, true);
            Debug.LogWarning(
                $"[GameMode] {painting.paintingName}: repaired orphaned player '{movement.name}' and attached it to '{painting.playerSystem.name}'.",
                movement);
        }

        return movement;
    }

    private static void ResetPaintingPlayer(PaintingEntry painting, PlayerMovement movement)
    {
        if (painting.playerSpawnPoint == null || movement == null)
            return;

        Rigidbody body = movement.GetComponent<Rigidbody>();
        movement.transform.SetPositionAndRotation(
            painting.playerSpawnPoint.position,
            painting.playerSpawnPoint.rotation);

        if (body != null)
        {
            body.position = painting.playerSpawnPoint.position;
            body.rotation = painting.playerSpawnPoint.rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private void SetAllPaintingSystemsActive(bool active)
    {
        if (paintings == null)
            return;

        foreach (PaintingEntry painting in paintings)
        {
            if (painting?.playerSystem != null)
                painting.playerSystem.SetActive(active);

            if (painting?.paintingCamera != null)
                painting.paintingCamera.gameObject.SetActive(active);
        }
    }

    private void ResolveRoomCamera()
    {
        if (roomCamera == null && roomPlayerSystem != null)
            roomCamera = roomPlayerSystem.GetComponentInChildren<Camera>(true);
    }

    private void SetExclusiveCamera(Camera target)
    {
        SetCameraActive(roomCamera, target == roomCamera);

        if (paintings != null)
        {
            foreach (PaintingEntry painting in paintings)
            {
                if (painting?.paintingCamera != null)
                    SetCameraActive(painting.paintingCamera, painting.paintingCamera == target);
            }
        }
    }

    private static void SetCameraActive(Camera camera, bool active)
    {
        if (camera == null)
            return;

        camera.gameObject.SetActive(active);
        camera.enabled = active;

        AudioListener listener = camera.GetComponent<AudioListener>();
        if (listener != null)
            listener.enabled = active;
    }

    private static void SetAllPaintingPlayersActive(bool active)
    {
        foreach (PlayerMovement player in Resources.FindObjectsOfTypeAll<PlayerMovement>())
        {
            if (player.gameObject.scene.IsValid())
                player.gameObject.SetActive(active);
        }
    }

}
