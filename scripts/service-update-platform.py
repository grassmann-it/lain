"""Finite proof for four unreadable-exe platform daemons; no generic exceptions."""
import json
import os
from pathlib import Path
import stat
import subprocess

DBUS = 'org.freedesktop.DBus'
DBUS_PATH = '/org/freedesktop/DBus'
MANAGER = 'org.freedesktop.systemd1'
MANAGER_PATH = '/org/freedesktop/systemd1'
SERVICE = MANAGER + '.Service'


def need(value):
    if not value:
        raise ValueError('Platform process proof failed')


def bus(arguments, signature):
    uid = os.getuid(); runtime = Path('/run/user') / str(uid); endpoint = runtime / 'bus'
    need(not runtime.is_symlink() and runtime.stat().st_uid == uid and runtime.stat().st_mode & 0o022 == 0)
    need(not endpoint.is_symlink() and stat.S_ISSOCK(endpoint.stat().st_mode) and endpoint.stat().st_uid == uid)
    result = subprocess.run(['/usr/bin/busctl', '--address=unix:path=' + str(endpoint), '--json=short', *arguments],
                            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                            timeout=2, check=True, env={'PATH': '/usr/bin:/bin', 'LANG': 'C'})
    value = json.loads(result.stdout)
    need(value['type'] == signature)
    data = value['data']
    if arguments[0] == 'call':
        need(isinstance(data, list) and len(data) == 1)
        data = data[0]
    else:
        need(arguments[0] == 'get-property')
    if signature == 'u': need(type(data) is int and 0 <= data <= 0xffffffff)
    elif signature in ('s', 'o'): need(isinstance(data, str))
    else: need(signature == 'a(sasbttttuii)' and isinstance(data, list))
    return data


def owner():
    name = bus(['call', DBUS, DBUS_PATH, DBUS, 'GetNameOwner', 's', MANAGER], 's')
    need(isinstance(name, str) and name.startswith(':'))
    pid = bus(['call', DBUS, DBUS_PATH, DBUS, 'GetConnectionUnixProcessID', 's', name], 'u')
    uid = bus(['call', DBUS, DBUS_PATH, DBUS, 'GetConnectionUnixUser', 's', name], 'u')
    need(type(pid) is int and pid > 0 and type(uid) is int and uid == os.getuid())
    return name, pid, uid


def snapshot(entry, prefixes):
    raw = (entry / 'stat').read_text(); head, tail = raw.rsplit(')', 1); fields = tail.split()
    comm = (entry / 'comm').read_text().rstrip('\n')
    need(head.split('(', 1)[0].strip() == entry.name and head.split('(', 1)[1] == comm)
    arguments = tuple(os.fsdecode(a) for a in (entry / 'cmdline').read_bytes().split(b'\0') if a)
    need(arguments and not any(a.startswith(prefixes) for a in arguments))
    need(entry.stat().st_uid == os.getuid() and fields[0] not in ('Z', 'X'))
    raw_cgroup = tuple((entry / 'cgroup').read_text().splitlines())
    unified = [line for line in raw_cgroup if line.startswith('0::')]
    need(len(unified) == 1)
    return {'uid': entry.stat().st_uid, 'ppid': int(fields[1]), 'start': int(fields[19]), 'comm': comm,
            'arguments': arguments, 'cgroup': unified[0], 'rawCgroup': raw_cgroup}


def unit(name, authority, executable, group, main_pid):
    path = bus(['call', authority, MANAGER_PATH, MANAGER + '.Manager', 'GetUnit', 's', name], 'o')
    need(isinstance(path, str) and path.startswith(MANAGER_PATH + '/unit/'))
    prop = lambda field, signature: bus(['get-property', authority, path, SERVICE, field], signature)
    need(prop('MainPID', 'u') == main_pid and prop('ControlGroup', 's') == group)
    commands = prop('ExecStart', 'a(sasbttttuii)')
    need(isinstance(commands, list) and len(commands) == 1 and len(commands[0]) == 10)
    need(commands[0][0] == executable and commands[0][1] and commands[0][1][0] == executable)


def proves_platform_daemon(entry, prefixes):
    """Only manager, its sd-pam, exact polkit unit MainPID, or exact KWin wrapper child."""
    try:
        authority = owner(); name, manager_pid, uid = authority; proc = entry.parent
        group = '/user.slice/user-' + str(uid) + '.slice/user@' + str(uid) + '.service'
        manager_path = proc / str(manager_pid); manager = snapshot(manager_path, prefixes)
        need(manager['comm'] == 'systemd' and Path(manager['arguments'][0]).name == 'systemd' and '--user' in manager['arguments'])
        need(manager['cgroup'] == '0::' + group + '/init.scope')
        candidate = snapshot(entry, prefixes); wrapper_path = None; wrapper = None
        if int(entry.name) == manager_pid:
            need(candidate == manager)
        elif candidate['comm'] == '(sd-pam)':
            need(candidate['arguments'][0] == '(sd-pam)' and candidate['ppid'] == manager_pid and candidate['cgroup'] == manager['cgroup'])
        elif candidate['comm'] == 'polkit-kde-auth':
            executable = '/usr/lib/polkit-kde-authentication-agent-1'
            cg = group + '/background.slice/plasma-polkit-agent.service'
            need(candidate['arguments'][0] == executable and candidate['ppid'] == manager_pid and candidate['cgroup'] == '0::' + cg)
            unit('plasma-polkit-agent.service', name, executable, cg, int(entry.name))
        elif candidate['comm'] == 'kwin_wayland':
            executable = '/usr/bin/kwin_wayland_wrapper'; cg = group + '/session.slice/plasma-kwin_wayland.service'
            need(candidate['arguments'][0] == '/usr/bin/kwin_wayland' and candidate['cgroup'] == '0::' + cg)
            wrapper_path = proc / str(candidate['ppid']); wrapper = snapshot(wrapper_path, prefixes)
            need(wrapper['comm'] == 'kwin_wayland_wr' and wrapper['arguments'][0] == executable and wrapper['ppid'] == manager_pid)
            need(wrapper['cgroup'] == '0::' + cg and os.readlink(wrapper_path / 'exe') == executable)
            unit('plasma-kwin_wayland.service', name, executable, cg, int(wrapper_path.name))
        else:
            return False
        need(owner() == authority)
        need(snapshot(entry, prefixes) == candidate)
        if wrapper_path is not None:
            need(snapshot(wrapper_path, prefixes) == wrapper and os.readlink(wrapper_path / 'exe') == '/usr/bin/kwin_wayland_wrapper')
        need(snapshot(manager_path, prefixes) == manager)
        return True
    except (OSError, ValueError, KeyError, TypeError, IndexError, subprocess.SubprocessError):
        return False
