#!/usr/bin/env bash
# Regenerate C# bindings from the vendored LTS schema.
# Requires: capnp (brew install capnp) and capnpc-csharp on PATH
# (dotnet tool install -g capnpc-csharp).
set -euo pipefail
cd "$(dirname "$0")"
capnp compile -o csharp:../src/Lts/Schema open_motion_telemetry.capnp
echo "generated: ../src/Lts/Schema/open_motion_telemetry.capnp.cs"
