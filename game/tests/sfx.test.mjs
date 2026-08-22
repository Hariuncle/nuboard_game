import test from 'node:test';
import assert from 'node:assert/strict';

import { createSfx, cueDefinition } from '../src/sfx.mjs';

const CUE_NAMES = ['shot', 'hit', 'miss', 'boss', 'victory', 'defeat'];

test('defines every gameplay cue as bounded note or noise instructions', () => {
  for (const name of CUE_NAMES) {
    const cue = cueDefinition(name);

    assert.ok(cue.length > 0, `${name} should have at least one instruction`);
    for (const instruction of cue) {
      assert.ok(['note', 'noise'].includes(instruction.type));
      assert.ok(instruction.duration > 0);
      assert.ok(instruction.duration <= 0.45);
    }
  }

  assert.deepEqual(cueDefinition('unknown'), []);
  assert.deepEqual(cueDefinition('toString'), []);
});

test('returns defensive cue copies', () => {
  const first = cueDefinition('shot');
  first[0].duration = 99;
  first.push({ type: 'noise', duration: 99 });

  const second = cueDefinition('shot');
  assert.ok(second[0].duration <= 0.45);
  assert.notEqual(second.length, first.length);
});

test('is safe when Web Audio is unavailable', async () => {
  const sfx = createSfx(undefined);

  await assert.doesNotReject(sfx.unlock());
  assert.equal(sfx.play('shot'), false);
  assert.equal(sfx.play('unknown'), false);
});

test('lazily creates and resumes a suspended audio context', async () => {
  const { AudioContextClass, instances } = makeFakeAudioContext();
  const sfx = createSfx(AudioContextClass);

  assert.equal(instances.length, 0);
  await sfx.unlock();

  assert.equal(instances.length, 1);
  assert.equal(instances[0].resumeCalls, 1);
});

test('does not schedule audio nodes when resuming the context rejects', () => {
  const { AudioContextClass, instances } = makeFakeAudioContext({ failResume: true });
  const sfx = createSfx(AudioContextClass);

  const played = sfx.play('shot');

  assert.equal(played, false);
  assert.equal(instances[0].oscillators.length, 0);
  assert.equal(instances[0].bufferSources.length, 0);
  assert.equal(instances[0].gains.length, 0);
});

test('does not schedule audio nodes while the context remains suspended', () => {
  const { AudioContextClass, instances } = makeFakeAudioContext({ remainSuspended: true });
  const sfx = createSfx(AudioContextClass);

  const played = sfx.play('shot');

  assert.equal(played, false);
  assert.equal(instances[0].oscillators.length, 0);
  assert.equal(instances[0].bufferSources.length, 0);
  assert.equal(instances[0].gains.length, 0);
});

test('plays note and noise cues with short gain envelopes and stopped sources', () => {
  let totalNotes = 0;
  let totalNoise = 0;

  for (const name of CUE_NAMES) {
    const { AudioContextClass, instances } = makeFakeAudioContext();
    const sfx = createSfx(AudioContextClass);
    const cue = cueDefinition(name);
    const latestStopOffset = Math.max(
      ...cue.map((instruction) => (instruction.delay ?? 0) + instruction.duration),
    ) + 0.01;

    assert.equal(sfx.play(name), true);

    const context = instances[0];
    const sources = [...context.oscillators, ...context.bufferSources];
    const expectedNotes = cue.filter((instruction) => instruction.type === 'note').length;
    const expectedNoise = cue.filter((instruction) => instruction.type === 'noise').length;
    totalNotes += expectedNotes;
    totalNoise += expectedNoise;

    assert.equal(context.oscillators.length, expectedNotes);
    assert.equal(context.bufferSources.length, expectedNoise);
    assert.equal(context.gains.length, cue.length);

    for (const source of sources) {
      assert.equal(source.started, true);
      assert.ok(source.stopAt > context.currentTime);
      assert.ok(source.stopAt <= context.currentTime + latestStopOffset + 1e-9);
      source.onended?.();
      assert.equal(source.disconnected, true);
    }

    for (const gain of context.gains) {
      assert.ok(gain.gain.events.some((event) => event.method.includes('RampToValueAtTime')));
    }
  }

  assert.ok(totalNotes > 0);
  assert.ok(totalNoise > 0);
});

test('play returns false when no cue source can be scheduled', () => {
  const { AudioContextClass } = makeFakeAudioContext({
    failOscillator: true,
    initialState: 'running',
  });
  const sfx = createSfx(AudioContextClass);

  assert.equal(sfx.play('shot'), false);
  assert.equal(sfx.play('unknown'), false);
});

test('contains constructor, resume, and playback failures', async () => {
  class BrokenContext {
    constructor() {
      throw new Error('constructor failure');
    }
  }

  const broken = createSfx(BrokenContext);
  await assert.doesNotReject(broken.unlock());
  assert.equal(broken.play('shot'), false);

  const { AudioContextClass } = makeFakeAudioContext({ failResume: true, failOscillator: true });
  const flaky = createSfx(AudioContextClass);
  await assert.doesNotReject(flaky.unlock());
  assert.doesNotThrow(() => flaky.play('shot'));
});

function makeFakeAudioContext({
  failResume = false,
  failOscillator = false,
  remainSuspended = false,
  initialState = 'suspended',
} = {}) {
  const instances = [];

  class FakeAudioContext {
    constructor() {
      this.state = initialState;
      this.currentTime = 10;
      this.sampleRate = 8_000;
      this.destination = {};
      this.resumeCalls = 0;
      this.oscillators = [];
      this.bufferSources = [];
      this.gains = [];
      instances.push(this);
    }

    resume() {
      this.resumeCalls += 1;
      if (failResume) return Promise.reject(new Error('resume failure'));
      if (!remainSuspended) this.state = 'running';
      return Promise.resolve();
    }

    createOscillator() {
      if (failOscillator) throw new Error('oscillator failure');
      const oscillator = makeSource();
      oscillator.frequency = makeAudioParam();
      oscillator.detune = makeAudioParam();
      this.oscillators.push(oscillator);
      return oscillator;
    }

    createBufferSource() {
      const source = makeSource();
      this.bufferSources.push(source);
      return source;
    }

    createGain() {
      const gain = {
        gain: makeAudioParam(),
        connect() {},
        disconnect() {
          this.disconnected = true;
        },
      };
      this.gains.push(gain);
      return gain;
    }

    createBuffer(channels, length) {
      const data = new Float32Array(length);
      return {
        numberOfChannels: channels,
        length,
        getChannelData: () => data,
      };
    }
  }

  return { AudioContextClass: FakeAudioContext, instances };
}

function makeSource() {
  return {
    connect() {},
    disconnect() {
      this.disconnected = true;
    },
    start() {
      this.started = true;
    },
    stop(time) {
      this.stopAt = time;
    },
  };
}

function makeAudioParam() {
  const events = [];
  return {
    events,
    setValueAtTime(value, time) {
      events.push({ method: 'setValueAtTime', value, time });
    },
    linearRampToValueAtTime(value, time) {
      events.push({ method: 'linearRampToValueAtTime', value, time });
    },
    exponentialRampToValueAtTime(value, time) {
      events.push({ method: 'exponentialRampToValueAtTime', value, time });
    },
  };
}
