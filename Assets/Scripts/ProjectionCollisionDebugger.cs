using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectionCollisionDebugger : MonoBehaviour
{
    [SerializeField] private KeyCode projectionKey = KeyCode.P;
    [SerializeField, Min(0.01f)] private float inspectionDelay = 0.1f;

    private bool inspectionPending;
    private GameModeManager gameModeManager;

    private void Awake()
    {
        gameModeManager = GetComponent<GameModeManager>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(projectionKey) || inspectionPending)
            return;
        if (gameModeManager == null || !gameModeManager.IsProjectionViewActive)
            return;

        inspectionPending = true;
        StartCoroutine(InspectAfterProjection());
    }

    [ContextMenu("Dump Projection Colliders Now")]
    public void DumpProjectionCollidersNow()
    {
        DumpPainting("paint2");
        DumpPainting("paint4");
    }

    private IEnumerator InspectAfterProjection()
    {
        yield return new WaitForSecondsRealtime(inspectionDelay);
        inspectionPending = false;
        Debug.Log($"[ProjectionCollisionDebug] BEGIN frame={Time.frameCount}", this);
        DumpPainting("paint2");
        DumpPainting("paint4");
        DumpRuntimePlayer();
        Debug.Log("[ProjectionCollisionDebug] END", this);
    }

    private void DumpPainting(string paintingName)
    {
        Transform painting = FindSceneTransform(paintingName);
        Transform output = painting != null
            ? painting.Find("PuzzleRoot/ProjectionArea/ProjectedObjects")
            : null;
        if (output == null)
        {
            Debug.LogError(
                $"[ProjectionCollisionDebug] painting={paintingName} outputRoot=MISSING",
                this);
            return;
        }

        int enabledCount = 0;
        int meshCount = 0;
        for (int i = 0; i < output.childCount; i++)
        {
            Transform child = output.GetChild(i);
            if (!child.name.StartsWith("ProjectionCollision_"))
                continue;

            MeshCollider collider = child.GetComponent<MeshCollider>();
            Mesh mesh = collider != null ? collider.sharedMesh : null;
            if (collider != null && collider.enabled)
                enabledCount++;
            if (mesh != null)
                meshCount++;

            if (collider == null || collider.enabled || mesh != null)
            {
                Debug.Log(
                    $"[ProjectionCollisionDebug] painting={paintingName} slot={child.name} " +
                    $"active={child.gameObject.activeInHierarchy} collider={(collider != null)} " +
                    $"enabled={(collider != null && collider.enabled)} trigger={(collider != null && collider.isTrigger)} " +
                    $"convex={(collider != null && collider.convex)} layer={child.gameObject.layer} " +
                    $"mesh={(mesh != null ? mesh.name : "NULL")} vertices={(mesh != null ? mesh.vertexCount : 0)} " +
                    $"triangles={(mesh != null ? mesh.triangles.Length / 3 : 0)} " +
                    $"localBounds={(mesh != null ? mesh.bounds.ToString() : "NULL")} " +
                    $"worldBounds={(collider != null ? collider.bounds.ToString() : "NULL")}",
                    child);
            }
        }

        Debug.Log(
            $"[ProjectionCollisionDebug] painting={paintingName} summary " +
            $"enabledColliders={enabledCount} collidersWithMesh={meshCount} outputPath={GetPath(output)}",
            output);
    }

    private void DumpRuntimePlayer()
    {
        GameObject player = GameObject.Find("Player_2D_Runtime");
        if (player == null)
        {
            Debug.Log("[ProjectionCollisionDebug] player=Player_2D_Runtime MISSING (not inside painting).", this);
            return;
        }

        Rigidbody body = player.GetComponent<Rigidbody>();
        Collider collider = player.GetComponent<Collider>();
        Debug.Log(
            $"[ProjectionCollisionDebug] player={player.name} layer={player.layer} " +
            $"position={player.transform.position} rigidbody={(body != null)} " +
            $"kinematic={(body != null && body.isKinematic)} gravity={(body != null && body.useGravity)} " +
            $"collisionMode={(body != null ? body.collisionDetectionMode.ToString() : "NONE")} " +
            $"velocity={(body != null ? body.velocity.ToString() : "NONE")} " +
            $"collider={(collider != null ? collider.GetType().Name : "MISSING")} " +
            $"colliderEnabled={(collider != null && collider.enabled)} " +
            $"trigger={(collider != null && collider.isTrigger)} " +
            $"bounds={(collider != null ? collider.bounds.ToString() : "NONE")}",
            player);
    }

    private static Transform FindSceneTransform(string objectName)
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                return candidate;
        }
        return null;
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
