using UnityEngine;

public sealed class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.2f;

    private Vector3 followOffset;
    private float fixedZ;
    private Quaternion fixedRotation;
    private float velocityX;
    private float velocityY;

    private void Awake()
    {
        if (target == null)
        {
            PlayerMovement player = FindObjectOfType<PlayerMovement>();

            if (player != null)
                target = player.transform;
        }

        fixedZ = transform.position.z;
        fixedRotation = transform.rotation;

        if (target != null)
            followOffset = transform.position - target.position;
        else
        {
            Debug.LogError("Camera Follow could not find the player target.", this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        Vector3 desiredPosition = target.position + followOffset;
        Vector3 nextPosition = transform.position;

        nextPosition.x = Mathf.SmoothDamp(
            nextPosition.x,
            desiredPosition.x,
            ref velocityX,
            smoothTime);
        nextPosition.y = Mathf.SmoothDamp(
            nextPosition.y,
            desiredPosition.y,
            ref velocityY,
            smoothTime);
        nextPosition.z = fixedZ;

        transform.SetPositionAndRotation(nextPosition, fixedRotation);
    }
}
