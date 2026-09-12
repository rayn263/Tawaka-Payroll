#!/usr/bin/env bash
# Creates a fresh database from the migrations, seeds the statutory baseline, prints the rule
# register with verification status, and runs the live payroll gate.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet run --project tools/Tawaka.Foundation.Cli -- "${1:-}"
