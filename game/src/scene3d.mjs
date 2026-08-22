import * as THREE from '../vendor/three.module.min.js';
import {
  CAMERA_FAR,
  CAMERA_FOV_DEGREES,
  CAMERA_NEAR,
  CAMERA_POSITION_Z,
  projectWorldPosition,
} from './projection.mjs';

const MAX_DPR = 2;
const DEFEAT_SECONDS = 0.9;
const KINDS = new Set(['normal', 'armored', 'boss']);
const DEFAULT_RADII = { normal: 0.055, armored: 0.07, boss: 0.12 };

export function createScene3D(canvas) {
  if (!canvas) throw new TypeError('createScene3D requires a canvas');

  const renderer = new THREE.WebGLRenderer({
    canvas,
    alpha: true,
    antialias: true,
    powerPreference: 'high-performance',
    premultipliedAlpha: true,
  });
  renderer.setClearColor(0x000000, 0);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.08;

  const scene = new THREE.Scene();
  scene.fog = new THREE.Fog(0xd8d6e7, 5.8, 10.5);

  const camera = new THREE.PerspectiveCamera(
    CAMERA_FOV_DEGREES,
    16 / 9,
    CAMERA_NEAR,
    CAMERA_FAR,
  );
  camera.position.set(0, 0, CAMERA_POSITION_Z);

  const hemisphere = new THREE.HemisphereLight(0xfff3d1, 0x393b6c, 2.15);
  const key = new THREE.DirectionalLight(0xffd6a0, 3.4);
  key.position.set(-3.5, 5, 4.5);
  const rim = new THREE.DirectionalLight(0x63e8ff, 2.8);
  rim.position.set(4, 1.5, -2.5);
  scene.add(hemisphere, key, rim);

  const resources = createSharedResources();
  const activeActors = new Map();
  const actorPool = new Map();
  const reserveActors = new Map([
    ['normal', []],
    ['armored', []],
    ['boss', []],
  ]);
  let disposed = false;

  function resize(width, height, dpr = 1) {
    if (disposed) return;
    const safeWidth = Math.max(1, Math.round(finiteOr(width, 1)));
    const safeHeight = Math.max(1, Math.round(finiteOr(height, 1)));
    const safeDpr = Math.max(1, finiteOr(dpr, 1));
    camera.aspect = safeWidth / safeHeight;
    camera.updateProjectionMatrix();
    renderer.setPixelRatio(Math.min(MAX_DPR, safeDpr));
    renderer.setSize(safeWidth, safeHeight, false);
  }

  function sync(entities = [], defeatedActors = [], elapsed = 0) {
    if (disposed) return;
    const now = finiteOr(elapsed, 0);
    const desired = new Map();

    for (const entity of Array.isArray(entities) ? entities : []) {
      if (entity?.id === undefined || entity?.id === null) continue;
      desired.set(entity.id, { entity, defeated: false });
    }
    for (const entity of Array.isArray(defeatedActors) ? defeatedActors : []) {
      if (entity?.id === undefined || entity?.id === null) continue;
      desired.set(entity.id, { entity, defeated: true });
    }

    for (const [id, actor] of activeActors) {
      if (desired.has(id)) continue;
      releaseActor(id, actor);
    }

    for (const [id, state] of desired) {
      const kind = normalizeKind(state.entity.kind);
      let actor = activeActors.get(id);
      if (actor && actor.kind !== kind) {
        releaseActor(id, actor);
        actor = null;
      }
      if (!actor) actor = acquireActor(id, kind);
      poseActor(actor, state.entity, state.defeated, now, camera.aspect);
    }
  }

  function acquireActor(id, kind) {
    const reserve = reserveActors.get(kind);
    const actor = reserve.pop() ?? createActor(kind, resources);
    actor.id = id;
    actor.root.visible = true;
    actor.root.userData.entityId = id;
    actorPool.set(id, actor);
    activeActors.set(id, actor);
    if (!actor.root.parent) scene.add(actor.root);
    return actor;
  }

  function releaseActor(id, actor) {
    activeActors.delete(id);
    actorPool.delete(id);
    restoreSharedMaterials(actor);
    actor.root.visible = false;
    actor.id = null;
    reserveActors.get(actor.kind).push(actor);
  }

  function render() {
    if (!disposed) renderer.render(scene, camera);
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    for (const actor of activeActors.values()) restoreSharedMaterials(actor);
    for (const reserve of reserveActors.values()) {
      for (const actor of reserve) restoreSharedMaterials(actor);
    }
    activeActors.clear();
    actorPool.clear();
    for (const geometry of Object.values(resources.geometries)) geometry.dispose();
    for (const material of Object.values(resources.materials)) material.dispose();
    renderer.dispose();
  }

  return { resize, sync, render, dispose };
}

