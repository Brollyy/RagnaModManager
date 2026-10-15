"""Replace a stock Default Hammer head while retaining every donor UV channel.

Run from Blender 4.x with:
  blender --background --python build_stock_based_azure_fbx.py -- donor.fbx output.fbx

Export the donor as one FBX static mesh with all UV channels and material sections.
"""

import sys
from pathlib import Path

import bpy

ATLAS = {"cyan": (0.08, 0.08, 0.42, 0.42), "gold": (0.58, 0.08, 0.92, 0.42),
         "navy": (0.08, 0.58, 0.42, 0.92), "ice": (0.58, 0.58, 0.92, 0.92)}
SLOTS = ["M_Character", "M_JaugeManche", "M_JaugeTete"]


def add_quad(vertices, channel_count, faces, points, region):
    u0, v0, u1, v1 = ATLAS[region]
    coords = [(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
    start=len(vertices)
    vertices.extend(points)
    for corner_ids in ((0,1,2),(0,2,3)):
        indices=tuple(start+i for i in corner_ids)
        channel_uvs=[]
        for channel in range(channel_count):
            channel_uvs.append([coords[i] for i in corner_ids] if channel==0 else [(0.5,0.5)]*3)
        faces.append(("M_Character",indices,channel_uvs))


def main():
    args=sys.argv[sys.argv.index("--")+1:]
    if len(args)!=2: raise SystemExit("Expected donor FBX and output FBX paths after --")
    donor,output=map(Path,args)
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(donor),use_anim=False,automatic_bone_orientation=False)
    objects=[o for o in bpy.context.scene.objects if o.type=="MESH"]
    if len(objects)!=1: raise RuntimeError(f"Expected one donor mesh; imported {len(objects)}")
    source=objects[0]; source_mesh=source.data
    if len(source_mesh.uv_layers)<4:
        raise RuntimeError(f"Donor must retain four UV channels; got {len(source_mesh.uv_layers)}")
    # Blender adds numeric suffixes when the donor was previously opened or
    # round-tripped in the same project. Normalize only Blender's exact
    # .NNN suffix before checking and exporting section names.
    for slot in source.material_slots:
        if slot.material:
            import re
            slot.material.name = re.sub(r"\.\d{3}$", "", slot.material.name)
    material_names=[slot.material.name if slot.material else "" for slot in source.material_slots]
    if material_names!=SLOTS:
        raise RuntimeError(f"Expected material slots {SLOTS}; got {material_names}")
    donor_materials=[slot.material for slot in source.material_slots]

    vertices=[tuple(source.matrix_world @ v.co) for v in source_mesh.vertices]
    channel_count=len(source_mesh.uv_layers)
    faces=[]
    for polygon in source_mesh.polygons:
        slot=material_names[polygon.material_index]
        loops=list(polygon.loop_indices)
        ids=[source_mesh.loops[i].vertex_index for i in loops]
        if slot=="M_Character" and max(vertices[i][2] for i in ids)>=37.5:
            continue
        channel_uvs=[]
        for channel,layer in enumerate(source_mesh.uv_layers):
            values=[]
            for loop_index in loops:
                uv=tuple(layer.data[loop_index].uv)
                if channel==0 and slot=="M_Character":
                    uv=((ATLAS["navy"][0]+ATLAS["navy"][2])/2,
                        (ATLAS["navy"][1]+ATLAS["navy"][3])/2)
                values.append(uv)
            channel_uvs.append(values)
        faces.append((slot,tuple(ids),channel_uvs))

    # Append blockhead facets as new geometry, carrying four UV sets.
    x0,x1,yf,yb,z0,z1=-16.0,16.0,-13.25,13.5,37.5,55.25
    def quad(points,region): add_quad(vertices,channel_count,faces,points,region)
    quad([(x0,yf,z0),(x1,yf,z0),(x1,yf,z1),(x0,yf,z1)],"cyan")
    for xa,xb in ((-14.0,-11.5),(11.5,14.0)):
        quad([(xa,yf-.01,39.0),(xb,yf-.01,39.0),(xb,yf-.01,53.5),(xa,yf-.01,53.5)],"gold")
    quad([(x1,yb,z0),(x0,yb,z0),(x0,yb,z1),(x1,yb,z1)],"navy")
    quad([(x0,yb,z0),(x0,yf,z0),(x0,yf,z1),(x0,yb,z1)],"navy")
    quad([(x1,yf,z0),(x1,yb,z0),(x1,yb,z1),(x1,yf,z1)],"ice")
    quad([(x0,yf,z1),(x1,yf,z1),(x1,yb,z1),(x0,yb,z1)],"ice")
    quad([(x0,yb,z0),(x1,yb,z0),(x1,yf,z0),(x0,yf,z0)],"navy")

    bpy.data.objects.remove(source,do_unlink=True)
    mesh=bpy.data.meshes.new("SM_AzureBlockhead")
    mesh.from_pydata(vertices,[],[idx for _,idx,_ in faces]); mesh.update(calc_edges=True)
    obj=bpy.data.objects.new("SM_AzureBlockhead",mesh); bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj; obj.select_set(True)
    # Reuse the imported FBX material datablocks. Creating new materials with
    # the same names makes Blender suffix them with _001, which UE then imports
    # as different material slot names and breaks stock gauge binding.
    for material in donor_materials:
        mesh.materials.append(material)
    for poly,(slot,_,_) in zip(mesh.polygons,faces):
        poly.material_index=SLOTS.index(slot); poly.use_smooth=False
    for channel in range(channel_count):
        layer=mesh.uv_layers.new(name=f"UVChannel_{channel}")
        for poly,(_,_,channel_uvs) in zip(mesh.polygons,faces):
            for corner,loop_index in enumerate(poly.loop_indices):
                layer.data[loop_index].uv=channel_uvs[channel][corner]
    output.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(output),use_selection=True,object_types={"MESH"},
        use_mesh_modifiers=False,mesh_smooth_type="OFF",use_custom_props=False,path_mode="AUTO",
        axis_forward="-Z",axis_up="Y",use_triangles=True,bake_anim=False,embed_textures=False,
        apply_unit_scale=True)
    print(f"Exported {output}: {len(mesh.vertices)} vertices, {len(mesh.polygons)} polygons, {len(mesh.uv_layers)} UV channels")


if __name__=="__main__": main()
