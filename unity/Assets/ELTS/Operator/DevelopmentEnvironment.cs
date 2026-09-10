#nullable enable
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>
    /// Marks the synthetic desktop station that is serialized into ELTSDesktop.
    ///
    /// The children are explanatory, unmeasured development geometry. They make the
    /// scene understandable before Play mode but are not a calibrated representation
    /// of the physical apparatus. Dynamic poses, rays and targets remain owned by
    /// <see cref="DevelopmentView"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DevelopmentEnvironment : MonoBehaviour
    {
        [Tooltip("When true, DevelopmentView does not create a second floor/grid scaffold at runtime.")]
        [SerializeField] private bool authoredScaffold = true;

        [Tooltip("Persistent configured display outline authored by DesktopSceneSetup.")]
        [SerializeField] private Transform? displayReference;

        [Tooltip("Persistent synthetic floor grid authored by DesktopSceneSetup.")]
        [SerializeField] private Transform? floorReference;

        [Tooltip("Pose and target previews exist only to explain the saved scene before Play mode.")]
        [SerializeField] private Transform? editPreviewReference;

        [Tooltip("Saved cameras are configured by DevelopmentView when Play mode begins.")]
        [SerializeField] private Camera? participantCamera;
        [SerializeField] private Camera? operatorCamera;

        public bool HasAuthoredScaffold => authoredScaffold && displayReference != null && floorReference != null;
        public Transform? DisplayReference => displayReference;
        public Transform? FloorReference => floorReference;
        public Transform? EditPreviewReference => editPreviewReference;
        public bool HasAuthoredCameras => participantCamera != null && operatorCamera != null;
        public Camera? ParticipantCamera => participantCamera;
        public Camera? OperatorCamera => operatorCamera;

        /// <summary>Called only by the Editor authoring command after it creates the named children.</summary>
        public void SetAuthoredReferences(Transform display, Transform floor)
        {
            displayReference = display;
            floorReference = floor;
            authoredScaffold = true;
        }

        /// <summary>Records the editor-only preview group so runtime can hide it without affecting the station.</summary>
        public void SetEditPreviewReference(Transform preview) => editPreviewReference = preview;

        /// <summary>Dynamic runtime overlays replace these static explanatory previews.</summary>
        public void HideEditPreviewsForRuntime()
        {
            if (editPreviewReference != null) editPreviewReference.gameObject.SetActive(false);
        }

        /// <summary>Called only by the Editor authoring command for saved Edit-mode cameras.</summary>
        public void SetAuthoredCameras(Camera participant, Camera outsideView)
        {
            participantCamera = participant;
            operatorCamera = outsideView;
        }
    }

}
