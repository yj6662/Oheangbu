# 프리팹 참조 복구 기록

원본 FBX의 실제 노드와 .meta 이름 표를 대조했다. 같은 FBX의 동일 이름 메시·재질만 연결하며, 원본에 없는 모델·재질은 임의 생성하지 않는다.

- `Assets/KoreanTraditionalFestival/Prefabs/SM_FoodMesh.prefab` — **REPAIRED_SINGLE_LOD_AND_COLLIDER**
  - 근거: Binary FBX contains exactly one Geometry and one LOD0 Mesh Model; LOD1-3 and ConvexHulls are absent. Prefab and importer name table still contain obsolete local file IDs 4300002/4/6/8.
  - 한계/후속: Use the existing source LOD0, preserve the previous last LOD's cull threshold and collider convex/cooking settings. No substitute LOD geometry is generated.
- `Assets/KoreanTraditionalFestival/Prefabs/SM_GalaeShovel_NoRope.prefab` — **REPAIRED_SINGLE_LOD_AND_COLLIDER**
  - 근거: Binary FBX contains exactly one Geometry and one LOD0 Mesh Model; LOD1-3 and ConvexHulls are absent. Prefab and importer name table still contain obsolete local file IDs 4300002/4/6/8.
  - 한계/후속: Use the existing source LOD0, preserve the previous last LOD's cull threshold and collider convex/cooking settings. No substitute LOD geometry is generated.
- `Assets/HwaseongForteressGate/Prefabs/SM_Bastion_Parapet.prefab` — **UNRESOLVED_SOURCE_MATERIAL**
  - 근거: Source FBX and importer name table identify slot 2 as WorldGridMaterial; reconnect only an exact named embedded material from that same FBX.
  - 한계/후속: Original slot is Unreal's WorldGridMaterial. Its external material and Engine grid textures are absent. An exact embedded match is source fallback geometry coverage, not recovered stone artwork. Exact embedded match count=0; available names=
- `Assets/SeyeonjeongPavilion/Prefabs/SM_PIllar_2.prefab` — **REPAIRED_REFERENCE_SOURCE_APPEARANCE_REVIEW**
  - 근거: Source FBX and importer name table identify slot 1 as WorldGridMaterial; reconnect only an exact named embedded material from that same FBX.
  - 한계/후속: The missing second slot on all four LODs is Unreal's WorldGridMaterial. Engine grid textures are absent; the embedded source fallback still needs artistic review before scene placement.
- `Assets/SeyeonjeongPavilion/Prefabs/SM_Landscape_0.prefab` — **REPAIRED_REFERENCE_SOURCE_APPEARANCE_REVIEW**
  - 근거: Source FBX and importer name table identify slot 0 as MI_Landscape_blend(MaterialInstanceDynamic_69_52357) / MaterialInstanceDynamic_69; reconnect only an exact named embedded material from that same FBX.
  - 한계/후속: The external Dynamic_69 material GUID is missing. Reconnect only the matching embedded FBX material. Original Unreal terrain layer blending was not preserved by the exported URP/Lit material; reference repair does not restore those layer weights or the original landscape appearance.
- `Assets/SeyeonjeongPavilion/Prefabs/SM_Landscape_1.prefab` — **REPAIRED_REFERENCE_SOURCE_APPEARANCE_REVIEW**
  - 근거: Source FBX and importer name table identify slot 0 as MI_Landscape_blend(MaterialInstanceDynamic_68_52155) / MaterialInstanceDynamic_68; reconnect only an exact named embedded material from that same FBX.
  - 한계/후속: The external Dynamic_68 material GUID is missing. Reconnect only the matching embedded FBX material. Original Unreal terrain layer blending was not preserved by the exported URP/Lit material; reference repair does not restore those layer weights or the original landscape appearance.
- `Assets/HwaseongHaenggung/Prefabs/SM_SkySphere.prefab` — **UNRESOLVED_SOURCE_MESH_AND_MATERIAL**
  - 근거: Mesh GUID abc00000000003283870018903009347 and material GUID abc00000000010257273874139969994 have no matching asset meta in Assets.
  - 한계/후속: Incomplete exported sky utility. Preserve the prefab and exclude it from world placement; do not substitute an arbitrary sphere or count this item as visually inspected geometry.
- `Assets/SeyeonjeongPavilion/Prefabs/Plane.prefab` — **UNRESOLVED_SOURCE_MESH**
  - 근거: Mesh GUID abc00000000016999198234246839076 has no matching asset meta in Assets. The existing material GUID resolves to SeyeonjeongPavilion/Material/Ground/MI_Water.mat.
  - 한계/후속: Water-plane utility with a 1 x 0 x 1 box collider. Preserve the prefab and classify as missing-source water utility. The separate Haenggung Plane.fbx has a different GUID and is not a proven matching source.
