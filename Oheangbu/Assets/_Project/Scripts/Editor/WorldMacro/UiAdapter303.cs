using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#303 test harnesses reach the playtest UI only by name (reflection), because the UI is being rewritten (#304)
    /// while these tools are written. No UI type is referenced at compile time. A missing member is SKIPPED (recorded),
    /// never a FAIL — the same rule as HarnessUiRules304. Every call that changes UI state is logged so the report can
    /// list it as a fixture intervention.</summary>
    internal static class UiAdapter303
    {
        const string RootTypeName = "Oheangbu.App.World.UI.PlaytestUiRoot";
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        /// <summary>Calls that changed UI state or were skipped, in order. Cleared by the harness at run start.</summary>
        public static readonly List<string> Log = new List<string>();
        public static void Reset() => Log.Clear();

        static Type rootType;
        static Type RootType
        {
            get
            {
                if (rootType != null) return rootType;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType(RootTypeName, false);
                    if (t != null) { rootType = t; break; }
                }
                return rootType;
            }
        }

        static object Read(object target, string name)
        {
            if (target == null) return null;
            var type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            var p = type.GetProperty(name, Members);
            if (p != null && p.GetIndexParameters().Length == 0 && p.CanRead) { try { return p.GetValue(instance); } catch { return null; } }
            var f = type.GetField(name, Members);
            if (f != null) { try { return f.GetValue(instance); } catch { return null; } }
            return null;
        }

        static string Call(object target, string method, string label)
        {
            if (target == null) { string r = "SKIPPED: " + label + " (no target)"; Log.Add(r); return r; }
            var m = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == 0);
            if (m == null) { string r = "SKIPPED: " + label + " (member missing)"; Log.Add(r); return r; }
            try { m.Invoke(target, null); Log.Add("OK: " + label); return "OK"; }
            catch (Exception e) { string r = "SKIPPED: " + label + " threw " + (e.InnerException ?? e).GetType().Name; Log.Add(r); return r; }
        }

        public static object Instance => RootType != null ? Read(RootType, "Instance") : null;
        public static bool Present => Instance != null;

        /// <summary>null when the member is missing (treated as "not loading").</summary>
        public static bool? LoadingInProgress => Read(Instance, "LoadingInProgress") as bool?;
        public static string Page => Read(Instance, "Page") as string ?? "";
        public static bool? IsMenuOpen => Read(Instance, "IsMenuOpen") as bool?;
        public static bool? IsTitle => Read(Instance, "IsTitle") as bool?;
        static object Gate => Read(Instance, "Gate");
        public static bool? GateBlocked => Read(Gate, "InputBlocked") as bool?;
        public static bool? GateReleasePending => Read(Gate, "ReleasePending") as bool?;

        public static string Back() => Call(Instance, "Back", "PlaytestUiRoot.Back()");
        public static string CloseMenu() => Call(Instance, "CloseMenu", "PlaytestUiRoot.CloseMenu()");
        public static string ReleaseGate() => Call(Gate, "ReleaseImmediately", "GameplayUiGate.ReleaseImmediately()");

        public static string Describe()
        {
            if (!Present) return "ui=absent";
            return "ui page='" + Page + "' menu=" + (IsMenuOpen?.ToString() ?? "?") + " title=" + (IsTitle?.ToString() ?? "?") +
                   " loading=" + (LoadingInProgress?.ToString() ?? "?") + " gate=" + (GateBlocked?.ToString() ?? "?") + "/" + (GateReleasePending?.ToString() ?? "?");
        }
    }
}
