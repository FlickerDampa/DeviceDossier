"""Static checks for DeviceDossier's XAML: resource keys, theme parity, event handlers and element names."""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
SRC = Path(__file__).resolve().parents[2] / 'src' / 'DeviceDossier'
EVENTS = ('Click', 'Checked', 'Unchecked', 'TextChanged', 'SelectionChanged', 'Loaded')


def keys(path):
    return [e.get(X + 'Key') for e in ET.parse(path).getroot().iter() if e.get(X + 'Key')]


def main():
    problems = []
    light, dark = keys(SRC / 'Themes/Light.xaml'), keys(SRC / 'Themes/Dark.xaml')
    styles = keys(SRC / 'Themes/Theme.xaml')
    if set(light) != set(dark):
        problems.append(f'Light/Dark palettes differ: {sorted(set(light) ^ set(dark))}')
    for name, ks in (('Light', light), ('Dark', dark), ('Theme', styles)):
        dups = {k for k in ks if ks.count(k) > 1}
        if dups:
            problems.append(f'{name}.xaml has duplicate keys: {sorted(dups)}')
    clash = set(light) & set(styles)
    if clash:
        problems.append(f'Keys defined both as colours and styles: {sorted(clash)}')

    manager = (SRC / 'Views/ThemeManager.cs').read_text(encoding='utf-8')
    block = manager[manager.index('HighContrastPalette()'):manager.index('private static bool IsLight')]
    contrast = set(re.findall(r'"(\w+)"', block))
    if contrast != set(light):
        problems.append(f'High Contrast palette differs from the colour palettes: {sorted(contrast ^ set(light))}')

    code = {p.name: p.read_text(encoding='utf-8') for p in SRC.rglob('*.cs') if 'obj' not in p.parts}
    for xaml in [SRC / 'Themes/Theme.xaml', SRC / 'MainWindow.xaml']:
        text = xaml.read_text(encoding='utf-8')
        for kind, key in re.findall(r'\{(StaticResource|DynamicResource) (\w+)\}', text):
            if kind == 'StaticResource' and key not in styles:
                problems.append(f'{xaml.name}: StaticResource {key} is not defined in Theme.xaml')
            if kind == 'DynamicResource' and key not in light:
                problems.append(f'{xaml.name}: DynamicResource {key} is not defined in the colour palettes')

    window = ET.parse(SRC / 'MainWindow.xaml').getroot()
    behind = code.get('MainWindow.xaml.cs', '')
    names = []
    for e in window.iter():
        if e.get(X + 'Name'):
            names.append(e.get(X + 'Name'))
        for attr, value in e.attrib.items():
            if attr in EVENTS and not re.search(r'void ' + re.escape(value) + r'\(', behind):
                problems.append(f'MainWindow.xaml: handler {value} for {attr} is missing in MainWindow.xaml.cs')
    dup_names = {n for n in names if names.count(n) > 1}
    if dup_names:
        problems.append(f'MainWindow.xaml: duplicate x:Name {sorted(dup_names)}')

    all_code = '\n'.join(code.values())
    for key in re.findall(r'SetResourceReference\([^,]+,[^;]*?"(\w+)"', all_code):
        if key not in light:
            problems.append(f'code references colour {key} that is not in the palettes')
    for key in re.findall(r'Property, (?:[^;]*?\? )?"(\w+)"', all_code):
        if key not in light:
            problems.append(f'code references colour {key} that is not in the palettes')
    for key in re.findall(r'\bS\("(\w+)"\)', all_code):
        if key not in styles:
            problems.append(f'code references style {key} that is not in Theme.xaml')

    for p in problems:
        print('FAIL', p)
    print(f'XAML checks: {len(problems)} problem(s), {len(light)} colours, {len(styles)} styles, {len(names)} named elements')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
