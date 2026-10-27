"""Check real compiled numeral vertices against OBJ source and runtime orientation.

Requires Source2Viewer-CLI 20.1+ (20.0 does not decode this installed KV3 revision).
The exporter bakes SourceToGltfRotation (yaw=0,pitch=-90,roll=-90) and 0.0254 scale:
glTF=(source_engine.y,source_engine.z,source_engine.x)*0.0254. Undo that conversion
before checking the ModelDoc import basis. This is geometry QA, not a game screenshot.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess


def vertices(gltf_path):
    data = json.loads(gltf_path.read_text(encoding='utf-8'))
    identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    for node in data['nodes']:
        assert not any(k in node for k in ('translation', 'rotation', 'scale', 'skin')), 'Unexpected non-static mesh'
        assert max(abs(a-b) for a,b in zip(node.get('matrix', identity), identity)) < 1e-5, 'Unexpected exporter node transform'
    output = []
    for mesh in data['meshes']:
        for primitive in mesh['primitives']:
            acc = data['accessors'][primitive['attributes']['POSITION']]
            assert acc['componentType'] == 5126 and acc['type'] == 'VEC3'
            view = data['bufferViews'][acc['bufferView']]
            path = (gltf_path.parent / data['buffers'][view['buffer']]['uri']).resolve()
            assert path.is_relative_to(gltf_path.parent.resolve()), 'Unexpected external buffer path'
            buffer = path.read_bytes()
            offset = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
            stride = view.get('byteStride', 12)
            for n in range(acc['count']):
                x, y, z = struct.unpack_from('<fff', buffer, offset + n * stride)
                output.append((z / .0254, x / .0254, y / .0254))
    return output


def bounds(points):
    return ([min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)])


def rotation(v, pitch, yaw, roll):
    p, y, r = map(math.radians, (pitch, yaw, roll))
    cp, sp, cy, sy, cr, sr = math.cos(p), math.sin(p), math.cos(y), math.sin(y), math.cos(r), math.sin(r)
    basis = ((cp*cy, cp*sy, -sp), (sr*sp*cy-cr*sy, sr*sp*sy+cr*cy, sr*cp), (cr*sp*cy+sr*sy, cr*sp*sy-sr*cy, cr*cp))
    return tuple(sum(v[j]*basis[j][i] for j in range(3)) for i in range(3))


def verify(cli, models, sources, report):
    export = report.parent / 'geometry-exports'
    export.mkdir(parents=True, exist_ok=True)
    results = []
    for digit in range(10):
        model = models / f'{digit}.vmdl_c'
        obj = sources / f'{digit}.obj'
        gltf = export / f'{digit}.gltf'
        proc = subprocess.run([str(cli), '-i', str(model), '-o', str(gltf), '-d', '--gltf_export_format', 'gltf'], capture_output=True, encoding='utf-8', errors='replace')
        (export / f'{digit}.log').write_text(proc.stdout + proc.stderr, encoding='utf-8')
        assert proc.returncode == 0 and gltf.exists(), f'Failed to decode digit {digit}'
        actual = vertices(gltf)
        source = [tuple(map(float, line.split()[1:4])) for line in obj.read_text(encoding='utf-8').splitlines() if line.startswith('v ')]
        assert source and actual
        expected = [(z, x, y) for x, y, z in source]
        # Bidirectional check: lost vertices and unexpected vertices both fail.
        error = max(max(min(math.dist(a, b) for b in expected) for a in actual), max(min(math.dist(b, a) for a in actual) for b in expected))
        assert error < .0001, f'Compiled basis changed for digit {digit}: {error}'
        source_min, source_max = bounds(source)
        compiled_min, compiled_max = bounds(actual)
        widths = []
        for yaw in range(0, 360, 45):
            angle = math.radians(yaw)
            right = (-math.sin(angle), math.cos(angle), 0)
            projected = []
            for v in actual:
                world = rotation(v, -90, yaw - 90, 0)
                projected.append((sum(world[i]*right[i] for i in range(3)), world[2]))
            width = max(v[0] for v in projected)-min(v[0] for v in projected)
            height = max(v[1] for v in projected)-min(v[1] for v in projected)
            assert abs(width-(source_max[1]-source_min[1])) < .0001
            assert abs(height-(source_max[2]-source_min[2])) < .0001
            assert abs(min(v[1] for v in projected)-source_min[2]) < .0001
            widths.append({'yaw': yaw, 'width': width, 'height': height})
        results.append({'digit': digit, 'compiled_sha256': hashlib.sha256(model.read_bytes()).hexdigest(),
                        'source_sha256': hashlib.sha256(obj.read_bytes()).hexdigest(),
                        'compiled_bounds': [compiled_min, compiled_max], 'max_vertex_error': error,
                        'old_yaw_only_width': compiled_max[1]-compiled_min[1], 'old_yaw_only_height': compiled_max[2]-compiled_min[2],
                        'fixed_projections': widths})
    result = {'passed': True, 'models': len(results), 'projection_checks': len(results)*8,
              'compiled_basis': {'up': '+X', 'front': '+Y', 'text_right': '+Z'},
              'source_to_compiled': '(x,y,z) -> (z,x,y)',
              'client_rendering_verified': False, 'digits': results}
    report.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f'PASS {len(results)} actual compiled meshes, {len(results)*8} facing projections, all source vertices matched')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', type=Path, required=True)
    parser.add_argument('--models', type=Path, required=True, help='Directory of the ten actual .vmdl_c files')
    parser.add_argument('--sources', type=Path, required=True, help='Directory of the ten original .obj files')
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    args.report.parent.mkdir(parents=True, exist_ok=True)
    verify(args.cli.resolve(), args.models.resolve(), args.sources.resolve(), args.report.resolve())
