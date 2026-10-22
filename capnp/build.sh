#!/usr/bin/env bash
# Regenerate the C# readers (Capnp.Net, receiving side) from the vendored
# schema. The sending side's writers come from gen_writers.py.
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
capnp compile -o "$PLUGIN:$REPO/src/Ogsa.Telemetry.Receiving/Schema" ogsa_telemetry.capnp
echo "generated: $REPO/src/Ogsa.Telemetry.Receiving/Schema/ogsa_telemetry.capnp.cs"

# The generator emits nullable-unsafe patterns (CS86xx) under the project's
# nullable context; silence them at the file level — generated code is
# policy-exempt from hand edits, and this is a mechanical part of generation.
GEN="$REPO/src/Ogsa.Telemetry.Receiving/Schema/ogsa_telemetry.capnp.cs"
printf '#pragma warning disable 8600,8601,8602,8603,8604,8618,8619,8625,8629,8632\n' > "$GEN.tmp"
cat "$GEN" >> "$GEN.tmp"
printf '#pragma warning restore 8600,8601,8602,8603,8604,8618,8619,8625,8629,8632\n' >> "$GEN.tmp"
mv "$GEN.tmp" "$GEN"
echo "suppressed nullable warnings in generated file"
