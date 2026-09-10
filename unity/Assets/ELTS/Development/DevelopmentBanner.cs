using UnityEngine;

namespace Elts.Development
{
    // Every development player visibly states its authority boundary, even in an empty scene.
    public sealed class DevelopmentBanner : MonoBehaviour
    {
        // The two-window station displays this identity in its administrator UI.
        public static bool ShowInPlayer { get; set; } = true;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var banner = new GameObject("ELTS development identity");
            DontDestroyOnLoad(banner);
            banner.AddComponent<DevelopmentBanner>();
        }
        private void OnGUI()
        {
            if(!ShowInPlayer)return;
            GUI.Box(new Rect(10, 10, Mathf.Max(300, Screen.width - 20), 55),
                "ELTS SYNTHETIC DEVELOPMENT — NOT FOR PARTICIPANT DATA\n" + Application.version);
        }
    }
}
