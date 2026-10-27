"""Author solid seven-segment numerals. ModelDoc's OBJ import permutes the axes;
the deployed compiled basis is up=+X, front=+Y, text-right=+Z. CommanderCountdownLayout
corrects that basis at runtime. Does not touch supplied Luna or marker sources."""
import argparse
import json
import pathlib
import struct

p = argparse.ArgumentParser()
p.add_argument('content', type=pathlib.Path)
args = p.parse_args()
root = args.content
mesh = root / 'models/saibochana/commander/digits'
mat = root / 'materials/saibochana/commander/digits'
mesh.mkdir(parents=True, exist_ok=True)
mat.mkdir(parents=True, exist_ok=True)
(mat/'white.tga').write_bytes(struct.pack('<BBBHHBHHHHBB',0,0,2,0,0,0,0,0,8,8,32,0x28) + b'\xff\xff\xff\xff' * 64)
(mat/'digit.vmat').write_text('''Layer0
{
 shader "csgo_unlitgeneric.vfx"
 F_RENDER_BACKFACES 1
 TextureColor "materials/saibochana/commander/digits/white.tga"
 g_vColorTint "[1 1 1 0]"
}
''', encoding='utf-8')
header='<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc41:version{12fc9d44-453a-4ae4-b4d9-7e2ac0bbd4e0} -->'
# SOURCE OBJ only: +X front, +Y text-right, +Z up (48 units).
# Verified COMPILED vertices: (source Z, source X, source Y). Do not assume OBJ axes survive import.
segments={
 'a':(-8,8,43,48), 'g':(-8,8,21.5,26.5), 'd':(-8,8,0,5),
 'f':(-12,-7,26,44), 'b':(7,12,26,44), 'e':(-12,-7,4,22), 'c':(7,12,4,22),
}
digits=['abcdef','bc','abdeg','abcdg','bcfg','acdfg','acdefg','abc','abcdefg','abcdfg']
counts=[]
for n, active in enumerate(digits):
    lines=['o digit_'+str(n), 'vt 0 0','vt 1 0','vt 1 1','vt 0 1','usemtl digit']
    verts=[]; faces=[]
    for seg in active:
        y0,y1,z0,z1=segments[seg]; base=len(verts)
        verts += [(-1.5,y0,z0),(1.5,y0,z0),(1.5,y1,z0),(-1.5,y1,z0),(-1.5,y0,z1),(1.5,y0,z1),(1.5,y1,z1),(-1.5,y1,z1)]
        for f in [(1,4,3,2),(5,6,7,8),(1,2,6,5),(2,3,7,6),(3,4,8,7),(4,1,5,8)]:
            faces.extend([(base+f[0],base+f[1],base+f[2]),(base+f[0],base+f[2],base+f[3])])
    lines += ['v '+' '.join(map(str,v)) for v in verts]
    lines += [f'f {a}/1 {b}/2 {c}/3' for a,b,c in faces]
    (mesh/f'{n}.obj').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    source=header+'''
{ rootNode = { _class = "RootNode" children = [
{ _class = "RenderMeshList" children = [ { _class = "RenderMeshFile" name = "digit" filename = "models/saibochana/commander/digits/DIGIT.obj" import_scale = 1.0 } ] },
{ _class = "MaterialGroupList" children = [ { _class = "DefaultMaterialGroup" remaps = [] use_global_default = true global_default_material = "materials/saibochana/commander/digits/digit.vmat" } ] }
] model_archetype = "static_prop_model" } }
'''.replace('DIGIT',str(n))
    (mesh/f'{n}.vmdl').write_text(source,encoding='utf-8')
    assert all(0 <= z <= 48 for _,_,z in verts)
    counts.append({'digit':n,'vertices':len(verts),'triangles':len(faces),'source_up_axis':'+Z',
                   'compiled_up_axis':'+X','compiled_front_axis':'+Y','compiled_right_axis':'+Z','height':48})
(root/'countdown-geometry.json').write_text(json.dumps(counts,indent=2),encoding='utf-8')
print(json.dumps({'models':len(counts),'triangles':sum(x['triangles'] for x in counts),'height':48}))
