#!/usr/bin/python3
"""Prepare public OAuth registrations offline. Never registers, installs or qualifies clients."""
import argparse
from datetime import datetime
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import stat
from urllib.parse import urlsplit

VERSION = 1
GOOGLE_SCOPE = "https://www.googleapis.com/auth/cloud-platform"
CF_SCOPES_URL = "https://api.cloudflare.com/client/v4/oauth/scopes"
SOURCES = {
    "google": "https://developers.google.com/identity/protocols/oauth2/native-app",
    "cloudflare": "https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/",
    "cloudflareApi": "https://developers.cloudflare.com/api/resources/iam/subresources/oauth_clients/methods/create/",
    "poe": "https://creator.poe.com/docs/external-applications/sign-in-with-poe",
}
CHECKS = {
    "google": ["owned-desktop-registration", "production-consent-and-scope-review",
               "loopback-code-exchange", "offline-refresh-and-revocation", "project-discovery-and-iam"],
    "cloudflare": ["owned-public-client-none-and-s256", "publisher-domain-verification",
                   "release-loopback-callback", "scope-and-account-discovery", "refresh-rotation-and-revocation"],
    "poe": ["owned-release-registration", "release-loopback-callback", "pkce-one-use-key-issuance",
            "key-checkpoint-expiry-and-reconnect"],
}


def fail(message):
    raise ValueError(message)


def shape(value, required, optional=()):
    if not isinstance(value, dict) or not set(required) <= set(value) or set(value) - set(required) - set(optional):
        fail("Unsupported fields or missing fields; secrets, tokens and qualification switches are not accepted.")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            fail("Duplicate JSON property.")
        result[key] = value
    return result


def read_json(path, limit=65536):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC)
    try:
        details = os.fstat(descriptor)
        if not stat.S_ISREG(details.st_mode) or details.st_size > limit:
            fail("Input must be a bounded regular nonsymlink JSON file.")
        with os.fdopen(descriptor, "rb", closefd=False) as stream:
            raw = stream.read(limit + 1)
        if len(raw) > limit:
            fail("Input exceeds the size limit.")
        try:
            value = json.loads(raw, object_pairs_hook=unique_object,
                               parse_constant=lambda _: fail("Non-finite JSON number."))
        except (json.JSONDecodeError, UnicodeDecodeError, RecursionError):
            fail("Input is not supported JSON.")
        return value, hashlib.sha256(raw).hexdigest()
    finally:
        os.close(descriptor)


def text(value, maximum=512):
    if not isinstance(value, str) or not value or len(value) > maximum or any(ord(c) < 32 or ord(c) == 127 for c in value):
        fail("Invalid public registration text.")
    return value


def client_id(value, google=False):
    if value is None:
        return
    if not re.fullmatch(r"[A-Za-z0-9._-]{1,256}", text(value)):
        fail("Invalid public client ID.")
    if google and not value.endswith(".apps.googleusercontent.com"):
        fail("Google requires the maintainer's own Desktop client ID.")


def callback(value):
    value = text(value, 2048)
    parsed = urlsplit(value)
    try:
        port = parsed.port
    except ValueError:
        fail("Invalid registered loopback callback port.")
    if (parsed.scheme != "http" or parsed.hostname not in ("127.0.0.1", "localhost")
            or port is None or not 1024 <= port <= 65535 or parsed.path in ("", "/")
            or parsed.username is not None or parsed.password is not None or parsed.query or parsed.fragment
            or re.search(r"[\s\\%]", value) or ".." in parsed.path.split("/")
            or parsed.netloc != f"{parsed.hostname}:{port}" or not parsed.path.startswith("/")):
        fail("Callback must be an exact HTTP localhost/127.0.0.1 URL with a fixed nonprivileged port and path.")


def public_url(value):
    parsed = urlsplit(text(value, 2048))
    if (parsed.scheme != "https" or not parsed.hostname or "." not in parsed.hostname
            or parsed.username is not None or parsed.password is not None or parsed.fragment
            or parsed.hostname in ("localhost", "127.0.0.1") or re.search(r"[\s\\%]", value)):
        fail("Publisher metadata requires an absolute public HTTPS URL without credentials.")
    try:
        ipaddress.ip_address(parsed.hostname)
    except ValueError:
        pass
    else:
        fail("Publisher metadata requires an owned domain, not an IP address.")
    try:
        if parsed.port not in (None, 443):
            fail("Publisher URL must use the default HTTPS port.")
    except ValueError:
        fail("Invalid publisher URL port.")


def scope_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9._-]{1,256}", value):
        fail("Use actual Cloudflare OAuth scope IDs, not colon-delimited permission names.")


