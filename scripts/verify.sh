#!/usr/bin/env bash
# Runs every check the project has. CI calls this script, so a green local run and a
# green pipeline mean the same thing.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== build =="
dotnet build --nologo

echo "== unit and window tests =="
dotnet test --nologo -v q

echo "== web UI tests =="
npx playwright test

echo "== report replay =="
dotnet run --project src/OpenWiimote.App/OpenWiimote.App.csproj -c Release -- \
  --replay tools/sample-reports.txt


# Licensing checks use the Python standard library and never fetch dependencies.
python3 -m unittest discover -s tests/licensing -p 'test_*.py'

echo "== all checks passed =="
