"""Replace a stock Default Hammer head while retaining every donor UV channel.

Run from Blender 4.x with:
  blender --background --python build_stock_based_azure_fbx.py -- donor.fbx output.fbx

Export the donor as one FBX static mesh with all UV channels and material sections.
"""

import sys
from pathlib import Path

import bpy

# Legacy atlas bounds are retained only for the unused donor body UV channels.
# New head surfaces use local UVs and one Texture2D material per tile.
ATLAS = {"cyan": (0.15625, 0.15625, 0.34375, 0.34375),
         "gold": (0.65625, 0.15625, 0.84375, 0.34375),
         "navy": (0.15625, 0.65625, 0.34375, 0.84375),
         "ice": (0.65625, 0.65625, 0.84375, 0.84375)}
STOCK_SLOTS = ["M_Character", "M_JaugeManche", "M_JaugeTete"]
SLOTS = STOCK_SLOTS + ["M_Azure_Cyan", "M_Azure_Gold", "M_Azure_Ice"]
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


def projected_uvs(points, u_axis, v_axis, u_bounds, v_bounds):
    """Project a mesh patch through the UV layout of its complete box face."""
    u0, u1 = u_bounds
    v0, v1 = v_bounds
    return [
        ((point[u_axis] - u0) / (u1 - u0), (point[v_axis] - v0) / (v1 - v0))
        for point in points
    ]


def body_uv_patch(corner_count, radius=0.001):
    """Return a small non-degenerate UV patch wholly inside the navy texture."""
    import math

    center_u = 0.5
    cooked_center_v = 0.5
    if corner_count == 3:
        offsets = ((-1.0, -1.0), (1.0, -1.0), (0.0, 1.0))
    else:
        offsets = tuple(
            (math.cos(2 * math.pi * i / corner_count), math.sin(2 * math.pi * i / corner_count))
            for i in range(corner_count)
        )
    # Store V pre-flipped because UE 4.27 flips FBX V during import.
    return [
        (center_u + du * radius, 1.0 - (cooked_center_v + dv * radius))
        for du, dv in offsets
    ]


