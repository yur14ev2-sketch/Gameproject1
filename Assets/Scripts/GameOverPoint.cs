using UnityEngine;

[DisallowMultipleComponent]
public sealed class GameOverPoint : MonoBehaviour
{
    private const string GoalReachedMessage = "到达终点";
    private bool hasPlayerReachedGoal;

    private void OnTriggerEnter(Collider other)
    {
        if (hasPlayerReachedGoal)
            return;

        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();

        if (player == null)
            return;

        hasPlayerReachedGoal = true;
        Debug.Log(GoalReachedMessage, this);
    }
}
