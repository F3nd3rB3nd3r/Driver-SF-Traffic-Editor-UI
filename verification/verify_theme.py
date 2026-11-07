#!/usr/bin/env python3
"""Static WPF theme regression checks; does not require .NET / Windows."""
from __future__ import annotations
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]/'DriverSFTrafficEditor'
window=(ROOT/'MainWindow.xaml').read_text(encoding='utf-8')
colors={}
for theme in ('Light','Dark'):
    t=ET.parse(ROOT/'Themes'/f'{theme}.xaml')
    ns='{http://schemas.microsoft.com/winfx/2006/xaml}'
    palette={node.attrib[ns+'Key']:node.attrib['Color'] for node in t.getroot() if ns+'Key' in node.attrib and 'Color' in node.attrib}
    colors[theme]=palette
    assert len(palette)>=20
ET.parse(ROOT/'MainWindow.xaml')
ET.parse(ROOT/'App.xaml')
assert colors['Light'].keys()==colors['Dark'].keys()

# StaticResource holds onto a brush from the palette at initial window load,
# while the theme toggle replaces that palette. All palette bindings must be dynamic.
keys=set(colors['Light'])
for path in ROOT.rglob('*.xaml'):
    contents=path.read_text(encoding='utf-8')
    for brush in keys:
        bad='{StaticResource '+brush+'}'
        assert bad not in contents, f'{path}: {bad} must be dynamic'

for key in ('PanelBrush','LineBrush','TextBrush','MutedTextBrush'):
    assert '{DynamicResource '+key+'}' in window
for ident in ('FileLabel', 'OldSpoolId', 'NewSpoolId', 'StatusLabel'):
    assert f'Name="{ident}"' in window

# Key text-to-surface pairs must have good contrast in both modes.
def luminance(hexcolor):
    vals=[int(hexcolor[i:i+2],16)/255 for i in (1,3,5)]
    rgb=[v/12.92 if v<=0.04045 else ((v+0.055)/1.055)**2.4 for v in vals]
    return sum(a*b for a,b in zip(rgb,(0.2126,0.7152,0.0722)))
def contrast(a,b):
    l1,l2=sorted((luminance(a),luminance(b)),reverse=True)
    return (l1+0.05)/(l2+0.05)

pairs=[('TextBrush','PanelBrush'),('MutedTextBrush','PanelBrush'),
       ('TextBrush','InputBrush'),('TextBrush','GridRowBrush'),
       ('TextBrush','GridHeaderBrush'),('ButtonTextBrush','ButtonBackgroundBrush'),
       ('DisabledTextBrush','DisabledBackgroundBrush')]
for theme,palette in colors.items():
    for fg,bg in pairs:
        ratio=contrast(palette[fg],palette[bg])
        assert ratio>=4.5, f'{theme}: {fg}/{bg} contrast {ratio:.2f}:1 < 4.5:1'
    print(f'PASS {theme}: {len(palette)} palette keys, all {len(pairs)} text/surface pairings >= 4.5:1')
print('PASS: XAML parses, palettes match, no static palette brush references, and essential control names exist')
