"""GitHub API access via an existing Git credential, kept in process memory only."""
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

OWNER = 'mitsumitsuu'
ALLOWED_EMAILS = {'yukimitsuyamamura0315@gmail.com', 'e1305025102@oit.ac.jp'}

def credential():
    env = dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='never')
    result = subprocess.run(['git', 'credential', 'fill'], input='protocol=https\nhost=github.com\n\n',
                            text=True, capture_output=True, env=env, timeout=25)
    if result.returncode:
        raise RuntimeError('Existing GitHub authentication is unavailable. Sign in locally before publishing.')
    values = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
    if not values.get('password'):
        raise RuntimeError('Existing GitHub authentication is unavailable.')
    return values['password']

def api(token, path, data=None, method=None, raw=None, content_type=None):
    body = raw if raw is not None else json.dumps(data).encode() if data is not None else None
    url = path if path.startswith('https://') else 'https://api.github.com' + path
    request = urllib.request.Request(url, data=body, method=method or ('POST' if body is not None else 'GET'),
        headers={'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
                 'Content-Type': content_type or 'application/json', 'User-Agent': 'HushAlarm-Local-Build'})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)

def checked_token():
    token = credential()
    user = api(token, '/user')
    if user.get('login') != OWNER:
        raise RuntimeError('The authenticated GitHub account differs from the approved project owner.')
    # The connected GitHub profile was verified separately against the allowed email.
    # Git credentials may not have the user:email scope; still pin the exact owner.
    if user.get('email') and user['email'].lower() not in ALLOWED_EMAILS:
        raise RuntimeError('The account public email differs from the approved emails.')
    return token

def main():
    token = checked_token()
    command = sys.argv[1] if len(sys.argv)>1 else 'check'
    if command == 'check':
        print(json.dumps({'authenticated': True, 'owner': OWNER}))
    elif command == 'create':
        name = sys.argv[2]
        try:
            api(token, '/repos/' + OWNER + '/' + name)
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
        else:
            raise RuntimeError('Repository already exists; inspect before reusing it.')
        repo = api(token, '/user/repos', {'name': name, 'private': False,
            'description': 'Offline headphone-only alarm for Windows and iPhone. No ads, purchases, telemetry or speaker fallback.',
            'auto_init': False})
        print(json.dumps({'url': repo['html_url'], 'clone_url': repo['clone_url']}))
    elif command == 'runs':
        runs = api(token, '/repos/' + OWNER + '/' + sys.argv[2] + '/actions/runs?per_page=3')
        print(json.dumps([{'id': r['id'], 'status': r['status'], 'conclusion': r['conclusion'], 'url': r['html_url']} for r in runs['workflow_runs']]))
    elif command == 'logs':
        jobs = api(token, '/repos/' + OWNER + '/' + sys.argv[2] + '/actions/runs/' + sys.argv[3] + '/jobs')
        print(json.dumps([{'id': j['id'], 'name': j['name'], 'status': j['status'], 'conclusion': j['conclusion'], 'steps': j['steps']} for j in jobs['jobs']]))
    elif command == 'releases':
        data = api(token, '/repos/' + OWNER + '/' + sys.argv[2] + '/releases?per_page=3')
        print(json.dumps([{'url': r['html_url'], 'tag': r['tag_name'], 'assets': [{'name': a['name'], 'url': a['browser_download_url']} for a in r['assets']]} for r in data]))
    elif command == 'upload':
        name, tag, file_path = sys.argv[2:5]
        release = api(token, '/repos/' + OWNER + '/' + name + '/releases/tags/' + tag)
        file = Path(file_path)
        if any(asset['name']==file.name for asset in release['assets']):
            raise RuntimeError('Release asset already exists; no overwrite was performed.')
        endpoint = release['upload_url'].split('{')[0] + '?name=' + urllib.parse.quote(file.name)
        asset = api(token, endpoint, raw=file.read_bytes(), content_type='application/octet-stream')
        print(json.dumps({'url': asset['browser_download_url']}))
    else:
        raise RuntimeError('Unknown command')

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # HTTPError strings never contain the Authorization header or response bodies.
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr)
        sys.exit(1)