function createSharedResources() {
  const geometries = {
    body: new THREE.SphereGeometry(0.52, 12, 8),
    head: new THREE.SphereGeometry(0.42, 12, 8),
    muzzle: new THREE.SphereGeometry(0.19, 10, 6),
    eye: new THREE.SphereGeometry(0.075, 8, 6),
    ear: new THREE.ConeGeometry(0.2, 0.5, 3),
    limb: new THREE.CylinderGeometry(0.09, 0.12, 0.5, 8),
    paw: new THREE.SphereGeometry(0.135, 8, 6),
    tail: new THREE.CylinderGeometry(0.075, 0.105, 0.62, 8),
    plate: new THREE.SphereGeometry(0.34, 10, 7),
    heart: new THREE.OctahedronGeometry(0.18, 1),
    thorn: new THREE.ConeGeometry(0.09, 0.36, 6),
    shadow: new THREE.CircleGeometry(0.63, 20),
  };

  const standard = (color, extra = {}) => new THREE.MeshStandardMaterial({
    color,
    roughness: 0.72,
    metalness: 0.02,
    ...extra,
  });
  const materials = {
    normalFur: standard(0xc88bd9),
    armoredFur: standard(0xe994a8),
    bossFur: standard(0x563052, { roughness: 0.52 }),
    cream: standard(0xffddba),
    armor: standard(0x7c6a9f, { metalness: 0.62, roughness: 0.3 }),
    bossArmor: standard(0x3b345c, { metalness: 0.7, roughness: 0.25 }),
    eye: standard(0xfff8d7, { emissive: 0xffb84d, emissiveIntensity: 1.7 }),
    heart: standard(0xff4ca6, { emissive: 0xff176d, emissiveIntensity: 2.8 }),
    shadow: new THREE.MeshBasicMaterial({
      color: 0x281e3d,
      transparent: true,
      opacity: 0.26,
      depthWrite: false,
    }),
  };
  return { geometries, materials };
}

function createActor(kind, resources) {
  const { geometries, materials } = resources;
  const root = new THREE.Group();
  const rig = new THREE.Group();
  root.add(rig);

  const fur = kind === 'boss'
    ? materials.bossFur
    : kind === 'armored'
      ? materials.armoredFur
      : materials.normalFur;
  const body = mesh(geometries.body, fur, 0, 0.08, 0);
  body.scale.set(0.78, 1.02, 0.7);
  const head = mesh(geometries.head, fur, 0, 0.72, 0.03);
  head.scale.set(1.02, 0.92, 0.92);
  const muzzle = mesh(geometries.muzzle, materials.cream, 0, 0.61, 0.36);
  muzzle.scale.set(1.18, 0.68, 0.62);
  const leftEye = mesh(geometries.eye, materials.eye, -0.15, 0.79, 0.38);
  const rightEye = mesh(geometries.eye, materials.eye, 0.15, 0.79, 0.38);
  const leftEar = mesh(geometries.ear, fur, -0.24, 1.12, -0.01);
  const rightEar = mesh(geometries.ear, fur, 0.24, 1.12, -0.01);
  leftEar.rotation.z = 0.12;
  rightEar.rotation.z = -0.12;

  const limbs = [];
  for (const x of [-0.26, 0.26]) {
    const limb = mesh(geometries.limb, fur, x, -0.5, 0.02);
    const paw = mesh(geometries.paw, materials.cream, x, -0.77, 0.08);
    paw.scale.set(1.15, 0.65, 1.3);
    limbs.push(limb, paw);
    rig.add(limb, paw);
  }

  const tail = mesh(geometries.tail, fur, 0.48, -0.02, -0.18);
  tail.rotation.z = -0.78;
  tail.rotation.x = 0.28;
  const shadow = mesh(geometries.shadow, materials.shadow, 0, -0.83, -0.25);
  shadow.scale.set(0.9, 0.22, 1);
  rig.add(body, head, muzzle, leftEye, rightEye, leftEar, rightEar, tail, shadow);

  const armorParts = [];
  if (kind !== 'normal') {
    const armorMaterial = kind === 'boss' ? materials.bossArmor : materials.armor;
    const breastplate = mesh(geometries.plate, armorMaterial, 0, 0.04, 0.38);
    breastplate.scale.set(0.78, 0.82, 0.22);
    const leftShoulder = mesh(geometries.plate, armorMaterial, -0.43, 0.16, 0.04);
    const rightShoulder = mesh(geometries.plate, armorMaterial, 0.43, 0.16, 0.04);
    leftShoulder.scale.set(0.48, 0.38, 0.48);
    rightShoulder.scale.copy(leftShoulder.scale);
    armorParts.push(breastplate, leftShoulder, rightShoulder);
    rig.add(...armorParts);
  }

  let heart = null;
  const crown = [];
  if (kind === 'boss') {
    heart = mesh(geometries.heart, materials.heart, 0, 0.05, 0.59);
    heart.rotation.z = Math.PI / 4;
    rig.add(heart);
    for (let index = 0; index < 5; index += 1) {
      const thorn = mesh(
        geometries.thorn,
        materials.bossArmor,
        (index - 2) * 0.17,
        1.2 + (2 - Math.abs(index - 2)) * 0.07,
        -0.04,
      );
      thorn.rotation.z = (index - 2) * -0.13;
      crown.push(thorn);
      rig.add(thorn);
    }
  }

  root.visible = false;
  return {
    id: null,
    kind,
    root,
    rig,
    body,
    head,
    ears: [leftEar, rightEar],
    tail,
    limbs,
    armorParts,
    heart,
    crown,
    fadedMaterials: null,
  };
}

