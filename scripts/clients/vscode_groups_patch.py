"""Exact transformations for the locally qualified Codex IDE build.

Foreign bundles remain in the private candidate, never in this repository.
"""
from pathlib import Path
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
VERSION = '26.1021.22027'
PINS = {
    'package.json': 'ea31244803e0f7b70071b1743bdf50fcf197534db032cc0fcbc48347a44de4ed',
    'out/extension.js': 'a9cc6736cd2c2d22f6cf43fc5763d5f8a873e4f741f522b44a600b1926f4e627',
    'bin/linux-x86_64/codex': 'e6d386e8822288f6d5f327e966a5a5e1de928de8795bb6149716005a410f38f2',
    'webview/assets/model-queries-643676f8d27a.js': 'a6bca3813a355d8fc3f582738951fb71748dbd204566b447fee2699295cdbc06',
    'webview/assets/model-picker-options-2e5c9d5f09f6.js': '4ed468ee7276465093996f01fe20654a8b0c380ba58843d1052eacc97c7cd47d',
    'webview/assets/access-row-56c64d268a49.js': 'ed7d897994129ac9aa1c9c5af316cf12a48c6eab8272bf5395d6ac58610117fb',
}


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError('unqualified bundle structure: ' + before[:100])
    return text.replace(before, after, 1)


def patch_text(name, text):
    if name == 'out/extension.js':
        return replace_once(text, 'handlers={"set-remote-wsl-connections-enabled":',
            'handlers={"lain-model-groups":async e=>require("./lain-groups-host.cjs").readGroups({request:(m,p)=>this.sendInternalAppServerRequest(m,p),vscode:require("vscode"),cwd:process.cwd(),signal:e.signal},e),"set-remote-wsl-connections-enabled":')
    if name.startswith('webview/assets/model-queries-'):
        text = 'import{loadLainModels as LainLoad}from"./lain-groups-query.js";import{n as LainApiInit,t as LainApi}from"./vscode-api-44b4910fc88f.js";' + text
        return replace_once(text, 'm(h,i).sendRequest(`model/list`,{includeHidden:!0,cursor:null,limit:s},{priority:n})',
            'LainLoad({request:params=>m(h,i).sendRequest(`model/list`,params,{priority:n}),groups:(params,signal)=>{LainApiInit();return LainApi(`lain-model-groups`,{params,signal})},hostId:i,modelProvider:l,modelCatalogPath:c,limit:s,signal:t,onStale:()=>p.invalidateQueries({exact:!0,queryKey:e})})')
    if name.startswith('webview/assets/model-picker-options-'):
        return replace_once(text, 'return{disabled:A||U||n!=null,id:t.model,',
            'return{_lainGroups:t._lainGroups,disabled:A||U||n!=null,id:t.model,')
    if name.startswith('webview/assets/access-row-'):
        text = 'import{groupedRows as LainRows}from"./lain-groups-ui.js";' + text
        return replace_once(text, 'Ye=f.options.map(e)',
                            'Ye=LainRows({jsx:Q.jsx,React:Dr,Menu:Pe,options:f.options,renderOption:e})')
    return text


def prepare_tree(source: Path, dest: Path) -> dict:
    source, dest = Path(source), Path(dest)
    if dest.exists():
        raise ValueError('candidate destination already exists')
    for name, pin in PINS.items():
        if (source/name).is_symlink() or digest(source/name) != pin:
            raise ValueError('unsupported input: ' + name)
    package = json.loads((source/'package.json').read_text())
    if package['publisher'] != 'openai' or package['name'] != 'chatgpt' or package['version'] != VERSION:
        raise ValueError('unsupported extension identity')
    shutil.copytree(source, dest, symlinks=False)
    for name in PINS:
        if name.endswith('.js'):
            target = dest/name
            target.write_text(patch_text(name, target.read_text()))
    for name, target in [('lain-groups-host.cjs', 'out/lain-groups-host.cjs'),
                         ('lain-groups-query.js', 'webview/assets/lain-groups-query.js'),
                         ('lain-groups-ui.js', 'webview/assets/lain-groups-ui.js')]:
        shutil.copyfile(ROOT/'clients/vscode'/name, dest/target)
    package['displayName'] = package['displayName'] + ' (Lain local groups)'
    package['lainLocalModelGroups'] = {'revision': 1, 'upstreamVersion': VERSION, 'official': False}
    (dest/'package.json').write_text(json.dumps(package, indent=2)+'\n')
    files = {str(file.relative_to(dest)): digest(file) for file in sorted(dest.rglob('*')) if file.is_file()}
    return {'schemaVersion': 1, 'upstreamVersion': VERSION, 'variant': 'lain-local-groups-r1', 'files': files,
            'inputPins': PINS, 'sourcePins': {name: digest(ROOT/'clients/vscode'/name) for name in ('lain-groups-host.cjs','lain-groups-query.js','lain-groups-ui.js')}}
