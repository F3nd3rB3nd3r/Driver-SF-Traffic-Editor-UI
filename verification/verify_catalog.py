#!/usr/bin/env python3
"""Catalog coverage, source association and XAML event wiring smoke check."""
import csv,re,xml.etree.ElementTree as ET
from pathlib import Path
root=Path(__file__).resolve().parents[1]
core=(root/'DriverSFTrafficEditor/Core/ProfileCatalog.cs').read_text(encoding='utf-8')
ui=(root/'DriverSFTrafficEditor/MainWindow.xaml').read_text(encoding='utf-8')
code=(root/'DriverSFTrafficEditor/MainWindow.xaml.cs').read_text(encoding='utf-8')
with (root/'verification/associations_source.csv').open(encoding='utf-8-sig',newline='') as f:
    source=list(csv.DictReader(f))
assert len(source)==213
expected={(int(x['table_index']),x['setting_name'].strip(),int(x['frequency'])) for x in source}
assert len({x[0] for x in expected})==135
assert 'for (int t = 0; t < 208; t++)' in core
assert 'ProfileCatalog.All' in (root/'DriverSFTrafficEditor/Core/TrafficDocument.cs').read_text()
assert 'ProfileCatalog.Main' not in (root/'DriverSFTrafficEditor/Core/TrafficDocument.cs').read_text()
# Extract only the generated association records, not the model definitions.
entry=re.compile(r'^\s*new\((\d+),\s*"((?:[^"\\]|\\.)*)",\s*(\d+),\s*(?:\d+|null),\s*"(?:[^"\\]|\\.)*"\),\s*$',re.M)
rows=entry.findall(core)
assert len(rows)==213,len(rows)
assert {(int(a),b,int(c)) for a,b,c in rows}==expected
assert len({int(t) for t,_,_ in rows})==135
assert set(map(int,[t for t,_,_ in rows])).issubset(set(range(208)))
assert 'string.Join(" / ", associations.Select(a => a.Setting).Distinct(StringComparer.OrdinalIgnoreCase))' in core
assert 'Name = associations.Count == 0 ? "(unnamed)"' in core
assert 'return definitions.OrderBy(p => p.Number).ToArray();' in core
assert 'SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)' in code
assert 'if (query.All(char.IsDigit))' not in code
assert 'StartsWith("0x", StringComparison.OrdinalIgnoreCase)' not in code[code.index('private bool FilterProfile'):code.index('private void UpdateProfileCount')]
assert 'ProfileGroupPicker' not in ui + code
assert 'ListBox.GroupStyle' not in ui
assert 'PropertyGroupDescription' not in code
assert 'Category' not in core
assert 'Side missions' not in core
assert 'ASI discovery' not in ui and 'discovery picks' not in ui
assert 'profile.Definition.Name' in code
# Verify all 208 default labels / case-insensitive Contains behavior from archive rows.
from collections import defaultdict
names=defaultdict(list)
for x in source:
    t=int(x['table_index']); n=x['setting_name'].strip()
    if n.casefold() not in {p.casefold() for p in names[t]}:
        names[t].append(n)
assert len(names)==135
labels={t: f'{t:3}  —  '+(' / '.join(names.get(t, [])) if t in names else '(unnamed)') for t in range(208)}
def contains(q):
    return [t for t, label in labels.items() if q.casefold() in (label+f' 0x{t:X}').casefold()]
assert 166 in contains('heat') and 178 in contains('sM2')
assert 166 in contains('166') and 166 in contains('66') and 166 in contains('0xA6')
assert len(contains(''))==208
assert labels[166] == '166  —  SM2 Heat From Above'
assert labels[50] == ' 50  —  (unnamed)'
assert len({t for t in names})==135
ET.parse(root/'DriverSFTrafficEditor/MainWindow.xaml')
for name in ('ProfileNumberSearchBox','ProfileCountLabel','ProfilesList','WeightGrid'):
    assert f'Name="{name}"' in ui,name
for handler in set(re.findall(r'(?:Click|SelectionChanged|TextChanged|CellEditEnding)="([A-Za-z_][A-Za-z0-9_]*)"',ui)):
    assert re.search(r'\b'+handler+r'\s*\(',code),handler
assert 'all 208 probability tables' in ui
assert 'all 208 tables' in code
print('PASS: 208 table definitions and 213 archived runtime associations across 135 tables')
print('PASS: 73 unnamed tables, no guessed category labels or source strings in UI')
print('PASS: case-insensitive Contains on names, numbers and hex substrings; XAML/event wiring')
