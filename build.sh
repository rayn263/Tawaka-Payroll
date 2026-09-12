#!/usr/bin/env bash
# Builds the cross-platform solution (everything except the Windows desktop shell).
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build Tawaka.Payroll.Core.sln --nologo "$@"
