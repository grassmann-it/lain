#!/usr/bin/env python3
"""Observe an owned installed .desktop launch on a new private Xvfb; no UI input."""
import argparse
import array
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import secrets
import shlex
import shutil
import signal
import subprocess
import tempfile
import time

ARTWORK = 'd36cb05911456a363b275631ad7a3b1176770f0f052d2e9747593e33f947a6fd'
TITLE = 'lain — Local AI Gateway'


def require(value, message):
    if not value:
        raise RuntimeError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def desktop_string(value):
    return value.replace('\\', '\\\\').replace('\t', '\\t')


def exec_argument(value):
    value = value.replace('%', '%%')
    for character in ('\\', '"', '`', '$'):
        value = value.replace(character, '\\' + character)
    return desktop_string('"' + value + '"')


def regular_absolute(value):
    path = Path(value)
    require(path.is_absolute() and '\n' not in value and '\r' not in value, 'Paths must be absolute and single-line')
    require(path.is_file() and not path.is_symlink(), 'Expected regular owned file: ' + value)
    return path


def descendants(parent):
    found = set()
    todo = [parent]
    while todo:
        current = todo.pop()
        try:
            children = (Path('/proc') / str(current) / 'task' / str(current) / 'children').read_text().split()
        except OSError:
            continue
        for value in children:
            child = int(value)
            if child not in found:
                found.add(child)
                todo.append(child)
    return found


def tagged(pid, nonce):
    try:
        return ('LAIN_NATIVE_PROBE_TAG=' + nonce).encode() in (Path('/proc') / str(pid) / 'environ').read_bytes().split(b'\0')
    except OSError:
        return False


