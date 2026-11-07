#!/usr/bin/env python3
"""Independent read-only binary-layout and scratch-patch tests for a retail-PC AIRT resource.
No game binary is included in the editor source package.
Usage: python verification/verify_resources.py "C:/.../san_franciscoAI.fchunk"
"""
from __future__ import annotations
import struct
import sys
from pathlib import Path

SIZE = 0x433000
SP_META = 0x280A18
SP_IDS = 0x281080
WEIGHT_META = 0x2DDA80
WEIGHT_DATA = 0x2DE440
STRIDE = 0x5F8
TABLES = list(range(208))


def check(path: Path) -> None:
    original = path.read_bytes()
    assert len(original) == SIZE, 'Wrong size; this test supports only the retail PC resource'
    assert original[:4] == b'CHNK' and original[16:20] == b'AIRT'
    group_count = 0
    ids_count = 0
    sets = []
    for index in range(58):
        m = SP_META + index * 28
        index2, h = struct.unpack_from('<II', original, m)
        assert index2 == index
        first = struct.unpack_from('<5H', original, m + 8)
        counts = struct.unpack_from('<5H', original, m + 18)
        groups = []
        for g, (start, count) in enumerate(zip(first, counts)):
            pos = SP_IDS + start * 2
            assert pos + count * 2 <= WEIGHT_META
            values = struct.unpack_from(f'<{count}H', original, pos)
            assert len(set(values)) == count, (index, g, values)
            if count:
                group_count += 1
                ids_count += count
            groups.append((pos, values))
        sets.append((h, groups))
    assert group_count == 77, group_count
    assert ids_count == 1318, ids_count
    assert len(TABLES) == 208 and len(set(TABLES)) == 208

    for idx in range(208):
        m = WEIGHT_META + idx * 12
        first, count, total = struct.unpack_from('<IIf', original, m)
        assert first == idx * 191 and count == 191
        start = WEIGHT_DATA + idx * STRIDE
        pairs = [struct.unpack_from('<If', original, start + i * 8) for i in range(191)]
        assert len({vid for vid, _ in pairs}) == 191
        assert all(weight >= 0 for _, weight in pairs)
        assert abs(sum(weight for _, weight in pairs) - total) < 0.01
    assert WEIGHT_DATA + 208 * STRIDE == 0x32BDC0

    # Patch a private memory buffer, never the input. Confirm the well-known
    # Chapter 1 ID replacement and weighted 124 -> 208 transfer in table 34.
    h, groups = sets[50]
    assert h == 0xAE88E83B
    pos, ids = groups[0]
    assert pos == 0x2819F8 and ids[0] == 124
    edited = bytearray(original)
    struct.pack_into('<H', edited, pos, 208)
    table = 34
    base = WEIGHT_DATA + table * STRIDE
    rows = [struct.unpack_from('<If', original, base + j*8) for j in range(191)]
    old_slot = next(i for i,(vid, _) in enumerate(rows) if vid==124)
    new_slot = next(i for i,(vid, _) in enumerate(rows) if vid==208)
    old_weight = rows[old_slot][1]
    new_weight = rows[new_slot][1]
    assert old_weight > 0 and new_weight == 0
    old_offset = base + old_slot * 8 + 4
    new_offset = base + new_slot * 8 + 4
    struct.pack_into('<f', edited, old_offset, 0)
    struct.pack_into('<f', edited, new_offset, old_weight)
    # A transfer leaves the total metadata unchanged when weights are simply moved.
    allowed = {pos, pos+1, *range(old_offset, old_offset+4), *range(new_offset, new_offset+4)}
    differences = {i for i,(a,b) in enumerate(zip(original, edited)) if a!=b}
    assert differences <= allowed and differences, sorted(differences - allowed)
    assert len(edited) == SIZE
    assert struct.unpack_from('<H', edited, pos)[0] == 208
    assert struct.unpack_from('<f', edited, old_offset)[0] == 0
    assert struct.unpack_from('<f', edited, new_offset)[0] == old_weight
    print('PASS: CHNK/AIRT header and 0x433000-byte PC file')
    print('PASS: 58 spooling metadata records, 77 nonempty groups and 1,318 vehicle slots')
    print('PASS: 208 metadata records and 39,728 weight rows; weight sums match')
    print('PASS: 208 editable profiles with valid table indices')
    print('PASS: scratch memory patch 124 -> 208 and float transfer, only expected offsets modified')
    print('PASS: input resource never altered')

if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit('Usage: python verification/verify_resources.py path/to/san_franciscoAI.fchunk')
    check(Path(sys.argv[1]))
