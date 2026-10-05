import bpy, math
from pathlib import Path
OUT = Path(r"C:/Users/yj666/Oheangbu/Art/Characters/MineBoss306/Rig")
bpy.ops.wm.open_mainfile(filepath=str(OUT / "MineBoss306_rig.blend"))
rig = bpy.data.objects["MineBoss306_Rig"]; pb = rig.pose.bones
def rot(name, x=0, y=0, z=0):
    b = pb["mixamorig:" + name]; b.rotation_mode = 'XYZ'; b.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
rot("LeftArm", z=-35); rot("RightArm", z=35); rot("LeftForeArm", x=60); rot("RightForeArm", x=-40)
rot("LeftUpLeg", x=-40); rot("LeftLeg", x=60); rot("Spine1", x=10); rot("Head", z=25)
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x, sc.render.resolution_y = 700, 900
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.9
for view, loc, rot_ in (("front", (0, -6, 1.25), (math.radians(90), 0, 0)), ("threeq", (4.2, -4.2, 1.6), (math.radians(85), 0, math.radians(45)))):
    cam.location = loc; cam.rotation_euler = rot_
    sc.render.filepath = str(OUT / f"pose_{view}.png"); bpy.ops.render.render(write_still=True)
print("POSED")
