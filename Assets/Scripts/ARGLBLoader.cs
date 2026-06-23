using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Networking;
using System.Linq;

namespace ARProject.Scripts
{
    public class ARGLBLoader : MonoBehaviour
    {
        [Header("AR References")]
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private ARCameraManager cameraManager;

        [Header("Placement Settings")]
        [SerializeField] private float cursorRaycastX = 0.5f;
        [SerializeField] private float cursorRaycastY = 0.5f;

        private GameObject loadedModel;
        private bool isDownloading = false;
        private bool hasPlacedModel = false;
        private bool isPreviewActive = false;

        private void Start()
        {
            if (raycastManager == null)
                raycastManager = GetComponent<ARRaycastManager>();

            if (raycastManager == null)
                Debug.LogError("[ARGLBLoader] ARRaycastManager is missing!");

            if (planeManager == null)
                planeManager = GetComponent<ARPlaneManager>();

            if (planeManager != null)
            {
                // Enable both horizontal and vertical plane detection (walls + floors)
                planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            }

            if (occlusionManager == null)
                occlusionManager = GetComponent<AROcclusionManager>();

            if (cameraManager == null)
                cameraManager = FindAnyObjectByType<ARCameraManager>();

            if (cameraManager != null)
            {
                cameraManager.frameReceived += OnCameraFrameReceived;
            }

            string url = GLBManager.Instance != null ? GLBManager.Instance.GlbUrl : "";
            if (!string.IsNullOrEmpty(url))
            {
                DownloadAndLoadModel(url);
            }
            else
            {
                Debug.LogWarning("[ARGLBLoader] No AssetBundle URL found in GLBManager. Please send it from Flutter.");
            }
        }

        private async void DownloadAndLoadModel(string url)
        {
            if (isDownloading) return;
            isDownloading = true;

            Debug.Log($"[ARGLBLoader] Starting download of AssetBundle: {url}");

            using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle(url))
            {
                var operation = uwr.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[ARGLBLoader] Failed to download AssetBundle: {uwr.error}");
                }
                else
                {
                    AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
                    if (bundle != null)
                    {
                        var prefabRequest = bundle.LoadAllAssetsAsync<GameObject>();
                        while (!prefabRequest.isDone)
                        {
                            await Task.Yield();
                        }

                        if (prefabRequest.allAssets.Length > 0)
                        {
                            GameObject prefabToInstantiate = null;
                            foreach (var asset in prefabRequest.allAssets)
                            {
                                Debug.Log($"[ARGLBLoader] Found asset in bundle: {asset.name} (Type: {asset.GetType()})");
                                if (asset is GameObject go)
                                {
                                    // Try to pick the prefab rather than the raw FBX if possible
                                    if (prefabToInstantiate == null || go.name.Contains("(Clone)") == false)
                                    {
                                        prefabToInstantiate = go;
                                    }
                                }
                            }

                            if (prefabToInstantiate != null)
                            {
                                loadedModel = Instantiate(prefabToInstantiate);
                                loadedModel.SetActive(false); // Keep inactive until placing
                                Debug.Log($"[ARGLBLoader] AssetBundle loaded and instantiated: {prefabToInstantiate.name}");
                            }
                            else
                            {
                                Debug.LogError("[ARGLBLoader] No GameObject found inside the downloaded AssetBundle.");
                            }
                        }
                        else
                        {
                            Debug.LogError("[ARGLBLoader] Downloaded AssetBundle is empty.");
                        }

                        // Unload the compressed bundle data from memory
                        bundle.Unload(false);
                    }
                    else
                    {
                        Debug.LogError("[ARGLBLoader] AssetBundle is null.");
                    }
                }
            }

