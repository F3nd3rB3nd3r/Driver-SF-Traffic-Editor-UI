#!/usr/bin/env python3
"""Independent, read-only validator for retail PC and expanded AI traffic files.
Usage: python verification/verify_resources.py file.fchunk [--compare source.fchunk]
No assets are included or changed. Assertions deliberately do not assume vanilla weights.
"""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path


def inspect(path):
    raw = Path(path).read_bytes()
    u32 = lambda p: struct.unpack_from('<I', raw, p)[0]
    leaves = {}
    labels = {}
    descriptors = {}

    def chunk(start, size, chain=(), depth=0):
        assert depth <= 8 and size >= 16
        assert raw[start:start+4] == b'CHNK' and u32(start+4) == size
        count = u32(start+8)
        assert count <= 4096 and 16 + count*16 <= size
        children = []
        for n in range(count):
            descriptor = start+16+n*16
            tag, offset, attributes, length = struct.unpack_from('<4sIII', raw, descriptor)
            exponent = (attributes >> 16) & 255
            assert exponent <= 20
            name_length = (attributes >> 8) & 255
            assert offset >= 16+count*16 and offset+length+name_length <= size
            assert offset % (1 << exponent) == 0
            children.append((offset, length+name_length, tag))
            absolute = start+offset
            key = chain+(tag.decode('ascii'),)
            labels['/'.join(key)] = raw[absolute+length:absolute+length+name_length].hex()
            descriptors['/'.join(key)] = attributes
            if raw[absolute:absolute+4] == b'CHNK':
                chunk(absolute, length, key, depth+1)
            else:
                assert key not in leaves
                leaves[key] = (absolute, length, attributes)
        previous_end = 16+count*16
        for offset, length, tag in sorted(children):
            assert offset >= previous_end, ('overlap', tag)
            previous_end = offset+length

    chunk(0, len(raw))
    assert raw[16:20] == b'AIRT'
    aiex = [v for k, v in leaves.items() if k[-1] == 'AIEX']
    assert len(aiex) == 1
    base, size, attributes = aiex[0]
    assert attributes & 0xff == 0x16 and size >= 0x180

    def array(field, stride):
        count, relative = struct.unpack_from('<II', raw, base+field)
        assert relative >= 0x180 and relative+count*stride <= size
        return count, base+relative

    settings_count, settings = array(0x118, 28)
    ids_count, ids = array(0x120, 2)
    table_count, metadata = array(0x158, 12)
    weight_count, weights = array(0x160, 8)
    assert 1 <= settings_count <= 256 and 1 <= table_count <= 256
    native_groups = 0
    for n in range(settings_count):
        m = settings+n*28
        assert raw[m+24] == n
        starts = struct.unpack_from('<5H', raw, m+4)
        counts = struct.unpack_from('<5H', raw, m+14)
        for first, count in zip(starts, counts):
            assert first+count <= ids_count
            entries = struct.unpack_from(f'<{count}H', raw, ids+first*2)
            assert len(set(entries)) == count
            native_groups += bool(count)
    counts = []
    for n in range(table_count):
        first, count, total = struct.unpack_from('<IIf', raw, metadata+n*12)
        assert first+count <= weight_count
        pairs = list(struct.iter_unpack('<If', raw[weights+first*8:weights+(first+count)*8]))
        assert len({i for i, w in pairs}) == count
        assert all(1 <= i <= 65535 and math.isfinite(w) and w >= 0 for i, w in pairs)
        observed = sum(w for i, w in pairs)
        assert math.isfinite(total) and total >= 0 and abs(observed-total) <= max(.01, observed*.00001)
        counts.append(count)
    summary = dict(path=str(path), bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest(),
                   settings=settings_count, nonempty_native_groups=native_groups, native_ids=ids_count,
                   tables=table_count, weight_rows=weight_count, min_table_rows=min(counts), max_table_rows=max(counts),
                   label_signature=hashlib.sha256(json.dumps(labels, sort_keys=True).encode()).hexdigest(),
                   descriptor_signature=hashlib.sha256(json.dumps(descriptors, sort_keys=True).encode()).hexdigest())
    return raw, leaves, summary


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('file')
    parser.add_argument('--compare', help='Verify that all opaque non-AIEX payloads match this source')
    args = parser.parse_args()
    raw, leaves, summary = inspect(args.file)
    if args.compare:
        old, old_leaves, previous_summary = inspect(args.compare)
        assert old_leaves.keys() == leaves.keys()
        assert summary['label_signature'] == previous_summary['label_signature']
        assert summary['descriptor_signature'] == previous_summary['descriptor_signature']
        for key, (offset, length, attributes) in leaves.items():
            previous_offset, previous_length, previous_attributes = old_leaves[key]
            assert attributes == previous_attributes
            if key[-1] != 'AIEX':
                assert raw[offset:offset+length] == old[previous_offset:previous_offset+previous_length], key
        print('PASS: all chunk labels, descriptor attributes and opaque resource payloads preserved')
    print(json.dumps(summary, indent=2))
    print('PASS: CHNK hierarchy, native array bounds, selectors, IDs, weights and totals')
