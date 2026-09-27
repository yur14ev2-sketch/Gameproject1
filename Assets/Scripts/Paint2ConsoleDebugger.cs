using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Paint2ConsoleDebugger : MonoBehaviour
{
    [SerializeField, Min(0.25f)] private float reportInterval = 2f;

    private float nextReportTime;
    private bool wasPaint2Active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeDebugger()
    {
        if (FindObjectOfType<Paint2ConsoleDebugger>(true) != null)
            return;

        GameObject debuggerObject = new GameObject("Paint2ConsoleDebugger_Runtime");
        debuggerObject.AddComponent<Paint2ConsoleDebugger>();
        Debug.Log("[Paint2Debug] Runtime debugger created automatically.");
    }

    private void Awake()
    {
        Debug.Log("[Paint2Debug] Debugger active. Waiting for Paint 2 mode.", this);
    }

    private void Update()
    {
        GameObject paint2System = FindSceneObject("2D_Playersystem_Paint2");
        bool isPaint2Active = paint2System != null && paint2System.activeInHierarchy;

        if (isPaint2Active && (!wasPaint2Active || Time.unscaledTime >= nextReportTime))
        {
            ReportState(paint2System);
            nextReportTime = Time.unscaledTime + reportInterval;
        }

        wasPaint2Active = isPaint2Active;
    }

    private static void ReportState(GameObject paint2System)
    {
        Camera paintCamera = paint2System.GetComponentInChildren<Camera>(true);
        GameObject paint2 = FindSceneObject("paint2");
        Transform targetCenter = paint2 != null ? paint2.transform.Find("PuzzleRoot/ProjectionArea") : null;
        Transform player = paint2System.transform.Find("Player_2D");

        var report = new StringBuilder();
        report.AppendLine("[Paint2Debug] ===== Runtime camera report =====");
        report.AppendLine($"Paint2 system active: {paint2System.activeInHierarchy}");
        report.AppendLine($"Enabled camera count: {Camera.allCamerasCount}");

        if (paintCamera == null)
        {
            report.AppendLine("ERROR: Paint2 camera was not found.");
            Debug.LogError(report.ToString());
            return;
        }

        report.AppendLine($"Camera path: {GetPath(paintCamera.transform)}");
        report.AppendLine($"Camera active/enabled: {paintCamera.gameObject.activeInHierarchy}/{paintCamera.enabled}");
        report.AppendLine($"Camera position: {paintCamera.transform.position}");
        report.AppendLine($"Camera rotation: {paintCamera.transform.eulerAngles}");
        report.AppendLine($"Camera forward: {paintCamera.transform.forward}");
        report.AppendLine($"Projection: {(paintCamera.orthographic ? "Orthographic" : "Perspective")}, size/FOV: {(paintCamera.orthographic ? paintCamera.orthographicSize : paintCamera.fieldOfView):F1}");
        report.AppendLine($"Culling mask: {paintCamera.cullingMask}");

        if (targetCenter != null)
        {
            Vector3 viewport = paintCamera.WorldToViewportPoint(targetCenter.position);
            report.AppendLine($"ProjectionArea position: {targetCenter.position}");
            report.AppendLine($"ProjectionArea viewport: {viewport} (visible when X/Y are 0..1 and Z > 0)");
        }
        else
        {
            report.AppendLine("ERROR: ProjectionArea was not found.");
        }

        if (player != null)
        {
            Vector3 viewport = paintCamera.WorldToViewportPoint(player.position);
            report.AppendLine($"Player_2D position: {player.position}");
            report.AppendLine($"Player_2D viewport: {viewport}");
        }

        int rendererCount = 0;
        int visibleRendererCount = 0;

        if (paint2 != null)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(paintCamera);
            foreach (Renderer renderer in paint2.GetComponentsInChildren<Renderer>(true))
            {
                rendererCount++;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                {
                    visibleRendererCount++;
                }
            }
        }

        report.AppendLine($"Paint2 renderers in camera frustum: {visibleRendererCount}/{rendererCount}");

        if (visibleRendererCount == 0)
            Debug.LogWarning(report.ToString());
        else
            Debug.Log(report.ToString());
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                return candidate.gameObject;
        }

        return null;
    }

    private static string GetPath(Transform target)
    {
        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }

        return path;
    }
}
