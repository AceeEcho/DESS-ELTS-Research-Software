#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class OperatorDashboardTests
    {
        private static void Submit(Button button)
        {using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}

        [UnityTest]
        public IEnumerator WorkbenchPreservesLiveViewsDraftsAndLocalWorkspace()
        {
            var host=new GameObject("Dashboard workspace verification");
            var view=host.AddComponent<DevelopmentView>();
            var panel=host.AddComponent<DevelopmentSessionPanel>();
            var target=new RenderTexture(1440,960,24,RenderTextureFormat.ARGB32);target.Create();panel.SetDiagnosticRenderTarget(target);
            try
            {
                for(int i=0;i<6;i++)yield return null;
                var root=panel.PanelRoot;var dashboard=panel.Dashboard;
                dashboard.SelectModule("preparation");
                Assert.That(root.Q<VisualElement>("workspaceRail").worldBound.xMax,Is.LessThanOrEqualTo(root.Q<VisualElement>("focusStage").worldBound.xMin));
                Assert.That(root.Q<VisualElement>("focusStage").worldBound.xMax,Is.LessThanOrEqualTo(root.Q<VisualElement>("livePanel").worldBound.xMin));
                Assert.That(root.Q<Image>("participantImage").image,Is.Not.Null);
                Assert.That(view.ParticipantCamera.targetTexture,Is.Not.Null);
                Assert.That((double)view.ParticipantCamera.targetTexture.width/view.ParticipantCamera.targetTexture.height,
                    Is.EqualTo(view.DisplayPlane.Width/view.DisplayPlane.Height).Within(0.01));
                Assert.That(panel.Engine,Is.Null,"Arranging a workspace must not create a recording");
                Capture(target,"dashboard-preparation.png");

                dashboard.SelectModule("checkpoints");
                Submit(root.Q<Button>("addCheckpoint"));
                var title=root.Q<TextField>("checkpointTitle");
                var instructions=root.Q<TextField>("checkpointInstructions");
                string longTitle="Confirm the test administrator can read the complete preparation checkpoint for the equipment handoff without losing the end of the sentence";
                title.value=longTitle;instructions.value="Keep the test identifier with the completed recording.\nReview the handoff with the next administrator.";
                root.Q<Toggle>("checkpointComplete").value=true;
                string id=dashboard.Workspace.Checkpoints[0].Id;
                var rowLabel=root.Q<Button>("checkpoint-label-"+id);
                title.Focus();yield return null;
                var focused=root.panel.focusController.focusedElement;
                title.value=longTitle+".";yield return null;
                Assert.That(root.panel.focusController.focusedElement,Is.SameAs(focused),"Editing must preserve input focus");
                Assert.That(root.Q<Button>("checkpoint-label-"+id),Is.SameAs(rowLabel),"Editing must update the existing row");
                Assert.That(rowLabel.text,Does.Contain(longTitle));
                Assert.That(rowLabel.resolvedStyle.whiteSpace,Is.EqualTo(WhiteSpace.Normal));
                dashboard.SelectModule("notes");root.Q<TextField>("note").value="Unsent administrator observation";
                dashboard.SelectModule("checkpoints");dashboard.SelectModule("notes");
                Assert.That(root.Q<TextField>("note").value,Is.EqualTo("Unsent administrator observation"));
                dashboard.SelectModule("checkpoints");Submit(root.Q<Button>("removeCheckpoint"));
                Assert.That(dashboard.Workspace.Checkpoints,Is.Empty);
                Submit(root.Q<Button>("undoCheckpoint"));
                Assert.That(dashboard.Workspace.Checkpoints.Single().Id,Is.EqualTo(id));
                Assert.That(dashboard.Workspace.Checkpoints.Single().Completed,Is.True);
                root.Q<Toggle>("reducedMotion").value=true;
                Assert.That(root.Q<VisualElement>("sessionPanel").ClassListContains("reduced-motion"),Is.True);
                dashboard.FlushWorkspace();
                var saved=DashboardWorkspace.Load(dashboard.WorkspaceFile);
                Assert.That(saved.Checkpoints.Single().Title,Is.EqualTo(longTitle+"."));
                Assert.That(saved.ReducedMotion,Is.True);
                Assert.That(saved.SelectedModule,Is.EqualTo("checkpoints"));
                for(int i=0;i<3;i++)yield return null;
                Capture(target,"dashboard-checkpoints.png");

                panel.SetVisible(false);Assert.That(view.ParticipantCamera.targetTexture,Is.Null);
                panel.SetVisible(true);Assert.That(view.ParticipantCamera.targetTexture,Is.Not.Null);
            }
            finally{UnityEngine.Object.DestroyImmediate(host);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }

        [UnityTest]
        public IEnumerator ReorderPreviewsFootprintCancelsAndPersistsWithReducedMotion()
        {
            var host=new GameObject("Dashboard reorder verification");host.AddComponent<DevelopmentView>();
            var panel=host.AddComponent<DevelopmentSessionPanel>();
            var target=new RenderTexture(1440,960,24,RenderTextureFormat.ARGB32);target.Create();panel.SetDiagnosticRenderTarget(target);
            try
            {
                for(int i=0;i<5;i++)yield return null;
                var root=panel.PanelRoot;var dashboard=panel.Dashboard;
                dashboard.SelectModule("preparation");
                var list=root.Q<VisualElement>("conditionList");
                var source=root.Q<VisualElement>("condition-WE_MT");var destination=root.Q<VisualElement>("condition-NE_FT");
                string[] before=dashboard.Workspace.Presets[0].ConditionOrder.ToArray();
                var reorder=dashboard.Reorders["condition:WE_MT"];
                float rowHeight=source.worldBound.height;
                Vector2 grab=source.worldBound.position+new Vector2(15,12);
                Vector2 drop=destination.worldBound.center;
                reorder.BeginDrag(PointerId.mousePointerId,grab);yield return null;
                Assert.That(source.parent,Is.SameAs(root.Q<VisualElement>("sessionPanel")));
                Assert.That(root.Q<VisualElement>("dashboardDragPlaceholder").resolvedStyle.height,Is.EqualTo(rowHeight).Within(1));
                reorder.DragTo(drop);yield return null;
                Assert.That(source.worldBound.position.x,Is.EqualTo(drop.x-15).Within(2));
                reorder.Finish(false);yield return null;
                Assert.That(list.Children().Select(item=>(string)item.userData).ToArray(),Is.EqualTo(before),"Cancellation restores the original order");
                Assert.That(root.Q<VisualElement>("dashboardDragPlaceholder"),Is.Null);
                var second=root.Q<VisualElement>("condition-NE_MT");
                reorder.BeginDrag(PointerId.mousePointerId,source.worldBound.position+new Vector2(15,12));yield return null;
                reorder.DragTo(new Vector2(second.worldBound.center.x,second.worldBound.yMax-3));yield return null;
                reorder.Finish(true);yield return null;
                Assert.That(root.Q<TextField>("conditionOrder").value,Is.EqualTo("NE_MT,WE_MT,WE_FT,NE_FT"),"Pointer drop commits the previewed order");
                reorder.MoveBy(-1);
                root.Q<Toggle>("reducedMotion").value=true;
                reorder.MoveBy(1);yield return null;
                Assert.That(root.Q<TextField>("conditionOrder").value,Is.EqualTo("NE_MT,WE_MT,WE_FT,NE_FT"));
                root.Q<TextField>("presetName").value="Administrator test order";Submit(root.Q<Button>("savePreset"));
                dashboard.Reorders["module:notes"].MoveBy(-1);
                dashboard.FlushWorkspace();
                var saved=DashboardWorkspace.Load(dashboard.WorkspaceFile);
                Assert.That(saved.Presets.Last().ConditionOrder,Is.EqualTo(new[]{"NE_MT","WE_MT","WE_FT","NE_FT"}));
                Assert.That(saved.ModuleOrder.IndexOf("notes"),Is.EqualTo(2));
                Assert.That(panel.Engine,Is.Null);

                dashboard.SelectModule("checkpoints");
                for(int i=0;i<22;i++){dashboard.AddCheckpoint();root.Q<TextField>("checkpointTitle").value=i%2==0?"Short checkpoint "+i:"Long administrator checkpoint "+i+" with a full explanation that must wrap instead of truncating";}
                var scroll=root.Q<ScrollView>("stageScroll");scroll.scrollOffset=Vector2.zero;
                for(int i=0;i<3;i++)yield return null;
                string firstId=dashboard.Workspace.Checkpoints[0].Id;
                var first=root.Q<VisualElement>("checkpoint-"+firstId);
                var longDrag=dashboard.Reorders["checkpoint:"+firstId];
                longDrag.BeginDrag(PointerId.mousePointerId,first.worldBound.center);yield return null;
                var viewport=scroll.contentViewport.worldBound;
                longDrag.DragTo(new Vector2(viewport.center.x,viewport.yMax-8));
                float started=scroll.scrollOffset.y;
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.That(scroll.scrollOffset.y,Is.GreaterThan(started),"Holding near the edge scrolls without further pointer motion");
                longDrag.Finish(false);
                Assert.That(dashboard.Workspace.Checkpoints[0].Id,Is.EqualTo(firstId));
            }
            finally{UnityEngine.Object.DestroyImmediate(host);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void Capture(RenderTexture target,string name)
        {
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics"));Directory.CreateDirectory(directory);
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(Path.Combine(directory,name),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
