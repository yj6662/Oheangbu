"""Derive two KTP flame graphs; preserve vendor shading and clip only at output.

No Unity process is started. --check verifies committed outputs without writing.
The JSON report includes source hashes and the graph/analytic checks performed.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import random
import uuid


REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "Oheangbu/Assets"
VENDOR = ASSETS / "KoreanTraditionalPattern_Effect/Shader"
OUTPUT = ASSETS / "_Project/Shaders"
NAMESPACE = uuid.UUID("0f6e5f40-de47-4e13-9295-ecaaef319c83")
ALPHA_BLOCK = "8290fcacd77041c0938c333151d3dddc"
COLOR_BLOCK = "9c84c7064aec4360a8a22efb82774e6b"
ALPHA_SOURCE = "54616f64fa16798e8f7a1e83aeb21dc0"
COLOR_SOURCE = "b01ef874d6bd008c83e2c1259443b55d"
INCLUDE_NAME = "DemoFlameConeClip.hlsl"
PROPERTIES = [
    ("_FlameConeOrigin", "Flame cone origin (absolute world)", 3, (0, 0, 0)),
    ("_FlameConeDirection", "Flame cone direction (world)", 3, (0, 0, 1)),
    ("_FlameConeRange", "Flame cone radial range (metres)", 1, 4.5),
    ("_FlameConeHalfAngle", "Flame cone half angle (degrees)", 1, 30.0),
    ("_FlameConeHeight", "Flame cone vertical tolerance (+/- metres)", 1, 1.6),
    ("_FlameConeFeather", "Flame cone inner feather (metres)", 1, 0.15),
    ("_FlameIntensity", "Flame intensity", 1, 0.7),
]

HLSL = """// Derived Nom-only material clipping. Original KTP texture and color graph is unchanged.
// Absolute world position must use float precision for the kilometre-scale world.
#ifndef OHEANGBU_DEMO_FLAME_CONE_CLIP_INCLUDED
#define OHEANGBU_DEMO_FLAME_CONE_CLIP_INCLUDED

void DemoFlameConeMask_float(float3 PositionWS, float3 Origin, float3 Direction,
    float Range, float HalfAngle, float Height, float Feather, out float Mask)
{
    float directionLengthSquared = dot(Direction.xz, Direction.xz);
    if (directionLengthSquared <= 0.00000001 || Range <= 0.0 ||
        HalfAngle <= 0.0 || HalfAngle >= 90.0 || Height <= 0.0)
    {
        Mask = 0.0;
        clip(-1.0);
        return;
    }
    float2 forward = Direction.xz * rsqrt(directionLengthSquared);
    float3 relative = PositionWS - Origin;
    float along = dot(relative.xz, forward);
    float across = abs(relative.x * forward.y - relative.z * forward.x);
    float angle = radians(HalfAngle);
    // Wedge distance is in metres, not an angular threshold or widened cone.
    float wedgeDistance = along * sin(angle) - across * cos(angle);
    float radialDistance = Range - length(relative.xz);
    float verticalDistance = Height - abs(relative.y);
    float insideDistance = min(radialDistance, min(wedgeDistance, verticalDistance));
    // A zero alpha clip threshold in the vendor graph does not discard alpha == 0.
    // Explicit fragment discard guarantees no contribution outside the combat volume.
    clip(insideDistance);
    Mask = Feather > 0.0 ? smoothstep(0.0, Feather, insideDistance) : 1.0;
}

