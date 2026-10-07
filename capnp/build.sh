#!/usr/bin/env bash
# Regenerate C# bindings from the vendored LTS schema.
# Requires: capnp (brew install capnp) and capnpc-csharp — either on PATH
# (dotnet tool install -g capnpc-csharp) or as a repo-local shim at
# tools-bin/capnpc-csharp (see AGENTS.md, sandbox notes).
set -euo pipefail
cd "$(dirname "$0")"
REPO=$(cd .. && pwd)
if [ -x "$REPO/tools-bin/capnpc-csharp" ]; then
  PLUGIN="$REPO/tools-bin/capnpc-csharp"
else
  PLUGIN=csharp
fi
capnp compile -o "$PLUGIN:$REPO/src/Lts/Schema" open_motion_telemetry.capnp
echo "generated: $REPO/src/Lts/Schema/open_motion_telemetry.capnp.cs"
