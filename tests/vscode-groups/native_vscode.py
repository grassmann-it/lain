#!/usr/bin/env python3
"""Private real VS Code + webview, no inference and no production reload."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import threading
import resource
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

CODE = '/snap/code/current/usr/share/code/code'
PW = '/home/tomka/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'


def inner():
    soft, hard = resource.getrlimit(resource.RLIMIT_NOFILE)
    resource.setrlimit(resource.RLIMIT_NOFILE, (min(hard, max(soft, 8192)), hard))
    root = Path('/mnt')
    plan = json.loads((root/'ui-plan.json').read_text())
    if plan['rollbackFirst']:
        subprocess.run(['python3','/mnt/vscode_groups.py','rollback','--bundle','/mnt/bundle',
            '--extensions-dir','/mnt/extensions','--user-data-dir','/mnt/user-data',
            '--code','/snap/code/current/usr/share/code/bin/code','--expect-manifest-sha256',plan['manifestSha256'],
            '--isolation-root','/mnt','--apply'],check=True,stdout=open(root/'rollback-result.json','wb'))
    env = dict(os.environ, DISPLAY=':97', XDG_RUNTIME_DIR='/mnt/run', LIBGL_ALWAYS_SOFTWARE='1',
               __EGL_VENDOR_LIBRARY_FILENAMES='/usr/share/glvnd/egl_vendor.d/50_mesa.json')
    (root/'run').mkdir(mode=0o700, exist_ok=True)
    requests = []
    class Fixture(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass
        def do_POST(self):
            try:
                assert self.path == '/v1/responses' and not self.headers.get('Content-Encoding')
                size = int(self.headers.get('Content-Length', '0'))
                assert 0 < size < 16*1024*1024
                body = json.loads(self.rfile.read(size))
                assert body['model'] in ('fixture-a','fixture-shared','fixture-router')
                assert 'LAIN_VSCODE_SYNTHETIC_TURN' in json.dumps(body.get('input'))
                effort = body.get('reasoning',{}).get('effort')
                assert effort in ('low','high')
                requests.append({'model':body['model'],'effort':effort})
                (root/'provider-requests.json').write_text(json.dumps(requests))
                rid = 'resp_lain_'+str(len(requests))
                item = {'id':'msg_lain_'+str(len(requests)),'type':'message','role':'assistant',
                        'content':[{'type':'output_text','text':'Synthetic VS Code response.'}]}
                events = [{'type':'response.created','response':{'id':rid}},
                          {'type':'response.output_item.done','item':item},
                          {'type':'response.completed','response':{'id':rid,'usage':{'input_tokens':1,'output_tokens':1,'total_tokens':2}}}]
                self.send_response(200);self.send_header('Content-Type','text/event-stream');self.end_headers()
                for event in events:
                    self.wfile.write(('event: '+event['type']+'\ndata: '+json.dumps(event)+'\n\n').encode())
                self.wfile.flush()
            except Exception as error:
                (root/'provider-error.txt').write_text(type(error).__name__)
                self.send_error(400,'Synthetic fixture rejected unexpected request')
    server = ThreadingHTTPServer(('127.0.0.1',18575),Fixture)
    threading.Thread(target=server.serve_forever,daemon=True).start()
    Path('/tmp/.X11-unix').mkdir(mode=0o1777, exist_ok=True)
    xvfb = subprocess.Popen(['Xvfb', ':97', '-screen', '0', '1280x900x24', '-nolisten', 'tcp'], env=env, stdout=open(root/'xvfb.log','wb'), stderr=subprocess.STDOUT)
    code = None
    try:
        time.sleep(.5)
        args = [CODE, '--no-sandbox', '--disable-gpu', '--no-proxy-server', '--skip-welcome', '--skip-release-notes',
                '--disable-workspace-trust', '--disable-updates', '--disable-telemetry',
                '--user-data-dir', '/mnt/user-data', '--extensions-dir', '/mnt/extensions',
                '--remote-debugging-port=19277',
                '--extensionDevelopmentPath=/mnt/controller', '/mnt/work']
        if not plan['installed']:
            args.append('--extensionDevelopmentPath=/mnt/extension')
        code = subprocess.Popen(args, env=env, stdout=open(root/'code.log','wb'), stderr=subprocess.STDOUT)
        result = subprocess.run(['node', '/mnt/native_ui_driver.cjs'], env=env, timeout=900)
        return result.returncode
    finally:
        if code is not None:
            code.terminate()
            try: code.wait(timeout=10)
            except subprocess.TimeoutExpired: code.kill(); code.wait()
        xvfb.terminate(); xvfb.wait(timeout=5)
        server.shutdown();server.server_close()


def main():
    if sys.argv[1:] == ['--inner']:
        return inner()
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--extension', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--bundle', type=Path)
    p.add_argument('--catalog', type=Path, help='exact repaired catalog for a private no-turn UI check')
    p.add_argument('--resume', action='store_true')
    p.add_argument('--rollback-first', action='store_true')
    args = p.parse_args()
    out = args.output.absolute()
    if not args.resume:
        out.mkdir(mode=0o700, parents=True, exist_ok=False)
    if os.environ.get('CODEX_HOME') is not None:
        p.error('this harness requires unset CODEX_HOME and never assigns it')
    backend = Path(__file__).with_name('native_backend.py')
    if not args.resume:
        backend_args = [sys.executable,str(backend),'--extension',str(args.extension),'--output',str(out/'backend'),'--no-project-override']
        if args.catalog:
            backend_args += ['--catalog', str(args.catalog)]
        subprocess.run(backend_args,check=True)
        shutil.copytree(out/'backend/home', out/'home')
        shutil.copytree(out/'backend/work', out/'work')
        for name in ['extensions','user-data/User','controller']:
            (out/name).mkdir(parents=True)
        (out/'user-data/User/settings.json').write_text(json.dumps({'workbench.startupEditor':'none','security.workspace.trust.enabled':False,
            'telemetry.telemetryLevel':'off','update.mode':'none','extensions.autoUpdate':False,'chatgpt.localeOverride':'en-US'}))
        (out/'home/.codex/auth.json').write_text('{"OPENAI_API_KEY":"synthetic-offline-only"}')
    manifest_sha = None
    if args.bundle:
        import hashlib
        manifest_sha = hashlib.sha256((args.bundle/'manifest.json').read_bytes()).hexdigest()
        if not args.resume:
            location = 'openai.chatgpt-26.1021.22027-linux-x64'
            subprocess.run(['cp','-a','--reflink=auto',str(args.bundle/'original'),str(out/'extensions'/location)],check=True)
            records = json.loads(Path('/home/tomka/.vscode/extensions/extensions.json').read_text())
            record = next(row for row in records if row['identifier']['id']=='openai.chatgpt')
            record.update(version='26.1021.22027', relativeLocation=location,
                          location={'$mid':1, 'scheme':'file', 'path':'/mnt/extensions/'+location})
            (out/'extensions/extensions.json').write_text(json.dumps([record]))
        shutil.copyfile(Path(__file__).resolve().parents[2]/'scripts/clients/vscode_groups.py',out/'vscode_groups.py')
    (out/'ui-plan.json').write_text(json.dumps({'installed':args.bundle is not None,'manifestSha256':manifest_sha,'rollbackFirst':args.rollback_first}))
    (out/'controller/package.json').write_text(json.dumps({'name':'lain-native-probe','publisher':'lain-local','version':'0.0.1',
        'engines':{'vscode':'^1.90.0'},'main':'index.cjs','activationEvents':['onStartupFinished']}))
    (out/'controller/index.cjs').write_text('''const vscode=require('vscode'),fs=require('fs');
exports.activate=async()=>{try{const ext=vscode.extensions.getExtension('openai.chatgpt');await ext.activate();
fs.writeFileSync('/mnt/activation.json',JSON.stringify({id:ext.id,version:ext.packageJSON.version,path:ext.extensionPath,active:ext.isActive}));
await vscode.commands.executeCommand('chatgpt.openSidebar');}catch(e){fs.writeFileSync('/mnt/activation-error.txt',String(e.stack));}};
''')
    for name in ['native_vscode.py','native_ui_driver.cjs','native_ui_cases.cjs']:
        shutil.copyfile(Path(__file__).with_name(name),out/name)
    for name in ['stop','command.json','command-result.json','driver-ready.json','driver-error.txt']:
        (out/name).unlink(missing_ok=True)
    env = {k:v for k,v in os.environ.items() if k in ('HOME','PATH','USER','LOGNAME','LANG','SHELL')}
    cmd = ['bwrap','--die-with-parent','--new-session','--unshare-net','--unshare-pid','--unshare-ipc',
        '--ro-bind','/','/','--proc','/proc','--dev','/dev','--tmpfs','/tmp','--tmpfs','/run',
        '--bind',str(out/'home'),os.environ['HOME'],'--bind',str(out),'/mnt',
        '--ro-bind',str(args.extension.absolute()),'/mnt/extension','--ro-bind',PW,'/mnt/node_modules',
        ]
    if args.bundle:
        cmd += ['--bind',str(args.bundle.absolute()),'/mnt/bundle']
    cmd += ['--chdir','/mnt/work','dbus-run-session','--',sys.executable,'/mnt/native_vscode.py','--inner']
    with (out/'supervisor.log').open('ab') as log:
        proc = subprocess.run(cmd,env=env,timeout=950,stdout=log,stderr=log)
    return proc.returncode


if __name__ == '__main__':
    raise SystemExit(main())
