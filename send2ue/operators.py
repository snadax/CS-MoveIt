# Copyright Epic Games, Inc. All Rights Reserved.

import bpy
from .functions import export, utilities
from .ui import importer, addon_preferences


class Send2Ue(bpy.types.Operator):
    """Quickly send your assets to an open unreal editor instance without a dialog"""
    bl_idname = "wm.send2ue"
    bl_label = "Send to Unreal"

    def execute(self, context):
        properties = bpy.context.preferences.addons[__package__].preferences
        export.send2ue(properties)
        return {'FINISHED'}


class AdvancedSend2Ue(bpy.types.Operator):
    """Send your assets to an open unreal editor instance only after confirming the settings in a dialog"""
    bl_idname = "wm.advanced_send2ue"
    bl_label = "Advanced Dialog"

    def execute(self, context):
        properties = bpy.context.preferences.addons[__package__].preferences
        export.send2ue(properties)
        return {'FINISHED'}

    def invoke(self, context, event):
        wm = context.window_manager
        return wm.invoke_props_dialog(self)

    def draw(self, context):
        # uses property group from
        properties = bpy.context.preferences.addons[__package__].preferences
        addon_preferences.SendToUnrealPreferences.draw(self, context, properties)


class ImportAsset(bpy.types.Operator, importer.ImportAsset):
    """Import a game asset"""
    bl_idname = "wm.import_asset"
    bl_label = "Import Asset"
    filename_ext = ".fbx"

    def execute(self, context):
        properties = bpy.context.preferences.addons[__package__].preferences
        utilities.import_asset(self.filepath, properties)
        return {'FINISHED'}


class CreatePredefinedCollections(bpy.types.Operator):
    """Create the pre-defined addon collections (Mesh, Rig, Collision, Extras)"""
    bl_idname = "wm.create_predefined_collections"
    bl_label = "Create Pre-defined Collections"

    def execute(self, context):
        properties = bpy.context.window_manager.send2ue
        utilities.create_groups(properties)
        return {'FINISHED'}        

#class MergeMeshs(bpy.types.Operator,importer.ImportJson):
class MergeMeshs(bpy.types.Operator):
    """"""
    bl_idname = "wm.merge_meshs"
    bl_label = "Merge Meshs"

    def execute(self, context):
        #utilities.merge_meshs_imp(self.filepath)
        utilities.merge_meshs_imp('C:\\Users\\JJYY\\AppData\\Local\\Colossal Order\\Cities_Skylines\\Addons\\ExportBuildings\\ExportedBuildings.json')
        
        return {'FINISHED'}  

class NullOperator(bpy.types.Operator):
    """This is an operator that changes nothing, but it used to clear the undo stack"""
    bl_idname = "send2ue.null_operator"
    bl_label = "Null Operator"

    def execute(self, context):
        return {'FINISHED'}
