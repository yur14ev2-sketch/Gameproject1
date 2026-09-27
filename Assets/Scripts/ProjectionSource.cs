using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProjectionSource : MonoBehaviour
{
    [Header("Replaceable Art")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Transform projectionAnchor;

    [Header("Projection Options")]
    [SerializeField] private bool projectionEnabled = true;
    [SerializeField] private Color silhouetteColor = new Color(0.08f, 0.08f, 0.08f, 1f);
    [SerializeField] private bool generateCollider = true;
    [SerializeField] private bool walkable = true;
    [SerializeField] private Vector3 positionOffset;
    [SerializeField] private Vector3 scaleMultiplier = Vector3.one;
    [SerializeField] private int priority;

    public Transform ModelRoot => modelRoot != null ? modelRoot : transform;
    public Transform ProjectionAnchor => projectionAnchor != null ? projectionAnchor : transform;
    public bool ProjectionEnabled => projectionEnabled;
    public Color SilhouetteColor => silhouetteColor;
    public bool GenerateCollider => generateCollider;
    public bool Walkable => walkable;
    public Vector3 PositionOffset => positionOffset;
    public Vector3 ScaleMultiplier => scaleMultiplier;
    public int Priority => priority;

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        Renderer[] renderers = ModelRoot.GetComponentsInChildren<Renderer>(true);
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
}
