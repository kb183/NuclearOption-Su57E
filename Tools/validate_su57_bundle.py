"""Check the deliverable itself, including the actual Blueprinter hangar op."""
import argparse
import json
import hashlib
from collections import Counter
from pathlib import Path
import UnityPy

parser = argparse.ArgumentParser()
parser.add_argument('bundle', type=Path)
parser.add_argument('--report', type=Path)
args = parser.parse_args()
env = UnityPy.load(str(args.bundle))
definitions = []
parameters = []
meshes = []
scripts = set()
manifest = None
component_counts = Counter()
registered_units = []
livery_data = []
light_effects = []
canopy_materials = []
body_renderers = []
for obj in env.objects:
    if obj.type.name == 'TextAsset':
        d = obj.read()
        if d.m_Name == 'patch_manifest':
            s = d.m_Script
            if isinstance(s, bytes):
                s = s.decode('utf-8-sig')
            manifest = json.loads(s.lstrip('\ufeff'))
    elif obj.type.name == 'MonoScript':
        d = obj.read()
        scripts.add((d.m_AssemblyName, d.m_ClassName))
    elif obj.type.name == 'MonoBehaviour':
        d = obj.read_typetree()
        try:
            component_counts[obj.read().m_Script.read().m_ClassName] += 1
        except (AttributeError, FileNotFoundError):
            pass
        if 'jsonKey' in d and 'unitName' in d:
            registered_units.append(d['jsonKey'])
        if d.get('m_Name') == 'SU57_Definition':
            definitions.append((obj,d))
        if d.get('m_Name') == 'SU57_Params':
            parameters.append(d)
        if d.get('m_Name') == 'SU57_Livery':
            livery_data.append(d)
    elif obj.type.name == 'Transform':
        d = obj.read()
        if d.m_GameObject.read().m_Name in ('navlight_L_effects','navlight_R_effects'):
            light_effects.append(d)
    elif obj.type.name == 'Material':
        if obj.read().m_Name == 'SU57_Canopy':
            canopy_materials.append((obj,obj.read_typetree()))
    elif obj.type.name == 'MeshRenderer':
        renderer=obj.read()
        if renderer.m_GameObject.read().m_Name.startswith('SU57_Body_LOD'):
            body_renderers.append(renderer)
    elif obj.type.name == 'Mesh':
        d = obj.read()
        meshes.append({'name':d.m_Name,'vertices':d.m_VertexData.m_VertexCount})

assert len(definitions) == 1, 'Expected exactly one SU57 definition'
assert len(parameters) == 1, 'Expected exactly one SU57 parameters asset'
assert registered_units == ['SU57'], 'Bundle unexpectedly includes other aircraft or ship definitions'
assert component_counts['Aircraft'] == 1
assert component_counts['Turbojet'] == 2
assert component_counts['LandingGear'] == 3
assert component_counts['WeaponManager'] == 1
assert component_counts['Cockpit'] == 1
definition_obj, definition = definitions[0]
assert definition['unitName'] == 'Su-57'
assert definition['jsonKey'] == 'SU57'
assert definition['value'] == 100.0
assert parameters[0]['rankRequired'] == 4
assert parameters[0]['aircraftName'] == 'Su-57'
prefab = definition_obj.read().unitPrefab.read()
assert prefab.m_Name == 'SU57', 'Definition does not spawn SU57 prefab'
components = []
for comp in prefab.m_Component:
    obj=comp.component.deref()
    if obj.type.name == 'MonoBehaviour':
        name=obj.read().m_Script.read().m_ClassName
        components.append(name)
        if name == 'Aircraft':
            assert obj.read_typetree()['definition']['m_PathID'] == definition_obj.path_id
