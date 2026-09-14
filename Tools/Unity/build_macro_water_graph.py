"""Build the macro-owned native Shader Graph from the preserved AreaWater graph.

Run from any directory. UV0.x is transverse metres, UV0.y is downstream metres;
vertex color R is a 0..1 displacement pin mask, G is measured terrain water depth / 8m,
A fades river starts/ends. Baked terrain depth is the default; optional reconstructed
scene depth is retained for diagnosis and comparison. A single external _EffectTime
clock drives downstream ripple, vertex wave and fragmented shallow-water foam.
This script validates serialized structure; Unity shader compilation is separate.
"""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import uuid


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Oheangbu/Assets/_Project/Art/SpellVFX120/AreaFive/AreaWater.shadergraph"
DESTINATION = ROOT / "Oheangbu/Assets/_Project/Art/World/WorldMacro/MacroRiver.shadergraph"
NAMESPACE = uuid.UUID("1e819e0b-031c-53c4-9f67-d415aac5b684")


def uid(label: str) -> str:
    return uuid.uuid5(NAMESPACE, label).hex


def parse(text: str) -> list[dict]:
    decoder = json.JSONDecoder()
    objects, cursor = [], 0
    while cursor < len(text):
        while cursor < len(text) and text[cursor].isspace():
            cursor += 1
        if cursor == len(text):
            break
        obj, cursor = decoder.raw_decode(text, cursor)
        objects.append(obj)
    return objects


