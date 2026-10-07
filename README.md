# lts-dotnet

.NET reference implementation of the [Local Telemetry Standard (LTS)](../Local-Telemetry-Standard)
— a two-layer, UDP-multicast motion telemetry protocol for motion platforms,
haptics, and cueing systems. Loopback-testable on any single machine
(macOS, Linux, Windows); the same code is the starting point for Unity
integrations and Windows-based receivers.

Status: **bootstrap** — envelope, multicast transport, generated Cap'n Proto
bindings, a demo sender, a reference receiver, and a passing loopback test
suite. The schema layer is generated from the standard's schema
(`open_motion_telemetry.capnp`, currently v1); wire compatibility follows
the standard's evolution rules (unknown fields/enums/frame types skipped).

## Layout

| Path | Contents |
|---|---|
| `src/Lts` | Library: `Envelope` (OMT1 header + Cap'n Proto message codec), `LtsSender`, `LtsReceiver` |
| `src/Lts/Schema` | Generated bindings (`capnp/build.sh` regenerates) |
| `src/LtsDemo` | Demo sender: one car, discovery every 2 s, ~60 Hz motion |
| `src/LtsReceive` | Reference receiver with conformance checks |
| `tests/Lts.Tests` | xunit: envelope codec, capnp roundtrip, UDP multicast loopback |

## Build & test

```bash
dotnet build
dotnet test
```

## Loopback demo

Two terminals:

```bash
dotnet run --project src/LtsReceive
dotnet run --project src/LtsDemo
```

The receiver prints the discovery frame (object, drive points, channels),
motion at a throttled rate, and conformance warnings (magic/schemaVersion
handled by the library; quaternion norm and primary-object checked here).

Default endpoint: multicast `239.255.17.17:40123`, TTL 1 — nothing leaves
the local subnet. See the standard's `implementation-udp.md` for the
liveness rules receivers should apply.

## Regenerating bindings after a schema change

```bash
cp ../Local-Telemetry-Standard/open_motion_telemetry.capnp capnp/
capnp/build.sh
```

Requires `capnp` (brew install capnp) and `capnpc-csharp`
(dotnet tool install -g capnpc-csharp).
