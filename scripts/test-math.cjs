// Expand test filenames ourselves so npm test also works in Windows shells.
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const directory = path.resolve(__dirname, '../tests/OpenWiimote.App');
const tests = fs.readdirSync(directory)
  .filter((name) => name.endsWith('.test.mjs')).sort()
  .map((name) => path.join(directory, name));
if (!tests.length) {
  console.error('No calibration/pose math tests found.');
  process.exit(1);
}
const result = spawnSync(process.execPath, ['--test', ...tests], { stdio: 'inherit' });
if (result.error) console.error(result.error.message);
process.exit(result.status ?? 1);
