using System;

namespace Elts.Tracking
{
    /// <summary>One shot per pull. Require a release after loss of input or a change
    /// of session/phase, so a held button cannot start the next task.</summary>
    public sealed class TriggerPressGate
    {
        private bool released;
        private string context = "";
        private long lastPress = long.MinValue;
        private readonly long lockoutTicks;

        public TriggerPressGate(TimeSpan lockout) { lockoutTicks = lockout.Ticks; }

        public bool Observe(bool available, bool pressed, bool allowed, string currentContext, long ticks)
        {
            if (currentContext != context)
            { context = currentContext; released = false; lastPress = long.MinValue; }
            if (!available || !allowed) { released = false; return false; }
            if (!pressed) { released = true; return false; }
            if (!released) return false;
            released = false;
            if (lastPress != long.MinValue && ticks - lastPress < lockoutTicks) return false;
            lastPress = ticks;
            return true;
        }
    }
}
