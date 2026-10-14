using UnityEngine;

/// <summary>
/// Makes 3D markers and billboard labels always face the active AR Camera.
/// </summary>
[ExecuteAlways]
public class BillboardLookAt : MonoBehaviour
{
    private Camera targetCamera;
    [SerializeField] private bool lockYAxis = false;

    private void Start()
    {
        FindTargetCamera();
    }

    private void FindTargetCamera()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
        {
            targetCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
            return;
        }
#endif
        targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
        {
            targetCamera = UnityEditor.SceneView.lastActiveSceneView.camera;
        }
        else if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
#else
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
#endif
        if (targetCamera == null) return;

        if (lockYAxis)
        {
            Vector3 lookPos = targetCamera.transform.position;
            lookPos.y = transform.position.y;
            transform.LookAt(lookPos);
            transform.Rotate(0, 180, 0);
        }
        else
        {
            transform.LookAt(transform.position + targetCamera.transform.rotation * Vector3.forward,
                             targetCamera.transform.rotation * Vector3.up);
        }
    }
}
