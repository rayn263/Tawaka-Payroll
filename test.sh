#!/usr/bin/env bash
# Runs every automated test. Skipped tests are expected: they are the compliance cases that need
# calculation-engine milestones still to come.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet test Tawaka.Payroll.Core.sln --nologo "$@"
