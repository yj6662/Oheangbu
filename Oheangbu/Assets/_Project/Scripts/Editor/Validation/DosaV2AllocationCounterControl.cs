using System;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    public static class DosaV2AllocationCounterControl
    {
        public static string Run()
        {
            var rows = new JArray();
            foreach (int length in new[] { 1048576, 1048576, 2097152 })
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] retained = Allocate(length);
                long after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(retained);
                rows.Add(new JObject { ["requestedArrayPayloadBytes"] = length,
                    ["before"] = before, ["after"] = after, ["delta"] = after - before,
                    ["positiveControlDetected"] = after - before >= length });
            }
            bool detected = true;
            foreach (JObject row in rows) detected &= (bool)row["positiveControlDetected"];
            var report = new JObject { ["status"] = detected ? "COUNTER_DETECTS_KNOWN_ALLOCATIONS" : "COUNTER_UNAVAILABLE_FOR_ZERO_ALLOCATION_CLAIMS",
                ["utc"] = DateTimeOffset.UtcNow.ToString("O"), ["unityVersion"] = Application.unityVersion,
                ["rows"] = rows, ["scope"] = "Positive control only. Byte arrays are retained across both counter reads; no GC collection or scene/profile changes. Undetected known allocations invalidate a zero-allocation interpretation of prior counter zeros, not their separately measured CPU times." };
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Validation"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "allocation-counter-positive-control.json"), report.ToString());
            return report.ToString();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte[] Allocate(int length)
        {
            var data = new byte[length]; data[0] = 1; data[length - 1] = 2; return data;
        }
    }
}