def add_quad(vertices, channel_count, faces, points, region, projected_coords):
    # The FBX axis conversion and UE import rotation reverse the custom head's
    # horizontal axis. Reflect U within each tile so the cooked front reads
    # left-to-right correctly. This matches the checked-in FBX: at x=-10.5 its
    # front samples the tile's high-U edge, and at x=+16 its low-U edge.
    # The UVs match the previously runtime-checked atlas orientation after UE's
    # FBX V conversion. The head regions are separate, clamp-addressed
    # Texture2D assets, so local UVs prevent sampling beyond a tile at grazing
    # angles.
    material_slot = "M_Character" if region == "navy" else "M_Azure_" + region.title()
    coords = [(1.0 - s, t) for s, t in projected_coords]
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
        faces.append((material_slot,indices,channel_uvs))


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
    if material_names!=STOCK_SLOTS:
        raise RuntimeError(f"Expected stock donor material slots {STOCK_SLOTS}; got {material_names}")
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
        body_patch=body_uv_patch(len(loops)) if slot=="M_Character" else None
        for channel,layer in enumerate(source_mesh.uv_layers):
            values=[]
            for corner,loop_index in enumerate(loops):
                if slot in ("M_JaugeManche", "M_JaugeTete") and channel < 4:
                    cooked_channel=UE_FBX_LAYER_FOR_COOKED_CHANNEL.index(channel)
                    source_channel, flip_v = gauge_uv_sources[cooked_channel]
                    uv=tuple(source_mesh.uv_layers[source_channel].data[loop_index].uv)
                    if flip_v:
                        uv=(uv[0], 1.0-uv[1])
                elif slot=="M_Character":
                    original=tuple(layer.data[loop_index].uv)
                    u0,v0,u1,v1=ATLAS["navy"]
                    # The donor's first two channels collapse most body
                    # triangles, producing degenerate tangent bases in UE.
                    # Give those channels a tiny non-degenerate patch in the
                    # navy tile. The material samples cooked TexCoord[0]; the
                    # patch keeps every retained handle/body face navy while
                    # allowing UE to build valid tangents. Channels 2 and 3
                    # retain their donor layout remapped into the same tile.
                    if channel < 2:
                        uv=body_patch[corner]
                    else:
                        uv=(u0+original[0]*(u1-u0),1.0-(v0+original[1]*(v1-v0)))
                else:
                    uv=tuple(layer.data[loop_index].uv)
                if slot=="M_Character":
                    # Retained body surfaces sample the standalone navy tile
                    # with their small cooked-channel-0 patch. Unused donor
                    # channels 2 and 3 retain their old in-atlas coordinates.
                    if channel < 2 and not (0.0 <= uv[0] <= 1.0 and 0.0 <= uv[1] <= 1.0):
                        raise RuntimeError(f"Retained body UV escaped the navy texture in channel {channel}: {uv}")
                values.append(uv)
            channel_uvs.append(values)
        faces.append((slot,tuple(ids),channel_uvs))

    # Append blockhead facets as new geometry, carrying four UV sets.
    # The marker plane is the hammer-head end, so make the head's long axis
    # run into the mesh along Y. A narrow end face keeps the gauge from reading
    # as a marker painted onto the broad side of the head.
    # Match the stock Default Hammer head body bounds. The donor's head body
    # spans X=-16..16, Y=-13.6..13.5, and Z=37.5..55.25; the front face sits
    # slightly proud of the stock gauge at Y=-15.875. Extending the back face
    # to Y=25 made the custom head over 40 units deep, so its two textured
    # faces appeared detached and crossed one another as the preview turned.
    x0,x1,yf,yb,z0,z1=-16.0,16.0,-15.875,13.5,37.5,55.25
    # Move the stock marker forward to reduce its overlap with the atlas face
    # while keeping its center close to the face plane at oblique viewing angles.
    marker_vertices=[vertices[index] for index in head_gauge_vertices.values()]
    if marker_vertices:
        marker_front_y=min(point[1] for point in marker_vertices)
        marker_shift_y=yf-0.5-marker_front_y
        for index in head_gauge_vertices.values():
            x,y,z=vertices[index]
            vertices[index]=(x,y+marker_shift_y,z)
    def quad(points, region, uv_projection):
        add_quad(vertices, channel_count, faces, points, region, uv_projection(points))

    def front_uv(points):
        return projected_uvs(points, 0, 2, (x0, x1), (z0, z1))

    def back_uv(points):
        return projected_uvs(points, 0, 2, (x0, x1), (z0, z1))

    def side_uv(points):
        return projected_uvs(points, 1, 2, (yf, yb), (z0, z1))

    def top_uv(points):
        return projected_uvs(points, 0, 1, (x0, x1), (yf, yb))

    def bottom_uv(points):
        return projected_uvs(points, 0, 1, (x0, x1), (yf, yb))

    def full_tile_uv(points):
        return [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
    # Leave a pocket around the stock head gauge rather than laying the bright
    # custom face behind its faceted geometry. Derive the opening from the
    # transformed gauge geometry and leave clearance for oblique views.
    hole_x0,hole_x1,hole_z0,hole_z1=-5.9,5.9,38.25,49.4
    pocket_y=-13.25
    marker_vertices=[vertices[index] for index in head_gauge_vertices.values()]
    if marker_vertices:
        # The exported stock marker spans about 9.3 x 8.9 units. The clearance
        # around its projected silhouette keeps most of the front texture
        # visible while limiting marker overlap at oblique angles.
        clearance=1.25
        hole_x0=max(x0+0.25,min(point[0] for point in marker_vertices)-clearance)
        hole_x1=min(x1-0.25,max(point[0] for point in marker_vertices)+clearance)
        hole_z0=max(z0+0.25,min(point[2] for point in marker_vertices)-clearance)
        hole_z1=min(z1-0.25,max(point[2] for point in marker_vertices)+clearance)
    quad([(x0,yf,z0),(x1,yf,z0),(x1,yf,hole_z0),(x0,yf,hole_z0)],"cyan",front_uv)
    quad([(x0,yf,hole_z1),(x1,yf,hole_z1),(x1,yf,z1),(x0,yf,z1)],"cyan",front_uv)
    quad([(x0,yf,hole_z0),(hole_x0,yf,hole_z0),(hole_x0,yf,hole_z1),(x0,yf,hole_z1)],"cyan",front_uv)
    quad([(hole_x1,yf,hole_z0),(x1,yf,hole_z0),(x1,yf,hole_z1),(hole_x1,yf,hole_z1)],"cyan",front_uv)
    quad([(hole_x0,pocket_y,hole_z0),(hole_x1,pocket_y,hole_z0),
          (hole_x1,pocket_y,hole_z1),(hole_x0,pocket_y,hole_z1)],"navy",front_uv)
    # Close the deep opening around the retained head marker. Without these
    # four inner walls, oblique views look through the clearance gap and reveal
    # the dark scene behind the head. Map each wall within the navy atlas tile.
    def cavity_solid_uvs(points, u_axis, v_axis, u_bounds, v_bounds):
        # Sample a small patch at the center of the navy tile. Mapping the full
        # tile onto a narrow cavity wall stretches its line art into prominent
        # stripes when the preview turns obliquely.
        coords = projected_uvs(points, u_axis, v_axis, u_bounds, v_bounds)
        return [(0.5 + (u - 0.5) * 0.04, 0.5 + (v - 0.5) * 0.04) for u, v in coords]

    def cavity_xy(points):
        return cavity_solid_uvs(points, 0, 1, (hole_x0, hole_x1), (yf, pocket_y))

    def cavity_yz(points):
        return cavity_solid_uvs(points, 1, 2, (yf, pocket_y), (hole_z0, hole_z1))

    # UE's FBX handedness conversion reverses these source windings. Order the
    # source faces so cooked normals point from each wall into the opening;
    # otherwise backface culling reveals the room through the pocket at oblique
    # angles.
    quad([(hole_x0,yf,hole_z0),(hole_x1,yf,hole_z0),
          (hole_x1,pocket_y,hole_z0),(hole_x0,pocket_y,hole_z0)],"navy",cavity_xy)
    quad([(hole_x0,yf,hole_z1),(hole_x0,pocket_y,hole_z1),
          (hole_x1,pocket_y,hole_z1),(hole_x1,yf,hole_z1)],"navy",cavity_xy)
    quad([(hole_x0,yf,hole_z0),(hole_x0,pocket_y,hole_z0),
          (hole_x0,pocket_y,hole_z1),(hole_x0,yf,hole_z1)],"navy",cavity_yz)
    quad([(hole_x1,yf,hole_z0),(hole_x1,yf,hole_z1),
          (hole_x1,pocket_y,hole_z1),(hole_x1,pocket_y,hole_z0)],"navy",cavity_yz)
    # Keep the gold trim proud of the cyan face. Near-coplanar overlays caused
    # visible raster flicker/bleeding around the front details in the race.
    for xa,xb in ((-9.0,-7.8),(7.8,9.0)):
        quad([(xa,yf-.35,39.0),(xb,yf-.35,39.0),(xb,yf-.35,53.5),(xa,yf-.35,53.5)],"gold",full_tile_uv)
    quad([(x1,yb,z0),(x0,yb,z0),(x0,yb,z1),(x1,yb,z1)],"navy",back_uv)
    quad([(x0,yb,z0),(x0,yf,z0),(x0,yf,z1),(x0,yb,z1)],"navy",side_uv)
    quad([(x1,yf,z0),(x1,yb,z0),(x1,yb,z1),(x1,yf,z1)],"ice",side_uv)
    # Outward normals are +Z on top and -Z on the bottom. Keep the face order
    # aligned with those directions because the opaque surface material culls
    # backfaces.
    quad([(x0,yf,z1),(x1,yf,z1),(x1,yb,z1),(x0,yb,z1)],"ice",top_uv)
    quad([(x0,yb,z0),(x1,yb,z0),(x1,yf,z0),(x0,yf,z0)],"navy",bottom_uv)

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
    for slot_name in SLOTS[len(donor_materials):]:
        mesh.materials.append(bpy.data.materials.new(slot_name))
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
