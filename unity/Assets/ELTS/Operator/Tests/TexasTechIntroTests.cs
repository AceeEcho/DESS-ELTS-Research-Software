using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class TexasTechIntroTests
    {
        [UnityTest]
        public IEnumerator IntroShowsOfficialWordmarkThenOpensGameEvenWithPausedTime()
        {
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                yield return SceneManager.LoadSceneAsync("TexasTechIntro");
                Assert.That(TexasTechIntro.IsShowing, Is.True);
                var intro = UnityEngine.Object.FindFirstObjectByType<TexasTechIntro>();
                Assert.That(intro, Is.Not.Null);
                var logo = intro.Root.Q<Image>("doubleT");
                var wordmark = intro.Root.Q<Image>("collegeWordmark");
                Assert.That(logo.image, Is.Not.Null);
                Assert.That(wordmark.image, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + 3;
                while (Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("TexasTechIntro"),
                    "Game objects must not start before the final branding hold.");
                Assert.That(wordmark.resolvedStyle.opacity, Is.EqualTo(1).Within(.01));
                Assert.That(logo.resolvedStyle.opacity, Is.EqualTo(0).Within(.01));
                Assert.That(wordmark.worldBound.width / wordmark.worldBound.height,
                    Is.EqualTo((float)wordmark.image.width / wordmark.image.height).Within(.03), "Preserve official artwork proportions, allowing pixel rounding.");
                string captures = Environment.GetEnvironmentVariable("ELTS_INTRO_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(captures))
                {
                    Directory.CreateDirectory(captures);
                    // A panel render target also works in batch mode, where screen capture is unavailable.
                    var panel = intro.GetComponent<UIDocument>().panelSettings;
                    var target = new RenderTexture(1280, 720, 0);
                    target.Create();
                    panel.targetTexture = target;
                    for (int frame = 0; frame < 5; frame++) yield return null;
                    var previousTarget = RenderTexture.active;
                    var pixels = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
                    try
                    {
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                        pixels.Apply();
                        File.WriteAllBytes(Path.Combine(captures, "intro-wordmark.png"), pixels.EncodeToPNG());
                    }
                    finally
                    {
                        RenderTexture.active = previousTarget;
                        panel.targetTexture = null;
                        UnityEngine.Object.Destroy(pixels);
                        UnityEngine.Object.Destroy(target);
                    }
                }
                deadline = Time.realtimeSinceStartup + 15;
                while (TexasTechIntro.IsShowing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(TexasTechIntro.IsShowing, Is.False, "Intro must release the participant input gate.");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("ELTSDesktop"));
                Assert.That(UnityEngine.Object.FindFirstObjectByType<TexasTechIntro>(), Is.Null);
            }
            finally { Time.timeScale = previousTimeScale; }
        }
    }
}
