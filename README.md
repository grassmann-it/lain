# lain

**Your AI accounts, quotas and models in one local app.**

lain combines an account dashboard with a local AI gateway. Check your remaining
quotas, connect providers, organize models into groups and use them from your
coding tools through one endpoint. A desktop app and CLI share a background
service, so the gateway can keep running when you close the window.

## What you can do

- **See account quotas and usage.** View the limits a provider actually reports,
  including short windows, weekly allowances, credits and reset times. Missing
  readings are explained rather than presented as zero.
- **Connect different providers.** Use API keys, supported OAuth/device flows or
  supported local CLI imports. Login and quota support vary by provider.
- **Organize models and routes.** Build profiles and custom groups, choose a
  specific provider account or use ordered automatic routing.
- **Inspect requests.** Follow gateway traffic, responses, tool calls and usage
  in the Live inspector. Capture depends on the protocol and client information
  available to the gateway.
- **Use a desktop app or terminal.** Avalonia Desktop and the CLI use the same
  local configuration and service.

## Choose a mode

| Mode | Use it for |
| --- | --- |
| **Monitor** | Account quotas and existing local usage history, without running an inference proxy. |
| **Gateway** | Everything in Monitor, plus model profiles, routing and a local inference endpoint backed by Lain's CLIProxyAPI integration. |

The current packaging target is **Linux x64**. Windows and macOS installer work
is still being validated. This is an early project; expect rough edges and
provider-specific limitations.

## Getting started

See [Releases](https://github.com/grassmann-it/lain/releases) for any published
packages. To build from source, use the pinned .NET SDK in `global.json`; Gateway
builds also need the Go toolchain specified in `proxy/toolchain.json`.

```sh
git clone https://github.com/grassmann-it/lain.git
cd lain

# Choose monitor or gateway. The output directory must not already exist.
./scripts/publish-linux.sh --mode gateway "$HOME/lain-linux-x64"
"$HOME/lain-linux-x64/install-linux.sh" "$HOME/lain-linux-x64"
```

Follow the [Linux installation guide](docs/install.md) for prerequisites, service
setup, updates and recovery. After installation:

```sh
systemctl --user start lain.service
lain ui
lain status --json
lain usage
lain history --today --json
```

Add an account in the app, select the models you want to use and apply your
Gateway profile. A successful login does not necessarily grant API access or
quota monitoring; the account's provider and plan determine those capabilities.

## Client integrations

Gateway exposes a local inference endpoint at `http://127.0.0.1:18417` by default.
Supported clients can connect to it with Lain's local authentication helper.
Optional client adaptations add model-group menus:

- [Codex CLI setup and grouped model picker](clients/codex/README.md)
- [VS Code / Codex extension integration](clients/vscode/README.md)

These adaptations are version-specific. Follow their compatibility and recovery
instructions when updating a client.

## Local data and recording

Accounts, configuration and history are stored locally. Credentials use a
separate private store; provider requests still go to the selected provider.
The Live inspector is a bounded view and does not promise a complete archive.

Persistent saved conversations and recording controls are being developed on
[the recording-settings branch](https://github.com/grassmann-it/lain/tree/development/recording-settings-20261101).
That work includes local message/tool capture, retention limits and explicit
capture-gap reporting. Recording is separate from permission to export data for
training or send it to an external evaluator.

## Documentation

- [CLI reference](docs/cli-usage.md)
- [Provider signup and referral links](docs/provider-referrals-overview-2026-10-26.md)
- [OAuth and device-login support](docs/oauth-provider-overview-2026-10-26.md)
- [Development](docs/development.md) · [Architecture](docs/architecture.md)
- [Issues and feature requests](https://github.com/grassmann-it/lain/issues)

## License and credits

First-party code is licensed under the [Artistic License 2.0](LICENSE).
Dependencies and bundled assets retain their own licenses; see [NOTICE](NOTICE).
The Gateway uses a pinned CLIProxyAPI build with Lain integration patches.

Cat photo: [Alexander Huxel · Webentwicklung Huxel](https://webentwicklung-huxel.de/)
([Flickr](https://flickr.com/people/204852984@N05/)).

If lain is useful to you, you can [support development with a voluntary donation](https://www.paypal.com/donate/?hosted_button_id=5BR4MK748PDSQ).
