using System.Linq;
using UnityEditor;

namespace Elts.Operator.Editor
{
    /// <summary>Import only the sourced mannequin; other model imports are untouched.</summary>
    public sealed class DevelopmentMannequinImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.EndsWith("/ELTS/Mannequin/UAL1_Standard.fbx")) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.addCollider = false;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.EndsWith("/ELTS/Mannequin/UAL1_Standard.fbx")) return;
            var importer = (ModelImporter)assetImporter;
            // The in-place export preserves scenario-owned target positions.
            var names = new[] { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop",
                "Crouch_Idle_Loop", "Crouch_Fwd_Loop" };
            var clips = importer.defaultClipAnimations.Where(clip =>
                names.Contains(clip.name.Substring(clip.name.LastIndexOf('|') + 1))).ToArray();
            foreach (var clip in clips)
                clip.wrapMode = UnityEngine.WrapMode.Loop;
            importer.clipAnimations = clips;
        }

        // Reimport existing checkouts when the retained clip set changes.
        public override uint GetVersion() => 3;
    }
}