function poseActor(actor, entity, defeated, elapsed, aspect) {
  const position = projectWorldPosition(entity, aspect);
  const radius = Math.max(0.02, finiteOr(entity.radius, DEFAULT_RADII[actor.kind]));
  const modelScale = radius * 3.15 * (actor.kind === 'boss' ? 1.08 : 1);
  const phase = numericId(entity.id) * 0.71;
  const idle = Math.sin(elapsed * 3.2 + phase);
  const breath = 1 + idle * 0.025;
  const hitAge = elapsed - finiteOr(entity.hitAt, -99);
  const hit = hitAge >= 0 && hitAge < 0.18 ? 1 - hitAge / 0.18 : 0;
  const defeatAge = Math.max(0, finiteOr(entity.defeatAge, 0));
  const defeat = defeated ? Math.min(1, defeatAge / DEFEAT_SECONDS) : 0;
  const direction = numericId(entity.id) % 2 ? -1 : 1;

  actor.root.position.set(position.x, position.y, position.z - hit * 0.28);
  actor.root.position.x += Math.sin(elapsed * 1.7 + phase) * modelScale * 0.06;
  actor.root.position.y -= defeat * defeat * modelScale * 3.4;
  actor.root.rotation.set(defeat * 0.48, 0, direction * defeat * 1.32);
  actor.root.scale.set(
    modelScale * (1 + hit * 0.1) * (1 - defeat * 0.22),
    modelScale * breath * (1 - hit * 0.14) * (1 - defeat * 0.42),
    modelScale * (1 + hit * 0.08) * (1 - defeat * 0.22),
  );
  actor.rig.position.y = idle * 0.025;
  actor.head.rotation.z = idle * 0.018 + Math.sin(hitAge * 130) * hit * 0.1;
  actor.ears[0].rotation.z = 0.12 + Math.sin(elapsed * 4.1 + phase) * 0.055;
  actor.ears[1].rotation.z = -0.12 - Math.sin(elapsed * 4.1 + phase + 0.8) * 0.055;
  actor.tail.rotation.z = -0.78 + Math.sin(elapsed * 2.5 + phase) * 0.17;

  if (actor.heart) {
    const maxHp = Math.max(1, finiteOr(entity.maxHp, 8));
    const secondPhase = finiteOr(entity.hp, maxHp) <= maxHp / 2;
    const pulse = 1 + Math.sin(elapsed * (secondPhase ? 8 : 4)) * (secondPhase ? 0.2 : 0.08);
    actor.heart.scale.setScalar(pulse);
  }

  if (defeated) applyFade(actor, 1 - defeat);
  else restoreSharedMaterials(actor);
  actor.root.visible = defeat < 1;
}

function applyFade(actor, opacity) {
  if (!actor.fadedMaterials) {
    const clones = new Map();
    actor.root.traverse((object) => {
      if (!object.isMesh || object.material === undefined) return;
      const shared = object.material;
      let faded = clones.get(shared);
      if (!faded) {
        faded = shared.clone();
        faded.transparent = true;
        faded.depthWrite = false;
        faded.userData.actorBaseOpacity = shared.opacity;
        clones.set(shared, faded);
      }
      object.userData.sharedMaterial = shared;
      object.material = faded;
    });
    actor.fadedMaterials = [...clones.values()];
  }
  for (const material of actor.fadedMaterials) {
    material.opacity = material.userData.actorBaseOpacity * Math.max(0, opacity);
  }
}

function restoreSharedMaterials(actor) {
  if (!actor.fadedMaterials) return;
  actor.root.traverse((object) => {
    if (!object.isMesh || !object.userData.sharedMaterial) return;
    object.material = object.userData.sharedMaterial;
    delete object.userData.sharedMaterial;
  });
  for (const material of actor.fadedMaterials) material.dispose();
  actor.fadedMaterials = null;
}

function mesh(geometry, material, x, y, z) {
  const object = new THREE.Mesh(geometry, material);
  object.position.set(x, y, z);
  object.frustumCulled = true;
  return object;
}

function normalizeKind(kind) {
  return KINDS.has(kind) ? kind : 'normal';
}

function numericId(id) {
  if (Number.isFinite(id)) return Math.abs(id);
  return [...String(id)].reduce((total, character) => total + character.charCodeAt(0), 0);
}

function finiteOr(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}
