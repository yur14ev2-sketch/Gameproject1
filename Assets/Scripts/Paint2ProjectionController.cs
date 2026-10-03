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
    }

    [Header("Input")]
    [SerializeField] private KeyCode projectionKey = KeyCode.P;

    [Header("Paint 2 Output")]
    [SerializeField] private Transform projectionObjectsRoot;
    [SerializeField] private Camera paint2Camera;
    [SerializeField] private Transform paint2Model;
    [SerializeField] private Transform projectionArea;
    [SerializeField] private Transform projectedObjectsRoot;
    [SerializeField, Min(0.01f)] private float silhouetteDepth = 0.25f;
    [SerializeField, Range(0.5f, 1f)] private float outputViewportScale = 0.92f;

    private GameModeManager gameModeManager;
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly List<ProjectionTarget> projectionTargets = new List<ProjectionTarget>();

    private void Awake()
    {
        gameModeManager = GetComponent<GameModeManager>();
        ResolveReferences();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(projectionKey))
            return;

        if (gameModeManager != null && gameModeManager.IsInsidePainting)
        {
            Debug.Log("[Paint2 Projection] P ignored: return to the 3D room before projecting.", this);
            return;
        }

        ProjectVisibleSources();
    }

    private void ResolveReferences()
    {
        if (projectionObjectsRoot == null)
            projectionObjectsRoot = FindSceneTransform("Furniture");

        Transform paint2 = FindSceneTransform("paint2");
        if (paint2 != null)
        {
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
        AddProjectionTarget("paint2", "Camera_Paint2", paint2Camera, paint2Model, projectedObjectsRoot);
        AddProjectionTarget("paint4", "Camera_Paint4", null, null, null);
    }

    private void ProjectVisibleSources()
    {
        ResolveReferences();
        Camera roomCamera = gameModeManager != null ? gameModeManager.RoomCamera : null;
        if (roomCamera == null || projectionObjectsRoot == null || projectionTargets.Count == 0)
        {
            Debug.LogError("[Painting Projection] Required room camera, Furniture, or Paint 2/4 references are missing.", this);
            return;
        }

        ClearPreviousProjection();
        int totalProjectedCount = 0;

        for (int targetIndex = 0; targetIndex < projectionTargets.Count; targetIndex++)
            totalProjectedCount += ProjectToTarget(projectionTargets[targetIndex], roomCamera);

        Debug.Log(
            totalProjectedCount > 0
                ? $"[Painting Projection] P projected {totalProjectedCount} object(s) into Paint 2/4."
                : "[Painting Projection] P detected, but no Projection Source overlapped Paint 2 or Paint 4.",
            this);
    }

    private int ProjectToTarget(ProjectionTarget target, Camera roomCamera)
    {
        if (target.Camera == null || target.Model == null || target.OutputRoot == null)
            return 0;

        if (!TryCalculateRendererBounds(target.Model, out Bounds paintBounds))
            return 0;

        Rect paintRect = RendererBoundsToViewportRect(
            roomCamera, target.Model, out bool paintInFront);
        if (!paintInFront || !OverlapsScreen(paintRect))
            return 0;

        int projectedCount = 0;

        for (int sourceIndex = 0; sourceIndex < projectionObjectsRoot.childCount; sourceIndex++)
        {
            Transform sourceTransform = projectionObjectsRoot.GetChild(sourceIndex);
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
            if (CreateProjectedMesh(modelRoot, sourceTransform.name, color, generateCollider, positionOffset,
                    roomCamera, paintRect, paintBounds, target.Camera, target.Model, target.OutputRoot))
                projectedCount++;
        }

        if (projectedCount > 0)
            Debug.Log($"[Painting Projection] {target.Name} received {projectedCount} projection(s).", this);

        return projectedCount;
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
        Transform targetOutputRoot)
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
                        point, paintRect, paintBounds, positionOffset, targetCamera, targetModel));

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

        GameObject silhouette = new GameObject($"Projected_{sourceName}");
        silhouette.transform.SetParent(targetOutputRoot, false);

        Vector3[] localVertices = new Vector3[worldVertices.Count];
        for (int i = 0; i < worldVertices.Count; i++)
            localVertices[i] = targetOutputRoot.InverseTransformPoint(worldVertices[i]);

        Mesh visualMesh = new Mesh { name = $"{sourceName}_ProjectedVisual" };
        visualMesh.SetVertices(localVertices);
        visualMesh.SetTriangles(triangles, 0);
        visualMesh.RecalculateNormals();
        visualMesh.RecalculateBounds();

        MeshFilter outputFilter = silhouette.AddComponent<MeshFilter>();
        outputFilter.sharedMesh = visualMesh;
        MeshRenderer outputRenderer = silhouette.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = silhouetteColor };
        outputRenderer.sharedMaterial = material;
        runtimeMaterials.Add(material);

        if (generateCollider)
        {
            Mesh collisionMesh = BuildExtrudedCollisionMesh(
                localVertices,
                triangles,
                targetOutputRoot.InverseTransformDirection(targetCamera.transform.forward));
            MeshCollider meshCollider = silhouette.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = collisionMesh;
            meshCollider.convex = false;
        }

        return true;
    }

    private Vector3 MapViewportPointToPaintPlane(
        Vector2 viewportPoint,
        Rect paintRect,
        Bounds paintBounds,
        Vector3 positionOffset,
        Camera targetCamera,
        Transform targetModel)
    {
        Vector3 cameraRight = targetCamera.transform.right;
        Vector3 cameraUp = targetCamera.transform.up;
        Vector3 cameraForward = targetCamera.transform.forward;
        Vector3 paintCenter = paintBounds.center;

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

        float u = Mathf.InverseLerp(paintRect.xMin, paintRect.xMax, viewportPoint.x);
        float v = Mathf.InverseLerp(paintRect.yMin, paintRect.yMax, viewportPoint.y);
        float margin = (1f - outputViewportScale) * 0.5f;
        u = margin + u * outputViewportScale;
        v = margin + v * outputViewportScale;

        float centerDepth = Vector3.Dot(paintCenter - targetCamera.transform.position, cameraForward);
        Vector3 planeCenter = paintCenter - cameraForward *
            (centerDepth - nearestDepth + silhouetteDepth * 0.5f + 0.02f);

        return planeCenter +
               cameraRight * Mathf.Lerp(minRight, maxRight, u) +
               cameraUp * Mathf.Lerp(minUp, maxUp, v) +
               positionOffset;
    }

    private Mesh BuildExtrudedCollisionMesh(Vector3[] frontVertices, List<int> frontTriangles, Vector3 depthDirection)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Vector3 halfDepth = depthDirection.normalized * (silhouetteDepth * 0.5f);

        for (int i = 0; i + 2 < frontTriangles.Count; i += 3)
        {
            Vector3 a = frontVertices[frontTriangles[i]];
            Vector3 b = frontVertices[frontTriangles[i + 1]];
            Vector3 c = frontVertices[frontTriangles[i + 2]];
            int start = vertices.Count;
            vertices.Add(a - halfDepth);
            vertices.Add(b - halfDepth);
            vertices.Add(c - halfDepth);
            vertices.Add(a + halfDepth);
            vertices.Add(b + halfDepth);
            vertices.Add(c + halfDepth);

            triangles.AddRange(new[]
            {
                start, start + 1, start + 2,
                start + 5, start + 4, start + 3,
                start, start + 3, start + 4, start, start + 4, start + 1,
                start + 1, start + 4, start + 5, start + 1, start + 5, start + 2,
                start + 2, start + 5, start + 3, start + 2, start + 3, start
            });
        }

        Mesh mesh = new Mesh { name = "ProjectedCollision" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
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

    private void CreateProjectedBox(ProjectionSource source, Rect normalizedRect, Bounds paintBounds)
    {
        float margin = (1f - outputViewportScale) * 0.5f;
        Rect output = new Rect(
            margin + normalizedRect.x * outputViewportScale,
            margin + normalizedRect.y * outputViewportScale,
            normalizedRect.width * outputViewportScale,
            normalizedRect.height * outputViewportScale);

        Vector3 cameraRight = paint2Camera.transform.right;
        Vector3 cameraUp = paint2Camera.transform.up;
        Vector3 cameraForward = paint2Camera.transform.forward;
        Vector3 paintCenter = paintBounds.center;

        float minRight = float.PositiveInfinity;
        float maxRight = float.NegativeInfinity;
        float minUp = float.PositiveInfinity;
        float maxUp = float.NegativeInfinity;
        float nearestDepth = float.PositiveInfinity;

        foreach (Vector3 corner in GetCorners(paintBounds))
        {
            Vector3 fromCenter = corner - paintCenter;
            minRight = Mathf.Min(minRight, Vector3.Dot(fromCenter, cameraRight));
            maxRight = Mathf.Max(maxRight, Vector3.Dot(fromCenter, cameraRight));
            minUp = Mathf.Min(minUp, Vector3.Dot(fromCenter, cameraUp));
            maxUp = Mathf.Max(maxUp, Vector3.Dot(fromCenter, cameraUp));
            nearestDepth = Mathf.Min(
                nearestDepth,
                Vector3.Dot(corner - paint2Camera.transform.position, cameraForward));
        }

        float left = Mathf.Lerp(minRight, maxRight, output.xMin);
        float right = Mathf.Lerp(minRight, maxRight, output.xMax);
        float bottom = Mathf.Lerp(minUp, maxUp, output.yMin);
        float top = Mathf.Lerp(minUp, maxUp, output.yMax);
        float centerRight = (left + right) * 0.5f;
        float centerUp = (bottom + top) * 0.5f;
        float centerDepth = Vector3.Dot(paintCenter - paint2Camera.transform.position, cameraForward);
        Vector3 planeCenter = paintCenter - cameraForward *
            (centerDepth - nearestDepth + silhouetteDepth * 0.5f + 0.02f);
        Vector3 center = planeCenter + cameraRight * centerRight + cameraUp * centerUp + source.PositionOffset;

        GameObject silhouette = GameObject.CreatePrimitive(PrimitiveType.Cube);
        silhouette.name = $"Projected_{source.name}";
        silhouette.transform.SetParent(projectedObjectsRoot, true);
        silhouette.transform.SetPositionAndRotation(
            center,
            Quaternion.LookRotation(paint2Camera.transform.forward, paint2Camera.transform.up));

        float width = Mathf.Abs(right - left);
        float height = Mathf.Abs(top - bottom);

        silhouette.transform.localScale = Vector3.Scale(
            new Vector3(width, height, silhouetteDepth),
            source.ScaleMultiplier);

        Renderer renderer = silhouette.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = source.SilhouetteColor };
        renderer.sharedMaterial = material;
        runtimeMaterials.Add(material);

        Collider collider = silhouette.GetComponent<Collider>();
        collider.enabled = source.GenerateCollider;
    }

    private void ClearPreviousProjection()
    {
        for (int targetIndex = 0; targetIndex < projectionTargets.Count; targetIndex++)
        {
            Transform outputRoot = projectionTargets[targetIndex].OutputRoot;
            if (outputRoot == null)
                continue;

            for (int i = outputRoot.childCount - 1; i >= 0; i--)
                Destroy(outputRoot.GetChild(i).gameObject);
        }

        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
    }

    private void AddProjectionTarget(
        string paintingName,
        string cameraName,
        Camera configuredCamera,
        Transform configuredModel,
        Transform configuredOutputRoot)
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
            : EnsureOutputRoot(painting);

        if (targetCamera == null || model == null || outputRoot == null)
            return;

        projectionTargets.Add(new ProjectionTarget
        {
            Name = paintingName,
            Camera = targetCamera,
            Model = model,
            OutputRoot = outputRoot
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
    }
}
