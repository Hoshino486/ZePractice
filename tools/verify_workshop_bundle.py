"""Read back VPK CRCs/MD5s/member hashes, then compare downloaded copies. No extraction."""
import argparse
import hashlib
import json
import pathlib
import struct
import zlib


def verify(file, manifest):
    data = file.read_bytes()
    magic, version, tree_size = struct.unpack_from('<III', data)
    assert magic == 0x55AA1234 and version in (1, 2), 'Not a supported VPK'
    header = 28 if version == 2 else 12
    p, tree_end = header, header + tree_size

    def string():
        nonlocal p
        end = data.index(b'\0', p, tree_end)
        value = data[p:end].decode('utf-8')
        p = end + 1
        return value

    entries = {}
    while (ext := string()):
        while (folder := string()):
            while (name := string()):
                key = (('' if folder == ' ' else folder + '/') + name + ('' if ext == ' ' else '.' + ext)).lower()
                assert key not in entries and '..' not in key.split('/')
                crc, preload, index, offset, size, term = struct.unpack_from('<IHHIIH', data, p)
                assert term == 65535
                p += 18
                content = data[p:p+preload]
                p += preload
                if index == 32767:
                    content += data[tree_end+offset:tree_end+offset+size]
                else:
                    base = file.stem.removesuffix('_dir')
                    archive = file.with_name(f'{base}_{index:03d}.vpk')
                    with archive.open('rb') as stream:
                        stream.seek(offset)
                        content += stream.read(size)
                assert len(content) == preload + size, f'Truncated: {key}'
                assert zlib.crc32(content) == crc, f'CRC mismatch: {key}'
                entries[key] = {'bytes': len(content), 'sha256': hashlib.sha256(content).hexdigest()}
    assert p == tree_end
    expected = {x['Path'].replace('\\', '/').lower(): x for x in manifest['Files']}
    assert entries.keys() == expected.keys(), 'VPK member list differs from build manifest'
    for key, value in entries.items():
        assert value['bytes'] == expected[key]['Bytes'] and value['sha256'] == expected[key]['SHA256'].lower(), key
    md5s = 'not-present'
    if version == 2:
        payload_size, archive_size, other_size, signature_size = struct.unpack_from('<IIII', data, 12)
        archive_start = tree_end + payload_size
        other_start = archive_start + archive_size
        assert other_start + other_size + signature_size == len(data)
        if other_size:
            assert other_size == 48
            assert hashlib.md5(data[header:tree_end]).digest() == data[other_start:other_start+16]
            assert hashlib.md5(data[archive_start:other_start]).digest() == data[other_start+16:other_start+32]
            assert hashlib.md5(data[:other_start+32]).digest() == data[other_start+32:other_start+48]
            md5s = 'tree/archive-table/whole-file-valid'
    return {'path': str(file.resolve()), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'member_count': len(entries), 'crc32': 'all-valid', 'md5': md5s, 'members': entries}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True, type=pathlib.Path)
    parser.add_argument('--report', required=True, type=pathlib.Path)
    parser.add_argument('files', nargs='+', type=pathlib.Path)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    reports = [verify(file, manifest) for file in args.files]
    assert len({x['sha256'] for x in reports}) == 1, 'Downloaded bytes differ from candidate'
    args.report.write_text(json.dumps({'complete': True, 'copies': reports}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'complete': True, 'copies': len(reports), 'members': reports[0]['member_count'],
                     'sha256': reports[0]['sha256']}, indent=2))


if __name__ == '__main__':
    main()