assert 'Aircraft' in components
assert any(m['name'] == 'RU_SU_57_-837618460868326853' and m['vertices'] == 11932 for m in meshes)
assert not any(assembly.startswith('Aryx') for assembly,_ in scripts)
assert any(name == 'Turbojet' for _, name in scripts), 'Twin-engine template missing'
assert manifest and manifest['schemaVersion'] == 3
assert manifest['Patches'], 'Runtime game references have not been generated'
liveries = parameters[0]['liveries']
assert len(liveries) == 1, 'CurrentLivery needs a selectable entry'
assert liveries[0]['faction']['m_PathID'] == 0, 'Default livery must work for all factions'
assert len(livery_data) == 1 and livery_data[0]['Texture']['m_PathID'] and livery_data[0]['Colors']
guid = liveries[0]['assetReference']['m_AssetGUID']
assert any(a['guid'] == guid and a['BundleAsset']['name'] == 'SU57_Livery' for a in manifest['Addressables']), 'Missing runtime livery addressable'
assert any(m['name'] == 'SU57_NavLens' and m['vertices'] == 6 for m in meshes)
assert len(light_effects) == 2
assert all(abs(d.m_LocalPosition.x)+abs(d.m_LocalPosition.y)+abs(d.m_LocalPosition.z)<1e-6 for d in light_effects), 'Navigation effects detached from lenses'
assert len(canopy_materials)==1, 'Expected one shared canopy material'
canopy_obj,canopy=canopy_materials[0]
glass_floats=dict(canopy['m_SavedProperties']['m_Floats'])
glass_colors=dict(canopy['m_SavedProperties']['m_Colors'])
assert glass_floats['_Surface']==1 and glass_floats['_ZWrite']==0
assert glass_floats['_SrcBlend']==1 and glass_floats['_DstBlend']==10
assert glass_floats['_Metallic']==0 and 0<glass_colors['_BaseColor']['a']<.3
assert canopy['m_CustomRenderQueue']==3000
assert dict(canopy['stringTagMap'])['RenderType']=='Transparent'
assert {'_SURFACE_TYPE_TRANSPARENT','_ALPHAPREMULTIPLY_ON'}<=set(canopy['m_ValidKeywords'])
assert {'SHADOWCASTER','DEPTHONLY'}<=set(p.upper() for p in canopy['disabledShaderPasses'])
assert len(body_renderers)==3
assert all(len(r.m_Materials)==2 and r.m_Materials[0].path_id==canopy_obj.path_id
           and r.m_Materials[1].read().m_Name=='SU57_Camouflage' for r in body_renderers), 'Glass/skin slot mismatch across LODs'
skin=body_renderers[0].m_Materials[1].read_typetree()
assert dict(skin['m_SavedProperties']['m_Floats'])['_Surface']==0, 'Fuselage must remain opaque'
ops=[json.loads(op['payloadJson']) for op in manifest['Ops'] if op['opId']=='OpAddAircraftToHangars']
assert len(ops)==1 and ops[0]['aircraftJsonKey']=='SU57'
assert ops[0]['hangars']==[{'hangarUnitJsonKey':'shelter1','hangarNames':['shelter1']}]
report={'bundle':str(args.bundle.resolve()),'size_bytes':args.bundle.stat().st_size,
        'sha256':hashlib.sha256(args.bundle.read_bytes()).hexdigest(),
        'display_name':definition['unitName'],'rank':4,'price_million':100,
        'prefab':prefab.m_Name,'root_script_components':components,
        'aircraft_component_counts':{k:component_counts[k] for k in ('Aircraft','Turbojet','LandingGear','WeaponManager','Cockpit')},
        'meshes':meshes,'runtime_patch_groups':len(manifest['Patches']),
        'hangar_op':ops[0],'aryx_dll_required':False,
        'selectable_liveries':len(liveries),'livery_addressable_guid':guid,
        'navigation_light_effects_aligned':True,
        'canopy':{'alpha':glass_colors['_BaseColor']['a'],'metallic':glass_floats['_Metallic'],
                  'render_queue':canopy['m_CustomRenderQueue'],'zwrite':False,'lods_checked':3},
        'validation':'passed: serialized bundle and manifest; runtime checks reported separately in runtime-validation.txt'}
if args.report:
    args.report.write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps(report,indent=2))
