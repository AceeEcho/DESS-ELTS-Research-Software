#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Elts.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    public sealed partial class OperatorDashboard
    {
        private string selectedCheckpoint="",selectedPreset="",removePresetArmed="";
        private DashboardCheckpoint? removedCheckpoint;
        private int removedCheckpointIndex;
        private readonly List<string> conditionOrder=new List<string>();
        private readonly Dictionary<string,Label> preflightValues=new Dictionary<string,Label>();
        private readonly Dictionary<string,Label> blockValues=new Dictionary<string,Label>();

        private static Button Handle(string description)
        {var handle=new Button {text="::",tooltip=description+". Use arrow keys to reorder."};handle.AddToClassList("drag-handle");return handle;}
        private DashboardReorder RegisterReorder(string key,VisualElement row,VisualElement list,ScrollView scroll,Button handle,Action changed)
        {
            var reorder=new DashboardReorder(row,list,scroll,surface,changed,()=>Workspace.ReducedMotion);
            handle.AddManipulator(reorder);reorders[key]=reorder;return reorder;
        }
        private void ClearReorders(string prefix)
        {foreach(string key in reorders.Keys.Where(key=>key.StartsWith(prefix,StringComparison.Ordinal)).ToList()){reorders[key].Dispose();reorders.Remove(key);}}
        private static string ConditionName(string code)
        {
            switch(code){case "WE_MT":return "With ELTS · moving target";case "NE_MT":return "Without ELTS · moving target";
                case "WE_FT":return "With ELTS · fixed target";case "NE_FT":return "Without ELTS · fixed target";default:return code;}
        }
        private static bool ValidOrder(string[] order)=>order.Length==4 && order.Distinct().Count()==4 && order.All(DashboardWorkspace.ConditionCodes.Contains);
        private string[] ReadOrder()=>Q<TextField>("conditionOrder").value.Split(',').Select(value=>value.Trim()).ToArray();
        private void InitializeOrders()
        {
            if(!System.IO.File.Exists(WorkspaceFile) && ValidOrder(ReadOrder()))Workspace.ActiveConditionOrder=ReadOrder().ToList();
            conditionOrder.AddRange(Workspace.ActiveConditionOrder);
            Q<TextField>("conditionOrder").SetValueWithoutNotify(String.Join(",",conditionOrder));
            if(!ValidOrder(conditionOrder.ToArray())){conditionOrder.Clear();conditionOrder.AddRange(DashboardWorkspace.ConditionCodes);}
            var list=Q<VisualElement>("conditionList");
            foreach(string code in DashboardWorkspace.ConditionCodes)
            {
                var row=new VisualElement {name="condition-"+code,userData=code};row.AddToClassList("order-row");
                var handle=Handle("Arrange "+ConditionName(code));row.Add(handle);
                var position=new Label();position.AddToClassList("order-position");row.Add(position);
                var content=new VisualElement();content.AddToClassList("row-content");
                var title=new Label(ConditionName(code));title.AddToClassList("row-title");content.Add(title);
                var meta=new Label(code+" · 05:00");meta.AddToClassList("row-meta");content.Add(meta);row.Add(content);list.Add(row);
                RegisterReorder("condition:"+code,row,list,Q<ScrollView>("stageScroll"),handle,()=> {
                    conditionOrder.Clear();conditionOrder.AddRange(list.Children().Select(item=>(string)item.userData));
                    Workspace.ActiveConditionOrder=conditionOrder.ToList();QueueSave();
                    Q<TextField>("conditionOrder").SetValueWithoutNotify(String.Join(",",conditionOrder));UpdateConditionPositions();RefreshBlocks(read());
                });
            }
            Q<TextField>("conditionOrder").RegisterValueChangedCallback(evt=> {
                var values=evt.newValue.Split(',').Select(value=>value.Trim()).ToArray();
                if(!ValidOrder(values)){Text("conditionError","Include each code exactly once: WE_MT, NE_MT, WE_FT, NE_FT. The last valid order remains visible.");Refresh(true);return;}
                Text("conditionError","");conditionOrder.Clear();conditionOrder.AddRange(values);Workspace.ActiveConditionOrder=conditionOrder.ToList();QueueSave();ArrangeConditionRows();Refresh(true);
            });
            ArrangeConditionRows();
            selectedPreset=Workspace.Presets[0].Id;RefreshPresets(false);
            Q<DropdownField>("orderPreset").RegisterValueChangedCallback(evt=> {
                var dropdown=Q<DropdownField>("orderPreset");int index=dropdown.choices.IndexOf(evt.newValue);
                if(index<0 || index>=Workspace.Presets.Count)return;
                var preset=Workspace.Presets[index];selectedPreset=preset.Id;
                Q<TextField>("presetName").SetValueWithoutNotify(preset.Name);
                if(read().PreparationEditable)Q<TextField>("conditionOrder").value=String.Join(",",preset.ConditionOrder);
                removePresetArmed="";Q<Button>("removePreset").text="Remove preset";
            });
            On("savePreset",()=>PresetAction(()=> {
                string name=Q<TextField>("presetName").value.Trim();if(name.Length==0)throw new InvalidOperationException("Enter a name for the preset.");
                if(!ValidOrder(ReadOrder()))throw new InvalidOperationException("Correct the condition order before saving a preset.");
                selectedPreset=Workspace.AddPreset(name,ReadOrder()).Id;RefreshPresets(false);Text("presetMessage","Preset saved locally.");
            }));
            On("updatePreset",()=>PresetAction(()=> {
                string name=Q<TextField>("presetName").value.Trim();if(name.Length==0)throw new InvalidOperationException("Enter a name for the preset.");
                Workspace.UpdatePreset(selectedPreset,name,ReadOrder());RefreshPresets(false);Text("presetMessage","Preset updated locally.");
            }));
            On("removePreset",()=> {
                if(removePresetArmed!=selectedPreset){removePresetArmed=selectedPreset;Q<Button>("removePreset").text="Confirm remove preset";Text("presetMessage","Press Confirm remove preset to remove this definition. The current condition order is retained.");return;}
                PresetAction(()=> {Workspace.RemovePreset(selectedPreset);selectedPreset=Workspace.Presets[0].Id;RefreshPresets(false);removePresetArmed="";Q<Button>("removePreset").text="Remove preset";Text("presetMessage","Preset removed. Current order retained.");});
            });
            BuildPreflight();BuildBlocks();
        }
        private void ArrangeConditionRows()
        {var list=Q<VisualElement>("conditionList");foreach(string code in conditionOrder)list.Add(Q<VisualElement>("condition-"+code));UpdateConditionPositions();}
        private void UpdateConditionPositions()
        {int index=1;foreach(var row in Q<VisualElement>("conditionList").Children())row.Q<Label>(className:"order-position").text=(index++).ToString("D2");}
        private void PresetAction(Action action)
        {try{if(!read().PreparationEditable)return;action();QueueSave();}catch(Exception exception){Text("presetMessage",exception.Message);}}
        private void RefreshPresets(bool apply)
        {
            var dropdown=Q<DropdownField>("orderPreset");dropdown.choices=Workspace.Presets.Select((preset,index)=>preset.Name+" · "+(index+1)).ToList();
            int at=Math.Max(0,Workspace.Presets.FindIndex(preset=>preset.Id==selectedPreset));selectedPreset=Workspace.Presets[at].Id;
            dropdown.SetValueWithoutNotify(dropdown.choices[at]);Q<TextField>("presetName").SetValueWithoutNotify(Workspace.Presets[at].Name);
            if(apply)Q<TextField>("conditionOrder").value=String.Join(",",Workspace.Presets[at].ConditionOrder);
        }
        private void SetPreparationEditable(bool editable)
        {
            foreach(string name in new[]{"conditionList","orderPreset","presetName","savePreset","updatePreset","removePreset"})Q<VisualElement>(name).SetEnabled(editable);
            // Readiness and busy locks remain owned by the session panel. This only
            // adds draft validation; it cannot bypass an engine precondition.
            if(!ValidOrder(ReadOrder()))Q<Button>("createSession").SetEnabled(false);
        }
        private void BuildPreflight()
        {
            string[] names={"Configuration","Synthetic tracking","Recording writer","Available disk","Displays and SteamVR"};
            foreach(string name in names)
            {
                var row=new VisualElement();row.AddToClassList("preflight-row");
                var title=new Label(name);title.AddToClassList("preflight-name");row.Add(title);
                var value=new Label();value.AddToClassList("preflight-value");row.Add(value);preflightValues[name]=value;Q<VisualElement>("preflightList").Add(row);
            }
            Text("deviceBindings","Head binding: "+view.Configuration.Rig.HeadSerial+"\nWeapon binding: "+view.Configuration.Rig.WeaponSerial+"\nBoth bindings identify synthetic inputs in this workspace.");
        }
        private void RefreshPreflight(DashboardSessionSnapshot state)
        {
            preflightValues["Configuration"].text="Loaded · synthetic";
            preflightValues["Synthetic tracking"].text=state.TrackingReady?"Observed · valid":state.State==SessionState.Idle?"Observing 2 seconds":"Waiting for recording";
            preflightValues["Recording writer"].text=state.WriterOpen?(state.WriterHealthy?"Active":"Needs attention"):"Not open";
            preflightValues["Available disk"].text=state.DiskFreeGb>0?state.DiskFreeGb.ToString("F1")+" GB free":"Checked on creation";
            preflightValues["Displays and SteamVR"].text="Emulated";
            preflightValues["Displays and SteamVR"].AddToClassList("pending");
            preflightValues["Synthetic tracking"].EnableInClassList("pending",!state.TrackingReady);
            preflightValues["Recording writer"].EnableInClassList("pending",!state.WriterHealthy);
        }
        private void BuildBlocks()
        {
            for(int index=0;index<4;index++)
            {
                var row=new VisualElement {name="block-row-"+index};row.AddToClassList("order-row");
                var number=new Label((index+1).ToString("D2"));number.AddToClassList("order-position");row.Add(number);
                var content=new VisualElement();content.AddToClassList("row-content");
                var title=new Label {name="block-name-"+index};title.AddToClassList("row-title");content.Add(title);
                var value=new Label();value.AddToClassList("row-meta");content.Add(value);blockValues[index.ToString()]=value;
                row.Add(content);Q<VisualElement>("blockTimeline").Add(row);
            }
        }
        private void RefreshBlocks(DashboardSessionSnapshot state)
        {
            for(int index=0;index<4;index++)
            {
                Text("block-name-"+index,ConditionName(conditionOrder[index]));
                string status="Queued · 05:00";
                if(state.State.HasValue)
                {
                    if(index<state.BlockIndex || state.State==SessionState.SessionComplete)status="Completed";
                    else if(index==state.BlockIndex && (state.State==SessionState.BlockRunning || state.State==SessionState.BlockReady || state.State==SessionState.BlockEnded || state.State==SessionState.Aborted || state.State==SessionState.Failed))
                        status=PhaseName(state.State)+(state.Running?" · "+ClockText(state.RemainingSeconds):"");
                }
                blockValues[index.ToString()].text=status;
                Q<VisualElement>("block-row-"+index).EnableInClassList("selected",index==state.BlockIndex && state.State.HasValue && state.State!=SessionState.SessionComplete);
            }
        }

        private void InitializeCheckpoints()
        {
            On("addCheckpoint",AddCheckpoint);On("removeCheckpoint",RemoveCheckpoint);On("undoCheckpoint",UndoCheckpoint);
            On("resetCheckpoints",()=> {foreach(var item in Workspace.Checkpoints)item.Completed=false;UpdateCheckpointLabels();if(selectedCheckpoint.Length>0)Q<Toggle>("checkpointComplete").SetValueWithoutNotify(false);QueueSave();Text("checkpointFeedback","Completion marks cleared. Checkpoint definitions retained.");});
            Q<TextField>("checkpointTitle").maxLength=300;Q<TextField>("checkpointInstructions").maxLength=4000;
            Q<TextField>("checkpointTitle").RegisterValueChangedCallback(evt=> {
                var item=Workspace.Checkpoints.FirstOrDefault(value=>value.Id==selectedCheckpoint);if(item==null)return;
                item.Title=evt.newValue;UpdateCheckpointLabels();QueueSave();
            });
            Q<TextField>("checkpointInstructions").RegisterValueChangedCallback(evt=> {
                var item=Workspace.Checkpoints.FirstOrDefault(value=>value.Id==selectedCheckpoint);if(item==null)return;
                item.Instructions=evt.newValue;QueueSave();
            });
            Q<Toggle>("checkpointComplete").RegisterValueChangedCallback(evt=> {
                var item=Workspace.Checkpoints.FirstOrDefault(value=>value.Id==selectedCheckpoint);if(item==null)return;
                item.Completed=evt.newValue;UpdateCheckpointLabels();QueueSave();
                Text("checkpointFeedback",evt.newValue?"Marked locally by administrator. This is not validation evidence.":"Completion mark cleared.");
            });
            RebuildCheckpointRows();
        }
        public void AddCheckpoint()
        {
            try {var item=Workspace.AddCheckpoint("New checkpoint","");RebuildCheckpointRows();SelectCheckpoint(item.Id);QueueSave();Q<TextField>("checkpointTitle").Focus();}
            catch(Exception exception){Text("checkpointFeedback",exception.Message);}
        }
        private void RebuildCheckpointRows()
        {
            ClearReorders("checkpoint:");var list=Q<VisualElement>("checkpointList");list.Clear();
            foreach(var item in Workspace.Checkpoints)
            {
                var row=new VisualElement {name="checkpoint-"+item.Id,userData=item.Id};row.AddToClassList("order-row");
                var handle=Handle("Arrange checkpoint");row.Add(handle);
                var select=new Button(()=>SelectCheckpoint(item.Id)){name="checkpoint-label-"+item.Id};select.AddToClassList("checkpoint-select");row.Add(select);list.Add(row);
                var reorder=RegisterReorder("checkpoint:"+item.Id,row,list,Q<ScrollView>("stageScroll"),handle,()=> {
                    Workspace.Checkpoints=list.Children().Select(child=>Workspace.Checkpoints.First(value=>value.Id==(string)child.userData)).ToList();QueueSave();
                });
            }
            UpdateCheckpointLabels();Q<Button>("undoCheckpoint").SetEnabled(removedCheckpoint!=null);
            if(selectedCheckpoint.Length>0 && Workspace.Checkpoints.All(item=>item.Id!=selectedCheckpoint))selectedCheckpoint="";
            Show(Q<VisualElement>("checkpointEditor"),selectedCheckpoint.Length>0);
        }
        private void UpdateCheckpointLabels()
        {
            Show(Q<Label>("checkpointEmpty"),Workspace.Checkpoints.Count==0);
            foreach(var item in Workspace.Checkpoints)
            {
                var label=Q<Button>("checkpoint-label-"+item.Id);
                label.text=(item.Completed?"Completed · ":"To do · ")+(String.IsNullOrWhiteSpace(item.Title)?"Unnamed checkpoint":item.Title);
                Q<VisualElement>("checkpoint-"+item.Id).EnableInClassList("selected",item.Id==selectedCheckpoint);
            }
            Refresh(true);
        }
        public void SelectCheckpoint(string id)
        {
            var item=Workspace.Checkpoints.First(value=>value.Id==id);selectedCheckpoint=id;
            Q<TextField>("checkpointTitle").SetValueWithoutNotify(item.Title);
            Q<TextField>("checkpointInstructions").SetValueWithoutNotify(item.Instructions);
            Q<Toggle>("checkpointComplete").SetValueWithoutNotify(item.Completed);
            Show(Q<VisualElement>("checkpointEditor"),true);UpdateCheckpointLabels();
        }
        public void RemoveCheckpoint()
        {
            int index=Workspace.Checkpoints.FindIndex(item=>item.Id==selectedCheckpoint);if(index<0)return;
            removedCheckpoint=Workspace.Checkpoints[index];removedCheckpointIndex=index;
            Workspace.RemoveCheckpoint(selectedCheckpoint);selectedCheckpoint="";RebuildCheckpointRows();
            if(Workspace.Checkpoints.Count>0)SelectCheckpoint(Workspace.Checkpoints[Math.Min(index,Workspace.Checkpoints.Count-1)].Id);
            else Q<Button>("addCheckpoint").Focus();
            Text("checkpointFeedback","Checkpoint removed. Undo removal restores it and its completion mark.");QueueSave();
        }
        public void UndoCheckpoint()
        {
            if(removedCheckpoint==null)return;
            try {var restored=removedCheckpoint;Workspace.RestoreCheckpoint(restored,removedCheckpointIndex);removedCheckpoint=null;RebuildCheckpointRows();SelectCheckpoint(restored.Id);Text("checkpointFeedback","Checkpoint restored.");QueueSave();}
            catch(Exception exception){Text("checkpointFeedback",exception.Message);}
        }

        private void InitializeLiveViews()
        {
            var list=Q<VisualElement>("liveList");foreach(string id in Workspace.LiveOrder)list.Add(Q<VisualElement>(id+"Card"));
            foreach(string id in Workspace.LiveOrder)
                RegisterReorder("live:"+id,Q<VisualElement>(id+"Card"),list,Q<ScrollView>("liveScroll"),Q<Button>(id+"Handle"),()=> {Workspace.LiveOrder=list.Children().Select(item=>item.name.Replace("Card","")).ToList();QueueSave();});
            orbitManipulator=new DashboardOrbit(view);
            Q<Image>("operatorImage").AddManipulator(orbitManipulator);
            On("resetOrbit",view.ResetOperatorView);
        }
    }
}
