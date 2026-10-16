#!/bin/bash
# Serialized dotnet for several agents sharing one worktree (see docs/DEV_GUIDE.md "Building in a shared worktree").
#   tools/agents/dn.sh build Rts.slnx
#   tools/agents/dn.sh test tests/Rts.Sim.Tests --filter "FullyQualifiedName~TransportTests"
# Run it from inside the worktree. One lock per worktree: <worktree>/.claude/build.lock (gitignored).
# flock -o: the lock fd is NOT inherited by dotnet/MSBuild children. Without -o, reused MSBuild nodes inherited it
# and kept the lock held forever (it happened in wave 4: four orphaned nodes blocked every build).
# MSBuild node reuse and the MSBuild server are off for the same reason.
set -e
root=$(git rev-parse --show-toplevel)
mkdir -p "$root/.claude"
export MSBUILDDISABLENODEREUSE=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
exec flock -o "$root/.claude/build.lock" dotnet "$@"
