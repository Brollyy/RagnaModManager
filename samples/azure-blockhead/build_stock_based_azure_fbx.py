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
# UE 4.27's FBX importer reorders UV layers and changes V orientation. The
# cooked-channel to FBX-layer order is stable in the measured 4.27 import, but
# V orientation can differ after Blender round trips. Find the donor layer and
# V orientation for each stock cooked channel by its measured bounds.
STOCK_GAUGE_UV_BOUNDS = {
    0: {"M_JaugeManche": (0.577, 0.144, 0.702, 0.166), "M_JaugeTete": (0.621, 0.136, 0.655, 0.172)},
    1: {"M_JaugeManche": (0.363, 0.003, 0.580, 0.997), "M_JaugeTete": (0.000, 0.004, 1.000, 1.001)},
    2: {"M_JaugeManche": (0.669, 0.008, 0.963, 0.075), "M_JaugeTete": (0.831, 0.379, 0.955, 0.504)},
    3: {"M_JaugeManche": (0.181, 0.801, 0.523, 0.875), "M_JaugeTete": (0.676, 0.033, 0.821, 0.180)},
}
UE_FBX_LAYER_FOR_COOKED_CHANNEL = (1, 2, 3, 0)
COOKED_BODY_UV_SOURCE_LAYER = 1


def uv_bounds(mesh, material_index, uv_layer, flip_v=False, engine_flip_v=False):
    u_values=[]
    v_values=[]
    for polygon in mesh.polygons:
        if polygon.material_index != material_index:
            continue
        for loop_index in polygon.loop_indices:
            uv=uv_layer.data[loop_index].uv
            u_values.append(float(uv.x))
            v=float(uv.y)
            if flip_v:
                v=1.0-v
            if engine_flip_v:
                v=1.0-v
            v_values.append(v)
    if not u_values:
        raise RuntimeError("No gauge UV vertices for material index " + str(material_index))
    return min(u_values),min(v_values),max(u_values),max(v_values)


def find_stock_gauge_uv_sources(mesh, material_names):
    tolerance=0.002
    result=[]
    for cooked_channel,targets in STOCK_GAUGE_UV_BOUNDS.items():
        engine_flip_v=True
        matches=[]
        for source_channel in range(min(4,len(mesh.uv_layers))):
            for flip_source_v in (False,True):
                valid=True
                for slot_name,target in targets.items():
                    material_index=material_names.index(slot_name)
                    actual=uv_bounds(mesh,material_index,mesh.uv_layers[source_channel],flip_source_v,engine_flip_v)
                    if any(abs(actual[i]-target[i])>tolerance for i in range(4)):
                        valid=False
                        break
                if valid:
                    matches.append((source_channel,flip_source_v))
        if len(matches)!=1:
            raise RuntimeError(
                "Could not uniquely map the Default Hammer gauge UV channel "
                + str(cooked_channel) + "; candidates=" + repr(matches)
                + ". Check the donor game version and refresh STOCK_GAUGE_UV_BOUNDS."
            )
        result.append(matches[0])
    return result


