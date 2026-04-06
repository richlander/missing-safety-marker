#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/FindCSharpUnsafeMethods/FindCSharpUnsafeMethods.csproj"

exec dotnet run -c Release --project "$PROJECT" -- "$@"