def terminate_owned_groups(nonce, launch_group):
    owned = {pid for pid in descendants(os.getpid()) if tagged(pid, nonce)}
    groups = {launch_group}
    for pid in owned:
        try:
            groups.add(os.getpgid(pid))
        except ProcessLookupError:
            pass
    records = []
    for group in groups:
        require(group != os.getpgrp(), 'Refusing caller process group')
        members = set()
        for entry in Path('/proc').iterdir():
            if entry.name.isdigit():
                try:
                    if os.getpgid(int(entry.name)) == group:
                        members.add(int(entry.name))
                except ProcessLookupError:
                    pass
        # A group is ours only if every surviving member carries this launch's nonce.
        require(all(tagged(pid, nonce) for pid in members), 'Refusing a process group containing an unrelated process')
        if members:
            os.killpg(group, signal.SIGTERM)
            time.sleep(.2)
            for pid in members:
                if tagged(pid, nonce):
                    try:
                        os.killpg(group, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    break
        records.append({'processGroup': group, 'ownedPids': sorted(members)})
    return records


class Attributes(C.Structure):
    _fields_ = [('x', C.c_int), ('y', C.c_int), ('width', C.c_int), ('height', C.c_int), ('border_width', C.c_int), ('depth', C.c_int),
                ('visual', C.c_void_p), ('root', C.c_ulong), ('window_class', C.c_int), ('bit_gravity', C.c_int), ('win_gravity', C.c_int),
                ('backing_store', C.c_int), ('backing_planes', C.c_ulong), ('backing_pixel', C.c_ulong), ('save_under', C.c_int),
                ('colormap', C.c_ulong), ('map_installed', C.c_int), ('map_state', C.c_int), ('all_event_masks', C.c_long),
                ('your_event_mask', C.c_long), ('do_not_propagate_mask', C.c_long), ('override_redirect', C.c_int), ('screen', C.c_void_p)]


class X11:
    def __init__(self, display):
        self.lib = C.CDLL('libX11.so.6')
        self.lib.XOpenDisplay.argtypes = [C.c_char_p]
        self.lib.XOpenDisplay.restype = C.c_void_p
        self.lib.XDefaultRootWindow.argtypes = [C.c_void_p]
        self.lib.XDefaultRootWindow.restype = C.c_ulong
        self.lib.XInternAtom.argtypes = [C.c_void_p, C.c_char_p, C.c_int]
        self.lib.XInternAtom.restype = C.c_ulong
        self.lib.XQueryTree.argtypes = [C.c_void_p, C.c_ulong, C.POINTER(C.c_ulong), C.POINTER(C.c_ulong), C.POINTER(C.POINTER(C.c_ulong)), C.POINTER(C.c_uint)]
        self.lib.XGetWindowProperty.argtypes = [C.c_void_p, C.c_ulong, C.c_ulong, C.c_long, C.c_long, C.c_int, C.c_ulong,
                                               C.POINTER(C.c_ulong), C.POINTER(C.c_int), C.POINTER(C.c_ulong), C.POINTER(C.c_ulong), C.POINTER(C.c_void_p)]
        self.lib.XGetWindowAttributes.argtypes = [C.c_void_p, C.c_ulong, C.POINTER(Attributes)]
        self.lib.XFree.argtypes = [C.c_void_p]
        self.lib.XCloseDisplay.argtypes = [C.c_void_p]
        # Startup creates/destroys helper windows; ignore stale-window X errors.
        self.error_handler = C.CFUNCTYPE(C.c_int, C.c_void_p, C.c_void_p)(lambda display, event: 0)
        self.lib.XSetErrorHandler.argtypes = [type(self.error_handler)]
        self.lib.XSetErrorHandler(self.error_handler)
        self.display = self.lib.XOpenDisplay(display.encode())
        require(self.display, 'Cannot open private Xvfb')
        self.root = self.lib.XDefaultRootWindow(self.display)

    def atom(self, name):
        return self.lib.XInternAtom(self.display, name.encode(), 0)

    def windows(self):
        todo, found = [self.root], []
        while todo:
            window = todo.pop()
            root, parent, count, children = C.c_ulong(), C.c_ulong(), C.c_uint(), C.POINTER(C.c_ulong)()
            if self.lib.XQueryTree(self.display, window, C.byref(root), C.byref(parent), C.byref(children), C.byref(count)):
                branch = [children[index] for index in range(count.value)]
                todo.extend(branch)
                found.extend(branch)
                if children:
                    self.lib.XFree(children)
        return found

    def prop(self, window, name):
        actual, form, count, after, data = C.c_ulong(), C.c_int(), C.c_ulong(), C.c_ulong(), C.c_void_p()
        result = self.lib.XGetWindowProperty(self.display, window, self.atom(name), 0, 8_000_000, 0, 0,
                                            C.byref(actual), C.byref(form), C.byref(count), C.byref(after), C.byref(data))
        try:
            if result or not data:
                return [] if form.value != 8 else ''
            require(after.value == 0, 'Oversized X11 property')
            if form.value == 8:
                return C.string_at(data, count.value).decode('utf-8', errors='strict')
            require(form.value == 32, 'Unexpected X11 property format')
            return [C.cast(data, C.POINTER(C.c_ulong))[index] & 0xffffffff for index in range(count.value)]
        finally:
            if data:
                self.lib.XFree(data)

    def inspect(self, nonce, executable):
        for window in self.windows():
            pids = self.prop(window, '_NET_WM_PID')
            if not pids or not tagged(pids[0], nonce):
                continue
            pid = pids[0]
            require(pid in descendants(os.getpid()), 'Window process is not a child of this launch')
            require((Path('/proc') / str(pid) / 'exe').resolve() == executable.resolve(), 'Native window belongs to another executable')
            title = self.prop(window, '_NET_WM_NAME')
            attrs = Attributes()
            require(self.lib.XGetWindowAttributes(self.display, window, C.byref(attrs)), 'Cannot inspect window attributes')
            if title != TITLE or attrs.map_state != 2:
                continue
            require(self.atom('_NET_WM_WINDOW_TYPE_NORMAL') in self.prop(window, '_NET_WM_WINDOW_TYPE'), 'Expected normal EWMH application window')
            icon = self.prop(window, '_NET_WM_ICON')
            if not icon:
                continue
            entries, offset = [], 0
            while offset < len(icon):
                require(offset + 2 <= len(icon), 'Truncated native icon header')
                width, height = icon[offset:offset + 2]
                count = width * height
                require(0 < width <= 4096 and 0 < height <= 4096 and offset + 2 + count <= len(icon), 'Invalid native icon dimensions')
                pixels = icon[offset + 2:offset + 2 + count]
                alpha = [pixel >> 24 for pixel in pixels]
                require(any(alpha) and min(alpha) < max(alpha), 'Native icon has no meaningful transparent artwork')
                entries.append({'width': width, 'height': height, 'argbSha256': hashlib.sha256(array.array('I', pixels).tobytes()).hexdigest(),
                                'alphaMin': min(alpha), 'alphaMax': max(alpha)})
                offset += 2 + count
            return {'windowId': window, 'pid': pid, 'processGroup': os.getpgid(pid), 'title': title,
                    'width': attrs.width, 'height': attrs.height, 'mapped': True, 'ewmhType': 'normal', 'nativeIcons': entries}
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('desktop', 'launcher', 'icon', 'state', 'output'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    # Unset the caller's native display/bus before starting any external process.
    for key in tuple(os.environ):
        if key.startswith('DBUS_') or key in ('DISPLAY', 'WAYLAND_DISPLAY', 'SESSION_MANAGER', 'XAUTHORITY', 'DESKTOP_STARTUP_ID', 'XDG_ACTIVATION_TOKEN',
                                             'XDG_CURRENT_DESKTOP', 'XDG_SESSION_DESKTOP', 'DESKTOP_SESSION', 'GIO_LAUNCHED_DESKTOP_FILE', 'GIO_LAUNCHED_DESKTOP_FILE_PID'):
            os.environ.pop(key, None)
    desktop, launcher, icon = (regular_absolute(getattr(args, name)) for name in ('desktop', 'launcher', 'icon'))
    state, output = Path(args.state), Path(args.output)
    require(state.is_absolute() and state.is_dir() and output.is_absolute() and not output.exists(), 'Use existing absolute state and nonexistent absolute output')
    app = state / 'app'
    require((app / '.lain-package').is_file(), 'Expected managed installed application')
    ownership = json.loads((state / 'desktop-integration.json').read_text())
    require(all(ownership.get(str(path)) == digest(path) for path in (desktop, icon)), 'Desktop/icon ownership hash differs')
    require(digest(icon) == ARTWORK and digest(app / 'desktop/Assets/Brand/lain-cat.png') == ARTWORK, 'Original cat artwork hash differs')
    wrapper = (app / 'packaging/lain').read_text().replace(': "${LAIN_HOME:=$HOME/.local/share/lain}"',
        'if [ -z "${LAIN_HOME:-}" ]; then LAIN_HOME=' + shlex.quote(str(state)) + '; fi')
    require(launcher.read_text() == wrapper and os.access(launcher, os.X_OK), 'Launcher differs from owned managed wrapper')
    import configparser
    entry = configparser.ConfigParser(interpolation=None, strict=True)
    entry.optionxform = str
    entry.read_string(desktop.read_text())
    require(entry.sections() == ['Desktop Entry'], 'Unexpected desktop actions or groups')
    section = entry['Desktop Entry']
    require(set(section) == {'Type', 'Name', 'Comment', 'Exec', 'Icon', 'Terminal', 'Categories', 'StartupNotify'}, 'Unexpected desktop launch keys')
    require(section['Type'] == 'Application' and section['Name'] == TITLE and section['Terminal'] == 'false', 'Unexpected desktop application metadata')
    require(section['Exec'] == exec_argument(str(launcher)) + ' ui' and section['Icon'] == desktop_string(str(icon)), 'Desktop Exec/icon differs from exact expected owned paths')
    for command in ('Xvfb', 'xauth', 'gio'):
        require(shutil.which(command), 'Missing dependency: ' + command)
    # Adopt children when gio exits, retaining unambiguous ownership for cleanup.
    require(C.CDLL(None).prctl(36, 1, 0, 0, 0) == 0, 'Cannot enable child subreaper')
    output.mkdir(mode=0o700)
    report = {'result': 'FAIL', 'desktop': str(desktop), 'desktopSha256': digest(desktop), 'launcher': str(launcher),
              'icon': str(icon), 'artworkSha256': ARTWORK, 'expectedTitle': TITLE, 'privateXvfb': True}
    with tempfile.TemporaryDirectory(prefix='lain-native-launcher-') as temporary:
        runtime = Path(temporary)
        authority = runtime / 'Xauthority'
        authority.touch(mode=0o600)
        env = dict(os.environ, HOME=str(runtime / 'home'), XDG_CONFIG_HOME=str(runtime / 'config'), XDG_DATA_HOME=str(runtime / 'data'),
                   XDG_CACHE_HOME=str(runtime / 'cache'), XDG_RUNTIME_DIR=str(runtime / 'run'), LAIN_HOME=str(state), XAUTHORITY=str(authority))
        for path in ('home', 'config', 'data', 'cache', 'run'):
            (runtime / path).mkdir(mode=0o700)
        readfd, writefd = os.pipe()
        xvfb = launch = connection = None
        nonce = secrets.token_hex(24)
        try:
            with (output / 'xvfb.log').open('w') as log:
                xvfb = subprocess.Popen(['Xvfb', '-displayfd', str(writefd), '-screen', '0', '1600x1200x24', '-nolisten', 'tcp', '-auth', str(authority)],
                                        env=env, pass_fds=(writefd,), stdout=log, stderr=log)
            os.close(writefd)
            import select
            require(select.select([readfd], [], [], 8)[0], 'Private Xvfb did not allocate a display')
            number = os.read(readfd, 32).decode().strip()
            require(number.isdigit(), 'Invalid private Xvfb display')
            env['DISPLAY'] = ':' + number
            subprocess.run(['xauth', '-f', str(authority), 'add', env['DISPLAY'], 'MIT-MAGIC-COOKIE-1', secrets.token_hex(16)], env=env, check=True, capture_output=True)
            # ctypes uses the parent Python process's Xauthority environment.
            # Both values are from this newly created private Xvfb, never inherited.
            os.environ['DISPLAY'] = env['DISPLAY']
            os.environ['XAUTHORITY'] = str(authority)
            connection = X11(env['DISPLAY'])
            env['LAIN_NATIVE_PROBE_TAG'] = nonce
            with (output / 'gio.log').open('w') as log:
                launch = subprocess.Popen(['gio', 'launch', str(desktop)], env=env, stdout=log, stderr=log, start_new_session=True)
            deadline = time.monotonic() + 35
            while time.monotonic() < deadline:
                observed = connection.inspect(nonce, app / 'desktop/Lain.Desktop')
                if observed:
                    report.update(result='PASS', display=env['DISPLAY'], observed=observed)
                    break
                require(xvfb.poll() is None, 'Private Xvfb exited')
                time.sleep(.15)
            require(report['result'] == 'PASS', 'No mapped owned gateway window with expected title and native icon')
        except Exception as error:
            report['error'] = str(error)
        finally:
            os.close(readfd)
            if launch is not None:
                try:
                    # Reap a completed gio process before inspecting its group;
                    # zombie /proc environments no longer retain the nonce.
                    launch.poll()
                    report['terminatedOwnedGroups'] = terminate_owned_groups(nonce, launch.pid)
                    launch.wait(timeout=5)
                except Exception as error:
                    report.update(result='FAIL', cleanupError=str(error))
            if connection is not None:
                connection.lib.XCloseDisplay(connection.display)
            if xvfb is not None:
                xvfb.terminate()
                try:
                    xvfb.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    xvfb.kill()
                    xvfb.wait(timeout=5)
    report['scope'] = 'Actual owned .desktop launched by gio on private Xvfb. XQueryTree/EWMH mapped window title, tagged child PID/executable, normal window type and nonempty transparent _NET_WM_ICON inspected. Original installed icon/source PNG hashes verified. No UI input, screen capture, inference, provider refresh, service action or normal desktop session. Native icon property presence/content metadata does not independently compare every scaled pixel with the source.'
    (output / 'native-launcher.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    return 0 if report['result'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