def add_quad(vertices, channel_count, faces, points, region):
    u0, v0, u1, v1 = ATLAS[region]
    coords = [(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
    start=len(vertices)
    vertices.extend(points)
    for corner_ids in ((0,1,2),(0,2,3)):
        indices=tuple(start+i for i in corner_ids)
        channel_uvs=[]
        for channel in range(channel_count):
            # Keep every imported body UV channel non-degenerate. UE 4.27
            # emitted tangent-basis warnings when the added faces had constant
            # UVs in the donor's other channels, even though the cooked body
            # material samples the remapped channel. Gauge sections below
            # still retain their exact donor UV values.
            channel_uvs.append([coords[i] for i in corner_ids])
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
    gauge_uv_sources=find_stock_gauge_uv_sources(source_mesh,material_names)
    print("Stock cooked gauge UV source map: "+repr(gauge_uv_sources))
    donor_materials=[slot.material for slot in source.material_slots]

    vertices=[tuple(source.matrix_world @ v.co) for v in source_mesh.vertices]
    channel_count=len(source_mesh.uv_layers)
    faces=[]
    head_gauge_vertices={}
    head_gauge_source_ids={
        source_mesh.loops[loop_index].vertex_index
        for polygon in source_mesh.polygons
        if material_names[polygon.material_index] == "M_JaugeTete"
        for loop_index in polygon.loop_indices
    }
    head_gauge_center_x=(
        min(vertices[index][0] for index in head_gauge_source_ids)
        + max(vertices[index][0] for index in head_gauge_source_ids)
    )/2
    head_gauge_center_z=(
        min(vertices[index][2] for index in head_gauge_source_ids)
        + max(vertices[index][2] for index in head_gauge_source_ids)
    )/2
    for polygon in source_mesh.polygons:
        slot=material_names[polygon.material_index]
        loops=list(polygon.loop_indices)
        ids=[source_mesh.loops[i].vertex_index for i in loops]
        if slot=="M_Character" and max(vertices[i][2] for i in ids)>=37.5:
            continue
        if slot=="M_JaugeTete":
            # The donor shares some vertices between the head marker and the
            # body. Duplicate the section before a small scale/depth adjustment
            # to reduce z-fighting where the stock marker crosses a new face.
            offset_ids=[]
            for vertex_index in ids:
                if vertex_index not in head_gauge_vertices:
                    x,y,z=vertices[vertex_index]
                    head_gauge_vertices[vertex_index]=len(vertices)
                    vertices.append((
                        head_gauge_center_x+(x-head_gauge_center_x)*0.75,
                        y-0.75,
                        head_gauge_center_z+(z-head_gauge_center_z)*0.75,
                    ))
                offset_ids.append(head_gauge_vertices[vertex_index])
            ids=offset_ids
        channel_uvs=[]
        for channel,layer in enumerate(source_mesh.uv_layers):
            values=[]
            for loop_index in loops:
                if slot in ("M_JaugeManche", "M_JaugeTete") and channel < 4:
                    cooked_channel=UE_FBX_LAYER_FOR_COOKED_CHANNEL.index(channel)
                    source_channel, flip_v = gauge_uv_sources[cooked_channel]
                    uv=tuple(source_mesh.uv_layers[source_channel].data[loop_index].uv)
                    if flip_v:
                        uv=(uv[0], 1.0-uv[1])
                elif slot=="M_Character" and channel==COOKED_BODY_UV_SOURCE_LAYER:
                    original=tuple(layer.data[loop_index].uv)
                    u0,v0,u1,v1=ATLAS["navy"]
                    # Keep the donor body's UV island shape while fitting it
                    # inside the solid navy swatch. A single-point mapping
                    # looked correct on this flat tile but created degenerate
                    # tangents during the UE 4.27 FBX import.
                    uv=(u0+original[0]*(u1-u0),v0+original[1]*(v1-v0))
                else:
                    uv=tuple(layer.data[loop_index].uv)
                values.append(uv)
            channel_uvs.append(values)
        faces.append((slot,tuple(ids),channel_uvs))

    # Append blockhead facets as new geometry, carrying four UV sets.
    x0,x1,yf,yb,z0,z1=-16.0,16.0,-13.25,13.5,37.5,55.25
    def quad(points,region): add_quad(vertices,channel_count,faces,points,region)
    quad([(x0,yf,z0),(x1,yf,z0),(x1,yf,z1),(x0,yf,z1)],"cyan")
    # Keep the gold trim proud of the cyan face. Near-coplanar overlays caused
    # visible raster flicker/bleeding around the front details in the race.
    for xa,xb in ((-14.0,-11.5),(11.5,14.0)):
        quad([(xa,yf-.35,39.0),(xb,yf-.35,39.0),(xb,yf-.35,53.5),(xa,yf-.35,53.5)],"gold")
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
