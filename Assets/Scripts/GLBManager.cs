using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARProject.Scripts
{
    public class GLBManager : MonoBehaviour
    {
        public static GLBManager Instance { get; private set; }
        
        public string GlbUrl { get; private set; }
        
        [SerializeField]
        private string arSceneId = "1";

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// This method is called from Flutter to provide the GLB URL.
        /// Ensure the GameObject this is attached to is named "GLBManager" 
        /// so Flutter can find it with UnitySendMessage("GLBManager", "ReceiveGlbUrl", "https://url.glb").
        /// </summary>
        public void ReceiveGlbUrl(string url)
        {
            Debug.Log($"[GLBManager] Received GLB URL: {url}");
            GlbUrl = url;
            
            if (!string.IsNullOrEmpty(arSceneId))
            {
                SceneManager.LoadScene(arSceneId);
            }
            else
            {
                Debug.LogError("[GLBManager] AR Scene name not set.");
            }
        }

        public void TestGLBLoadingAndSceneTransition()
        {
            // You can use file:/// to test AssetBundles directly from your hard drive without uploading!
            string testUrl = "file:///d:/University_Final_Project/Smart_AR_Furniture_App/UnityAR/UnityAR/Assets/AssetBundles/chair";
            ReceiveGlbUrl(testUrl);
        }
    }
}
