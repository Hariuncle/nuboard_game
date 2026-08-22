import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const sceneUrl = new URL('../src/scene3d.mjs', import.meta.url);
const projectionUrl = new URL('../src/projection.mjs', import.meta.url);

test('the 3D scene uses the pinned local renderer and exports its lifecycle API', async () => {
  const source = await readFile(sceneUrl, 'utf8');

  assert.match(source, /from ['"]\.\.\/vendor\/three\.module\.min\.js['"]/);
  assert.match(source, /export function createScene3D\s*\(/);
  assert.doesNotMatch(source, /TextureLoader|character(?:-|\s)?sheet|spriteSheets/i);
  for (const method of ['resize', 'sync', 'render', 'dispose']) {
    assert.match(source, new RegExp(`\\b${method}\\b`));
  }
});

test('the vendored renderer resolves entirely from local files', async () => {
  const sceneModule = await import(sceneUrl.href);

  assert.equal(typeof sceneModule.createScene3D, 'function');
});

test('procedural actors cover every enemy class and depth animation state', async () => {
  const source = await readFile(sceneUrl, 'utf8');
  const projectionSource = await readFile(projectionUrl, 'utf8');

  for (const kind of ['normal', 'armored', 'boss']) {
    assert.match(source, new RegExp(`['"]${kind}['"]`));
  }
  assert.match(source, /defeatedActors/);
  assert.match(source, /actorPool/);
  assert.match(source, /projectWorldPosition/);
  assert.match(projectionSource, /entity\?\.depth/);
  assert.match(projectionSource, /entity\?\.laneX/);
  assert.match(source, /setPixelRatio\s*\(\s*Math\.min/);
});
