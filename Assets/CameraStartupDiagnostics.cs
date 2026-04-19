using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public static class CameraStartupDiagnostics
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureCameraReadiness()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
                Debug.Log("CameraStartupDiagnostics: Camera permission granted.");
            callbacks.PermissionDenied += _ =>
                Debug.LogError("CameraStartupDiagnostics: Camera permission denied.");
            callbacks.PermissionDeniedAndDontAskAgain += _ =>
                Debug.LogError("CameraStartupDiagnostics: Camera permission denied with 'Don't ask again'.");

            Permission.RequestUserPermission(Permission.Camera, callbacks);
            Debug.Log("CameraStartupDiagnostics: Requested camera permission.");
        }
#endif

        var host = new GameObject("CameraStartupDiagnosticsRunner");
        host.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<CameraStartupDiagnosticsRunner>();
    }

    private sealed class CameraStartupDiagnosticsRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
#if UNITY_EDITOR
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("CameraStartupDiagnostics: No webcam detected in Editor. Vuforia Play Mode camera feed will stay black.");
            }
            else
            {
                for (var i = 0; i < devices.Length; i++)
                {
                    Debug.Log($"CameraStartupDiagnostics: Webcam[{i}] = {devices[i].name}");
                }

                if (IsLikelyVirtualCamera(devices[0].name))
                {
                    Debug.LogWarning("CameraStartupDiagnostics: The first webcam looks virtual. Disable OBS Virtual Camera or set a physical camera as default.");
                }
            }
#endif

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("CameraStartupDiagnostics: Webcam access is not authorized.");
            }

            Destroy(gameObject);
        }

        private static bool IsLikelyVirtualCamera(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            return deviceName.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   deviceName.IndexOf("obs", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
