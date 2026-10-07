# lts-dotnet working conventions

- Schema is vendored at `capnp/open_motion_telemetry.capnp` from the
  Local-Telemetry-Standard repo (single source of truth lives there).
  After editing the schema there, copy it here and run `capnp/build.sh`
  to regenerate `src/Lts/Schema/*.cs` (generated code is committed).
- Product docs describe current state only; history lives in the
  Open-Game-Standards-Alliance knowledge base.
- Reference `Capnp.Net.Runtime` (CapnpC #); generated code stays under
  `CapnpGen` and is not hand-edited.
- Conformance checks a receiver should enforce live in
  `src/LtsReceive/Program.cs` (magic, schemaVersion, quaternion norm,
  primary object) — keep that file the canonical example.

## Sandbox notes (agent environment)

The agent shell cannot reach HTTPS from .NET (TLS interception breaks
NuGet) and cannot write `$HOME` dotdirs. Workarounds used during
bootstrap, in case they're needed again:

- `DOTNET_CLI_HOME=/tmp/dotnet-home`, `NUGET_PACKAGES=/tmp/nuget/packages`
- `MSBUILDDISABLENODEREUSE=1` and `-m:1` (parallel MSBuild nodes hang)
- Local NuGet proxy: `python3 /tmp/nuget-proxy.py <port>` and point
  NuGet.config at `http://127.0.0.1:<port>/v3/index.json` with
  `allowInsecureConnections="true"`. Committed NuGet.config must stay
  on nuget.org.
- First multicast loopback test after boot may time out (macOS route
  warm-up); re-run before treating as a failure.
