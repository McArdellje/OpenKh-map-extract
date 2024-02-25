bl_info = {
    "name": "Kingdom Hearts World Importer",
    "blender": (3, 4, 0),
    "category": "Import-Export",
}

import bpy
import os
import numpy as np

# ImportHelper is a helper class, defines filename and
# invoke() function which calls the file selector.
from bpy_extras.io_utils import ImportHelper
from bpy.props import StringProperty, BoolProperty, EnumProperty
from bpy.types import Operator

IS_OPAQUE = 1
IS_ALPHA = 2
IS_ALPHA_ADD = 4
IS_ALPHA_SUBTRACT = 8

class TextureInfo:
    def __init__(self, group_index: int, mesh_index: int, texture_name: str, alpha_flags: int, priority: int, draw_priority: int, wrap_u: str, wrap_v: str):
        self.group_index = group_index
        self.mesh_index = mesh_index
        self.texture_name = texture_name
        self.alpha_flags = alpha_flags
        self.priority = priority
        self.draw_priority = draw_priority
        self.wrap_u = wrap_u
        self.wrap_v = wrap_v

class ImportKHWorld(Operator, ImportHelper):
    """Import a Kingdom Hearts World from a preSliced-texture-info.txt file and a world.dae file"""
    bl_idname = "import_scene.kh_export"  # important since its how bpy.ops.import_scene.kh_export is constructed
    bl_label = "KH World"

    # ImportHelper mixin class uses this
    filename_ext = ".txt"

    filter_glob: StringProperty(
        default="*.txt",
        options={'HIDDEN'},
        maxlen=255,  # Max internal buffer length, longer would be clamped.
    )

    # List of operator properties, the attributes will be assigned
    # to the class instance from the operator settings before calling.
    viewport_alpha_mode: bpy.props.EnumProperty(
        name="Viewport Blend Mode",
        description="The Viewport Display Blend Mode to use for transparent objects, 'Blend' will always be used for additive and subtractive materials",
        items=(
            ('OPAQUE', "Opaque", "Opaque"),
            ('CLIP', "Clip", "Clip"),
            ('HASHED', "Hashed", "Hashed"),
            ('BLEND', "Blend", "Blend")
        ),
        default='BLEND',
    )

    material_mode: bpy.props.EnumProperty(
        name="Material Mode",
        description="The material setup that this importer should create, subtractive materials only work in unlit mode and will be treated as additive in lit mode)",
        items=(
            ('UNLIT_VERTEXCOL', "Original", "Create materials that try to be as close to the original as possible, this uses unlit (emissive) materials and vertex colour alpha for transparency"),
            ('UNLIT', "Unlit", "Pure unlit materials that do not use vertex colour alpha for transparency"),
            ('LIT_VERTEXCOL', "Lit", "Materials that use the diffuse bsdf node and vertex colour alpha for transparency"),
            ('LIT', "Lit (No Vertex Cols)", "Materials that use the diffuse bsdf node but do not use vertex colour alpha for transparency")
        ),
        default='UNLIT_VERTEXCOL',
    )

    unlit_emission_strength: bpy.props.FloatProperty(
        name="Unlit Strength",
        description="The strength of the emission shader for unlit materials",
        default=1.0,
        min=0.0,
        max=2.0
    )

    cutout_mode: bpy.props.EnumProperty(
        name="Cutout Mode",
        description="Set blendmode of cutout materials to CLIP",
        items=(
            ('ALWAYS', "Always", "Always set cutout materials to CLIP"),
            ('DETECT', "Detect", "Detect if the texture is cutout and set the blendmode to CLIP if it is (this is very expensive)"),
            ('NEVER', "Never", "Never set cutout materials to CLIP")
        ),
        default='ALWAYS',
    )

    transparent_nudge: bpy.props.FloatProperty(
        name="Transparent Nudge",
        description="The amount to nudge transparent materials out by to prevent z-fighting",
        default=0.1,
        min=0.0,
        max=1
    )

    def execute(self, context: bpy.types.Context):
        directory = os.path.dirname(self.filepath)
        world_id = os.path.basename(self.filepath)[:-len("-preSliced-texture-info.txt")]
        world_dae = os.path.join(directory, world_id + "-world.dae")
        if not os.path.exists(world_dae):
            self.report({'ERROR'}, f"DAE file {world_dae} not found")
            return {'CANCELLED'}
    
        texture_infos: [TextureInfo] = []

        failed = False

        def get_texture_path(texture: TextureInfo):
            return os.path.join(directory, texture.texture_name + ".png")

        # parse the file
        with open(self.filepath, "r") as f:
            line_number = 0
            for line in f.readlines():
                line_number += 1

                mesh_info, texture_name, alpha_flags, priority, draw_priority, wrap_mode_info = line.split(":")
                alpha_flags, priority, draw_priority = int(alpha_flags), int(priority), int(draw_priority)
                group_index, mesh_index = mesh_info.split(",")
                group_index, mesh_index = int(group_index), int(mesh_index)
                wrap_u, wrap_v = wrap_mode_info.split(",")
                wrap_u, wrap_v = wrap_u.strip(), wrap_v.strip()

                if "Region" in wrap_u or "Region" in wrap_v:
                    self.report({'WARNING'}, f"{self.filepath} Line {line_number} Region wrap modes are not supported")
                    failed = True
                    continue

                if wrap_u == "Wrap":
                    wrap_u = "Repeat"
                if wrap_v == "Wrap":
                    wrap_v = "Repeat"
            
                texture_infos.append(TextureInfo(group_index, mesh_index, texture_name, alpha_flags, priority, draw_priority, wrap_u, wrap_v))
                
                if not os.path.exists(get_texture_path(texture_infos[-1])):
                    self.report({'WARNING'}, f"{self.filepath} Line {line_number} Texture {texture_name} not found")
                    failed = True
                    continue

        if failed:
            return {'CANCELLED'}
        
        pre_import_root_objs_list: list[bpy.types.Object] = [obj for obj in bpy.context.scene.objects if obj.parent is None]
        # import the dae
        bpy.ops.wm.collada_import(filepath=world_dae)
        
        # get list of all objects in root of the dae
        kh_world_root_objects: list[bpy.types.Object] = [obj for obj in bpy.context.scene.objects if obj.parent is None and obj not in pre_import_root_objs_list]
        del pre_import_root_objs_list

        root_objs_by_group_index: dict[int, bpy.types.Object] = {}
        mesh_objs_by_group_index: dict[int, dict[int, bpy.types.Object]] = {}

        def get_root_obj(group_index: int) -> bpy.types.Object:
            if group_index in root_objs_by_group_index:
                return root_objs_by_group_index[group_index]
            for obj in kh_world_root_objects:
                split = obj.name.split(" ")
                if split[0] == f"{group_index}":
                    obj.name = f"{world_id} {group_index} {'Mesh Group' if split[1] == 'Mesh' else 'BOB'}"
                    obj.scale = (0.01, 0.01, 0.01)
                    obj.location *= 0.01
                    root_objs_by_group_index[group_index] = obj
                    return obj
            raise ValueError(f"Group {group_index} not found")
        
        def get_mesh(group_index, mesh_index) -> bpy.types.Object:
            if group_index in mesh_objs_by_group_index:
                if mesh_index in mesh_objs_by_group_index[group_index]:
                    return mesh_objs_by_group_index[group_index][mesh_index]
            else:
                mesh_objs_by_group_index[group_index] = {}
            for child in get_root_obj(group_index).children:
                if child.name.startswith(f"Group {group_index} Mesh {mesh_index}") or (child.name.startswith("BOB") and f"Mesh {mesh_index}" in child.name):
                    child.name = f"{world_id} {group_index} {mesh_index}"
                    mesh_objs_by_group_index[group_index][mesh_index] = child
                    return child
            raise ValueError(f"Mesh {mesh_index} not found in group {group_index}")

        # get the texture info
        for texture_info in texture_infos:
            # check if texture is already in the scene
            texture = bpy.data.textures.get(texture_info.texture_name + ".png")
            if texture is None:
                texture = bpy.data.textures.new(texture_info.texture_name + ".png", 'IMAGE')
                texture.image = bpy.data.images.load(get_texture_path(texture_info))
            elif texture.image is None:
                texture.image = bpy.data.images.load(get_texture_path(texture_info))
            
            # get the object for this texture_info
            obj = get_mesh(texture_info.group_index, texture_info.mesh_index)
            mesh: bpy.types.Mesh = obj.data

            if texture_info.alpha_flags != IS_OPAQUE:
                # push vertices out by a tiny amount to prevent z-fighting
                pos_to_normals = {}
                def pos_str(pos):
                    return f"{pos[0]} {pos[1]} {pos[2]}"
                for vert in mesh.vertices:
                    if pos_str(vert.co) in pos_to_normals:
                        pos_to_normals[pos_str(vert.co)].append(np.array(vert.normal.normalized()))
                    else:
                        pos_to_normals[pos_str(vert.co)] = [np.array(vert.normal.normalized())]
                # get the average normal for each position
                for pos, normals in pos_to_normals.items():
                    normals = np.linalg.norm(np.sum(normals))
                    pos_to_normals[pos] = normals
                for vert in mesh.vertices:
                     pos = np.array(vert.co)
                     vert.co = pos + pos_to_normals[pos_str(vert.co)] * self.transparent_nudge
            
            # get the material from the object
            materal_slot = obj.material_slots[0]
            material = materal_slot.material

            if material is not None:
                # delete this material
                bpy.data.materials.remove(material, do_unlink=True)
            
            # create a new material
            material_name = f"{texture_info.group_index} {texture_info.mesh_index} {texture_info.texture_name} {texture_info.alpha_flags} {texture_info.wrap_u} {texture_info.wrap_v}"
            if material_name in bpy.data.materials:
                raise ValueError(f"Material {material_name} already exists")
            else:
                materal_slot.material = material = bpy.data.materials.new(material_name)
                material.use_nodes = True
                material.node_tree.nodes.remove(material.node_tree.nodes.get("Principled BSDF"))
                # add uv map node
                uv2_map_node: bpy.types.ShaderNodeUVMap = material.node_tree.nodes.new('ShaderNodeUVMap')
                uv2_map_node.label = "Main UV Map"
                uv2_map_node.uv_map = mesh.uv_layers[0].name
                # add texture node
                texture_node: bpy.types.ShaderNodeTexImage = material.node_tree.nodes.new('ShaderNodeTexImage')
                uv2_map_node.label = "Main Image Texture"
                texture_node.image = texture.image
                self.report({'INFO'}, f"Creating material {material_name} for {texture_info.texture_name} {texture_info.wrap_u} {texture_info.wrap_v}")
                material_output_node: bpy.types.ShaderNodeOutputMaterial = material.node_tree.nodes.get("Material Output")
                if texture_info.wrap_u == texture_info.wrap_v:
                    texture_node.extension = "REPEAT" if texture_info.wrap_u == "Repeat" else "EXTEND"
                    texture_node.location = (uv2_map_node.location.x + 200, uv2_map_node.location.y)
                    material.node_tree.links.new(uv2_map_node.outputs[0], texture_node.inputs[0])
                else:
                    # split the uv into u and v using separate xyz
                    uv2_split_node: bpy.types.ShaderNodeSeparateXYZ = material.node_tree.nodes.new('ShaderNodeSeparateXYZ')
                    uv2_split_node.label = "UV Split"
                    uv2_split_node.location = (uv2_map_node.location.x + 200, uv2_map_node.location.y)
                    material.node_tree.links.new(uv2_map_node.outputs[0], uv2_split_node.inputs[0])
                    uv_combine_node: bpy.types.ShaderNodeCombineXYZ = material.node_tree.nodes.new('ShaderNodeCombineXYZ')
                    uv_combine_node.label = "UV Combine"
                    uv_combine_node.location = (uv2_map_node.location.x + 600, uv2_map_node.location.y)
                    def do_tex_mode(idx: int, wrap_name: str):
                        if wrap_name == "Repeat":
                            material.node_tree.links.new(uv2_split_node.outputs[idx], uv_combine_node.inputs[idx])
                        else:
                            # clamp the uv to 0-1
                            clamp_node: bpy.types.ShaderNodeClamp = material.node_tree.nodes.new('ShaderNodeClamp')
                            clamp_node.label = "Clamp UV Component"
                            clamp_node.inputs[1].default_value = 0
                            clamp_node.inputs[2].default_value = 1
                            clamp_node.location = (uv2_map_node.location.x + 400, uv2_map_node.location.y - idx * 100)
                            material.node_tree.links.new(uv2_split_node.outputs[idx], clamp_node.inputs[0])
                            material.node_tree.links.new(clamp_node.outputs[0], uv_combine_node.inputs[idx])
                    
                    do_tex_mode(0, texture_info.wrap_u)
                    do_tex_mode(1, texture_info.wrap_v)
                        
                    texture_node.extension = "REPEAT"

                colour_source = texture_node.outputs[0]
                alpha_source = texture_node.outputs[1]

                if self.material_mode.endswith("VERTEXCOL"):
                    # add vertex colour node
                    vertex_colour_node: bpy.types.ShaderNode = material.node_tree.nodes.new('ShaderNodeVertexColor')
                    vertex_colour_node.label = "Vertex Colour RGB"
                    vertex_colour_node.location = (texture_node.location.x - 200, texture_node.location.y + 200)
                    # power of 2.2 for gamma correction
                    gamma_node: bpy.types.ShaderNodeMath = material.node_tree.nodes.new('ShaderNodeMath')
                    gamma_node.label = "Gamma Correction"
                    gamma_node.operation = 'POWER'
                    gamma_node.inputs[1].default_value = 2.2
                    gamma_node.location = (vertex_colour_node.location.x + 200, vertex_colour_node.location.y)
                    material.node_tree.links.new(vertex_colour_node.outputs[0], gamma_node.inputs[0])
                    # multiply the vertex colour with the texture colour
                    colour_multiply_node: bpy.types.ShaderNodeMixRGB = material.node_tree.nodes.new('ShaderNodeMixRGB')
                    colour_multiply_node.label = "Vertex Colour Multiply"
                    colour_multiply_node.blend_type = 'MULTIPLY'
                    colour_multiply_node.location = (vertex_colour_node.location.x + 200, vertex_colour_node.location.y)
                    # set factor to 1
                    colour_multiply_node.inputs[0].default_value = 1
                    material.node_tree.links.new(vertex_colour_node.outputs[0], colour_multiply_node.inputs[1])
                    material.node_tree.links.new(colour_source, colour_multiply_node.inputs[2])
                    colour_source = colour_multiply_node.outputs[0]

                    # add second uv map node (alpha is stored in the second uv map's x coordinate)
                    uv2_map_node: bpy.types.ShaderNodeUVMap = material.node_tree.nodes.new('ShaderNodeUVMap')
                    uv2_map_node.label = "Alpha UV Map"
                    uv2_map_node.uv_map = mesh.uv_layers[1].name
                    uv2_map_node.location = (texture_node.location.x - 200, texture_node.location.y + 400)
                    # split the uv into u and v using separate xyz
                    uv2_split_node: bpy.types.ShaderNodeSeparateXYZ = material.node_tree.nodes.new('ShaderNodeSeparateXYZ')
                    uv2_split_node.label = "Alpha UV Split"
                    uv2_split_node.location = (uv2_map_node.location.x + 200, uv2_map_node.location.y)
                    material.node_tree.links.new(uv2_map_node.outputs[0], uv2_split_node.inputs[0])
                    # multiply the alpha with the vertex colour alpha
                    alpha_multiply_node: bpy.types.ShaderNodeMath = material.node_tree.nodes.new('ShaderNodeMath')
                    alpha_multiply_node.label = "Vertex Alpha Multiply"
                    alpha_multiply_node.operation = 'MULTIPLY'
                    alpha_multiply_node.location = (uv2_split_node.location.x + 200, uv2_split_node.location.y)
                    material.node_tree.links.new(uv2_split_node.outputs[0], alpha_multiply_node.inputs[0])
                    material.node_tree.links.new(alpha_source, alpha_multiply_node.inputs[1])


                    alpha_source = alpha_multiply_node.outputs[0]



                if self.material_mode.startswith("UNLIT"):
                    main_shader_node: bpy.types.ShaderNodeEmission = material.node_tree.nodes.new('ShaderNodeEmission')
                    main_shader_node.location = (texture_node.location.x + 200, texture_node.location.y)
                    # set emission strength to 1
                    main_shader_node.inputs[1].default_value = self.unlit_emission_strength
                    material.node_tree.links.new(colour_source, main_shader_node.inputs[0])
                else:
                    main_shader_node: bpy.types.ShaderNodeBsdfDiffuse = material.node_tree.nodes.new('ShaderNodeBsdfDiffuse')
                    main_shader_node.location = (texture_node.location.x + 200, texture_node.location.y)
                    material.node_tree.links.new(colour_source, main_shader_node.inputs[0])
                if texture_info.alpha_flags == IS_OPAQUE:
                    if self.cutout_mode == "ALWAYS":
                        material.blend_method = 'CLIP'
                    elif self.cutout_mode == "NEVER":
                        material.blend_method = 'OPAQUE'
                    elif self.cutout_mode == "DETECT":
                        if texture.image.channels == 3:
                            material.blend_method = 'OPAQUE'
                        elif texture.image.channels == 4:
                            # check if the texture has any data in the alpha channel
                            pixels = texture.image.pixels
                            for i in range(3, len(pixels), 4):
                                if pixels[i] < 1:
                                    material.blend_method = 'CLIP'
                                    break
                            else:
                                material.blend_method = 'OPAQUE'
                        else:
                            raise ValueError(f"Texture {texture_info.texture_name} has an invalid number of channels")
                    else:
                        raise ValueError(f"Invalid cutout mode {self.cutout_mode}")
                is_alpha = texture_info.alpha_flags & IS_ALPHA != 0
                if is_alpha or material.blend_method == 'CLIP':
                    if is_alpha:
                        material.blend_method = self.viewport_alpha_mode
                    
                    is_additive = texture_info.alpha_flags & IS_ALPHA_ADD != 0
                    is_subtractive = texture_info.alpha_flags & IS_ALPHA_SUBTRACT != 0

                    transparent_shader_node: bpy.types.ShaderNodeBsdfTransparent = material.node_tree.nodes.new('ShaderNodeBsdfTransparent')
                    # white color for the transparent shader, this means the transparent shader will be fully transparent
                    transparent_shader_node.inputs[0].default_value = [1, 1, 1, 1]
                    transparent_shader_node.location = (main_shader_node.location.x, main_shader_node.location.y - 200)

                    if is_additive or is_subtractive:
                        material.blend_method = 'BLEND'
                        if is_subtractive and self.material_mode.startswith("UNLIT"):
                            negative_emission_strength_node: bpy.types.ShaderNodeValue = material.node_tree.nodes.new('ShaderNodeValue')
                            negative_emission_strength_node.label = "Negative Emission Strength"
                            negative_emission_strength_node.outputs[0].default_value = -self.unlit_emission_strength
                            negative_emission_strength_node.location = (main_shader_node.location.x + 200, main_shader_node.location.y)
                            add_shader_node: bpy.types.ShaderNodeMath = material.node_tree.nodes.new('ShaderNodeMath')
                        add_shader_node: bpy.types.ShaderNodeAddShader = material.node_tree.nodes.new('ShaderNodeAddShader')
                        add_shader_node.location = (main_shader_node.location.x + 200, main_shader_node.location.y)
                        material_output_node.location = (add_shader_node.location.x + 200, add_shader_node.location.y)

                        material.node_tree.links.new(transparent_shader_node.outputs[0], add_shader_node.inputs[0])
                        material.node_tree.links.new(main_shader_node.outputs[0], add_shader_node.inputs[1])
                        material.node_tree.links.new(add_shader_node.outputs[0], material_output_node.inputs[0])
                    else:
                        mix_shader_node: bpy.types.ShaderNodeMixShader = material.node_tree.nodes.new('ShaderNodeMixShader')
                        mix_shader_node.location = (main_shader_node.location.x + 200, main_shader_node.location.y)
                        material_output_node.location = (mix_shader_node.location.x + 200, mix_shader_node.location.y)

                        material.node_tree.links.new(alpha_source, mix_shader_node.inputs[0])
                        material.node_tree.links.new(transparent_shader_node.outputs[0], mix_shader_node.inputs[1])
                        material.node_tree.links.new(main_shader_node.outputs[0], mix_shader_node.inputs[2])
                        material.node_tree.links.new(mix_shader_node.outputs[0], material_output_node.inputs[0])

            if material.blend_method == "BLEND":
                material.shadow_method = "HASHED"
            else:
                material.shadow_method = material.blend_method
    
        return {'FINISHED'}




# Only needed if you want to add into a dynamic menu.
def menu_func_import(self, context):
    self.layout.operator(ImportKHWorld.bl_idname, text="KH World")


# Register and add to the "file selector" menu (required to use F3 search "Import KH World" for quick access).
def register():
    bpy.utils.register_class(ImportKHWorld)
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)


def unregister():
    bpy.utils.unregister_class(ImportKHWorld)
    bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)


if __name__ == "__main__":
    register()