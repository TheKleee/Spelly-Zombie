# Spelly Zombie — bean character blockout (Blender 4.x)
#
# Builds the goofy T-pose bean: egg body, big head, stub arms with mitten
# hands, boot feet — then voxel-remeshes everything into ONE hand-molded
# blob (the brand: deliberately not good-looking) and adds Fat/Skinny
# shape keys. Exports spelly_bean.fbx next to this script.
#
# RUN (either way):
#   A) Command line:  blender --background --python spelly_characters.py
#   B) In Blender:    Scripting tab → Open this file → Run Script
#
# THEN (the whole rigging step, ~2 min, free):
#   1. Go to mixamo.com → Upload Character → spelly_bean.fbx
#   2. Place the markers (chin, wrists, elbows, knees, groin) → Auto-rig
#   3. Pick animations (Idle, Walking, Zombie Walk, Punching…) →
#      Download as FBX (with skin for the first one, without for the rest)
#   4. Unity: import, Rig → Humanoid. Done — no manual rigging ever.
#
# Marko's creative pass: run once, then sculpt the result in Blender
# (Sculpt mode, the blob loves it), re-export, re-Mixamo. Tweak the
# PROPORTIONS below for variants (bigger zombies = bigger numbers).

import bpy
import os
from math import radians

# ---- PROPORTIONS (meters) — the whole character is these numbers -------
BODY_CENTER = 0.75
BODY_RADIUS = 0.32
BODY_STRETCH = 1.25   # egg-ness
HEAD_CENTER = 1.38
HEAD_RADIUS = 0.30    # deliberately too big
ARM_Y = 1.02          # shoulder height
ARM_REACH = 0.78      # T-pose fingertip distance from center
ARM_RADIUS = 0.09
HAND_RADIUS = 0.14    # mittens
LEG_X = 0.14
LEG_TOP = 0.5
LEG_RADIUS = 0.11
FOOT_RADIUS = 0.14
VOXEL = 0.055         # remesh size: smaller = smoother, heavier
BELLY_KEY = 0.14      # how far the Fat/Skinny keys push

# ---- scene reset --------------------------------------------------------
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

parts = []

def sphere(name, loc, r, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=24, ring_count=16)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    parts.append(o)
    return o

def cylinder(name, loc, r, depth, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=depth, location=loc,
                                        rotation=rot, vertices=16)
    o = bpy.context.active_object
    o.name = name
    parts.append(o)
    return o

# ---- the bean ------------------------------------------------------------
sphere("Body", (0, 0, BODY_CENTER), BODY_RADIUS, (1, 0.92, BODY_STRETCH))
sphere("Head", (0, 0, HEAD_CENTER), HEAD_RADIUS, (1, 0.95, 1.05))

for side in (-1, 1):
    s = "L" if side < 0 else "R"
    # T-pose arms: straight out along X, so Mixamo's auto-rigger is happy
    cylinder(f"Arm{s}", (side * (BODY_RADIUS + ARM_REACH - BODY_RADIUS) / 2 + side * 0.16,
                         0, ARM_Y),
             ARM_RADIUS, ARM_REACH - 0.16, rot=(0, radians(90), 0))
    sphere(f"Hand{s}", (side * ARM_REACH, 0, ARM_Y), HAND_RADIUS)
    cylinder(f"Leg{s}", (side * LEG_X, 0, LEG_TOP / 2 + 0.07), LEG_RADIUS, LEG_TOP)
    sphere(f"Foot{s}", (side * LEG_X, -0.05, 0.09), FOOT_RADIUS, (1, 1.5, 0.75))

# ---- merge into ONE hand-molded blob --------------------------------------
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
bean = bpy.context.active_object
bean.name = "SpellyBean"

remesh = bean.modifiers.new("Remesh", 'REMESH')
remesh.mode = 'VOXEL'
remesh.voxel_size = VOXEL
bpy.ops.object.modifier_apply(modifier=remesh.name)
bpy.ops.object.shade_smooth()

# ---- Fat / Skinny shape keys (the roster is one bean + these dials) -------
bean.shape_key_add(name="Basis")
belly = (0, 0, BODY_CENTER - 0.05)
for key_name, direction in (("Fat", 1.0), ("Skinny", -0.8)):
    key = bean.shape_key_add(name=key_name, from_mix=False)
    for pt in key.data:
        dx = pt.co.x - belly[0]
        dy = pt.co.y - belly[1]
        dz = (pt.co.z - belly[2]) * 1.4          # belly zone, not the head
        d = (dx * dx + dy * dy + dz * dz) ** 0.5
        if d < BODY_RADIUS * 1.35 and d > 1e-4:
            w = 1.0 - d / (BODY_RADIUS * 1.35)   # soft falloff
            pt.co.x += (dx / d) * BELLY_KEY * w * direction
            pt.co.y += (dy / d) * BELLY_KEY * w * direction

# ---- one flat material (Marko recolors) ------------------------------------
mat = bpy.data.materials.new("SpellyBeanSkin")
mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.93, 0.86, 0.72, 1)
bean.data.materials.append(mat)

# ---- export next to this script (or Desktop from the Scripting tab) --------
try:
    out_dir = os.path.dirname(os.path.abspath(__file__))
except NameError:
    out_dir = os.path.join(os.path.expanduser("~"), "Desktop")
out_path = os.path.join(out_dir, "spelly_bean.fbx")

bpy.ops.object.select_all(action='DESELECT')
bean.select_set(True)
bpy.ops.export_scene.fbx(filepath=out_path, use_selection=True,
                         add_leaf_bones=False, apply_scale_options='FBX_SCALE_ALL')
print(f"[SpellyZombie] exported {out_path}")
