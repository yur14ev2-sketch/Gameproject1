using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Paint2ProjectionController : MonoBehaviour
{
    private sealed class ProjectionTarget
    {
        public string Name;
        public Camera Camera;
        public Transform Model;
        public Transform OutputRoot;
        public BoxCollider ClipArea;
        public Transform Background;
        public int NextCollisionSlot;
    }

    [Header("Input")]
    [SerializeField] private KeyCode projectionKey = KeyCode.P;
    [Header("Paint 2 Output")]
    [SerializeField] private Transform projectionObjectsRoot;
    [SerializeField] private Camera paint2Camera;
    [SerializeField] private Transform paint2Model;
    [SerializeField] private Transform projectionArea;
    [SerializeField] private Transform projectedObjectsRoot;
    [SerializeField] private BoxCollider paint2ClipArea;
    [SerializeField] private BoxCollider paint4ClipArea;
    [SerializeField, Min(0.01f)] private float silhouetteDepth = 0.25f;
    [SerializeField, Range(0.5f, 1f)] private float outputViewportScale = 0.92f;
    [SerializeField, Range(256, 2048)] private int silhouetteMaskResolution = 1024;
    [SerializeField, Range(64, 512)] private int paint4CollisionResolution = 256;

    private GameModeManager gameModeManager;
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly List<ProjectionTarget> projectionTargets = new List<ProjectionTarget>();
    private Camera silhouetteCamera;
    private RenderTexture silhouetteMask;
    private const int SilhouetteLayer = 31;

    private void Awake()
    {
        gameModeManager = GetComponent<GameModeManager>();
        ResolveReferences();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(projectionKey))
            return;

        if (gameModeManager == null || !gameModeManager.IsProjectionViewActive)
        {
            Debug.Log("[Paint2 Projection] P ignored: enter CtrlCamera with E before projecting.", this);
            return;
        }

        ProjectVisibleSources();
    }

    private void ResolveReferences()
    {
        if (silhouetteCamera == null)
            silhouetteCamera = FindSceneCamera("Projection_SilhouetteCamera");

        if (projectionObjectsRoot == null)
            projectionObjectsRoot = FindSceneTransform("Furniture");

        Transform paint2 = FindSceneTransform("paint2");
        if (paint2 != null)
        {
            if (paint2ClipArea == null)
                paint2ClipArea = paint2.Find("ProjectionClipArea")?.GetComponent<BoxCollider>();
            if (paint2Model == null)
                paint2Model = paint2.Find("Model");

            Transform puzzleRoot = paint2.Find("PuzzleRoot");
            if (puzzleRoot != null)
            {
                if (projectionArea == null)
                    projectionArea = puzzleRoot.Find("ProjectionArea");
                if (projectedObjectsRoot == null)
                    projectedObjectsRoot = puzzleRoot.Find("ProjectedObjects");
            }
        }

        if (paint2Camera == null)
        {
            foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (candidate.gameObject.scene.IsValid() && candidate.name == "Camera_Paint2")
                {
                    paint2Camera = candidate;
                    break;
                }
            }
        }


        projectionTargets.Clear();
        Transform paint4 = FindSceneTransform("paint4");
        if (paint4 != null && paint4ClipArea == null)
            paint4ClipArea = paint4.Find("ProjectionClipArea")?.GetComponent<BoxCollider>();

        AddProjectionTarget("paint2", "Camera_Paint2", paint2Camera, paint2Model, projectedObjectsRoot, paint2ClipArea);
        AddProjectionTarget("paint4", "Camera_Paint4", null, null, null, paint4ClipArea);
    }

    private void ProjectVisibleSources()
    {
        ResolveReferences();
        Camera roomCamera = gameModeManager != null ? gameModeManager.ProjectionCamera : null;
        if (roomCamera == null || projectionObjectsRoot == null || projectionTargets.Count == 0)
        {
            Debug.LogError("[Painting Projection] Required room camera, Furniture, or Paint 2/4 references are missing.", this);
            return;
        }

        ClearPreviousProjection();
        HashSet<Transform> visibleSources = CollectEnabledProjectionSources();
        if (visibleSources.Count == 0)
        {
            Debug.Log("[Painting Projection] P detected, but no Projection Source overlapped Paint 2 or Paint 4.", this);
            return;
        }

        if (!RenderVisibleSilhouetteMask(roomCamera, visibleSources))
        {
            Debug.LogError("[Painting Projection] Could not render the live silhouette mask.", this);
            return;
        }

        int totalProjectedCount = 0;

        for (int targetIndex = 0; targetIndex < projectionTargets.Count; targetIndex++)
            totalProjectedCount += ProjectToTarget(projectionTargets[targetIndex], roomCamera, visibleSources);

        Debug.Log(
            totalProjectedCount > 0
                ? $"[Painting Projection] P projected {totalProjectedCount} object(s) into Paint 2/4."
                : "[Painting Projection] P detected, but no Projection Source overlapped Paint 2 or Paint 4.",
            this);
    }

    private int ProjectToTarget(
        ProjectionTarget target,
        Camera roomCamera,
        HashSet<Transform> visibleSources)
    {
        if (target.Camera == null || target.Model == null || target.OutputRoot == null)
            return 0;

        if (!TryCalculateRendererBounds(target.Model, out Bounds paintBounds))
            return 0;

        if (!TryGetTargetViewportRect(roomCamera, target, paintBounds, out Rect paintRect, out bool paintInFront))
            return 0;

        if (!paintInFront || !OverlapsScreen(paintRect))
            return 0;

        // The visible silhouette is the exact room-camera mask sampled only on this BG.
        // It must not depend on renderer bounds, which are only an approximation and can
        // explode when furniture crosses the camera near plane.
        CreateSilhouetteMaskSurface(target, roomCamera, paintBounds);

        int projectedCount = 0;
        bool isPaint4 = target.Name == "paint4";

        for (int sourceIndex = 0; sourceIndex < projectionObjectsRoot.childCount; sourceIndex++)
        {
            Transform sourceTransform = projectionObjectsRoot.GetChild(sourceIndex);
            if (!visibleSources.Contains(sourceTransform))
                continue;
            if (!sourceTransform.gameObject.activeInHierarchy)
                continue;

            ProjectionSource settings = sourceTransform.GetComponent<ProjectionSource>();
            if (settings == null || !settings.ProjectionEnabled)
                continue;

            Transform modelRoot = settings != null ? settings.ModelRoot : sourceTransform;
            if (!TryCalculateRendererBounds(modelRoot, out Bounds sourceBounds))
                continue;

            Rect sourceRect = BoundsToViewportRect(roomCamera, sourceBounds, out bool sourceInFront);
            if (!sourceInFront)
                continue;

            Rect overlap = Intersect(sourceRect, paintRect);
            if (overlap.width <= 0.001f || overlap.height <= 0.001f)
                continue;

            Color color = settings != null ? settings.SilhouetteColor : new Color(0.08f, 0.08f, 0.08f, 1f);
            bool generateCollider = settings == null || settings.GenerateCollider;
            Vector3 positionOffset = settings != null ? settings.PositionOffset : Vector3.zero;
            // Paint 4's visible result is the final occlusion-aware mask on BG.
            // Its collision is built once from that same mask after all sources
            // have been evaluated; projecting each source mesh separately would
            // reintroduce hidden/internal collision surfaces.
            if (isPaint4)
            {
                projectedCount++;
                continue;
            }

            if (CreateProjectedMesh(modelRoot, sourceTransform.name, color, generateCollider, positionOffset,
                    roomCamera, paintRect, paintBounds, target.Camera, target.Model,
                    target.Background, target.OutputRoot, false))
                projectedCount++;
        }

        if (isPaint4 && projectedCount > 0 &&
            !CreatePaint4MaskCollision(target, roomCamera, paintBounds))
            Debug.LogError("[Painting Projection] Paint 4 mask collision could not be built.", this);

        if (projectedCount > 0)
            Debug.Log($"[Painting Projection] {target.Name} received {projectedCount} projection(s).", this);

        return projectedCount;
    }

    private HashSet<Transform> CollectEnabledProjectionSources()
    {
        var visibleSources = new HashSet<Transform>();

        for (int sourceIndex = 0; sourceIndex < projectionObjectsRoot.childCount; sourceIndex++)
        {
            Transform source = projectionObjectsRoot.GetChild(sourceIndex);
            ProjectionSource settings = source.GetComponent<ProjectionSource>();
            if (!source.gameObject.activeInHierarchy || settings == null || !settings.ProjectionEnabled)
                continue;
            visibleSources.Add(source);
        }

        return visibleSources;
    }

    private bool CreateProjectedMesh(
        Transform sourceRoot,
        string sourceName,
        Color silhouetteColor,
        bool generateCollider,
        Vector3 positionOffset,
        Camera roomCamera,
        Rect paintRect,
        Bounds paintBounds,
        Camera targetCamera,
        Transform targetModel,
        Transform targetBackground,
        Transform targetOutputRoot,
        bool orientPaint4BoundarySurfacesUpward)
    {
        var worldVertices = new List<Vector3>();
        var triangles = new List<int>();

        foreach (MeshFilter meshFilter in sourceRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
                continue;

            Vector3[] sourceVertices = mesh.vertices;
            int[] sourceTriangles = mesh.triangles;
            for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
            {
                Vector3 a = roomCamera.WorldToViewportPoint(
                    meshFilter.transform.TransformPoint(sourceVertices[sourceTriangles[i]]));
                Vector3 b = roomCamera.WorldToViewportPoint(
                    meshFilter.transform.TransformPoint(sourceVertices[sourceTriangles[i + 1]]));
                Vector3 c = roomCamera.WorldToViewportPoint(
                    meshFilter.transform.TransformPoint(sourceVertices[sourceTriangles[i + 2]]));

                if (a.z <= 0f || b.z <= 0f || c.z <= 0f)
                    continue;

                List<Vector2> clipped = ClipTriangleToRect(
                    new Vector2(a.x, a.y),
                    new Vector2(b.x, b.y),
                    new Vector2(c.x, c.y),
                    paintRect);

                if (clipped.Count < 3)
                    continue;

                int first = worldVertices.Count;
                foreach (Vector2 point in clipped)
                    worldVertices.Add(MapViewportPointToPaintPlane(
                        point, paintRect, paintBounds, positionOffset, targetCamera,
                        targetModel, targetBackground));

                for (int vertex = 1; vertex < clipped.Count - 1; vertex++)
                {
                    triangles.Add(first);
                    triangles.Add(first + vertex);
                    triangles.Add(first + vertex + 1);
                }
            }
        }

        if (triangles.Count == 0)
            return false;

        Transform collisionSlot = targetOutputRoot.Find(
            $"ProjectionCollision_{GetCollisionSlotIndex(targetOutputRoot):00}");
        if (collisionSlot == null)
        {
            Debug.LogError($"[Painting Projection] No persistent collision slot is available under {targetOutputRoot.name}.", this);
            return false;
        }

        Vector3[] localVertices = new Vector3[worldVertices.Count];
        for (int i = 0; i < worldVertices.Count; i++)
            localVertices[i] = targetOutputRoot.InverseTransformPoint(worldVertices[i]);

        if (generateCollider)
        {
            Mesh collisionMesh = BuildExtrudedCollisionMesh(
                localVertices,
                triangles,
                targetOutputRoot.InverseTransformVector(
                    targetCamera.transform.forward.normalized * silhouetteDepth),
                targetOutputRoot,
                orientPaint4BoundarySurfacesUpward);
            MeshCollider meshCollider = collisionSlot.GetComponent<MeshCollider>();
            if (meshCollider == null)
                return false;
            meshCollider.enabled = false;
            meshCollider.sharedMesh = null;
            meshCollider.isTrigger = false;
            meshCollider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices;
            meshCollider.sharedMesh = collisionMesh;
            meshCollider.convex = false;
            meshCollider.enabled = true;

            // Persistent BoxColliders remain visible/editable in the Hierarchy, but
            // Paint 4 collision now comes from the exact projected silhouette mesh.
            // A bounds-sized support creates invisible platforms across empty pixels.
            if (orientPaint4BoundarySurfacesUpward)
            {
                BoxCollider oldTopSupport = collisionSlot.GetComponent<BoxCollider>();
                if (oldTopSupport != null)
                    oldTopSupport.enabled = false;
            }
        }

        return true;
    }

    private bool CreatePaint4MaskCollision(
        ProjectionTarget target,
        Camera roomCamera,
        Bounds paintBounds)
    {
        if (silhouetteMask == null || target.OutputRoot == null || target.Camera == null)
            return false;

        Transform collisionSlot = target.OutputRoot.Find(
            $"ProjectionCollision_{GetCollisionSlotIndex(target.OutputRoot):00}");
        if (collisionSlot == null)
            return false;

        Vector3 bottomLeft = MapModelBoundsPoint(target, paintBounds, 0f, 0f);
        Vector3 bottomRight = MapModelBoundsPoint(target, paintBounds, 1f, 0f);
        Vector3 topRight = MapModelBoundsPoint(target, paintBounds, 1f, 1f);
        Vector3 topLeft = MapModelBoundsPoint(target, paintBounds, 0f, 1f);

        float horizontalLength = Mathf.Max(
            0.001f,
            0.5f * ((bottomRight - bottomLeft).magnitude + (topRight - topLeft).magnitude));
        float verticalLength = Mathf.Max(
            0.001f,
            0.5f * ((topLeft - bottomLeft).magnitude + (topRight - bottomRight).magnitude));
        int gridWidth = paint4CollisionResolution;
        int gridHeight = Mathf.Clamp(
            Mathf.RoundToInt(gridWidth * verticalLength / horizontalLength),
            32,
            512);

        Texture2D readableMask = new Texture2D(
            silhouetteMask.width,
            silhouetteMask.height,
            TextureFormat.RGBA32,
            false,
            true);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            RenderTexture.active = silhouetteMask;
            readableMask.ReadPixels(
                new Rect(0f, 0f, silhouetteMask.width, silhouetteMask.height),
                0,
                0,
                false);
            readableMask.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previousActive;
        }

        Color32[] maskPixels = readableMask.GetPixels32();
        var occupied = new bool[gridWidth * gridHeight];
        int occupiedCount = 0;
        for (int y = 0; y < gridHeight; y++)
        {
            float v = (y + 0.5f) / gridHeight;
            for (int x = 0; x < gridWidth; x++)
            {
                float u = (x + 0.5f) / gridWidth;
                Vector3 worldPoint = BilinearPaintPoint(
                    bottomLeft, bottomRight, topRight, topLeft, u, v);
                Vector3 viewport = roomCamera.WorldToViewportPoint(worldPoint);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f ||
                    viewport.y < 0f || viewport.y > 1f)
                    continue;

                int pixelX = Mathf.Clamp(
                    Mathf.FloorToInt(viewport.x * silhouetteMask.width),
                    0,
                    silhouetteMask.width - 1);
                int pixelY = Mathf.Clamp(
                    Mathf.FloorToInt(viewport.y * silhouetteMask.height),
                    0,
                    silhouetteMask.height - 1);
                bool isOccupied = maskPixels[pixelY * silhouetteMask.width + pixelX].a >= 128;
                occupied[y * gridWidth + x] = isOccupied;
                if (isOccupied)
                    occupiedCount++;
            }
        }
        Destroy(readableMask);

        if (occupiedCount == 0)
            return false;

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Vector3 localDepth = target.OutputRoot.InverseTransformVector(
            target.Camera.transform.forward.normalized * silhouetteDepth);
        Vector3 horizontalDirection = (
            (bottomRight - bottomLeft) + (topRight - topLeft)).normalized;
        Vector3 verticalDirection = (
            (topLeft - bottomLeft) + (topRight - bottomRight)).normalized;

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!occupied[y * gridWidth + x])
                    continue;

                float left = (float)x / gridWidth;
                float right = (float)(x + 1) / gridWidth;
                float bottom = (float)y / gridHeight;
                float top = (float)(y + 1) / gridHeight;

                if (y == gridHeight - 1 || !occupied[(y + 1) * gridWidth + x])
                    AddMaskBoundaryQuad(
                        vertices, triangles, target.OutputRoot, localDepth,
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, left, top),
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, right, top),
                        verticalDirection);
                if (y == 0 || !occupied[(y - 1) * gridWidth + x])
                    AddMaskBoundaryQuad(
                        vertices, triangles, target.OutputRoot, localDepth,
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, left, bottom),
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, right, bottom),
                        -verticalDirection);
                if (x == 0 || !occupied[y * gridWidth + x - 1])
                    AddMaskBoundaryQuad(
                        vertices, triangles, target.OutputRoot, localDepth,
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, left, bottom),
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, left, top),
                        -horizontalDirection);
                if (x == gridWidth - 1 || !occupied[y * gridWidth + x + 1])
                    AddMaskBoundaryQuad(
                        vertices, triangles, target.OutputRoot, localDepth,
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, right, bottom),
                        BilinearPaintPoint(bottomLeft, bottomRight, topRight, topLeft, right, top),
                        horizontalDirection);
            }
        }

        if (triangles.Count == 0)
            return false;

        Mesh collisionMesh = new Mesh { name = "Paint4MaskCollision" };
        if (vertices.Count > 65535)
            collisionMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        collisionMesh.SetVertices(vertices);
        collisionMesh.SetTriangles(triangles, 0);
        collisionMesh.RecalculateBounds();

        MeshCollider meshCollider = collisionSlot.GetComponent<MeshCollider>();
        if (meshCollider == null)
            return false;
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        meshCollider.isTrigger = false;
        meshCollider.cookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices;
        meshCollider.sharedMesh = collisionMesh;
        meshCollider.convex = false;
        meshCollider.enabled = true;

        BoxCollider oldTopSupport = collisionSlot.GetComponent<BoxCollider>();
        if (oldTopSupport != null)
            oldTopSupport.enabled = false;

        Debug.Log(
            $"[Painting Projection] Paint 4 collision matched to BG mask: " +
            $"grid={gridWidth}x{gridHeight}, occupied={occupiedCount}, " +
            $"boundaryTriangles={triangles.Count / 3}.",
            this);
        return true;
    }

    private static Vector3 BilinearPaintPoint(
        Vector3 bottomLeft,
        Vector3 bottomRight,
        Vector3 topRight,
        Vector3 topLeft,
        float u,
        float v)
    {
        return Vector3.Lerp(
            Vector3.Lerp(bottomLeft, bottomRight, u),
            Vector3.Lerp(topLeft, topRight, u),
            v);
    }

    private static void AddMaskBoundaryQuad(
        List<Vector3> vertices,
        List<int> triangles,
        Transform outputTransform,
        Vector3 localDepth,
        Vector3 worldA,
        Vector3 worldB,
        Vector3 desiredWorldNormal)
    {
        Vector3 worldDepth = outputTransform.TransformVector(localDepth);
        if (Vector3.Dot(Vector3.Cross(worldDepth, worldB - worldA), desiredWorldNormal) < 0f)
        {
            Vector3 swap = worldA;
            worldA = worldB;
            worldB = swap;
        }

        Vector3 localA = outputTransform.InverseTransformPoint(worldA);
        Vector3 localB = outputTransform.InverseTransformPoint(worldB);
        Vector3 halfDepth = localDepth * 0.5f;
        int first = vertices.Count;
        vertices.Add(localA - halfDepth);
        vertices.Add(localB - halfDepth);
        vertices.Add(localA + halfDepth);
        vertices.Add(localB + halfDepth);
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 3);
        triangles.Add(first);
        triangles.Add(first + 3);
        triangles.Add(first + 1);
    }

    private Vector3 MapViewportPointToPaintPlane(
        Vector2 viewportPoint,
        Rect paintRect,
        Bounds paintBounds,
        Vector3 positionOffset,
        Camera targetCamera,
        Transform targetModel,
        Transform targetBackground)
    {
        Vector3 paintCenter = paintBounds.center;

        float rawU = Mathf.InverseLerp(paintRect.xMin, paintRect.xMax, viewportPoint.x);
        float rawV = Mathf.InverseLerp(paintRect.yMin, paintRect.yMax, viewportPoint.y);
        if (targetBackground != null &&
            TryMapBackgroundPoint(targetBackground, targetCamera, rawU, rawV, out Vector3 backgroundPoint))
            return backgroundPoint + positionOffset;

        if (!TryCalculateCameraAlignedExtents(
                targetModel,
                targetCamera,
                paintCenter,
                out float minRight,
                out float maxRight,
                out float minUp,
                out float maxUp,
                out float nearestDepth))
            return paintCenter + positionOffset;

        float u = rawU;
        float v = rawV;
        float margin = (1f - outputViewportScale) * 0.5f;
        u = margin + u * outputViewportScale;
        v = margin + v * outputViewportScale;

        float projectionDepth = nearestDepth + 0.03f;
        return targetCamera.ViewportToWorldPoint(
                   new Vector3(u, v, projectionDepth)) +
               positionOffset;
    }

    private static Mesh BuildExtrudedCollisionMesh(
        Vector3[] frontVertices,
        List<int> frontTriangles,
        Vector3 localDepthVector,
        Transform outputTransform,
        bool orientBoundarySurfacesUpward)
    {
        const float weldPrecision = 10000f;
        var weldedVertices = new List<Vector3>();
        var weldedTriangles = new List<int>();
        var weldedLookup = new Dictionary<Vector3Int, int>();

        for (int i = 0; i + 2 < frontTriangles.Count; i += 3)
        {
            int a = GetWeldedVertex(
                frontVertices[frontTriangles[i]],
                weldPrecision,
                weldedVertices,
                weldedLookup);
            int b = GetWeldedVertex(
                frontVertices[frontTriangles[i + 1]],
                weldPrecision,
                weldedVertices,
                weldedLookup);
            int c = GetWeldedVertex(
                frontVertices[frontTriangles[i + 2]],
                weldPrecision,
                weldedVertices,
                weldedLookup);
            if (a == b || b == c || c == a)
                continue;

            weldedTriangles.Add(a);
            weldedTriangles.Add(b);
            weldedTriangles.Add(c);
        }

        var vertices = new List<Vector3>(weldedVertices.Count * 2);
        var triangles = new List<int>(weldedTriangles.Count * 2);
        // localDepthVector already represents silhouetteDepth in world units after
        // conversion through the output hierarchy. Do not normalize it here: doing
        // so would make scaled paintings produce paper-thin world colliders.
        Vector3 halfDepth = localDepthVector * 0.5f;
        int vertexCount = weldedVertices.Count;

        for (int i = 0; i < vertexCount; i++)
            vertices.Add(weldedVertices[i] - halfDepth);
        for (int i = 0; i < vertexCount; i++)
            vertices.Add(weldedVertices[i] + halfDepth);

        var edgeUseCount = new Dictionary<ulong, int>();
        for (int i = 0; i + 2 < weldedTriangles.Count; i += 3)
        {
            int a = weldedTriangles[i];
            int b = weldedTriangles[i + 1];
            int c = weldedTriangles[i + 2];

            // Paint 4 is a side-view physics world. Its player lives inside the
            // extrusion depth, so front/back caps only create Z-facing contacts
            // and prevent the player from reaching the actual platform edges.
            // Keep caps for Paint 2's established behaviour, but use boundary
            // walls only for Paint 4.
            if (!orientBoundarySurfacesUpward)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(c + vertexCount);
                triangles.Add(b + vertexCount);
                triangles.Add(a + vertexCount);
            }

            CountEdge(edgeUseCount, a, b);
            CountEdge(edgeUseCount, b, c);
            CountEdge(edgeUseCount, c, a);
        }

        foreach (KeyValuePair<ulong, int> edge in edgeUseCount)
        {
            if (edge.Value != 1)
                continue;

            int a = (int)(edge.Key >> 32);
            int b = (int)(edge.Key & uint.MaxValue);

            if (orientBoundarySurfacesUpward)
            {
                Vector3 worldDepth = outputTransform.TransformVector(localDepthVector);
                Vector3 worldEdge = outputTransform.TransformVector(
                    weldedVertices[b] - weldedVertices[a]);

                // CountEdge deliberately stores an unordered edge key. Restore a
                // useful winding for Paint 4 so ledges collide from above instead
                // of presenting their back face to the player.
                if (Vector3.Cross(worldDepth, worldEdge).y < 0f)
                {
                    int swap = a;
                    a = b;
                    b = swap;
                }
            }

            int backA = a + vertexCount;
            int backB = b + vertexCount;
            triangles.Add(a);
            triangles.Add(backA);
            triangles.Add(backB);
            triangles.Add(a);
            triangles.Add(backB);
            triangles.Add(b);
        }

        Mesh mesh = new Mesh { name = "ProjectedCollision" };
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int GetWeldedVertex(
        Vector3 vertex,
        float precision,
        List<Vector3> weldedVertices,
        Dictionary<Vector3Int, int> lookup)
    {
        Vector3Int key = new Vector3Int(
            Mathf.RoundToInt(vertex.x * precision),
            Mathf.RoundToInt(vertex.y * precision),
            Mathf.RoundToInt(vertex.z * precision));
        if (lookup.TryGetValue(key, out int existing))
            return existing;

        int index = weldedVertices.Count;
        weldedVertices.Add(vertex);
        lookup.Add(key, index);
        return index;
    }

    private static void CountEdge(Dictionary<ulong, int> edgeUseCount, int first, int second)
    {
        uint min = (uint)Mathf.Min(first, second);
        uint max = (uint)Mathf.Max(first, second);
        ulong key = ((ulong)min << 32) | max;
        edgeUseCount.TryGetValue(key, out int count);
        edgeUseCount[key] = count + 1;
    }

    private static List<Vector2> ClipTriangleToRect(Vector2 a, Vector2 b, Vector2 c, Rect rect)
    {
        var polygon = new List<Vector2> { a, b, c };
        polygon = ClipPolygon(polygon, 0, rect.xMin);
        polygon = ClipPolygon(polygon, 1, rect.xMax);
        polygon = ClipPolygon(polygon, 2, rect.yMin);
        polygon = ClipPolygon(polygon, 3, rect.yMax);
        return polygon;
    }

    private static List<Vector2> ClipPolygon(List<Vector2> input, int boundary, float value)
    {
        var output = new List<Vector2>();
        if (input.Count == 0)
            return output;

        Vector2 previous = input[input.Count - 1];
        bool previousInside = IsInside(previous, boundary, value);
        foreach (Vector2 current in input)
        {
            bool currentInside = IsInside(current, boundary, value);
            if (currentInside != previousInside)
                output.Add(IntersectBoundary(previous, current, boundary, value));
            if (currentInside)
                output.Add(current);
            previous = current;
            previousInside = currentInside;
        }
        return output;
    }

    private static bool IsInside(Vector2 point, int boundary, float value)
    {
        if (boundary == 0) return point.x >= value;
        if (boundary == 1) return point.x <= value;
        if (boundary == 2) return point.y >= value;
        return point.y <= value;
    }

    private static Vector2 IntersectBoundary(Vector2 a, Vector2 b, int boundary, float value)
    {
        bool vertical = boundary <= 1;
        float denominator = vertical ? b.x - a.x : b.y - a.y;
        float t = Mathf.Abs(denominator) < 0.00001f
            ? 0f
            : (value - (vertical ? a.x : a.y)) / denominator;
        return Vector2.LerpUnclamped(a, b, t);
    }

    private void ClearPreviousProjection()
    {
        nextCollisionSlots.Clear();

        for (int targetIndex = 0; targetIndex < projectionTargets.Count; targetIndex++)
        {
            Transform outputRoot = projectionTargets[targetIndex].OutputRoot;
            if (outputRoot == null)
                continue;

            for (int i = outputRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = outputRoot.GetChild(i);
                if (child.name == "ProjectionSurface")
                {
                    MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        renderer.enabled = false;
                    continue;
                }

                if (child.name.StartsWith("ProjectionCollision_"))
                {
                    MeshCollider collider = child.GetComponent<MeshCollider>();
                    if (collider != null)
                    {
                        collider.enabled = false;
                        collider.sharedMesh = null;
                    }
                    BoxCollider topSupport = child.GetComponent<BoxCollider>();
                    if (topSupport != null)
                        topSupport.enabled = false;
                    continue;
                }

                Destroy(child.gameObject);
            }
        }

        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
    }

    private bool RenderVisibleSilhouetteMask(
        Camera roomCamera,
        HashSet<Transform> visibleSources)
    {
        Shader maskShader = Shader.Find("Hidden/ProjectionSilhouetteMask");
        if (maskShader == null)
            return false;

        if (silhouetteCamera == null)
        {
            Debug.LogError(
                "[Painting Projection] Persistent Projection_SilhouetteCamera is missing. " +
                "Run Tools/Painting Projection/Ensure Persistent Clip Areas.",
                this);
            return false;
        }

        int maskWidth = silhouetteMaskResolution;
        int maskHeight = Mathf.Max(
            1,
            Mathf.RoundToInt(maskWidth / Mathf.Max(0.01f, roomCamera.aspect)));

        if (silhouetteMask == null ||
            silhouetteMask.width != maskWidth ||
            silhouetteMask.height != maskHeight)
        {
            if (silhouetteMask != null)
            {
                silhouetteMask.Release();
                Destroy(silhouetteMask);
            }

            silhouetteMask = new RenderTexture(
                maskWidth,
                maskHeight,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = "Projection_SilhouetteMask",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            silhouetteMask.Create();
        }

        silhouetteCamera.CopyFrom(roomCamera);
        silhouetteCamera.transform.SetPositionAndRotation(
            roomCamera.transform.position,
            roomCamera.transform.rotation);
        silhouetteCamera.targetTexture = silhouetteMask;
        silhouetteCamera.clearFlags = CameraClearFlags.SolidColor;
        silhouetteCamera.backgroundColor = Color.clear;
        silhouetteCamera.cullingMask = 1 << SilhouetteLayer;
        silhouetteCamera.allowHDR = false;
        silhouetteCamera.allowMSAA = false;

        var originalLayers = new Dictionary<GameObject, int>();
        var originalMaterials = new Dictionary<Renderer, Material[]>();
        Material maskMaterial = new Material(maskShader);
        for (int sourceIndex = 0; sourceIndex < projectionObjectsRoot.childCount; sourceIndex++)
        {
            Transform source = projectionObjectsRoot.GetChild(sourceIndex);
            if (!visibleSources.Contains(source))
                continue;
            ProjectionSource settings = source.GetComponent<ProjectionSource>();
            if (!source.gameObject.activeInHierarchy || settings == null || !settings.ProjectionEnabled)
                continue;

            foreach (Transform child in source.GetComponentsInChildren<Transform>(true))
            {
                originalLayers[child.gameObject] = child.gameObject.layer;
                child.gameObject.layer = SilhouetteLayer;
            }

            foreach (Renderer sourceRenderer in source.GetComponentsInChildren<Renderer>(true))
            {
                originalMaterials[sourceRenderer] = sourceRenderer.sharedMaterials;
                Material[] replacements = new Material[sourceRenderer.sharedMaterials.Length];
                for (int materialIndex = 0; materialIndex < replacements.Length; materialIndex++)
                    replacements[materialIndex] = maskMaterial;
                sourceRenderer.sharedMaterials = replacements;
            }
        }

        silhouetteCamera.Render();

        foreach (KeyValuePair<GameObject, int> entry in originalLayers)
        {
            if (entry.Key != null)
                entry.Key.layer = entry.Value;
        }

        foreach (KeyValuePair<Renderer, Material[]> entry in originalMaterials)
        {
            if (entry.Key != null)
                entry.Key.sharedMaterials = entry.Value;
        }

        Destroy(maskMaterial);

        silhouetteCamera.targetTexture = null;
        return true;
    }

    private void CreateSilhouetteMaskSurface(
        ProjectionTarget target,
        Camera roomCamera,
        Bounds paintBounds)
    {
        Shader displayShader = Shader.Find("Hidden/ProjectionSilhouetteDisplay");
        if (displayShader == null || silhouetteMask == null)
            return;

        Vector3 bottomLeft = MapModelBoundsPoint(target, paintBounds, 0f, 0f);
        Vector3 bottomRight = MapModelBoundsPoint(target, paintBounds, 1f, 0f);
        Vector3 topRight = MapModelBoundsPoint(target, paintBounds, 1f, 1f);
        Vector3 topLeft = MapModelBoundsPoint(target, paintBounds, 0f, 1f);

        Transform surface = target.OutputRoot.Find("ProjectionSurface");
        if (surface == null)
        {
            Debug.LogError($"[Painting Projection] Persistent ProjectionSurface is missing under {target.Name}.", this);
            return;
        }

        Vector3[] vertices =
        {
            target.OutputRoot.InverseTransformPoint(bottomLeft),
            target.OutputRoot.InverseTransformPoint(bottomRight),
            target.OutputRoot.InverseTransformPoint(topRight),
            target.OutputRoot.InverseTransformPoint(topLeft)
        };
        Vector2[] uvs =
        {
            ToViewportUv(roomCamera, bottomLeft),
            ToViewportUv(roomCamera, bottomRight),
            ToViewportUv(roomCamera, topRight),
            ToViewportUv(roomCamera, topLeft)
        };

        Mesh mesh = new Mesh { name = $"{target.Name}_VisibleSilhouette" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter surfaceFilter = surface.GetComponent<MeshFilter>();
        MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
        if (surfaceFilter == null || renderer == null)
            return;
        surfaceFilter.sharedMesh = mesh;
        Material material = new Material(displayShader);
        material.SetTexture("_MainTex", silhouetteMask);
        // Camera RenderTextures are sampled by Unity in the regular viewport UV
        // orientation. Supplying the render-target GPU matrix here flips Y a
        // second time on Direct3D and moves the silhouette away from the BG.
        Matrix4x4 projectionViewProjection =
            roomCamera.projectionMatrix * roomCamera.worldToCameraMatrix;
        material.SetMatrix("_ProjectionViewProjection", projectionViewProjection);
        renderer.sharedMaterial = material;
        renderer.enabled = true;
        runtimeMaterials.Add(material);
    }

    private readonly Dictionary<Transform, int> nextCollisionSlots =
        new Dictionary<Transform, int>();

    private int GetCollisionSlotIndex(Transform outputRoot)
    {
        nextCollisionSlots.TryGetValue(outputRoot, out int index);
        nextCollisionSlots[outputRoot] = index + 1;
        return index;
    }

    private Vector3 MapModelBoundsPoint(
        ProjectionTarget target,
        Bounds paintBounds,
        float normalizedX,
        float normalizedY)
    {
        if (target.Background != null &&
            TryMapBackgroundPoint(
                target.Background,
                target.Camera,
                normalizedX,
                normalizedY,
                out Vector3 backgroundPoint))
            return backgroundPoint;

        if (target.ClipArea != null)
            return GetClipAreaWorldPoint(target.ClipArea, normalizedX, normalizedY);

        Vector3 center = paintBounds.center;
        if (!TryCalculateCameraAlignedExtents(
                target.Model,
                target.Camera,
                center,
                out float minRight,
                out float maxRight,
                out float minUp,
                out float maxUp,
                out float nearestDepth))
            return center;

        float margin = (1f - outputViewportScale) * 0.5f;
        float x = Mathf.Lerp(minRight, maxRight, margin + normalizedX * outputViewportScale);
        float y = Mathf.Lerp(minUp, maxUp, margin + normalizedY * outputViewportScale);
        float centerDepth = Vector3.Dot(
            center - target.Camera.transform.position,
            target.Camera.transform.forward);
        Vector3 planeCenter = center + target.Camera.transform.forward *
            (nearestDepth + 0.03f - centerDepth);

        return planeCenter +
               target.Camera.transform.right * x +
               target.Camera.transform.up * y;
    }

    private static Vector2 ToViewportUv(Camera camera, Vector3 worldPoint)
    {
        Vector3 viewport = camera.WorldToViewportPoint(worldPoint);
        return new Vector2(viewport.x, viewport.y);
    }

    private static bool TryMapBackgroundPoint(
        Transform background,
        Camera targetCamera,
        float normalizedX,
        float normalizedY,
        out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (!TryGetBackgroundFrame(
                background,
                targetCamera,
                out Vector3 center,
                out Vector3 horizontalHalfExtent,
                out Vector3 verticalHalfExtent))
            return false;

        worldPoint = center +
                     horizontalHalfExtent * (normalizedX * 2f - 1f) +
                     verticalHalfExtent * (normalizedY * 2f - 1f) -
                     targetCamera.transform.forward * 0.01f;
        return true;
    }

    private static bool TryGetBackgroundFrame(
        Transform background,
        Camera targetCamera,
        out Vector3 center,
        out Vector3 horizontalHalfExtent,
        out Vector3 verticalHalfExtent)
    {
        center = Vector3.zero;
        horizontalHalfExtent = Vector3.zero;
        verticalHalfExtent = Vector3.zero;
        if (background == null || targetCamera == null)
            return false;

        MeshFilter meshFilter = background.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return false;

        Bounds bounds = meshFilter.sharedMesh.bounds;
        Vector3 size = bounds.size;
        int smallestAxis = 0;
        if (size.y < size.x && size.y <= size.z)
            smallestAxis = 1;
        else if (size.z < size.x && size.z < size.y)
            smallestAxis = 2;

        int firstAxis = smallestAxis == 0 ? 1 : 0;
        int secondAxis = smallestAxis == 2 ? 1 : 2;
        if (firstAxis == secondAxis)
            secondAxis = 2;

        Vector3 firstLocalHalfExtent = AxisVector(firstAxis) * AxisValue(size, firstAxis) * 0.5f;
        Vector3 secondLocalHalfExtent = AxisVector(secondAxis) * AxisValue(size, secondAxis) * 0.5f;
        Vector3 firstWorldHalfExtent = background.TransformVector(firstLocalHalfExtent);
        Vector3 secondWorldHalfExtent = background.TransformVector(secondLocalHalfExtent);

        float firstHorizontalAlignment = Mathf.Abs(Vector3.Dot(
            firstWorldHalfExtent.normalized,
            targetCamera.transform.right));
        float secondHorizontalAlignment = Mathf.Abs(Vector3.Dot(
            secondWorldHalfExtent.normalized,
            targetCamera.transform.right));

        if (firstHorizontalAlignment >= secondHorizontalAlignment)
        {
            horizontalHalfExtent = firstWorldHalfExtent;
            verticalHalfExtent = secondWorldHalfExtent;
        }
        else
        {
            horizontalHalfExtent = secondWorldHalfExtent;
            verticalHalfExtent = firstWorldHalfExtent;
        }

        if (Vector3.Dot(horizontalHalfExtent, targetCamera.transform.right) < 0f)
            horizontalHalfExtent = -horizontalHalfExtent;
        if (Vector3.Dot(verticalHalfExtent, targetCamera.transform.up) < 0f)
            verticalHalfExtent = -verticalHalfExtent;

        center = background.TransformPoint(bounds.center);
        return horizontalHalfExtent.sqrMagnitude > 0.000001f &&
               verticalHalfExtent.sqrMagnitude > 0.000001f;
    }

    private static Vector3 AxisVector(int axis)
    {
        if (axis == 0)
            return Vector3.right;
        if (axis == 1)
            return Vector3.up;
        return Vector3.forward;
    }

    private static float AxisValue(Vector3 value, int axis)
    {
        if (axis == 0)
            return value.x;
        if (axis == 1)
            return value.y;
        return value.z;
    }

    private static Vector3 GetClipAreaWorldPoint(
        BoxCollider clipArea,
        float normalizedX,
        float normalizedY)
    {
        Vector3 size = clipArea.size;
        Vector3 center = clipArea.center;
        Vector3 localPoint = new Vector3(
            center.x + (normalizedX - 0.5f) * size.x,
            center.y + (normalizedY - 0.5f) * size.y,
            center.z);
        return clipArea.transform.TransformPoint(localPoint);
    }

    private void AddProjectionTarget(
        string paintingName,
        string cameraName,
        Camera configuredCamera,
        Transform configuredModel,
        Transform configuredOutputRoot,
        BoxCollider configuredClipArea)
    {
        Transform painting = FindSceneTransform(paintingName);
        if (painting == null)
            return;

        Camera targetCamera = configuredCamera != null
            ? configuredCamera
            : FindSceneCamera(cameraName);
        Transform model = configuredModel != null
            ? configuredModel
            : painting.Find("Model");
        if (model == null)
            model = painting;
        Transform outputRoot = configuredOutputRoot != null
            ? configuredOutputRoot
            : painting.Find("PuzzleRoot/ProjectionArea/ProjectedObjects");
        BoxCollider clipArea = configuredClipArea != null
            ? configuredClipArea
            : painting.Find("ProjectionClipArea")?.GetComponent<BoxCollider>();
        Transform background = painting.Find("Model/BG");
        if (background == null)
            background = painting.Find("BG");

        if (targetCamera == null || model == null || outputRoot == null ||
            clipArea == null || background == null)
            return;

        projectionTargets.Add(new ProjectionTarget
        {
            Name = paintingName,
            Camera = targetCamera,
            Model = model,
            OutputRoot = outputRoot,
            ClipArea = clipArea,
            Background = background
        });
    }

    private static Transform EnsureOutputRoot(Transform painting)
    {
        Transform puzzleRoot = painting.Find("PuzzleRoot");
        if (puzzleRoot == null)
        {
            puzzleRoot = new GameObject("PuzzleRoot").transform;
            puzzleRoot.SetParent(painting, false);
        }

        Transform projectionArea = puzzleRoot.Find("ProjectionArea");
        if (projectionArea == null)
        {
            projectionArea = new GameObject("ProjectionArea").transform;
            projectionArea.SetParent(puzzleRoot, false);
        }

        Transform outputRoot = projectionArea.Find("ProjectedObjects");
        if (outputRoot == null)
        {
            outputRoot = new GameObject("ProjectedObjects").transform;
            outputRoot.SetParent(projectionArea, false);
        }

        return outputRoot;
    }

    private static Camera FindSceneCamera(string cameraName)
    {
        foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == cameraName)
                return candidate;
        }

        return null;
    }

    private static Rect BoundsToViewportRect(Camera camera, Bounds bounds, out bool inFront)
    {
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
        inFront = false;

        foreach (Vector3 corner in GetCorners(bounds))
        {
            Vector3 viewport = camera.WorldToViewportPoint(corner);
            if (viewport.z > 0f)
                inFront = true;
            min.x = Mathf.Min(min.x, viewport.x);
            min.y = Mathf.Min(min.y, viewport.y);
            max.x = Mathf.Max(max.x, viewport.x);
            max.y = Mathf.Max(max.y, viewport.y);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool TryGetTargetViewportRect(
        Camera roomCamera,
        ProjectionTarget target,
        Bounds paintBounds,
        out Rect viewportRect,
        out bool inFront)
    {
        viewportRect = new Rect();
        inFront = false;

        if (target.Background != null &&
            TryGetBackgroundFrame(
                target.Background,
                target.Camera,
                out Vector3 backgroundCenter,
                out Vector3 backgroundHorizontal,
                out Vector3 backgroundVertical))
        {
            Vector3[] backgroundCorners =
            {
                backgroundCenter - backgroundHorizontal - backgroundVertical,
                backgroundCenter + backgroundHorizontal - backgroundVertical,
                backgroundCenter + backgroundHorizontal + backgroundVertical,
                backgroundCenter - backgroundHorizontal + backgroundVertical
            };
            Vector2 backgroundMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 backgroundMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < backgroundCorners.Length; i++)
            {
                Vector3 point = roomCamera.WorldToViewportPoint(backgroundCorners[i]);
                if (point.z > 0f)
                    inFront = true;
                backgroundMin.x = Mathf.Min(backgroundMin.x, point.x);
                backgroundMin.y = Mathf.Min(backgroundMin.y, point.y);
                backgroundMax.x = Mathf.Max(backgroundMax.x, point.x);
                backgroundMax.y = Mathf.Max(backgroundMax.y, point.y);
            }

            viewportRect = Rect.MinMaxRect(
                backgroundMin.x,
                backgroundMin.y,
                backgroundMax.x,
                backgroundMax.y);
            return true;
        }

        if (target.ClipArea != null)
        {
            Vector3[] clipCorners =
            {
                GetClipAreaWorldPoint(target.ClipArea, 0f, 0f),
                GetClipAreaWorldPoint(target.ClipArea, 1f, 0f),
                GetClipAreaWorldPoint(target.ClipArea, 1f, 1f),
                GetClipAreaWorldPoint(target.ClipArea, 0f, 1f)
            };
            Vector2 clipMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 clipMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < clipCorners.Length; i++)
            {
                Vector3 point = roomCamera.WorldToViewportPoint(clipCorners[i]);
                if (point.z > 0f)
                    inFront = true;
                clipMin.x = Mathf.Min(clipMin.x, point.x);
                clipMin.y = Mathf.Min(clipMin.y, point.y);
                clipMax.x = Mathf.Max(clipMax.x, point.x);
                clipMax.y = Mathf.Max(clipMax.y, point.y);
            }

            viewportRect = Rect.MinMaxRect(clipMin.x, clipMin.y, clipMax.x, clipMax.y);
            return true;
        }

        if (!TryCalculateCameraAlignedExtents(
                target.Model,
                target.Camera,
                paintBounds.center,
                out _,
                out _,
                out _,
                out _,
                out float nearestDepth))
            return false;

        float projectionDepth = nearestDepth + 0.03f;
        Vector3[] targetCorners =
        {
            target.Camera.ViewportToWorldPoint(new Vector3(0f, 0f, projectionDepth)),
            target.Camera.ViewportToWorldPoint(new Vector3(1f, 0f, projectionDepth)),
            target.Camera.ViewportToWorldPoint(new Vector3(1f, 1f, projectionDepth)),
            target.Camera.ViewportToWorldPoint(new Vector3(0f, 1f, projectionDepth))
        };

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < targetCorners.Length; i++)
        {
            Vector3 viewportPoint = roomCamera.WorldToViewportPoint(targetCorners[i]);
            if (viewportPoint.z > 0f)
                inFront = true;

            min.x = Mathf.Min(min.x, viewportPoint.x);
            min.y = Mathf.Min(min.y, viewportPoint.y);
            max.x = Mathf.Max(max.x, viewportPoint.x);
            max.y = Mathf.Max(max.y, viewportPoint.y);
        }

        viewportRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static Rect RendererBoundsToViewportRect(
        Camera camera,
        Transform root,
        out bool inFront)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
        inFront = false;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            foreach (Vector3 corner in GetCorners(renderers[rendererIndex].bounds))
            {
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                if (viewport.z > 0f)
                    inFront = true;
                min.x = Mathf.Min(min.x, viewport.x);
                min.y = Mathf.Min(min.y, viewport.y);
                max.x = Mathf.Max(max.x, viewport.x);
                max.y = Mathf.Max(max.y, viewport.y);
            }
        }

        return renderers.Length > 0
            ? Rect.MinMaxRect(min.x, min.y, max.x, max.y)
            : new Rect();
    }

    private static bool TryCalculateCameraAlignedExtents(
        Transform root,
        Camera camera,
        Vector3 center,
        out float minRight,
        out float maxRight,
        out float minUp,
        out float maxUp,
        out float nearestDepth)
    {
        minRight = float.PositiveInfinity;
        maxRight = float.NegativeInfinity;
        minUp = float.PositiveInfinity;
        maxUp = float.NegativeInfinity;
        nearestDepth = float.PositiveInfinity;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return false;

        Vector3 cameraRight = camera.transform.right;
        Vector3 cameraUp = camera.transform.up;
        Vector3 cameraForward = camera.transform.forward;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            foreach (Vector3 corner in GetCorners(renderers[rendererIndex].bounds))
            {
                Vector3 fromCenter = corner - center;
                minRight = Mathf.Min(minRight, Vector3.Dot(fromCenter, cameraRight));
                maxRight = Mathf.Max(maxRight, Vector3.Dot(fromCenter, cameraRight));
                minUp = Mathf.Min(minUp, Vector3.Dot(fromCenter, cameraUp));
                maxUp = Mathf.Max(maxUp, Vector3.Dot(fromCenter, cameraUp));
                nearestDepth = Mathf.Min(
                    nearestDepth,
                    Vector3.Dot(corner - camera.transform.position, cameraForward));
            }
        }

        return true;
    }

    private static IEnumerable<Vector3> GetCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
            yield return new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : new Rect();
    }

    private static bool OverlapsScreen(Rect rect) => rect.xMax > 0f && rect.xMin < 1f && rect.yMax > 0f && rect.yMin < 1f;

    private static bool TryCalculateRendererBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
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

    private static bool IsInsidePaintingHierarchy(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            string normalizedName = current.name.Replace(" ", string.Empty).ToLowerInvariant();
            if (normalizedName.StartsWith("paint") && normalizedName.Length > 5)
            {
                bool numericSuffix = true;
                for (int i = 5; i < normalizedName.Length; i++)
                {
                    if (!char.IsDigit(normalizedName[i]))
                    {
                        numericSuffix = false;
                        break;
                    }
                }

                if (numericSuffix)
                    return true;
            }
        }

        return false;
    }

    private void OnDestroy()
    {
        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
                Destroy(material);
        }

        silhouetteCamera = null;

        if (silhouetteMask != null)
        {
            silhouetteMask.Release();
            Destroy(silhouetteMask);
        }
    }
}
