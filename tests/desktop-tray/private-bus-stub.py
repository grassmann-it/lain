#!/usr/bin/python3
"""StatusNotifier/Notifications fixture. Connects only to the runner's private bus."""
import json
import os
from pathlib import Path

import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

if os.environ.get("LAIN_TEST_PRIVATE_BUS") != "1" or not os.environ.get("DBUS_SESSION_BUS_ADDRESS"):
    raise SystemExit("Private bus marker and explicit address required")

DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus(private=True)
output = Path(os.environ["LAIN_TEST_OUTPUT"])
WATCHER = "org.kde.StatusNotifierWatcher"
PROPERTIES = "org.freedesktop.DBus.Properties"
NOTIFICATIONS = "org.freedesktop.Notifications"
CONTROL = "org.lain.TrayTest"


class Fixture(dbus.service.Object):
    def __init__(self):
        self.host = True
        self.items = {}
        self.notifications = []
        self.registrations = []
        super().__init__(bus, "/StatusNotifierWatcher")
        self.save()

    def save(self):
        temporary = output / "native-bus.json.tmp"
        temporary.write_text(json.dumps({"hostRegistered": self.host,
            "items": list(self.items.values()), "registrations": self.registrations,
            "notifications": self.notifications}, indent=2))
        temporary.replace(output / "native-bus.json")

    def values(self):
        return {"RegisteredStatusNotifierItems": dbus.Array(list(self.items), signature="s"),
            "IsStatusNotifierHostRegistered": dbus.Boolean(self.host), "ProtocolVersion": dbus.Int32(0)}

    @dbus.service.method(PROPERTIES, in_signature="ss", out_signature="v")
    def Get(self, interface, name):
        if interface != WATCHER or name not in self.values():
            raise dbus.exceptions.DBusException("Unknown property", name="org.freedesktop.DBus.Error.UnknownProperty")
        return self.values()[name]

    @dbus.service.method(PROPERTIES, in_signature="s", out_signature="a{sv}")
    def GetAll(self, interface):
        return self.values() if interface == WATCHER else {}

    @dbus.service.method(WATCHER, in_signature="s", out_signature="", sender_keyword="sender")
    def RegisterStatusNotifierItem(self, service, sender=None):
        path = str(service) if str(service).startswith("/") else "/StatusNotifierItem"
        owner = str(sender) if str(service).startswith("/") else str(service)
        # Resolve service ownership through this private daemon, not fixture guesses.
        daemon = bus.get_object("org.freedesktop.DBus", "/org/freedesktop/DBus", introspect=False)
        pid = int(daemon.GetConnectionUnixProcessID(owner, dbus_interface="org.freedesktop.DBus"))
        key = owner + path
        item = {"service": owner, "path": path, "processId": pid, "connection": str(bus.get_name_owner(owner))}
        self.items[key] = item
        self.registrations.append(item)
        self.save()
        self.StatusNotifierItemRegistered(key)

    @dbus.service.method(WATCHER, in_signature="s", out_signature="")
    def RegisterStatusNotifierHost(self, service):
        # There is deliberately no desktop host. This fixture is the host.
        pass

    @dbus.service.signal(WATCHER, signature="s")
    def StatusNotifierItemRegistered(self, service): pass

    @dbus.service.signal(WATCHER, signature="s")
    def StatusNotifierItemUnregistered(self, service): pass

    @dbus.service.signal(WATCHER, signature="")
    def StatusNotifierHostRegistered(self): pass

    @dbus.service.signal(WATCHER, signature="")
    def StatusNotifierHostUnregistered(self): pass

    @dbus.service.signal(PROPERTIES, signature="sa{sv}as")
    def PropertiesChanged(self, interface, changed, invalidated): pass

    @dbus.service.method(CONTROL, in_signature="b", out_signature="")
    def SetHostRegistered(self, registered):
        self.host = bool(registered)
        self.save()
        self.PropertiesChanged(WATCHER, {"IsStatusNotifierHostRegistered": dbus.Boolean(self.host)}, [])
        if self.host: self.StatusNotifierHostRegistered()
        else: self.StatusNotifierHostUnregistered()

    def owner_changed(self, name, old, new):
        if new: return
        removed = [key for key, value in self.items.items() if value["service"] == str(name) or value["connection"] == str(name)]
        for key in removed:
            del self.items[key]
            self.StatusNotifierItemUnregistered(key)
        if removed: self.save()


class Notifications(dbus.service.Object):
    def __init__(self, fixture):
        self.fixture = fixture
        super().__init__(bus, "/org/freedesktop/Notifications")

    @dbus.service.method(NOTIFICATIONS, in_signature="", out_signature="as")
    def GetCapabilities(self): return ["body", "body-markup"]

    @dbus.service.method(NOTIFICATIONS, in_signature="", out_signature="ssss")
    def GetServerInformation(self): return ("lain private fixture", "lain tests", "1", "1.2")

    @dbus.service.method(NOTIFICATIONS, in_signature="susssasa{sv}i", out_signature="u")
    def Notify(self, app, replaces, icon, summary, body, actions, hints, timeout):
        number = len(self.fixture.notifications) + 1
        self.fixture.notifications.append({"app": str(app), "icon": str(icon), "summary": str(summary),
            "body": str(body), "timeout": int(timeout), "desktopEntry": str(hints.get("desktop-entry", "")),
            "urgency": int(hints.get("urgency", 0))})
        self.fixture.save()
        return dbus.UInt32(number)

    @dbus.service.method(NOTIFICATIONS, in_signature="u", out_signature="")
    def CloseNotification(self, notification): pass


# No service activation: these are the only two non-daemon well-known names.
watcher_name = dbus.service.BusName(WATCHER, bus=bus, do_not_queue=True)
notification_name = dbus.service.BusName(NOTIFICATIONS, bus=bus, do_not_queue=True)
fixture = Fixture()
notifications = Notifications(fixture)
bus.add_signal_receiver(fixture.owner_changed, signal_name="NameOwnerChanged", dbus_interface="org.freedesktop.DBus")
(output / "native-bus.ready").write_text(str(os.getpid()))
GLib.MainLoop().run()
