using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class ProjectionClipAreaHandle : MonoBehaviour
{
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 1f, 0.9f);

    private void OnDrawGizmos()
    {
        BoxCollider area = GetComponent<BoxCollider>();
        if (area == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(area.center, area.size);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
