"""Dense contact recheck of hand corrective derivative against unchanged real brush."""
from pathlib import Path
source=Path(__file__).with_name('validate_refined_hand_contact.py').read_text()
source=source.replace("SOURCE=ART/'DosaV2_ArmsWrapped.blend';PREFIX='wrapped'", "SOURCE=ART/'DosaV2_HandsHarmonicProbe.blend';PREFIX='self-fold-corrected'")
source=source.replace("bpy.context.view_layer.update();grip=bpy.data.objects['GripSocket'];handle=bpy.data.objects['DosaBrushV2_Handle']", """with bpy.data.libraries.load(str(ART/'DosaBrushV2.blend'),link=False) as(src,dst):dst.objects=[name for name in src.objects if name.startswith('DosaBrushV2_') or name in ['GripSocket','TipSocket']]
for o in dst.objects:
    if o and not o.users_collection:bpy.context.scene.collection.objects.link(o)
bpy.context.view_layer.update();grip=bpy.data.objects['GripSocket'];handle=bpy.data.objects['DosaBrushV2_Handle']""")
exec(compile(source,'dense_contact','exec'))
