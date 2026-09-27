using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Paint2ProjectionController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private KeyCode projectionKey = KeyCode.P;

    [Header("Paint 2 Output")]
    [SerializeField] private Camera paint2Camera;
    [SerializeField] private Transform paint2Model;
    [SerializeField] private Transform projectionArea;
    [SerializeField] private Transform projectedObjectsRoot;
    [SerializeField, Min(0.01f)] private float silhouetteDepth = 0.25f;
    [SerializeField, Range(0.5f, 1f)] private float outputViewportScale = 0.92f;

    private GameModeManager gameModeManager;
    private readonly List<Material> runtimeMaterials = new List<Material>();

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
    }

    private void ProjectVisibleSources()
    {
        ResolveReferences();
        Camera roomCamera = Camera.main;
        if (roomCamera == null || paint2Camera == null || paint2Model == null || projectedObjectsRoot == null)
        {
            Debug.LogError("[Paint2 Projection] Required room camera or Paint 2 references are missing.", this);
            return;
        }

        if (!TryCalculateRendererBounds(paint2Model, out Bounds paintBounds))
        {
            Debug.LogError("[Paint2 Projection] Paint 2 Model has no Renderer bounds.", this);
            return;
        }

        Rect paintRect = BoundsToViewportRect(roomCamera, paintBounds, out bool paintInFront);
        if (!paintInFront || !OverlapsScreen(paintRect))
        {
            Debug.Log("[Paint2 Projection] P detected, but Paint 2 is not visible from the current room camera.", this);
            return;
        }

        ClearPreviousProjection();
        int projectedCount = 0;

        foreach (ProjectionSource source in Resources.FindObjectsOfTypeAll<ProjectionSource>())
        {
            if (!source.gameObject.scene.IsValid() || !source.ProjectionEnabled || !source.TryGetWorldBounds(out Bounds sourceBounds))
                continue;

            Rect sourceRect = BoundsToViewportRect(roomCamera, sourceBounds, out bool sourceInFront);
            if (!sourceInFront)
                continue;

            Rect overlap = Intersect(sourceRect, paintRect);
            if (overlap.width <= 0.001f || overlap.height <= 0.001f)
                continue;

            if (CreateProjectedMesh(source, roomCamera, paintRect, paintBounds))
                projectedCount++;
        }

        Debug.Log(
            projectedCount > 0
                ? $"[Paint2 Projection] P projected {projectedCount} object(s) into Paint 2."
                : "[Paint2 Projection] P detected, but no ProjectionSource overlapped Paint 2.",
            this);
    }

    private bool CreateProjectedMesh(
        ProjectionSource source,
        Camera roomCamera,
        Rect paintRect,
        Bounds paintBounds)
    {
        var worldVertices = new List<Vector3>();
        var triangles = new List<int>();

        foreach (MeshFilter meshFilter in source.ModelRoot.GetComponentsInChildren<MeshFilter>(true))
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
                    worldVertices.Add(MapViewportPointToPaintPlane(point, paintRect, paintBounds, source.PositionOffset));

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

        GameObject silhouette = new GameObject($"Projected_{source.name}");
        silhouette.transform.SetParent(projectedObjectsRoot, false);

        Vector3[] localVertices = new Vector3[worldVertices.Count];
        for (int i = 0; i < worldVertices.Count; i++)
            localVertices[i] = projectedObjectsRoot.InverseTransformPoint(worldVertices[i]);

        Mesh visualMesh = new Mesh { name = $"{source.name}_ProjectedVisual" };
        visualMesh.SetVertices(localVertices);
        visualMesh.SetTriangles(triangles, 0);
        visualMesh.RecalculateNormals();
        visualMesh.RecalculateBounds();

        MeshFilter outputFilter = silhouette.AddComponent<MeshFilter>();
        outputFilter.sharedMesh = visualMesh;
        MeshRenderer outputRenderer = silhouette.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = source.SilhouetteColor };
        outputRenderer.sharedMaterial = material;
        runtimeMaterials.Add(material);

        if (source.GenerateCollider)
        {
            Mesh collisionMesh = BuildExtrudedCollisionMesh(localVertices, triangles, projectedObjectsRoot.InverseTransformDirection(paint2Camera.transform.forward));
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
        Vector3 positionOffset)
    {
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
            nearestDepth = Mathf.Min(nearestDepth, Vector3.Dot(corner - paint2Camera.transform.position, cameraForward));
        }

        float u = Mathf.InverseLerp(paintRect.xMin, paintRect.xMax, viewportPoint.x);
        float v = Mathf.InverseLerp(paintRect.yMin, paintRect.yMax, viewportPoint.y);
        float margin = (1f - outputViewportScale) * 0.5f;
        u = margin + u * outputViewportScale;
        v = margin + v * outputViewportScale;

        float centerDepth = Vector3.Dot(paintCenter - paint2Camera.transform.position, cameraForward);
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
        for (int i = projectedObjectsRoot.childCount - 1; i >= 0; i--)
            Destroy(projectedObjectsRoot.GetChild(i).gameObject);

        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
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

    private void OnDestroy()
    {
        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
                Destroy(material);
        }
    }
}
