"""Read actual RERL dependencies; verify custom closure against official base-game VPK indices."""
import hashlib
import json
import pathlib
import struct
import sys

root, game, report = map(pathlib.Path, sys.argv[1:4])

def vpk_paths(file):
    data = file.read_bytes()
    magic, version, tree_size = struct.unpack_from('<III', data)
    assert magic == 0x55AA1234 and version in (1, 2)
    p = 28 if version == 2 else 12
    paths = set()
    def string():
        nonlocal p
        end = data.index(b'\0', p)
        result = data[p:end].decode('utf-8')
        p = end + 1
        return result
    while (ext := string()):
        while (directory := string()):
            while (name := string()):
                _, preload, _, _, _, term = struct.unpack_from('<IHHIIH', data, p)
                assert term == 0xFFFF
                p += 18 + preload
                paths.add(((directory + '/') if directory != ' ' else '') + name + '.' + ext)
    return paths

def references(file):
    data = file.read_bytes()
    _, header_version, _, offset, count = struct.unpack_from('<IHHII', data)
    if header_version != 12: raise ValueError(f'Unknown resource header: {file}')
    result = []
    for n in range(count):
        p = 8 + offset + n * 12
        kind, delta, size = struct.unpack_from('<4sII', data, p)
        if kind != b'RERL': continue
        start = p + 4 + delta
        items, length = struct.unpack_from('<II', data, start)
        for i in range(length):
            entry = start + items + i * 16
            string_pos = entry + 8 + struct.unpack_from('<q', data, entry + 8)[0]
            result.append(data[string_pos:data.index(b'\0', string_pos)].decode('utf-8'))
    return result

base = set()
for directory in ('csgo', 'core'):
    for vpk in (game / directory).glob('*_dir.vpk'):
        base |= vpk_paths(vpk)
custom = {p.relative_to(root).as_posix(): p for p in root.rglob('*_c')}
missing, external, nodes = set(), set(), []
for name, file in sorted(custom.items()):
    deps = references(file)
    for dep in deps:
        compiled = dep if dep.endswith('_c') else dep + '_c'
        if compiled in custom: continue
        if compiled in base: external.add(compiled)
        else: missing.add(compiled)
    nodes.append({'path': name, 'bytes': file.stat().st_size, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'references': deps})
payload = {'complete': not missing, 'custom_count': len(custom), 'official_external': sorted(external), 'missing': sorted(missing), 'resources': nodes}
report.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps({k: v for k, v in payload.items() if k != 'resources'}, ensure_ascii=False, indent=2))
sys.exit(1 if missing else 0)
