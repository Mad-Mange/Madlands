"""Check the Warden's back cloth in both long strides, at every level of detail.

Run after building KorrinWarden:
  blender -b art/blend/KorrinWarden.blend -P art/blender/check_warden_cloak.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import tripo

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for mesh in sorted((o for o in arm.children if o.type == "MESH"), key=lambda o: o.name):
    rest = [v.co.copy() for v in mesh.data.vertices]
    # The back cloth at the height where its weights used to abruptly change to the thigh.
    edges = [(a, b) for edge in mesh.data.edges for a, b in [edge.vertices]
             if 0.10 < (rest[a].x + rest[b].x) / 2 < 0.20
             and (rest[a].y + rest[b].y) / 2 > 0.22
             and 0.60 < (rest[a].z + rest[b].z) / 2 < 0.72
             and (rest[a] - rest[b]).length > 1e-5]
    if not edges:
        raise RuntimeError(mesh.name + ": no cloth edges found")
    for direction in (1, -1):
        tripo._reset_pose(arm)
        for bone, axis, degrees in tripo.TEST_POSES["stride"]:
            if "Leg" in bone:
                degrees *= direction
            tripo._turn(arm, bone, axis, degrees)
        evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        posed = evaluated.to_mesh()
        worst = max((posed.vertices[a].co - posed.vertices[b].co).length / (rest[a] - rest[b]).length for a, b in edges)
        evaluated.to_mesh_clear()
        print("%s stride %d: %d cloth edges, worst %.2fx" % (mesh.name, direction, len(edges), worst))
        if worst > 2.5:
            raise RuntimeError(mesh.name + ": back cloth stretched too far")
print("Warden cloak check passed")
