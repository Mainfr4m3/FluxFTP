"""Check the shipped mIRC patterns against JSON fixtures; not a runtime emulator."""
import json
import re
from pathlib import Path

script = Path('extras/irc/FluxFTP.mrc').read_text(encoding='utf-8')
object_line = next(line for line in script.splitlines() if 'while ($regex(fluxftp.object,' in line)
object_pattern = object_line.split('%rest,/', 1)[1].rsplit('/))', 1)[0]
decode_line = next(line for line in script.splitlines() if 'return $regsubex(fluxftp.decode,' in line)
escape_pattern = decode_line.split('$1,/', 1)[1].split('/g,', 1)[0]

def decode(value):
    escapes = {'n': '\n', 'r': '\r', 't': '\t', 'b': '\b', 'f': '\f'}
    return re.sub(escape_pattern, lambda match: chr(int(match[1][1:], 16))
                  if match[1].startswith('u') else escapes.get(match[1], match[1]), value)

fixtures = [
    {'name': 'JBG', 'result': '200-\r\n200-  [1] User 100MB/s\r\n\r\n200 Command okay.', 'code': 200, 'error': None, 'outcome': 'success'},
    {'name': 'TIS', 'result': '211-ä <3 & "quoted" \\ path {not an object}\n211 End', 'code': 211, 'error': None, 'outcome': 'success'},
    {'name': 'FAIL', 'result': '', 'code': None, 'error': 'Login failed\r\nTry later', 'outcome': 'failed'},
    {'name': 'LITERAL', 'result': r'C:\temp\new and \n stays literal; $me %var | commands are data', 'code': 200, 'error': None, 'outcome': 'success'},
    {'name': 'MATH', 'result': '1 + 2', 'code': 200, 'error': None, 'outcome': 'success'},
]
for indent in (None, 2):
    payload = json.dumps(fixtures, ensure_ascii=True, indent=indent)
    # System.Text.Json also escapes HTML-sensitive ASCII characters.
    payload = payload.replace('<', r'\u003C').replace('&', r'\u0026')
    objects = re.findall(object_pattern, payload)
    assert len(objects) == len(fixtures)
    rendered = []
    for obj, expected in zip(objects, fixtures):
        for field in ('name', 'result', 'error'):
            found = re.search('"' + field + r'"\s*:\s*("(?:\\.|[^"\\])*")', obj)
            actual = decode(found[1][1:-1]) if found else None
            assert actual == expected[field], (field, actual, expected[field])
        text = expected['error'] or expected['result']
        rendered.extend(line for line in text.replace('\r', '').split('\n') if line)
    assert '200 Command okay.' in rendered
    assert '211 End' in rendered and 'Try later' in rendered
assert 'var -n %result' in script and '$eval(' not in script
assert 'fluxftp.reply %fluxftp.reply' in script
assert 'if ($isalias(fluxftp.reply)) fluxftp.reply %reply' in Path('extras/irc/FluxFTP-DLL.mrc').read_text(encoding='utf-8')
print('PASS: shipped RAW patterns, multiple sites/errors, CRLF/blank lines, escaped quotes/backslashes/Unicode, literal escape and command text. IRC runtime verification still required.')
