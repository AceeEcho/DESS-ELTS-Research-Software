#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Shared motion constants in UI pixels and seconds, never apparatus units.</summary>
    public static class DashboardMotion
    {
        public const float ReflowSeconds = 0.18f;
        public const float EdgeBandPixels = 38;
        public const float EdgeScrollPixelsPerSecond = 320;
        public const float KeyboardResizePixels = 12;
    }

    /// <summary>
    /// Moves the actual row under the original grab point. A same-size placeholder
    /// previews the destination; Escape, pointer cancellation, or an outside drop
    /// restores the original order. Reordering never invokes the row's action.
    /// </summary>
    public sealed class DashboardReorder : PointerManipulator
    {
        private readonly VisualElement row, list, surface;
        private readonly ScrollView scroll;
        private readonly Action changed;
        private readonly Func<bool> reduceMotion;
        private VisualElement? placeholder;
        private int pointer = -1, originalIndex;
        private Vector2 grabOffset, lastPointer;
        private readonly Dictionary<VisualElement, Vector2> offsets = new Dictionary<VisualElement, Vector2>();
        private IVisualElementScheduledItem? ticker;
        private float lastTick;
        private bool restoring;
        private bool disposed;
        public bool IsDragging => placeholder != null;
        public void Dispose() { disposed=true;target?.RemoveManipulator(this);ticker?.Pause();foreach(var item in offsets.Keys)item.style.translate=new Translate(0,0,0);offsets.Clear(); }

        public DashboardReorder(VisualElement row, VisualElement list, ScrollView scroll,
            VisualElement surface, Action changed, Func<bool> reduceMotion)
        { this.row=row; this.list=list; this.scroll=scroll; this.surface=surface; this.changed=changed; this.reduceMotion=reduceMotion; }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(Down);
            target.RegisterCallback<KeyDownEvent>(Key);
            surface.RegisterCallback<PointerMoveEvent>(Move);
            surface.RegisterCallback<PointerUpEvent>(Up);
            surface.RegisterCallback<PointerCancelEvent>(Cancel);
            surface.RegisterCallback<PointerCaptureOutEvent>(Lost);
            surface.RegisterCallback<KeyDownEvent>(Escape, TrickleDown.TrickleDown);
        }
        protected override void UnregisterCallbacksFromTarget()
        {
            if(IsDragging) Finish(false);
            target.UnregisterCallback<PointerDownEvent>(Down);
            target.UnregisterCallback<KeyDownEvent>(Key);
            surface.UnregisterCallback<PointerMoveEvent>(Move);
            surface.UnregisterCallback<PointerUpEvent>(Up);
            surface.UnregisterCallback<PointerCancelEvent>(Cancel);
            surface.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            surface.UnregisterCallback<KeyDownEvent>(Escape, TrickleDown.TrickleDown);
        }
        private void Down(PointerDownEvent evt)
        {
            if(evt.button!=0 || !target.enabledInHierarchy || IsDragging) return;
            evt.StopImmediatePropagation();
            BeginDrag(evt.pointerId, evt.position);
        }
        // Public commands also allow deterministic runtime tests and accessibility adapters.
        public void BeginDrag(int pointerId, Vector2 position)
        {
            if(disposed || IsDragging || row.parent!=list) return;
            originalIndex=list.IndexOf(row); pointer=pointerId; lastPointer=position;
            var bounds=row.worldBound; grabOffset=position-bounds.position;
            placeholder=new VisualElement { name="dashboardDragPlaceholder" };
            placeholder.AddToClassList("drag-placeholder");
            placeholder.style.height=bounds.height;
            placeholder.style.marginTop=row.resolvedStyle.marginTop;
            placeholder.style.marginBottom=row.resolvedStyle.marginBottom;
            row.RemoveFromHierarchy(); list.Insert(originalIndex,placeholder);
            surface.Add(row); row.AddToClassList("dragging");
            // Geometry is intentionally inline while dragging; styling remains in USS.
            row.style.width=bounds.width; row.style.height=bounds.height;
            row.style.left=0; row.style.top=0; row.style.marginTop=0; row.style.marginBottom=0;
            surface.CapturePointer(pointer); surface.focusable=true; surface.Focus();
            PositionRow(); lastTick=Time.realtimeSinceStartup;
            ticker=surface.schedule.Execute(Tick).Every(16);
        }
        private void Move(PointerMoveEvent evt)
        { if(!IsDragging || evt.pointerId!=pointer)return; DragTo(evt.position);evt.StopPropagation(); }
        public void DragTo(Vector2 position)
        { if(!IsDragging)return;lastPointer=position;PositionRow();FindDestination(); }
        private void PositionRow()
        {
            var at=surface.WorldToLocal(lastPointer-grabOffset);
            row.style.translate=new Translate(at.x,at.y,0);
        }
        private void FindDestination()
        {
            if(placeholder==null || !scroll.worldBound.Contains(lastPointer))return;
            var candidates=list.Children().Where(item=>item!=placeholder).ToList();
            int insertion=0;
            // Use unanimated layout positions; moving neighbors cannot jitter the threshold.
            foreach(var item in candidates)
            {
                var baseY=list.LocalToWorld(item.layout.position).y;
                if(lastPointer.y > baseY+item.layout.height*0.5f)insertion++;
            }
            if(list.IndexOf(placeholder)==insertion)return;
            var before=candidates.ToDictionary(item=>item,item=>item.worldBound.position);
            placeholder.RemoveFromHierarchy();list.Insert(Math.Min(insertion,list.childCount),placeholder);
            surface.schedule.Execute(()=> {
                if(disposed || !IsDragging)return;
                foreach(var item in candidates)
                {
                    if(item.parent!=list)continue;
                    var unshifted=list.LocalToWorld(item.layout.position);
                    var delta=before[item]-unshifted;
                    offsets[item]=reduceMotion()?Vector2.zero:delta;
                    item.style.translate=new Translate(offsets[item].x,offsets[item].y,0);
                }
            }).ExecuteLater(1);
        }
        private void Tick()
        {
            float now=Time.realtimeSinceStartup,dt=Mathf.Min(0.05f,now-lastTick);lastTick=now;
            foreach(var item in offsets.Keys.ToList())
            {
                var delta=reduceMotion()?Vector2.zero:Vector2.Lerp(offsets[item],Vector2.zero,Mathf.Min(1,dt*6/DashboardMotion.ReflowSeconds));
                if(delta.sqrMagnitude<0.1f){delta=Vector2.zero;offsets.Remove(item);}else offsets[item]=delta;
                item.style.translate=new Translate(delta.x,delta.y,0);
            }
            if(!IsDragging){if(offsets.Count==0)ticker?.Pause();return;}
            var viewport=scroll.contentViewport.worldBound;
            float direction=lastPointer.y<viewport.yMin+DashboardMotion.EdgeBandPixels?-1:
                lastPointer.y>viewport.yMax-DashboardMotion.EdgeBandPixels?1:0;
            if(direction!=0 && lastPointer.x>=viewport.xMin && lastPointer.x<=viewport.xMax)
            {
                scroll.scrollOffset=new Vector2(scroll.scrollOffset.x,Mathf.Max(0,scroll.scrollOffset.y+direction*DashboardMotion.EdgeScrollPixelsPerSecond*dt));
                PositionRow();FindDestination();
            }
        }
        private void Up(PointerUpEvent evt)
        {if(!IsDragging || evt.pointerId!=pointer)return;Finish(scroll.worldBound.Contains(evt.position));evt.StopPropagation();}
        private void Cancel(PointerCancelEvent evt){if(IsDragging && evt.pointerId==pointer)Finish(false);}
        private void Lost(PointerCaptureOutEvent evt){if(IsDragging && !restoring && evt.pointerId==pointer)Finish(false);}
        private void Escape(KeyDownEvent evt){if(IsDragging && evt.keyCode==KeyCode.Escape){Finish(false);evt.StopImmediatePropagation();}}
        public void Finish(bool commit)
        {
            if(placeholder==null)return;
            int proposed=commit?list.IndexOf(placeholder):originalIndex;
            int destination=Math.Max(0,proposed<0?originalIndex:proposed);
            restoring=true;surface.ReleasePointer(pointer);pointer=-1;
            placeholder.RemoveFromHierarchy();placeholder=null;
            row.RemoveFromClassList("dragging");
            row.style.width=StyleKeyword.Null;row.style.height=StyleKeyword.Null;
            row.style.left=StyleKeyword.Null;row.style.top=StyleKeyword.Null;
            row.style.marginTop=StyleKeyword.Null;row.style.marginBottom=StyleKeyword.Null;
            row.style.translate=new Translate(0,0,0);
            row.RemoveFromHierarchy();list.Insert(Math.Min(destination,list.childCount),row);
            target.Focus();restoring=false;
            if(commit && destination!=originalIndex)changed();
        }
        private void Key(KeyDownEvent evt)
        {
            if(evt.keyCode==KeyCode.UpArrow || evt.keyCode==KeyCode.DownArrow)
            {MoveBy(evt.keyCode==KeyCode.UpArrow?-1:1);evt.StopImmediatePropagation();}
        }
        public void MoveBy(int delta)
        {
            if(IsDragging || row.parent!=list || !target.enabledInHierarchy)return;
            int old=list.IndexOf(row),at=Mathf.Clamp(old+delta,0,list.childCount-1);
            if(old==at)return;
            row.RemoveFromHierarchy();list.Insert(at,row);target.Focus();scroll.ScrollTo(row);changed();
        }
    }

    /// <summary>Resizing retains user preferences; fitting a smaller window never overwrites them.</summary>
    public sealed class DashboardResize : PointerManipulator
    {
        private readonly Func<float> read;
        private readonly Action<float> write;
        private readonly Action done;
        private readonly float direction;
        private int pointer=-1;
        private float origin,start;
        public DashboardResize(Func<float> read,Action<float> write,Action done,float direction)
        {this.read=read;this.write=write;this.done=done;this.direction=direction;}
        public void Dispose(){End(false);target?.RemoveManipulator(this);}
        protected override void RegisterCallbacksOnTarget()
        {target.RegisterCallback<PointerDownEvent>(Down);target.RegisterCallback<PointerMoveEvent>(Move);target.RegisterCallback<PointerUpEvent>(Up);target.RegisterCallback<KeyDownEvent>(Key);target.RegisterCallback<PointerCancelEvent>(Cancel);target.RegisterCallback<PointerCaptureOutEvent>(Lost);}
        protected override void UnregisterCallbacksFromTarget()
        {target.UnregisterCallback<PointerDownEvent>(Down);target.UnregisterCallback<PointerMoveEvent>(Move);target.UnregisterCallback<PointerUpEvent>(Up);target.UnregisterCallback<KeyDownEvent>(Key);target.UnregisterCallback<PointerCancelEvent>(Cancel);target.UnregisterCallback<PointerCaptureOutEvent>(Lost);}
        private void Down(PointerDownEvent evt){if(evt.button!=0)return;pointer=evt.pointerId;origin=evt.position.x;start=read();target.CapturePointer(pointer);target.Focus();evt.StopPropagation();}
        private void Move(PointerMoveEvent evt){if(pointer!=evt.pointerId)return;write(start+(evt.position.x-origin)*direction);evt.StopPropagation();}
        private void Up(PointerUpEvent evt){if(pointer!=evt.pointerId)return;End(true);evt.StopPropagation();}
        private void Cancel(PointerCancelEvent evt){if(evt.pointerId==pointer)End(false);}
        private void Lost(PointerCaptureOutEvent evt){if(evt.pointerId==pointer)End(false);}
        private void End(bool commit)
        {if(pointer<0)return;int held=pointer;pointer=-1;target.ReleasePointer(held);if(commit)done();else write(start);}
        private void Key(KeyDownEvent evt)
        {
            if(evt.keyCode==KeyCode.Escape && pointer>=0){End(false);evt.StopPropagation();return;}
            if(evt.keyCode==KeyCode.LeftArrow || evt.keyCode==KeyCode.RightArrow)
            {write(read()+(evt.keyCode==KeyCode.LeftArrow?-1:1)*DashboardMotion.KeyboardResizePixels*direction);done();evt.StopPropagation();}
        }
    }

    /// <summary>Disposable orbit/pan/zoom input; the rendering camera remains owned by DevelopmentView.</summary>
    public sealed class DashboardOrbit : PointerManipulator
    {
        private readonly DevelopmentView view;
        private int pointer=-1;
        private Vector2 previous;
        public DashboardOrbit(DevelopmentView view){this.view=view;}
        public void Dispose(){Release();target?.RemoveManipulator(this);}
        protected override void RegisterCallbacksOnTarget()
        {target.RegisterCallback<PointerDownEvent>(Down);target.RegisterCallback<PointerMoveEvent>(Move);target.RegisterCallback<PointerUpEvent>(Up);target.RegisterCallback<PointerCaptureOutEvent>(Lost);target.RegisterCallback<PointerCancelEvent>(Cancel);target.RegisterCallback<WheelEvent>(Wheel);target.RegisterCallback<KeyDownEvent>(Key);}
        protected override void UnregisterCallbacksFromTarget()
        {target.UnregisterCallback<PointerDownEvent>(Down);target.UnregisterCallback<PointerMoveEvent>(Move);target.UnregisterCallback<PointerUpEvent>(Up);target.UnregisterCallback<PointerCaptureOutEvent>(Lost);target.UnregisterCallback<PointerCancelEvent>(Cancel);target.UnregisterCallback<WheelEvent>(Wheel);target.UnregisterCallback<KeyDownEvent>(Key);}
        private void Down(PointerDownEvent evt){if(evt.button!=0)return;pointer=evt.pointerId;previous=evt.position;target.CapturePointer(pointer);target.Focus();evt.StopPropagation();}
        private void Move(PointerMoveEvent evt){if(evt.pointerId!=pointer)return;Vector2 at=evt.position;view.AdjustOperatorView(at-previous,evt.shiftKey,0);previous=at;evt.StopPropagation();}
        private void Up(PointerUpEvent evt){if(evt.pointerId!=pointer)return;Release();evt.StopPropagation();}
        private void Release(){if(pointer<0)return;int held=pointer;pointer=-1;target.ReleasePointer(held);}
        private void Lost(PointerCaptureOutEvent evt){if(evt.pointerId==pointer)pointer=-1;}
        private void Cancel(PointerCancelEvent evt){if(evt.pointerId==pointer)Release();}
        private void Wheel(WheelEvent evt){view.AdjustOperatorView(Vector2.zero,false,evt.delta.y);evt.StopPropagation();}
        private void Key(KeyDownEvent evt)
        {
            Vector2 delta=evt.keyCode==KeyCode.LeftArrow?new Vector2(-12,0):evt.keyCode==KeyCode.RightArrow?new Vector2(12,0):evt.keyCode==KeyCode.UpArrow?new Vector2(0,-12):evt.keyCode==KeyCode.DownArrow?new Vector2(0,12):Vector2.zero;
            float zoom=evt.keyCode==KeyCode.Equals?-1:evt.keyCode==KeyCode.Minus?1:0;
            if(delta!=Vector2.zero || zoom!=0){view.AdjustOperatorView(delta,evt.shiftKey,zoom);evt.StopPropagation();}
        }
    }
}