def catalog_ids(catalog):
    shape(catalog, ("schemaVersion", "sourceUrl", "capturedAt", "pages"))
    if type(catalog["schemaVersion"]) is not int or catalog["schemaVersion"] != VERSION or catalog["sourceUrl"] != CF_SCOPES_URL:
        fail("Unsupported scope-catalog provenance.")
    if not re.fullmatch(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", text(catalog["capturedAt"])):
        fail("Scope catalog requires an explicit UTC capture timestamp.")
    try:
        datetime.strptime(catalog["capturedAt"], "%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        fail("Invalid scope-catalog capture timestamp.")
    pages = catalog["pages"]
    if not isinstance(pages, list) or not 1 <= len(pages) <= 100:
        fail("Invalid scope-catalog pages.")
    ids = set()
    total = per_page = None
    for number, page in enumerate(pages, 1):
        shape(page, ("success", "result", "result_info"), ("errors", "messages"))
        if page["success"] is not True or page.get("errors", []) != [] or not isinstance(page["result"], list):
            fail("Scope catalog contains an unsuccessful page.")
        info = page["result_info"]
        shape(info, ("page", "per_page", "total_count", "count"), ("total_pages",))
        if any(type(info[key]) is not int for key in ("page", "per_page", "total_count", "count")):
            fail("Invalid scope-catalog pagination.")
        if number == 1:
            total, per_page = info["total_count"], info["per_page"]
        if (not 1 <= total <= 10000 or not 1 <= per_page <= 10000 or info["page"] != number
                or info["total_count"] != total or info["per_page"] != per_page
                or info["count"] != len(page["result"])
                or info["count"] != min(per_page, total - (number - 1) * per_page)
                or "total_pages" in info and (type(info["total_pages"]) is not int
                    or info["total_pages"] != (total + per_page - 1) // per_page)):
            fail("Inconsistent scope-catalog pagination.")
        for scope in page["result"]:
            shape(scope, ("id", "name"), ("category", "scopes"))
            scope_id(scope["id"])
            text(scope["name"])
            if scope["id"] in ids:
                fail("Duplicate scope-catalog ID.")
            ids.add(scope["id"])
    if len(ids) != total or len(pages) != (total + per_page - 1) // per_page:
        fail("Scope catalog is incomplete; preserve all pages before selecting IDs.")
    return ids


def validate(spec, catalog=None, catalog_sha=None):
    shape(spec, ("schemaVersion",), ("google", "cloudflare", "poe"))
    if type(spec["schemaVersion"]) is not int or spec["schemaVersion"] != VERSION or len(spec) == 1:
        fail("Unsupported or empty maintainer setup schema.")
    if "google" in spec:
        shape(spec["google"], ("clientId",))
        client_id(spec["google"]["clientId"], google=True)
    if "poe" in spec:
        shape(spec["poe"], ("clientId", "callbackUri"))
        client_id(spec["poe"]["clientId"])
        callback(spec["poe"]["callbackUri"])
    if "cloudflare" in spec:
        cf = spec["cloudflare"]
        shape(cf, ("clientId", "accountId", "clientName", "clientUri", "logoUri", "callbackUri", "scopes", "scopeCatalogSha256"),
              ("policyUri", "tosUri"))
        client_id(cf["clientId"])
        if not isinstance(cf["accountId"], str) or not re.fullmatch(r"[a-fA-F0-9]{32}", cf["accountId"]):
            fail("Cloudflare requires an explicit maintainer account ID.")
        text(cf["clientName"], 128)
        callback(cf["callbackUri"])
        for name in ("clientUri", "logoUri", "policyUri", "tosUri"):
            if name in cf:
                public_url(cf[name])
        scopes = cf["scopes"]
        if not isinstance(scopes, list) or not 2 <= len(scopes) <= 32 or any(not isinstance(s, str) for s in scopes):
            fail("Cloudflare requires offline_access and actual resource scope IDs.")
        for value in scopes:
            scope_id(value)
        if len(set(scopes)) != len(scopes) or "offline_access" not in scopes:
            fail("Cloudflare requires unique scopes including offline_access.")
        if catalog is None or not isinstance(cf["scopeCatalogSha256"], str) or not re.fullmatch(r"[a-f0-9]{64}", cf["scopeCatalogSha256"]):
            fail("Cloudflare requires a pinned complete OAuth scope catalog.")
        if cf["scopeCatalogSha256"] != catalog_sha:
            fail("Scope-catalog bytes changed since review.")
        ids = catalog_ids(catalog)
        if any(value != "offline_access" and value not in ids for value in scopes):
            fail("Requested scope is absent from the reviewed OAuth scope catalog.")
    elif catalog is not None:
        fail("A scope catalog was supplied without a Cloudflare setup.")


def plan(spec):
    providers = {}
    for provider in ("google", "cloudflare", "poe"):
        if provider not in spec:
            continue
        providers[provider] = {"providerClientRegistration": "external-maintainer-step",
                               "clientIdPresent": spec[provider]["clientId"] is not None,
                               "qualificationRequired": CHECKS[provider], "source": SOURCES[provider],
                               "availabilityChanged": False}
    if "google" in providers:
        providers["google"].update(registrationUrl="https://console.cloud.google.com/auth/clients",
                                    applicationType="Desktop app", scopes=[GOOGLE_SCOPE],
                                    callback="existing IPv4 loopback listener with an ephemeral port; qualify actual runtime path")
    if "poe" in providers:
        providers["poe"].update(registrationUrl="https://poe.com/api/clients", scope="apikey:create",
                                 callbackUri=spec["poe"]["callbackUri"],
                                 callbackQualification="localhost automatic acceptance is documented for local development only")
    if "cloudflare" in providers:
        cf = spec["cloudflare"]
        body = {"client_name": cf["clientName"], "client_uri": cf["clientUri"], "logo_uri": cf["logoUri"],
                "grant_types": ["authorization_code", "refresh_token"], "response_types": ["code"],
                "token_endpoint_auth_method": "none", "redirect_uris": [cf["callbackUri"]],
                "scopes": [scope for scope in cf["scopes"] if scope != "offline_access"]}
        for key in ("policy", "tos"):
            if key + "Uri" in cf:
                body[key + "_uri"] = cf[key + "Uri"]
        providers["cloudflare"].update(registrationRequest={
            "method": "POST", "url": "https://api.cloudflare.com/client/v4/accounts/" + cf["accountId"] + "/oauth_clients",
            "requiredPermission": "OAuth Client Write", "body": body,
            "notSent": True, "tokenInputAcceptedByTool": False},
            protocolScope="offline_access is provider-managed by refresh_token grant registration; requested at login",
            publisherGate="new client is private; domain TXT verification then separately reviewed permanent public promotion")
    return {"schemaVersion": VERSION, "kind": "offline-maintainer-registration-plan", "providers": providers,
            "externalRequestsMade": False, "availabilityChanged": False}


def outputs(spec):
    clients = []
    for key, provider in (("google", "gemini-api"), ("cloudflare", "cloudflare-workers-ai")):
        if key in spec:
            item = spec[key]
            if item["clientId"] is None:
                fail("Staging requires a provider-issued owned client ID; registration is still external.")
            client = {"provider": provider, "clientId": item["clientId"], "qualified": False,
                      "scopes": [GOOGLE_SCOPE] if key == "google" else item["scopes"]}
            if key == "cloudflare":
                client["registeredCallback"] = item["callbackUri"]
            clients.append(client)
    result = {}
    if clients:
        result["maintainer/cloud-oauth.json"] = {"clients": clients, "cloudflareNeuronCandidateQualified": False}
    if "poe" in spec:
        item = spec["poe"]
        if item["clientId"] is None:
            fail("Staging requires a provider-issued owned client ID; registration is still external.")
        result["maintainer/poe-login.json"] = {"clientId": item["clientId"], "callbackUri": item["callbackUri"],
                                                "registrationQualified": False}
    return result


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode()


def write_new(path, data):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())


def stage(directory, files, registration_plan, spec_sha, catalog_sha):
    directory = directory.absolute()
    if ".." in directory.parts or any(p.is_symlink() for p in (directory, *directory.parents)):
        fail("Staging path must contain no symlinks or traversal.")
    parent = directory.parent.stat()
    if not stat.S_ISDIR(parent.st_mode) or parent.st_uid != os.getuid() or parent.st_mode & 0o022:
        fail("Staging parent must be an owned directory protected from other writers.")
    # mkdir refuses an existing path, including a live state root. No file is replaced.
    directory.mkdir(mode=0o700)
    (directory / "maintainer").mkdir(mode=0o700)
    serialized = {name: json_bytes(value) for name, value in files.items()}
    serialized["registration-plan.json"] = json_bytes(registration_plan)
    for name, data in serialized.items():
        write_new(directory / name, data)
    receipt = {"schemaVersion": VERSION, "kind": "unqualified-staging-receipt", "inputSha256": spec_sha,
               "scopeCatalogSha256": catalog_sha, "toolSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
               "files": {name: hashlib.sha256(data).hexdigest() for name, data in serialized.items()},
               "installed": False, "externalRequestsMade": False, "availabilityChanged": False}
    write_new(directory / "setup-receipt.json", json_bytes(receipt))
    for path in (directory / "maintainer", directory, directory.parent):
        descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("plan", "validate", "stage"))
    parser.add_argument("--spec", required=True, type=Path)
    parser.add_argument("--cloudflare-scope-catalog", type=Path)
    parser.add_argument("--output", type=Path, help="new staging directory, never a live state root")
    args = parser.parse_args()
    try:
        if (args.operation == "stage") != (args.output is not None):
            fail("Only stage requires --output; plan and validate never write files.")
        spec, spec_sha = read_json(args.spec)
        catalog, catalog_sha = read_json(args.cloudflare_scope_catalog, 1048576) if args.cloudflare_scope_catalog else (None, None)
        validate(spec, catalog, catalog_sha)
        result = plan(spec)
        if args.operation == "stage":
            result = stage(args.output, outputs(spec), result, spec_sha, catalog_sha)
        elif args.operation == "validate":
            result = {"schemaVersion": VERSION, "valid": True, "inputSha256": spec_sha,
                      "registrationQualified": False, "externalRequestsMade": False}
        print(json.dumps(result, indent=2))
    except (OSError, ValueError, TypeError, KeyError) as error:
        # Input values and provider bodies are never included in diagnostics.
        parser.exit(2, "OAuth maintainer setup refused: " + (str(error) if type(error) is ValueError else "invalid input or filesystem state") + "\n")


if __name__ == "__main__":
    main()
