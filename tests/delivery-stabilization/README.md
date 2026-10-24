# Delivery stabilization checks

Run `python3 tests/delivery-stabilization/test_delivery.py` from a checkout.
It uses temporary synthetic packages, the real installer and maintenance helper;
it starts no service, builds no project and makes no network request.

The checks exercise Monitor/Gateway metadata, fresh selection, retained data and
mode during upgrade, refusal of Core removal, checksum coverage/traversal,
retained `app.previous`, stopped snapshot/restore, no overwrite, corruption and
symlink rejection. Embedded publish Python blocks are syntax-checked. Actual
publish/build, Host behavior and safe generation recovery require integrator
qualification; see `docs/linux-delivery-stabilization.md`.
