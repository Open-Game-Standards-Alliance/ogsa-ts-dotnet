# ogsa-ts-dotnet working conventions

- Schema is vendored at `capnp/ogsa_telemetry.capnp` from the
  OGSA-Telemetry-Standard repo (single source of truth lives there).
  After editing the schema there, copy it here and regenerate both sides
  (generated code is committed): `python capnp/gen_writers.py` →
  `src/Ogsa.Telemetry/Schema/ogsa_telemetry.writers.cs`, and
  `capnp/build.sh` → `src/Ogsa.Telemetry.Receiving/Schema/ogsa_telemetry.capnp.cs`.
- Product docs describe current state only; history lives in the
  Open-Game-Standards-Alliance knowledge base.
- `src/Ogsa.Telemetry` is the vendored core of the engine plugins and has
  no package dependencies beyond System.Memory on netstandard2.0: frames
  are written by the generated writers (`Ogsa.Telemetry.Schema`) over
  `Wire.cs`, in canonical OGSA encoding. Never add Capnp.Net (or anything
  reflection-based) to it.
- Decoding uses `Capnp.Net.Runtime` in `src/Ogsa.Telemetry.Receiving` only;
  its generated readers stay under `CapnpGen`. Generated code in either
  project is not hand-edited.
- Conformance checks a receiver should enforce live in
  `src/TelemetryReceive/Program.cs` (magic, schemaVersion, quaternion norm,
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
