#nullable enable
using UnityEngine;
using System;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>One observed input frame. Pointer coordinates use UI panel pixels.</summary>
    public readonly struct DesktopControlFrame
    {
        public readonly bool Focused, Pressed, Released, ReturnToAdministrator, Reset;
        public readonly Vector2 Pointer;
        public readonly Vector3 Movement;
        public DesktopControlFrame(bool focused, Vector2 pointer, Vector3 movement,
            bool pressed=false, bool released=false, bool returnToAdministrator=false, bool reset=false)
        { Focused=focused;Pointer=pointer;Movement=movement;Pressed=pressed;Released=released;
          ReturnToAdministrator=returnToAdministrator;Reset=reset; }
    }

    /// <summary>Device input is replaceable for deterministic pipeline tests.</summary>
    public interface IDesktopControls { DesktopControlFrame Read(IPanel panel); }

    public sealed class MouseKeyboardControls : IDesktopControls
    {
        private readonly Func<bool> focused;
        public MouseKeyboardControls(Func<bool>? focused=null) { this.focused=focused ?? (()=>Application.isFocused); }
        public DesktopControlFrame Read(IPanel panel)
        {
            var mouse=Mouse.current;var keyboard=Keyboard.current;
            if (!focused() || mouse==null || keyboard==null)
                return new DesktopControlFrame(false,Vector2.zero,Vector3.zero);
            Vector2 screen=mouse.position.ReadValue();screen.y=Screen.height-screen.y;
            Vector2 pointer=RuntimePanelUtils.ScreenToPanel(panel,screen);
            var movement=new Vector3((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),
                (keyboard.eKey.isPressed?1:0)-(keyboard.qKey.isPressed?1:0),
                (keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
            return new DesktopControlFrame(true,pointer,movement,mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame,keyboard.escapeKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame,
                keyboard.rKey.wasPressedThisFrame);
        }
    }
}