class Graph:
    def __init__(self, objects: list[dict]):
        self.objects = copy.deepcopy(objects)
        self.by_id = {obj["m_ObjectId"]: obj for obj in self.objects}
        self.graph = self.objects[0]
        self.added = 0
        self.properties = []
        self.group = uid("group:downstream_foam")
        group = {
            "m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.GroupData",
            "m_ObjectId": self.group, "m_Title": "Macro river: metre UV / pinned wave / sparse shore foam",
            "m_Position": {"x": -3900.0, "y": 2600.0},
        }
        self.add(group)
        self.graph["m_GroupDatas"].append({"m_Id": self.group})
        self.category = {
            "m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.CategoryData",
            "m_ObjectId": uid("category:macro_river"), "m_Name": "Macro River",
            "m_ChildObjectList": [],
        }
        self.add(self.category)
        self.graph["m_CategoryData"].append({"m_Id": self.category["m_ObjectId"]})

    def add(self, obj: dict) -> dict:
        assert obj["m_ObjectId"] not in self.by_id
        self.objects.append(obj)
        self.by_id[obj["m_ObjectId"]] = obj
        return obj

    def slot(self, node: str, number: int) -> dict:
        return next(self.by_id[ref["m_Id"]] for ref in self.by_id[node]["m_Slots"]
                    if self.by_id[ref["m_Id"]]["m_Id"] == number)

    def value(self, node: str, number: int, value) -> None:
        slot = self.slot(node, number)
        if isinstance(slot["m_Value"], dict) and isinstance(value, (int, float)):
            value = {key: float(value) for key in slot["m_Value"]}
        slot["m_Value"] = copy.deepcopy(value)

    def clone_node(self, template: str, label: str) -> str:
        node = copy.deepcopy(self.by_id[template])
        node["m_ObjectId"] = uid("node:" + label)
        node["m_Group"] = {"m_Id": self.group}
        position = node["m_DrawState"]["m_Position"]
        position["x"] = -3800 + (self.added % 7) * 280
        position["y"] = 2700 + (self.added // 7) * 390
        self.added += 1
        refs = []
        for ref in node["m_Slots"]:
            slot = copy.deepcopy(self.by_id[ref["m_Id"]])
            slot["m_ObjectId"] = uid(f"slot:{label}:{slot['m_Id']}")
            self.add(slot)
            refs.append({"m_Id": slot["m_ObjectId"]})
        node["m_Slots"] = refs
        self.add(node)
        self.graph["m_Nodes"].append({"m_Id": node["m_ObjectId"]})
        return node["m_ObjectId"]

    def connect(self, source: str, output: int, target: str, input_slot: int) -> None:
        self.graph["m_Edges"] = [edge for edge in self.graph["m_Edges"]
            if not (edge["m_InputSlot"]["m_Node"]["m_Id"] == target
                    and edge["m_InputSlot"]["m_SlotId"] == input_slot)]
        self.graph["m_Edges"].append({
            "m_OutputSlot": {"m_Node": {"m_Id": source}, "m_SlotId": output},
            "m_InputSlot": {"m_Node": {"m_Id": target}, "m_SlotId": input_slot},
        })

    def property(self, reference: str, name: str, value, limits=None) -> str:
        colour = isinstance(value, dict)
        template = "b980957bfb1b454a8fc29fb44d89a4e5" if colour else "1814fce6419f407f85e46eb7be40c054"
        prop = copy.deepcopy(self.by_id[template])
        prop["m_ObjectId"] = uid("property:" + reference)
        prop["m_Guid"]["m_GuidSerialized"] = str(uuid.uuid5(NAMESPACE, "property-guid:" + reference))
        prop["m_Name"] = prop["m_RefNameGeneratedByDisplayName"] = name
        prop["m_DefaultReferenceName"] = prop["m_OverrideReferenceName"] = reference
        prop["m_Value"] = value
        if limits is not None:
            prop["m_FloatType"] = 1  # Slider; names and values remain editable material properties.
            prop["m_RangeValues"] = {"x": limits[0], "y": limits[1]}
        self.add(prop)
        self.graph["m_Properties"].append({"m_Id": prop["m_ObjectId"]})
        self.category["m_ChildObjectList"].append({"m_Id": prop["m_ObjectId"]})
        node = self.clone_node("235e4ae26aa04b75a988359e6b38ad40" if colour else "cccea02f78444423a2fad026f6b224ec", reference)
        self.by_id[node]["m_Property"] = {"m_Id": prop["m_ObjectId"]}
        slot = self.slot(node, 0)
        slot["m_DisplayName"] = name
        self.properties.append((reference, value))
        return node

    def geometry(self, kind: str, label: str) -> str:
        # These slots match the installed Shader Graph VertexColorNode / UVNode C# definitions.
        node = self.clone_node("4842607642694563be0a4c0be3b489b7", label)
        data = self.by_id[node]
        data["m_Type"] = "UnityEditor.ShaderGraph." + kind + "Node"
        data["m_Name"] = "Vertex Color" if kind == "VertexColor" else "UV"
        data.pop("m_ScreenSpaceType", None)
        if kind == "UV":
            data["m_OutputChannel"] = 0
        slot = self.slot(node, 0)
        slot["m_Type"] = "UnityEditor.ShaderGraph.Vector4MaterialSlot"
        slot["m_Value"] = slot["m_DefaultValue"] = {k: 1.0 if kind == "VertexColor" else 0.0 for k in "xyzw"}
        slot["m_StageCapability"] = 3
        return node


def build(objects: list[dict]) -> tuple[list[dict], dict]:
    g = Graph(objects)
    g.graph["m_Path"] = "Oheangbu/World"
    # UV distances reach kilometres; half precision would quantize flowing ripple phases.
    g.graph["m_GraphPrecision"] = 0  # Installed GraphPrecision.Single.
    # Preserve the existing depth / SceneColor refraction network and its sole clock.
    time = "463cc413a2b94505852c825815ab7af4"
    mul_template = "eaf2604864284a5a94dc37f505a7ff48"
    neg_template = "fe41da2d20594821b788319e86036caf"
    combine_template = "2ba5de7cf4b14f3a8b2e4f76fe10da70"
    split_template = "ff5700cc6d0545cbbc43d3b2b705d53a"
    tiling_template = "6752e902357849dbb47a0239cd12965a"
    subtract_template = "050edec134884ec9a89ecf346dbe15ee"
    divide_template = "36205672678744c6a2bdaaf0c260791d"
    saturate_template = "b6bcd87ea0fc4bb982632ec13f5968ed"
    uv = g.geometry("UV", "river_uv_metres")
    vertex = g.geometry("VertexColor", "bank_start_end_pin")
    pin = g.clone_node(split_template, "pin_red")
    g.connect(vertex, 0, pin, 0)

    # Optional scene-depth comparison path. Runtime capture has not yet verified
    # this reconstruction; default rendering instead uses collider-measured depth
    # baked by WorldMacroWaterGeometry into vertex color G. Both are vertical metres.
    raw_depth = "2a20a9892547488b9bda4f9e9e9d5e60"
    g.by_id[raw_depth]["m_DepthSamplingMode"] = 1  # Installed DepthSamplingMode.Raw.
    surface_position = g.clone_node("a9bc813db47b4ed3937ea40fd3f0963b", "surface_world_position")
    g.by_id[surface_position]["m_Space"] = 2  # World; URP uses this same space in I_VP reconstruction.
    depth_function = g.clone_node("14ad598ede554e2092e98e8af5213f22", "vertical_scene_water_depth")
    function = g.by_id[depth_function]
    function.update({
        "m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.CustomFunctionNode",
        "m_Name": "MacroVerticalWaterDepth (Custom Function)", "m_SourceType": 1,
        "m_FunctionName": "MacroVerticalWaterDepth", "m_FunctionSource": "",
        "m_FunctionSourceUsePragmas": True, "m_Precision": 1,
        "m_FunctionBody": (
            "float deviceDepth = RawDepth;\n"
            "#if !defined(UNITY_REVERSED_Z)\n"
            "deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);\n"
            "#endif\n"
            "float3 sceneWS = ComputeWorldSpacePosition(ScreenUV, deviceDepth, UNITY_MATRIX_I_VP);\n"
            "Depth = max(SurfaceWS.y - sceneWS.y, 0.0);"
        ),
    })
    refs = []
    for number, name, dimensions, output in ((0, "ScreenUV", 2, False), (1, "RawDepth", 1, False),
                                               (2, "SurfaceWS", 3, False), (3, "Depth", 1, True)):
        template = next(obj for obj in g.objects if obj["m_Type"] == f"UnityEditor.ShaderGraph.Vector{dimensions}MaterialSlot")
        slot = copy.deepcopy(template)
        slot.update({"m_ObjectId": uid("slot:vertical_scene_depth:" + name), "m_Id": number,
                     "m_DisplayName": name, "m_ShaderOutputName": name, "m_SlotType": int(output),
                     "m_StageCapability": 2})
        slot["m_Value"] = slot["m_DefaultValue"] = 0.0 if dimensions == 1 else {key: 0.0 for key in "xyz"[:dimensions]}
        g.add(slot)
        refs.append({"m_Id": slot["m_ObjectId"]})
    function["m_Slots"] = refs
    g.connect("dfe36825e10f449280d1ee79b4632954", 0, depth_function, 0)
    g.connect(raw_depth, 1, depth_function, 1)
    g.connect(surface_position, 0, depth_function, 2)
    baked_depth = g.clone_node(mul_template, "baked_terrain_depth_metres")
    g.connect(pin, 2, baked_depth, 0)  # Split G: measured max(waterY - terrainY, 0) / 8m.
    g.value(baked_depth, 1, 8.0)
    scene_depth_selection = g.property("_UseSceneDepth", "Use Scene Depth (diagnostic)", 0.0, (0, 1))
    # A native Boolean comparison + Branch avoids evaluating baked + 0 * NaN
    # as an arithmetic lerp would do if an optional screen-depth input is invalid.
    depth_selection_test = g.clone_node(subtract_template, "scene_depth_enabled_comparison")
    comparison = g.by_id[depth_selection_test]
    comparison.update({"m_Type": "UnityEditor.ShaderGraph.ComparisonNode", "m_Name": "Comparison",
                       "m_ComparisonType": 4})  # Installed ComparisonType.Greater.
    for number in (0, 1, 2):
        slot = g.slot(depth_selection_test, number)
        slot["m_Type"] = "UnityEditor.ShaderGraph.BooleanMaterialSlot" if number == 2 else "UnityEditor.ShaderGraph.Vector1MaterialSlot"
        slot["m_Value"] = slot["m_DefaultValue"] = False if number == 2 else 0.0
    g.value(depth_selection_test, 1, .5)
    g.connect(scene_depth_selection, 0, depth_selection_test, 0)
    selected_depth = g.clone_node("b8450f7b69cc4606af7d1178c28d66a3", "baked_or_scene_vertical_depth")
    g.by_id[selected_depth].update({"m_Type": "UnityEditor.ShaderGraph.BranchNode", "m_Name": "Branch"})
    predicate_slot = g.slot(selected_depth, 0)
    predicate_slot.update({"m_Type": "UnityEditor.ShaderGraph.BooleanMaterialSlot", "m_DisplayName": "Predicate", "m_ShaderOutputName": "Predicate",
                           "m_Value": False, "m_DefaultValue": False})
    for number, name in ((1, "True"), (2, "False"), (3, "Out")):
        slot = g.slot(selected_depth, number)
        slot["m_DisplayName"] = slot["m_ShaderOutputName"] = name
    g.connect(depth_selection_test, 2, selected_depth, 0)
    g.connect(depth_function, 3, selected_depth, 1)
    g.connect(baked_depth, 2, selected_depth, 2)
    g.connect(selected_depth, 3, divide_template, 0)

    flow_speed = g.property("_FlowSpeed", "Downstream Flow (m/s)", .8, (0, 3))
    flow_time = g.clone_node(mul_template, "flow_distance")
    g.connect(time, 0, flow_time, 0)
    g.connect(flow_speed, 0, flow_time, 1)
    negative = g.clone_node(neg_template, "negative_flow_distance")
    g.connect(flow_time, 2, negative, 0)
    offset = g.clone_node(combine_template, "downstream_offset_xy")
    g.connect(negative, 1, offset, 1)  # (0,-time*speed) moves pattern towards positive downstream V.
    for node in (tiling_template, "84e2690a6287415ca3e0eef0d1f05b58"):
        g.connect(uv, 0, node, 0)
        g.connect(offset, 6, node, 2)
    # Both layers advect at the same physical speed, while the second has a different spatial scale.
    secondary_uv = g.clone_node(tiling_template, "secondary_ripple_domain")
    g.connect(tiling_template, 3, secondary_uv, 0)
    g.value(secondary_uv, 1, {"x": 1.67, "y": 1.37})
    g.value(secondary_uv, 2, {"x": 17.3, "y": 9.7})
    g.connect(secondary_uv, 3, "9587c88a88f64e6dbffe0c051ab7d3cf", 0)
    ripple = g.property("_RippleStrength", "Primary Ripple Normal", .075, (0, .4))
    secondary_ripple = g.property("_SecondaryRippleStrength", "Secondary Ripple Normal", .045, (0, .4))
    g.connect(ripple, 0, "a668bfeafc384162b35fdad2dc925d9e", 2)
    g.connect(secondary_ripple, 0, "28c18f6381c545d2bfd394f40d44abe4", 2)

    # Properly normalize the combined tangent-space normals; retain the refraction and NormalTS consumers.
    normal = g.by_id["0b3cdaa8e52a45f496ac7476bdb67921"]
    normal["m_Type"] = "UnityEditor.ShaderGraph.NormalBlendNode"
    normal["m_Name"] = "Normal Blend"
    normal["m_BlendMode"] = 0
    for ref in normal["m_Slots"]:
        slot = g.by_id[ref["m_Id"]]
        slot["m_Type"] = "UnityEditor.ShaderGraph.Vector3MaterialSlot"
        slot["m_Value"] = slot["m_DefaultValue"] = {"x": 0.0, "y": 0.0, "z": 1.0}

    # Small longitudinal sine displacement uses the same clock, plus vertex-color pinning.
    # _Speed_Wave remains an independent wave travel speed in m/s, not a second clock.
    wave_offset = g.clone_node(combine_template, "wave_downstream_offset")
    wave_negative = g.clone_node(neg_template, "negative_wave_distance")
    g.connect("eaf2604864284a5a94dc37f505a7ff48", 2, wave_negative, 0)
    g.connect(wave_negative, 1, wave_offset, 1)
    wave_uv = g.clone_node(tiling_template, "wave_uv_metres")
    g.connect(uv, 0, wave_uv, 0)
    g.connect(wave_offset, 6, wave_uv, 2)
    wave_split = g.clone_node(split_template, "wave_downstream_metres")
    g.connect(wave_uv, 3, wave_split, 0)
    g.connect(wave_split, 2, "b52ff8c2710c42128f202ed04e9873e4", 0)
    g.connect("b52ff8c2710c42128f202ed04e9873e4", 2, "dce12182b4c3488d96fc1baee69694f2", 0)
    pinned = g.clone_node(mul_template, "pinned_wave_amplitude")
    g.connect("5b8dc2defafb40d788240cf0d9d68623", 2, pinned, 0)
    pin_saturated = g.clone_node(saturate_template, "clamp_pin")
    g.connect(pin, 1, pin_saturated, 0)
    g.connect(pin_saturated, 1, pinned, 1)
    g.connect(pinned, 2, "49b00c1b4f2348bdb851e56ffa5fed2f", 0)
    shore_depth = g.property("_ShoreFadeDepth", "Shore Opacity Fade (m)", .45, (.05, 1.5))
    river_opacity = g.property("_RiverOpacity", "Open Water Opacity", .82, (0, 1))
    shore_divide = g.clone_node(divide_template, "depth_over_shore_fade")
    g.connect(selected_depth, 3, shore_divide, 0)
    g.connect(shore_depth, 0, shore_divide, 1)
    shore_alpha = g.clone_node(saturate_template, "shore_opacity")
    g.connect(shore_divide, 2, shore_alpha, 0)
    surface_alpha = g.clone_node(mul_template, "shore_times_open_water_opacity")
    g.connect(shore_alpha, 1, surface_alpha, 0)
    g.connect(river_opacity, 0, surface_alpha, 1)
    alpha_pin = g.clone_node(saturate_template, "clamp_start_end_alpha")
    g.connect(pin, 4, alpha_pin, 0)
    alpha = g.clone_node(mul_template, "depth_times_start_end_fade")
    g.connect(surface_alpha, 2, alpha, 0)
    g.connect(alpha_pin, 1, alpha, 1)
    g.connect(alpha, 2, "fc4cc376e24647ecb5e98bcd09c113d8", 0)

    # Foam is weak base-color pigment only; the selected depth also drives shallow color and opacity.
    foam_depth = g.property("_FoamDepth", "Shallow Foam Depth (m)", .35, (.05, 1.5))
    foam_strength = g.property("_FoamStrength", "Shallow Foam Coverage", .14, (0, .35))
    foam_scale = g.property("_FoamScale", "Foam Noise Per Metre", 1.7, (.2, 6))
    foam_colour = g.property("_FoamColor", "Foam Pigment (non emissive)", {"r": .47, "g": .53, "b": .50, "a": 1.0})
    shallow = g.clone_node(divide_template, "depth_over_foam_depth")
    g.connect(selected_depth, 3, shallow, 0)
    g.connect(foam_depth, 0, shallow, 1)
    shallow_clamp = g.clone_node(saturate_template, "clamp_shallow_depth")
    g.connect(shallow, 2, shallow_clamp, 0)
    shallow_inverse = g.clone_node(subtract_template, "one_minus_shallow_depth")
    g.value(shallow_inverse, 0, 1)
    g.connect(shallow_clamp, 1, shallow_inverse, 1)
    foam_noise = g.clone_node("999fbea241e74569a8930dece7023636", "fragmented_foam_noise")
    g.connect(tiling_template, 3, foam_noise, 0)
    g.connect(foam_scale, 0, foam_noise, 1)
    noise_offset = g.clone_node(subtract_template, "foam_noise_threshold")
    g.connect(foam_noise, 2, noise_offset, 0)
    g.value(noise_offset, 1, .53)
    noise_softness = g.clone_node(divide_template, "foam_noise_soft_threshold")
    g.connect(noise_offset, 2, noise_softness, 0)
    g.value(noise_softness, 1, .20)
    mask = g.clone_node(saturate_template, "fragmented_foam_mask")
    g.connect(noise_softness, 2, mask, 0)
    shallow_noise = g.clone_node(mul_template, "shallow_fragmented_foam")
    g.connect(shallow_inverse, 2, shallow_noise, 0)
    g.connect(mask, 1, shallow_noise, 1)
    weak_foam = g.clone_node(mul_template, "restrained_foam_coverage")
    g.connect(shallow_noise, 2, weak_foam, 0)
    g.connect(foam_strength, 0, weak_foam, 1)
    foam_lerp = g.clone_node("b8450f7b69cc4606af7d1178c28d66a3", "non_emissive_foam_colour")
    g.connect("d2f219318f3d4cb0b5ef1eeb1100773d", 2, foam_lerp, 0)
    g.connect(foam_colour, 0, foam_lerp, 1)
    g.connect(weak_foam, 2, foam_lerp, 2)
    g.connect(foam_lerp, 3, "e8bc2e5541194438bb32a72ff892775d", 0)
    smoothness = g.property("_RiverSmoothness", "Water Smoothness", .72, (0, 1))
    metallic = g.property("_RiverMetallic", "Water Metallic", .02, (0, 1))
    g.connect(smoothness, 0, "c9b59bc6f9214863b09ba9c6db4c4497", 0)
    g.connect(metallic, 0, "9514096d6b7b406f9cc97cfd4e114c8f", 0)

    defaults = {"_Amplitude_Wave": .025, "_Frequency_Wave": 1.1, "_Speed_Wave": .8,
                "_Scale_Noise": .65, "_Max_Depth": .7, "_Fall_Off": .9, "_Refration_Intensity": .008}
    for obj in g.objects:
        reference = obj.get("m_DefaultReferenceName")
        if reference in defaults:
            obj["m_Value"] = defaults[reference]
        if reference == "_Shallow_Water_Color":
            obj["m_Value"] = {"r": .19, "g": .24, "b": .23, "a": .6}
        if reference == "_Deep_Water_Color":
            obj["m_Value"] = {"r": .10, "g": .13, "b": .12, "a": .65}
    # Drop superseded disconnected branches from the old diagonal/adversarial panners.
    output_nodes = [ref["m_Id"] for key in ("m_VertexContext", "m_FragmentContext") for ref in g.graph[key]["m_Blocks"]]
    active = set(output_nodes)
    while True:
        prior = len(active)
        for edge in g.graph["m_Edges"]:
            if edge["m_InputSlot"]["m_Node"]["m_Id"] in active:
                active.add(edge["m_OutputSlot"]["m_Node"]["m_Id"])
        if len(active) == prior:
            break
    all_nodes = {ref["m_Id"] for ref in g.graph["m_Nodes"]}
    removed = all_nodes - active
    removed_slots = {ref["m_Id"] for key in removed for ref in g.by_id[key]["m_Slots"]}
    used_properties = {g.by_id[key]["m_Property"]["m_Id"] for key in active if "m_Property" in g.by_id[key]}
    removed_properties = {ref["m_Id"] for ref in g.graph["m_Properties"]} - used_properties
    g.graph["m_Nodes"] = [ref for ref in g.graph["m_Nodes"] if ref["m_Id"] in active]
    g.graph["m_Edges"] = [edge for edge in g.graph["m_Edges"] if edge["m_OutputSlot"]["m_Node"]["m_Id"] in active and edge["m_InputSlot"]["m_Node"]["m_Id"] in active]
    g.graph["m_Properties"] = [ref for ref in g.graph["m_Properties"] if ref["m_Id"] in used_properties]
    for obj in g.objects:
        if obj["m_Type"].endswith("CategoryData"):
            obj["m_ChildObjectList"] = [ref for ref in obj["m_ChildObjectList"] if ref["m_Id"] in used_properties]
    result = [obj for obj in g.objects if obj["m_ObjectId"] not in removed | removed_slots | removed_properties]
    # Every object and property gets an asset-local deterministic identity; source identities are never shared accidentally.
    mapping = {obj["m_ObjectId"]: uid("copy:" + obj["m_ObjectId"]) for obj in result}
    def remap(value):
        if isinstance(value, dict):
            return {key: remap(item) for key, item in value.items()}
        if isinstance(value, list):
            return [remap(item) for item in value]
        return mapping.get(value, value) if isinstance(value, str) else value
    result = remap(result)
    for obj in result:
        if "m_Guid" in obj:
            obj["m_Guid"]["m_GuidSerialized"] = str(uuid.uuid5(NAMESPACE, "guid:" + obj["m_ObjectId"]))
    return result, {"new_properties": dict(g.properties), "default_overrides": defaults, "removed_old_nodes": len(removed)}


def validate(objects: list[dict]) -> dict:
    by_id = {obj["m_ObjectId"]: obj for obj in objects}
    assert len(by_id) == len(objects), "Duplicate object identity"
    graph = objects[0]
    nodes = {ref["m_Id"] for ref in graph["m_Nodes"]}
    incoming, outgoing, occupied = {key: [] for key in nodes}, {key: [] for key in nodes}, set()
    def refs(value):
        if isinstance(value, dict):
            for key, item in value.items():
                if key == "m_Id" and isinstance(item, str) and item:
                    assert item in by_id, "Dangling reference: " + item
                refs(item)
        elif isinstance(value, list):
            for item in value:
                refs(item)
    refs(objects)
    for edge in graph["m_Edges"]:
        a, b = edge["m_OutputSlot"], edge["m_InputSlot"]
        source, target = a["m_Node"]["m_Id"], b["m_Node"]["m_Id"]
        assert source in nodes and target in nodes
        for endpoint, kind in ((a, 1), (b, 0)):
            slots = [by_id[ref["m_Id"]] for ref in by_id[endpoint["m_Node"]["m_Id"]]["m_Slots"]]
            assert any(slot["m_Id"] == endpoint["m_SlotId"] and slot["m_SlotType"] == kind for slot in slots), "Bad typed port"
        port = (target, b["m_SlotId"])
        assert port not in occupied, "Two edges feed one input"
        occupied.add(port)
        incoming[target].append(source)
        outgoing[source].append(target)
    done, queue = set(), [key for key in nodes if not incoming[key]]
    while queue:
        node = queue.pop()
        if node in done:
            continue
        done.add(node)
        queue.extend(target for target in outgoing[node] if all(key in done for key in incoming[target]))
    assert done == nodes, "Graph contains a cycle"
    def reaches(start):
        seen, pending = set(start), list(start)
        while pending:
            for target in outgoing[pending.pop()]:
                if target not in seen:
                    seen.add(target)
                    pending.append(target)
        return {by_id[key].get("m_Name") for key in seen}
    property_ids = {obj.get("m_DefaultReferenceName"): obj["m_ObjectId"] for obj in objects if "ShaderProperty" in obj["m_Type"]}
    def property_nodes(reference):
        return [key for key in nodes if by_id[key].get("m_Property", {}).get("m_Id") == property_ids[reference]]
    assert not any(obj["m_Type"].endswith("TimeNode") for obj in objects)
    time_outputs = reaches(property_nodes("_EffectTime"))
    assert {"VertexDescription.Position", "SurfaceDescription.NormalTS", "SurfaceDescription.BaseColor"}.issubset(time_outputs)
    foam_outputs = reaches(property_nodes("_FoamStrength"))
    assert "SurfaceDescription.BaseColor" in foam_outputs and "SurfaceDescription.Emission" not in foam_outputs
    pin_nodes = [key for key in nodes if by_id[key]["m_Type"].endswith("VertexColorNode")]
    assert {"VertexDescription.Position", "SurfaceDescription.Alpha", "SurfaceDescription.BaseColor"}.issubset(reaches(pin_nodes))
    assert {"SurfaceDescription.Alpha", "SurfaceDescription.BaseColor"}.issubset(reaches(property_nodes("_UseSceneDepth")))
    assert by_id[property_ids["_UseSceneDepth"]]["m_Value"] == 0.0
    assert any(obj["m_Type"].endswith("BranchNode") for obj in objects)
    assert any(obj["m_Type"].endswith("ComparisonNode") and obj.get("m_ComparisonType") == 4 for obj in objects)
    assert any(obj["m_Type"].endswith("SceneDepthNode") for obj in objects)
    assert any(obj.get("m_FunctionName") == "MacroVerticalWaterDepth" for obj in objects)
    assert any(obj["m_Type"].endswith("SceneColorNode") for obj in objects)
    assert any(obj["m_Type"].endswith("UniversalLitSubTarget") for obj in objects)
    return {"nodes": len(nodes), "edges": len(graph["m_Edges"]), "properties": sorted(property_ids),
            "single_effect_clock": True, "vertex_color_pinned": True, "vertex_color_alpha_fade": True,
            "non_emissive_foam": True, "default_depth_source": "baked_terrain_collider_vertex_green_times_8m",
            "world_vertical_scene_depth": "diagnostic_optional_not_render_verified", "depth_selection": "native_comparison_and_branch_not_lerp",
            "typed_ports_acyclic_references": "PASS", "unity_compilation": "NOT_RUN"}


def main() -> None:
    original = SOURCE.read_bytes()
    original_hash = hashlib.sha256(original).hexdigest()
    result, changes = build(parse(original.decode("utf-8-sig")))
    report = validate(result)
    serialized = "\n\n".join(json.dumps(obj, indent=4, ensure_ascii=False) for obj in result) + "\n"
    validate(parse(serialized))
    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    DESTINATION.write_text(serialized, encoding="utf-8", newline="\n")
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == original_hash, "Source was modified"
    print(json.dumps({"output": str(DESTINATION), "source_sha256": original_hash,
                      "output_sha256": hashlib.sha256(DESTINATION.read_bytes()).hexdigest(), **report, **changes}, indent=2))


if __name__ == "__main__":
    main()
