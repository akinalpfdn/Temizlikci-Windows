"""
Replaces private-use characters (Segoe Fluent Icons glyphs, U+E000–U+F8FF) in C# sources with \\uXXXX escapes, so
glyph codes stay readable and reviewable instead of showing as invisible characters.

Usage: python scripts/escape-glyphs.py
"""
import pathlib
import re

root = pathlib.Path(__file__).resolve().parent.parent
changed = 0
for path in list((root / 'src').rglob('*.cs')) + list((root / 'tests').rglob('*.cs')):
    if any(part in ('bin', 'obj') for part in path.parts):
        continue
    text = path.read_text(encoding='utf-8')
    escaped = re.sub('[-]', lambda match: '\\u%04X' % ord(match.group(0)), text)
    if escaped != text:
        path.write_text(escaped, encoding='utf-8')
        changed += 1
        print(path.relative_to(root))
print(f'{changed} files escaped')
