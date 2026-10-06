# Launch helper: enable the BlenderMCP addon and start its socket server after the UI is ready.
import bpy, addon_utils
try:
    addon_utils.enable("addon", default_set=True, persistent=True)
except Exception as e:
    print("addon enable:", e)
def _start():
    try:
        bpy.ops.blendermcp.start_server()
        print("BlenderMCP started")
    except Exception as e:
        print("start failed:", e); return 2.0
    return None
bpy.app.timers.register(_start, first_interval=3.0)
