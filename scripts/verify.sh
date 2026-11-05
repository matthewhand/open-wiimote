#!/usr/bin/env bash
# Local verification. GitHub Actions is intentionally disabled.
# Restore/download dependencies beforehand for a strictly offline run.
set -euo pipefail
cd "$(dirname "$0")/.."
echo '== build =='
dotnet build --nologo -c Release
echo '== unit and window tests =='
dotnet test --no-build --no-restore --nologo -c Release -v q
echo '== calibration and pose math =='
node scripts/test-math.cjs
echo '== web UI tests =='
npx --no-install playwright test
echo '== report replay =='
dotnet run --no-build --no-restore --project src/OpenWiimote.App/OpenWiimote.App.csproj -c Release -- --replay tools/sample-reports.txt
echo '== license and notices =='
python3 -m unittest discover -s tests/licensing -p 'test_*.py'
echo '== all checks passed =='
