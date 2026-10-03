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
    }

    [Header("Player Systems")]
    [SerializeField] private GameObject roomPlayerSystem;
    [SerializeField] private Transform roomPlayer;
    [SerializeField] private Camera roomCamera;
    [Tooltip("The single prefab instantiated when entering any painting.")]
    [SerializeField] private GameObject paintingPlayerPrefab;
    [SerializeField] private PaintingEntry[] paintings;

    [Header("Painting Interaction")]
    [SerializeField] private KeyCode interactionKey = KeyCode.E;
    [SerializeField, Min(0f)] private float interactionDistance = 75f;

    public bool IsInsidePainting { get; private set; }
    public Camera RoomCamera => roomCamera;
    private PaintingEntry activePainting;
    private GameObject activePaintingPlayer;

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
        DestroyActivePaintingPlayer();

        if (roomPlayerSystem != null)
            roomPlayerSystem.SetActive(true);

        SetAllPaintingSystemsActive(false);
        SetAllPaintingPlayersActive(false);

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
        if (painting.paintingCamera != null)
            painting.paintingCamera.gameObject.SetActive(true);

        SetExclusiveCamera(painting.paintingCamera);
        SpawnPaintingPlayer(painting);
    }

    private void SpawnPaintingPlayer(PaintingEntry painting)
    {
        DestroyActivePaintingPlayer();

        if (paintingPlayerPrefab == null)
        {
            Debug.LogError("[GameMode] Player_2D prefab is not assigned.", this);
            return;
        }

        Transform anchor = painting.interactionPoint != null
            ? painting.interactionPoint.Find("Player2DAnchor")
            : null;
        if (anchor == null)
        {
            Debug.LogError($"[GameMode] {painting.paintingName} has no Player2DAnchor.", this);
            return;
        }

        activePaintingPlayer = Instantiate(
            paintingPlayerPrefab,
            anchor.position,
            anchor.rotation);
        activePaintingPlayer.name = "Player_2D_Runtime";

        PaintingPlayer2DController controller =
            activePaintingPlayer.GetComponent<PaintingPlayer2DController>();
        if (controller == null)
        {
            Debug.LogError("[GameMode] Player_2D prefab requires PaintingPlayer2DController.", activePaintingPlayer);
            DestroyActivePaintingPlayer();
            return;
        }

        controller.SetControlEnabled(true, painting.paintingCamera);
        Debug.Log(
            $"[GameMode] Spawned the only Player_2D at {painting.paintingName}/Player2DAnchor.",
            activePaintingPlayer);
    }

    private void DestroyActivePaintingPlayer()
    {
        if (activePaintingPlayer == null)
            return;

        PaintingPlayer2DController controller =
            activePaintingPlayer.GetComponent<PaintingPlayer2DController>();
        if (controller != null)
            controller.SetControlEnabled(false, null);

        activePaintingPlayer.SetActive(false);
        Destroy(activePaintingPlayer);
        activePaintingPlayer = null;
    }

    private void OnDestroy()
    {
        DestroyActivePaintingPlayer();
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
