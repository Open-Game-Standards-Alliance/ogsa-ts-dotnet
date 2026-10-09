# ogsa-ts-dotnet

.NET reference implementation of the [OGSA Telemetry Standard (OGSA-TS)](../OGSA-Telemetry-Standard)
— a two-layer, UDP-multicast motion telemetry protocol for motion platforms,
haptics, and cueing systems. Loopback-testable on any single machine
(macOS, Linux, Windows); the same code is the starting point for Unity
integrations and Windows-based receivers.

Status: **bootstrap** — envelope, multicast transport, generated Cap'n Proto
bindings, a demo sender, a reference receiver, and a passing loopback test
suite. The schema layer is generated from the standard's schema
(`ogsa_telemetry.capnp`, currently v1); wire compatibility follows
the standard's evolution rules (unknown fields/enums/frame types skipped).

## Layout

| Path | Contents |
|---|---|
| `src/Ogsa.Telemetry` | Library: `Envelope` (OMT1 header + Cap'n Proto message codec), `TelemetrySender`, `TelemetryReceiver`, `TelemetryPublisher` (declare/bind/push wiring), `Unit` + `Conversions` (bind-time unit massaging) |
| `src/Ogsa.Telemetry/Schema` | Generated bindings (`capnp/build.sh` regenerates) |
| `src/TelemetryDemo` | Demo sender: one car, discovery every 2 s, ~60 Hz motion |
| `src/TelemetryReceive` | Reference receiver with conformance checks |
| `src/ConformanceGen` | Deterministic conformance-corpus generator (writes to the standard repo) |
| `tests/Ogsa.Telemetry.Tests` | xunit: envelope codec, capnp roundtrip, UDP multicast loopback |

## Build & test

```bash
dotnet build
dotnet test
```

## Multi-target (Unity/Godot compat)

`Ogsa.Telemetry` targets `netstandard2.1;net10.0` at `LangVersion 9.0`
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
capnp/build.sh
```

Requires `capnp` (brew install capnp) and `capnpc-csharp`
(dotnet tool install -g capnpc-csharp).
