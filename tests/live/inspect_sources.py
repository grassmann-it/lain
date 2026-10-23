#!/usr/bin/env python3
"""Read only legacy source metadata. Never print credentials or contact services."""
import argparse
import base64
import datetime as dt
import json
import pathlib
import urllib.parse

import yaml


def inspect(path):
    source = yaml.safe_load(path.read_text())
    report = {"config": str(path), "commandRoutes": [], "codexSnapshots": []}
    for section in ("codex-api-key", "claude-api-key", "openai-compatibility"):
        for provider in source.get(section, []) or []:
            url = urllib.parse.urlsplit(provider.get("base-url", ""))
            if url.hostname != "api.commandcode.ai":
                continue
            for model in provider.get("models", []):
                if model.get("name") == "gpt-6-luna":
                    report["commandRoutes"].append({"section": section, "endpoint": "https://api.commandcode.ai" + url.path,
                                                    "upstream": model["name"], "alias": model.get("alias", model["name"])})
    auth = pathlib.Path(source.get("auth-dir", "")).expanduser()
    if not auth.is_absolute():
        auth = path.parent / auth
    for file in sorted(auth.glob("*.json")):
        snapshot = json.loads(file.read_text())
        if snapshot.get("type") != "codex":
            continue
        expiration = None
        field_expiration = None
        try:
            field_expiration = dt.datetime.fromisoformat(snapshot.get("expired", "").replace("Z", "+00:00"))
            if field_expiration.tzinfo is None:
                field_expiration = None
        except (ValueError, TypeError, AttributeError):
            pass
        try:
            part = snapshot.get("access_token", "").split(".")[1]
            expiration = json.loads(base64.urlsafe_b64decode(part + "=" * (-len(part) % 4))).get("exp")
            expiration = dt.datetime.fromtimestamp(expiration, dt.timezone.utc)
        except (ValueError, TypeError, IndexError, OverflowError, OSError):
            pass
        report["codexSnapshots"].append({"file": str(file), "disabled": snapshot.get("disabled") is True,
                                         "hasAccessToken": bool(snapshot.get("access_token")),
                                         "hasAccountId": bool(snapshot.get("account_id")), "expiredField": field_expiration.isoformat() if field_expiration else None,
                                         "jwtExpiresAt": expiration.isoformat() if expiration else None,
                                         "jwtUnexpired": expiration > dt.datetime.now(dt.timezone.utc) if expiration else None})
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=pathlib.Path, default=pathlib.Path.home() / ".local/share/easycliproxyapi/cpa-core/config.yaml")
    options = parser.parse_args()
    try:
        print(json.dumps(inspect(options.config), indent=2))
    except Exception:
        raise SystemExit("Source metadata unavailable; no raw exception data emitted.") from None
