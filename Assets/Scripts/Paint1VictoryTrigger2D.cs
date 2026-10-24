using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class Paint1VictoryTrigger2D : MonoBehaviour
{
    [SerializeField] private Paint1NpcSequence2D sequence;
    [SerializeField] private Color gizmoColor = new Color(0f, 0.75f, 1f, 0.28f);

    public void Configure(Paint1NpcSequence2D targetSequence)
    {
        sequence = targetSequence;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PaintingPlayer2DController>() != null)
            sequence?.NotifyVictoryReached();
    }

    private void OnDrawGizmos()
    {
        BoxCollider area = GetComponent<BoxCollider>();
        if (area == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(area.center, area.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.95f);
        Gizmos.DrawWireCube(area.center, area.size);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
