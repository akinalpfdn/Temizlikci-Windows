"""
Adds user-facing strings to L10n.cs and Strings.resx together, so the two never drift (StringCatalogTests checks it).

Usage: python scripts/add-strings.py strings.txt

Each non-empty line of the input file is tab-separated:
    Member<TAB>key<TAB>English value<TAB>translator comment[<TAB>param1,param2]
Members with parameters become methods that format {0}, {1}… with those arguments (strings unless typed "int name").
Members are appended before the closing brace of L10n; existing keys are skipped. A member named "-" adds only the
resx entry, for keys the code computes (rule reasons, known folders, ecosystems).
"""
import sys
import xml.sax.saxutils as saxutils
from pathlib import Path

root = Path(__file__).resolve().parent.parent
code_path = root / 'src/Temizlikci.Presentation/Strings/L10n.cs'
resx_path = root / 'src/Temizlikci.Presentation/Strings/Strings.resx'

code = code_path.read_text(encoding='utf-8')
resx = resx_path.read_text(encoding='utf-8')

members = []
entries = []
for raw in Path(sys.argv[1]).read_text(encoding='utf-8').splitlines():
    if not raw.strip() or raw.startswith('#'):
        continue
    parts = raw.split('\t')
    member, key, value, comment = parts[0], parts[1], parts[2], parts[3]
    params = [p.strip() for p in parts[4].split(',')] if len(parts) > 4 and parts[4].strip() else []
    if f'name="{key}"' in resx:
        continue
    if member == '-':
        pass  # Looked up by a computed key (rule IDs, enum names): resx entry only.
    elif params:
        typed = [p if ' ' in p else f'string {p}' for p in params]
        names = [p.split(' ')[-1] for p in typed]
        members.append(f'    public static string {member}({", ".join(typed)}) => Format("{key}", {", ".join(names)});')
    else:
        members.append(f'    public static string {member} => Get("{key}");')
    entries.append(
        f'  <data name="{key}" xml:space="preserve">\n'
        f'    <value>{saxutils.escape(value)}</value>\n'
        f'    <comment>{saxutils.escape(comment)}</comment>\n'
        f'  </data>\n')

if members:
    closing = code.rstrip().rfind('}')
    code = code[:closing].rstrip() + '\n\n' + '\n'.join(members) + '\n}\n'
    code_path.write_text(code, encoding='utf-8')
if entries:
    closing = resx.rfind('</root>')
    resx = resx[:closing] + ''.join(entries) + '</root>\n'
    resx_path.write_text(resx, encoding='utf-8')
print(f'added {len(entries)} strings ({len(members)} members)')
