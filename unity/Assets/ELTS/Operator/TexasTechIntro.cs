using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Presentation-only entry scene. Preloads the game without starting its session objects.</summary>
    public sealed class TexasTechIntro : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Settings
        {
            // Durations are seconds of real time, independent of the experiment clock.
            public float revealSeconds = 1.2f;
            public float settleSeconds = 1.1f;
            public float holdSeconds = 2f;
            public float fadeSeconds = .65f;
            public float gameFadeSeconds = .7f;
            public string gameScene = "ELTSDesktop";
        }

        public static bool IsShowing { get; private set; }
        public VisualElement Root { get; private set; }
        private PanelSettings panelSettings;
        private VisualElement artwork, accent;
        private Image logo, signature;
        private Label caption;
        private Settings settings;

        private void Awake()
        {
            IsShowing = true;
            DontDestroyOnLoad(gameObject);
            var configuration = Resources.Load<TextAsset>("ELTS/Branding/IntroSettings");
            settings = configuration == null ? new Settings() : JsonUtility.FromJson<Settings>(configuration.text);
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.sortingOrder = 1000;
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ELTS/SessionTheme");
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            Root = document.rootVisualElement;
            Root.name = "texasTechIntro";
            Root.style.position = Position.Absolute;
            Root.style.left = Root.style.right = Root.style.top = Root.style.bottom = 0;
            Root.style.backgroundColor = new Color(.98f, .98f, .97f);
            Root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            artwork = new VisualElement { name = "introArtwork" };
            artwork.style.position = Position.Absolute;
            artwork.style.left = artwork.style.right = artwork.style.top = artwork.style.bottom = 0;
            Root.Add(artwork);
            signature = AddImage("collegeWordmark", "WhitacreSignature");
            logo = AddImage("doubleT", "TexasTechDoubleT");
            accent = new VisualElement { name = "scarletRule" };
            accent.style.position = Position.Absolute;
            // Current Texas Tech scarlet; the official college signature stays unmodified.
            accent.style.backgroundColor = new Color(233f / 255f, 8f / 255f, 2f / 255f);
            artwork.Add(accent);
            caption = new Label("ELTS RESEARCH SOFTWARE") { name = "introCaption" };
            caption.style.position = Position.Absolute;
            caption.style.color = new Color(.38f, .38f, .38f);
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            caption.style.letterSpacing = 2.5f;
            artwork.Add(caption);
            Present(0);
        }

        private Image AddImage(string elementName, string resource)
        {
            var texture = Resources.Load<Texture2D>("ELTS/Branding/" + resource);
            if (texture == null) Debug.LogError("Missing intro branding asset: " + resource);
            var image = new Image { name = elementName, image = texture, scaleMode = ScaleMode.ScaleToFit };
            image.style.position = Position.Absolute;
            artwork.Add(image);
            return image;
        }

        private IEnumerator Start()
        {
            // Keep the participant scene inactive until the complete wordmark has been shown.
            var game = SceneManager.LoadSceneAsync(settings.gameScene);
            if (game == null) { Destroy(gameObject); yield break; }
            game.allowSceneActivation = false;
            float elapsed = 0;
            float duration = settings.revealSeconds + settings.settleSeconds + settings.holdSeconds + settings.fadeSeconds;
            while (elapsed < duration || game.progress < .9f)
            {
                Present(elapsed);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            artwork.style.opacity = 0;
            game.allowSceneActivation = true;
            yield return game;
            // RuntimeInitializeOnLoad runs only for the first scene. The desktop
            // station must also be initialized when arriving through this entry scene.
            DevelopmentTestStation.EnsureCreated();
            // Retain the opaque cover through activation to avoid a flash of the participant UI.
            elapsed = 0;
            while (elapsed < settings.gameFadeSeconds)
            {
                Root.style.opacity = 1 - Ease(elapsed / Mathf.Max(.01f, settings.gameFadeSeconds));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            Destroy(gameObject);
        }

        // Recalculate positions every frame so resizing during the intro keeps both marks proportional.
        public void Present(float seconds)
        {
            float width = Root.resolvedStyle.width, height = Root.resolvedStyle.height;
            if (!(width > 0) || !(height > 0)) { width = Screen.width; height = Screen.height; }
            float scale = Mathf.Min(width / 1280f, height / 720f);
            float reveal = Ease(seconds / Mathf.Max(.01f, settings.revealSeconds));
            float settle = Ease((seconds - settings.revealSeconds) / Mathf.Max(.01f, settings.settleSeconds));
            float fadeStart = settings.revealSeconds + settings.settleSeconds + settings.holdSeconds;
            artwork.style.opacity = 1 - Ease((seconds - fadeStart) / Mathf.Max(.01f, settings.fadeSeconds));
            float left = (width - 940 * scale) / 2;
            float top = height / 2 - 120 * scale;
            float signatureHeight = 940 * scale * signature.image.height / signature.image.width;
            Place(signature, left, top, 940 * scale, signatureHeight);
            float signatureOpacity = Ease((settle - .45f) / .55f);
            signature.style.opacity = signatureOpacity;
            // The destination matches the Double T within the official, unmodified college signature.
            Place(logo, Mathf.Lerp(width / 2 - 96 * scale, left, settle),
                Mathf.Lerp(height / 2 - 142 * scale + (1 - reveal) * 24 * scale, top, settle),
                Mathf.Lerp(192 * scale, signatureHeight * logo.image.width / logo.image.height, settle),
                Mathf.Lerp(224 * scale, signatureHeight, settle));
            logo.style.opacity = reveal * (1 - signatureOpacity);
            Place(accent, width / 2 - 120 * scale * reveal, height / 2 + 142 * scale,
                240 * scale * reveal, 2 * scale);
            Place(caption, 0, height / 2 + 166 * scale, width, 24 * scale);
            caption.style.fontSize = 12 * scale;
            caption.style.opacity = settle;
        }

        private static float Ease(float value)
        { value = Mathf.Clamp01(value); return value * value * (3 - 2 * value); }

        private static void Place(VisualElement element, float x, float y, float width, float height)
        { element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height; }

        private void OnDestroy()
        {
            IsShowing = false;
            if (panelSettings != null) Destroy(panelSettings);
        }
    }
}
