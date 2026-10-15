"""Package already-tested builds. Never overwrite an existing user deliverable."""
import hashlib
import json
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import urllib.request
import zipfile

root = Path(__file__).resolve().parents[1]
output = Path(sys.argv[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
files = {key: output / name for key, name in {
    'windows': 'HUSH-Alarm-Windows.zip',
    'iphone': 'HUSH-Alarm-iPhone-unsigned.ipa',
    'guide': 'HUSH-Alarm-使い方.md',
    'checks': 'HUSH-Alarm-検証結果.md',
    'source': 'HUSH-Alarm-Source.zip',
    'hashes': 'HUSH-Alarm-SHA256.txt',
}.items()}
if any(file.exists() for file in files.values()):
    raise SystemExit('A deliverable already exists. Choose a new output directory; no overwrite performed.')
exe = root / 'build/windows/HushAlarm.exe'
if exe.read_bytes()[:2] != b'MZ':
    raise SystemExit('Windows executable is invalid')
with zipfile.ZipFile(files['windows'], 'w', zipfile.ZIP_DEFLATED) as archive:
    for source, name in [(exe, 'HushAlarm.exe'), (root/'docs/使い方.md', '使い方.md'),
                         (root/'docs/検証結果.md', '検証結果.md'), (root/'LICENSE', 'LICENSE')]:
        archive.write(source, name)
url = 'https://github.com/mitsumitsuu/hush-alarm/releases/download/v0.1.0-build.1/HUSH-Alarm-iPhone-unsigned.ipa'
with urllib.request.urlopen(url, timeout=60) as response:
    files['iphone'].write_bytes(response.read())
with zipfile.ZipFile(files['iphone']) as archive:
    if archive.testzip() is not None:
        raise SystemExit('IPA archive is damaged')
    info = plistlib.loads(archive.read('Payload/HushAlarm.app/Info.plist'))
    assert info['CFBundleIdentifier'] == 'app.hushalarm.personal'
    assert info['UIBackgroundModes'] == ['audio']
    assert info['MinimumOSVersion'] == '17.0'
    assert len(archive.read('Payload/HushAlarm.app/HushAlarm')) > 1000
shutil.copyfile(root/'docs/使い方.md', files['guide'])
shutil.copyfile(root/'docs/検証結果.md', files['checks'])
subprocess.run(['git', 'archive', '--format=zip', '--prefix=HUSH-Alarm-Source/', '-o', str(files['source']), 'HEAD'], cwd=root, check=True)
for key in ['windows', 'source']:
    with zipfile.ZipFile(files[key]) as archive:
        assert archive.testzip() is None
files['hashes'].write_text('\n'.join(hashlib.sha256(file.read_bytes()).hexdigest()+'  '+file.name for key,file in files.items() if key!='hashes')+'\n', encoding='utf-8')
print(json.dumps({'verified':True, 'files':[{'name': file.name, 'bytes':file.stat().st_size} for file in files.values()]}, ensure_ascii=False))
