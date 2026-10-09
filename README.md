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
| `src/Ogsa.Telemetry` | Library: `Envelope` (OMT1 header + Cap'n Proto message codec), `TelemetrySender`, `TelemetryReceiver` |
| `src/Ogsa.Telemetry/Schema` | Generated bindings (`capnp/build.sh` regenerates) |
| `src/TelemetryDemo` | Demo sender: one car, discovery every 2 s, ~60 Hz motion |
| `src/TelemetryReceive` | Reference receiver with conformance checks |
| `tests/Ogsa.Telemetry.Tests` | xunit: envelope codec, capnp roundtrip, UDP multicast loopback |

## Build & test

```bash
dotnet build
dotnet test
```

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
