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
            // This method can be called from the Unity Editor for testing purposes.
            // It simulates receiving a GLB URL and transitioning to the AR scene.
            string testUrl = "http://192.168.0.107:8000/storage/01KNTXC14JNYJ108J346JKQ03F.glb";
            ReceiveGlbUrl(testUrl);
        }
    }
}
