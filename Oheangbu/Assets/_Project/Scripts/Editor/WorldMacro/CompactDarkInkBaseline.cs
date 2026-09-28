using System;
using UnityEngine;
using Oheangbu.App.World.Dressing;
namespace Oheangbu.EditorTools.WorldMacro
{
    // Editor-only, durable references to the immediately preceding revision.
    public sealed class CompactDarkInkBaseline : ScriptableObject
    {
        [Serializable] public sealed class Extra { public string Path; public EarlyRegionFoliage.Packet[] Packets; }
        public Extra[] Extras;
    }
}