            isDownloading = false;
        }

        private void Update()
        {
            if (loadedModel == null) return;
            
            // Prevent ARCore log spam/crashing if the session isn't fully initialized and tracking yet
            if (ARSession.state != ARSessionState.SessionTracking) return;

            // Preview placement at center of screen until user taps
            Vector2 screenCenter = Camera.main.ViewportToScreenPoint(new Vector2(cursorRaycastX, cursorRaycastY));
            List<ARRaycastHit> hits = new List<ARRaycastHit>();

            if (!hasPlacedModel)
            {
                if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
                {
                    Pose hitPose = hits[0].pose;
                    // show preview
                    loadedModel.SetActive(true);
                    isPreviewActive = true;
                    loadedModel.transform.position = hitPose.position;
                    
                    // If vertical (wall), align model to face outwards
                    if (planeManager != null)
                    {
                        var plane = planeManager.GetPlane(hits[0].trackableId);
                        if (plane != null && plane.alignment == PlaneAlignment.Vertical)
                        {
                            // align model to plane normal
                            Vector3 forward = -plane.normal;
                            loadedModel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                        }
                        else
                        {
                            loadedModel.transform.rotation = hitPose.rotation;
                        }
                    }
                    else
                    {
                        loadedModel.transform.rotation = hitPose.rotation;
                    }
                }
                else
                {
                    // Fallback floating preview if no AR plane is detected yet
                    loadedModel.SetActive(true);
                    isPreviewActive = true;
                    
                    // Place it 1.5 meters in front of the user
                    Vector3 fallbackPos = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
                    
                    // Smoothly follow the camera so it doesn't jump wildly
                    loadedModel.transform.position = Vector3.Lerp(loadedModel.transform.position, fallbackPos, Time.deltaTime * 5f);
                    
                    // Make the front of the model face the user
                    Vector3 lookPos = Camera.main.transform.position;
                    // Keep rotation purely horizontal (Y axis)
                    lookPos.y = loadedModel.transform.position.y; 
                    loadedModel.transform.LookAt(lookPos);
                }

                // On user touch, finalize placement (tap to place)
                if (Input.touchCount > 0)
                {
                    var touch = Input.GetTouch(0);
                    if (touch.phase == TouchPhase.Began)
                    {
                        List<ARRaycastHit> touchHits = new List<ARRaycastHit>();
                        if (raycastManager.Raycast(touch.position, touchHits, TrackableType.PlaneWithinPolygon))
                        {
                            Pose touchPose = touchHits[0].pose;
                            loadedModel.transform.position = touchPose.position;
                            loadedModel.transform.rotation = touchPose.rotation;
                            hasPlacedModel = true;
                            Debug.Log("[ARGLBLoader] Model placed by user.");

                            // Enable occlusion if available
                            if (occlusionManager != null)
                            {
                                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Medium;
                                occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
                                occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
                            }
                        }
                    }
                }
            }
            else
            {
                // Allow user to drag the placed model with a single touch
                if (Input.touchCount == 1)
                {
                    var touch = Input.GetTouch(0);
                    if (touch.phase == TouchPhase.Moved)
                    {
                        List<ARRaycastHit> dragHits = new List<ARRaycastHit>();
                        if (raycastManager.Raycast(touch.position, dragHits, TrackableType.PlaneWithinPolygon))
                        {
                            Pose dragPose = dragHits[0].pose;
                            loadedModel.transform.position = dragPose.position;
                        }
                    }
                }
            }
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            // Light estimation: apply intensity and color as quick reflections/lighting
            if (args.lightEstimation.mainLightIntensityLumens.HasValue)
            {
                float lumens = args.lightEstimation.mainLightIntensityLumens.Value;
                // Map lumens to ambient intensity
                RenderSettings.ambientIntensity = Mathf.Clamp01(lumens / 1000f);
            }

            if (args.lightEstimation.mainLightColor.HasValue)
            {
                RenderSettings.ambientLight = args.lightEstimation.mainLightColor.Value;
            }
        }

        private void OnDestroy()
        {
            if (cameraManager != null)
                cameraManager.frameReceived -= OnCameraFrameReceived;
        }

        private void FixMaterials(GameObject root)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            Shader standard = Shader.Find("Standard");

            foreach (var r in renderers)
            {
                r.receiveShadows = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    // If material shader is missing, assign URP/Lit or Standard
                    if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                    {
                        if (urpLit != null)
                            mat.shader = urpLit;
                        else if (standard != null)
                            mat.shader = standard;
                    }

                    // Enable reflection probes for better reflections
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                }
            }
        }
    }
}