#endif
"""


def stable_id(name: str) -> str:
    return uuid.uuid5(NAMESPACE, name).hex


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_graph(path: Path) -> list[dict]:
    text = path.read_text(encoding="utf-8-sig")
    decoder = json.JSONDecoder()
    cursor = 0
    objects = []
    while cursor < len(text):
        while cursor < len(text) and text[cursor].isspace():
            cursor += 1
        if cursor == len(text):
            break
        value, cursor = decoder.raw_decode(text, cursor)
        objects.append(value)
    return objects


def value(dimension: int, content=0):
    if dimension == 1:
        return float(content)
    if not isinstance(content, (list, tuple)):
        content = [content] * dimension
    return dict(zip("xyzw"[:dimension], map(float, content)))


def slot(key: str, number: int, name: str, dimension: int, output: bool = False,
         default=0) -> dict:
    return {
        "m_SGVersion": 0,
        "m_Type": f"UnityEditor.ShaderGraph.Vector{dimension}MaterialSlot",
        "m_ObjectId": stable_id(key), "m_Id": number, "m_DisplayName": name,
        "m_SlotType": 1 if output else 0, "m_Hidden": False,
        "m_ShaderOutputName": name, "m_StageCapability": 2,
        "m_Value": value(dimension, default),
        "m_DefaultValue": value(dimension, default), "m_Labels": [],
    }


def node(key: str, kind: str, name: str, slots: list[dict], x=600, y=500) -> dict:
    return {
        "m_SGVersion": 0, "m_Type": f"UnityEditor.ShaderGraph.{kind}",
        "m_ObjectId": stable_id(key), "m_Group": {"m_Id": ""}, "m_Name": name,
        "m_DrawState": {"m_Expanded": True, "m_Position": {
            "serializedVersion": "2", "x": x, "y": y, "width": 210, "height": 180}},
        "m_Slots": [{"m_Id": s["m_ObjectId"]} for s in slots], "synonyms": [],
        "m_Precision": 1, "m_PreviewExpanded": False, "m_PreviewMode": 0,
        "m_CustomColors": {"m_SerializableColors": []},
    }


def edge(source: str, output: int, target: str, input_slot: int) -> dict:
    return {
        "m_OutputSlot": {"m_Node": {"m_Id": source}, "m_SlotId": output},
        "m_InputSlot": {"m_Node": {"m_Id": target}, "m_SlotId": input_slot},
    }


def derive(original: list[dict]) -> list[dict]:
    derived = copy.deepcopy(original)
    graph = derived[0]
    lookup = {o["m_ObjectId"]: o for o in original}
    graph["m_Path"] = "Oheangbu/Demo"

    def add_node(n: dict, slots: list[dict]):
        derived.append(n)
        derived.extend(slots)
        graph["m_Nodes"].append({"m_Id": n["m_ObjectId"]})
        return n["m_ObjectId"]

    property_nodes = {}
    for index, (reference, label, dimension, default) in enumerate(PROPERTIES):
        property_id = stable_id("property:" + reference)
        prop = {
            "m_SGVersion": 1,
            "m_Type": f"UnityEditor.ShaderGraph.Internal.Vector{dimension}ShaderProperty",
            "m_ObjectId": property_id,
            "m_Guid": {"m_GuidSerialized": str(uuid.UUID(property_id))},
            "m_Name": label, "m_DefaultReferenceName": reference,
            "m_OverrideReferenceName": reference, "m_GeneratePropertyBlock": True,
            "m_Precision": 1, "overrideHLSLDeclaration": False,
            "hlslDeclarationOverride": 0, "m_Hidden": False,
            "m_Value": value(dimension, default),
        }
        if dimension == 1:
            prop.update(m_FloatType=0, m_RangeValues={"x": 0.0, "y": 1.0})
        derived.append(prop)
        graph["m_Properties"].append({"m_Id": property_id})
        s = slot("property-slot:" + reference, 0, "Out", dimension, True, default)
        n = node("property-node:" + reference, "PropertyNode", "Property", [s], 0, 500 + index * 170)
        n["m_Property"] = {"m_Id": property_id}
        property_nodes[reference] = add_node(n, [s])

    p_slot = slot("position:slot", 0, "Out", 3, True)
    position = node("position", "PositionNode", "Position", [p_slot], 0, 280)
    # CoordinateSpace.AbsoluteWorld = 4. Single precision is explicitly selected.
    position.update(m_SGVersion=1, m_Space=4, m_PositionSource=0)
    position_id = add_node(position, [p_slot])
    args = [("PositionWS", 3), ("Origin", 3), ("Direction", 3), ("Range", 1),
            ("HalfAngle", 1), ("Height", 1), ("Feather", 1), ("Mask", 1)]
    fn_slots = [slot("function-slot:" + name, i, name, dim, i == 7)
                for i, (name, dim) in enumerate(args)]
    function = node("function", "CustomFunctionNode", "DemoFlameConeMask (Custom Function)", fn_slots)
    function.update(m_SGVersion=1, m_SourceType=0, m_FunctionName="DemoFlameConeMask",
                    m_FunctionSource=stable_id(INCLUDE_NAME),
                    m_FunctionSourceUsePragmas=False, m_FunctionBody="")
    function_id = add_node(function, fn_slots)
    graph["m_Edges"].append(edge(position_id, 0, function_id, 0))
    for i, (reference, *_rest) in enumerate(PROPERTIES[:-1], 1):
        graph["m_Edges"].append(edge(property_nodes[reference], 0, function_id, i))

    def multiply(key: str, source_id: str, control_id: str, control_slot: int,
                 destination_id: str, y: int):
        n = copy.deepcopy(lookup[ALPHA_SOURCE])
        n["m_ObjectId"] = stable_id(key)
        n["m_DrawState"]["m_Position"].update(x=1000, y=y)
        slots = []
        for i, ref in enumerate(n["m_Slots"]):
            s = copy.deepcopy(lookup[ref["m_Id"]])
            s["m_ObjectId"] = stable_id(f"{key}:slot:{i}")
            s["m_StageCapability"] = 2
            slots.append(s)
        n["m_Slots"] = [{"m_Id": s["m_ObjectId"]} for s in slots]
        inserted_id = add_node(n, slots)
        original_edge = edge(source_id, 2, destination_id, 0)
        assert graph["m_Edges"].count(original_edge) == 1
        graph["m_Edges"].remove(original_edge)
        graph["m_Edges"].extend([
            edge(source_id, 2, inserted_id, 0),
            edge(control_id, control_slot, inserted_id, 1),
            edge(inserted_id, 2, destination_id, 0),
        ])

    multiply("cone-alpha", ALPHA_SOURCE, function_id, 7, ALPHA_BLOCK, 700)
    multiply("flame-intensity", COLOR_SOURCE, property_nodes["_FlameIntensity"], 0, COLOR_BLOCK, 300)
    return derived


def verify_structure(original: list[dict], derived: list[dict]) -> dict:
    old = {o["m_ObjectId"]: o for o in original}
    new = {o["m_ObjectId"]: o for o in derived}
    assert len(new) == len(derived), "Duplicate graph object ID"
    for object_id, obj in old.items():
        if obj is original[0]:
            continue
        assert new[object_id] == obj, f"Changed original shader object {object_id}"
    expected_removed = [edge(ALPHA_SOURCE, 2, ALPHA_BLOCK, 0), edge(COLOR_SOURCE, 2, COLOR_BLOCK, 0)]
    removed = [e for e in original[0]["m_Edges"] if e not in derived[0]["m_Edges"]]
    assert sorted(map(json.dumps, removed)) == sorted(map(json.dumps, expected_removed))
    for e in derived[0]["m_Edges"]:
        for endpoint in ("m_InputSlot", "m_OutputSlot"):
            endpoint = e[endpoint]
            n = new[endpoint["m_Node"]["m_Id"]]
            assert any(new[s["m_Id"]]["m_Id"] == endpoint["m_SlotId"] for s in n["m_Slots"])
    assert new[stable_id("position")]["m_Space"] == 4
    assert new[stable_id("function")]["m_Precision"] == 1
    for ref, *_rest in PROPERTIES:
        assert new[stable_id("property:" + ref)]["m_OverrideReferenceName"] == ref
    return {"original_shader_objects_preserved": len(original) - 1,
            "original_output_edges_replaced": len(removed),
            "derived_objects": len(derived), "absolute_world_float_clip": True}


def verify_geometry() -> int:
    """Compare the clipping inequalities with independent polar-angle classification."""
    rng = random.Random(173)
    count = 0
    for yaw in (0.0, 1.17, -2.85):
        forward = math.sin(yaw), math.cos(yaw)
        for _ in range(2000):
            x, y, z = rng.uniform(-6, 6), rng.uniform(-2, 2), rng.uniform(-6, 6)
            along = x * forward[0] + z * forward[1]
            across = x * forward[1] - z * forward[0]
            signed_distance = min(4.5 - math.hypot(x, z), 1.6 - abs(y),
                                  along * 0.5 - abs(across) * math.cos(math.pi / 6))
            expected = math.hypot(x, z) <= 4.5 and abs(y) <= 1.6 and abs(math.atan2(across, along)) <= math.pi / 6
            assert (signed_distance >= 0) == expected
            t = max(0.0, min(1.0, signed_distance / 0.15))
            fade = t * t * (3 - 2 * t)
            assert fade == 0 or expected, "Feather expanded the damage volume"
            count += 1
    for p, expected in [((0, 0, 4.6), False), ((0, 0, 4.4), True),
                        ((0, 1.61, 2), False), ((0, 0, -0.01), False),
                        ((2.3, 0, 3.9), False), ((2.2, 0, 3.9), True)]:
        x, y, z = p
        d = min(4.5 - math.hypot(x, z), 1.6 - abs(y), z * .5 - abs(x) * math.cos(math.pi / 6))
        assert (d >= 0) == expected
        count += 1
    return count


def metadata(guid: str, graph: bool) -> str:
    if graph:
        return (f"fileFormatVersion: 2\nguid: {guid}\nScriptedImporter:\n"
                "  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 2\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                "  script: {fileID: 11500000, guid: 625f186215c104763be7675aa2d941aa, type: 3}\n")
    return (f"fileFormatVersion: 2\nguid: {guid}\nShaderIncludeImporter:\n"
            "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify outputs without writing")
    args = parser.parse_args()
    sources = [VENDOR / (name + ".shadergraph") for name in ("AdditiveBlend_Scroll", "AlphaBlend_Scroll")]
    protected = sources + [Path(str(p) + ".meta") for p in sources] + [
        ASSETS / "KoreanTraditionalPattern_Effect/Materials/Bottom/Bottom05-01/par_Fire.mat",
        ASSETS / "_Project/Art/SpellVFX120/Traditional/Bodies/M_KTP_LooseEarthGrains.mat",
        ASSETS / "_Project/Art/SpellVFX120/Traditional/Bodies/PF_KTP_FlameCone.prefab",
    ]
    hashes = {str(p.relative_to(REPO)): digest(p) for p in protected}
    results = []
    outputs = {OUTPUT / INCLUDE_NAME: HLSL,
               OUTPUT / (INCLUDE_NAME + ".meta"): metadata(stable_id(INCLUDE_NAME), False)}
    for source, filename in zip(sources, ("DemoFlameAdditive.shadergraph", "DemoFlameAlpha.shadergraph")):
        original = read_graph(source)
        derived = derive(original)
        checks = verify_structure(original, derived)
        outputs[OUTPUT / filename] = "\n\n".join(json.dumps(o, indent=4, ensure_ascii=False) for o in derived) + "\n"
        outputs[OUTPUT / (filename + ".meta")] = metadata(stable_id(filename), True)
        results.append({"file": str((OUTPUT / filename).relative_to(REPO)),
                        "shader": "Oheangbu/Demo/" + Path(filename).stem,
                        "guid": stable_id(filename), **checks})
    geometry = verify_geometry()
    for path, text in outputs.items():
        if args.check:
            assert path.read_text(encoding="utf-8") == text, f"Generated output differs: {path}"
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8", newline="\n")
    assert all(digest(REPO / path) == before for path, before in hashes.items()), "Protected source changed"
    print(json.dumps({"status": "PASS", "mode": "check" if args.check else "generate",
                      "graphs": results, "analytic_geometry_cases": geometry,
                      "protected_sources_unchanged_sha256": hashes,
                      "unity_shader_import_gpu_validation": "NOT_RUN"}, indent=2))


if __name__ == "__main__":
    main()
