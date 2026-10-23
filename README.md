# ogsa-ts-dotnet

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/telemetry-standard-dark.svg">
  <img align="right" width="120" src="assets/telemetry-standard.svg" alt="OGSA Telemetry Standard logo">
</picture>

.NET reference implementation of the [OGSA Telemetry Standard (OGSA-TS)](https://github.com/Open-Game-Standards-Alliance/OGSA-Telemetry-Standard)
— a two-layer, UDP-multicast motion telemetry protocol for motion platforms,
haptics, and cueing systems. Loopback-testable on any single machine
(macOS, Linux, Windows); the same code is the starting point for Unity
integrations and Windows-based receivers.

Community & support: [Discord](https://discord.gg/tseF4Q6U99)

Status: **bootstrap** — envelope, multicast transport, canonical schema
writers, a demo sender, a reference receiver, and a passing loopback test
suite. The schema layer is generated from the standard's schema
(`ogsa_telemetry.capnp`, currently v1); wire compatibility follows
the standard's evolution rules (unknown fields/enums/frame types skipped).

The sending library (`Ogsa.Telemetry`) has **no Cap'n Proto runtime
dependency**: frames are written by generated writers over a small
pure-managed encoder (`Wire.cs`) in the standard's canonical OGSA encoding
(implementation-capnproto.md, Encoding rules) — no reflection, no unsafe
code, no host-endian memory views, so it runs on Mono, IL2CPP, NativeAOT,
ARM and big-endian hosts alike. Decoding (receivers, tests) lives in
`Ogsa.Telemetry.Receiving`, which uses Capnp.Net.Runtime.

**Built with AI assistance.** Significant portions of this code
(including the wire codec, unit conversions, and body-dynamics
derivation) were written with AI coding assistance under
human direction and review. Review and test before relying on it,
especially around networking and the conversion/derivation math.

## Layout

| Path | Contents |
|---|---|
| `src/Ogsa.Telemetry` | Sending library, no dependencies: `Envelope` (OMT1 header + message framing), `Wire` (canonical encoder), `TelemetrySender`, `TelemetryPublisher` (declare/bind/push wiring), `Unit` + `Conversions` (bind-time unit massaging) |
| `src/Ogsa.Telemetry/Schema` | Generated canonical writers (`capnp/gen_writers.py` regenerates) |
| `src/Ogsa.Telemetry.Receiving` | Receiving library: `TelemetryReceiver`, `MessageDecoder`, generated Capnp.Net readers in `Schema/` (`capnp/build.sh` regenerates) |
| `src/TelemetryDemo` | Demo sender: one car, discovery every 2 s, ~60 Hz motion |
| `src/TelemetryReceive` | Reference receiver with conformance checks |
| `src/ConformanceGen` | Deterministic conformance-corpus generator (writes to the standard repo) |
| `tests/Ogsa.Telemetry.Tests` | xunit: canonical encoding, writer → Capnp.Net roundtrip, envelope, UDP multicast loopback |

## Build & test

```bash
dotnet build
dotnet test
```

## Multi-target (Unity/Godot compat)

`Ogsa.Telemetry` targets `netstandard2.0;netstandard2.1;net10.0` at `LangVersion 9.0`
(Unity's compiler ceiling), no implicit usings — the library source is the
vendored core for engine packages and must compile identically in every
host. Console projects (`TelemetryDemo`, `TelemetryReceive`) stay net10.0.

## Publishing API (engine-agnostic)

`TelemetryPublisher` is the two-phase wiring layer the engine plugins wrap:
declare the object, wheels, and channels (name + required target unit) —
that becomes the DiscoveryFrame; then bind getters (pull) or push values
per tick — they ride MotionFrame/EventFrame. Bind-time source units
(`Unit.KilometrePerHour`, `Unit.Degree`, `Unit.Psi`, …) convert with
precomputed factors (`Conversions`); mismatches throw at bind, not on the
wire. See `tests/Ogsa.Telemetry.Tests/PublisherTests.cs` for the pattern.

## Loopback demo

Two terminals:

```bash
dotnet run --project src/TelemetryReceive
dotnet run --project src/TelemetryDemo
```

The receiver prints the discovery frame (object, drive points, channels),
motion at a throttled rate, and conformance warnings (magic/schemaVersion
handled by the library; quaternion norm and primary-object checked here).

Default endpoint: multicast `239.255.17.17:40123`, TTL 1 — nothing leaves
the local subnet. See the standard's `implementation-udp.md` for the
liveness rules receivers should apply.

## Regenerating bindings after a schema change

```bash
cp ../OGSA-Telemetry-Standard/ogsa_telemetry.capnp capnp/
python capnp/gen_writers.py   # writers (sending)  — needs: pip install pycapnp
capnp/build.sh                # readers (receiving) — needs: capnp + capnpc-csharp
```

`capnp` via your package manager (brew/apt install capnp) and
`capnpc-csharp` via `dotnet tool install -g capnpc-csharp`. The writer
generator fails loudly on schema constructs it does not support yet.
