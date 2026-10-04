const fs = require('node:fs');
const path = require('node:path');

const repositoryRoot = path.resolve(__dirname, '..');
const source = path.join(repositoryRoot, 'apps', 'desktop', 'angular', 'dist', 'angular', '3rdpartylicenses.txt');
const target = path.join(
  repositoryRoot,
  'apps',
  'desktop',
  'angular',
  'dist',
  'angular',
  'browser',
  'THIRD-PARTY-LICENSES.txt',
);

if (!fs.existsSync(source)) {
  throw new Error(`Angular third-party license bundle was not generated: ${source}`);
}

if (!fs.existsSync(path.dirname(target))) {
  throw new Error(`Angular browser output directory is missing: ${path.dirname(target)}`);
}

fs.copyFileSync(source, target);
