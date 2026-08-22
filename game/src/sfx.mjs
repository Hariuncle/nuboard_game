const CUES = Object.freeze({
  shot: Object.freeze([
    Object.freeze({ type: 'note', frequency: 920, endFrequency: 520, duration: 0.08, wave: 'sawtooth', volume: 0.11 }),
  ]),
  hit: Object.freeze([
    Object.freeze({ type: 'note', frequency: 520, endFrequency: 680, duration: 0.1, wave: 'square', volume: 0.1 }),
    Object.freeze({ type: 'note', frequency: 780, duration: 0.08, delay: 0.055, wave: 'sine', volume: 0.08 }),
  ]),
  miss: Object.freeze([
    Object.freeze({ type: 'noise', duration: 0.16, volume: 0.06 }),
    Object.freeze({ type: 'note', frequency: 180, endFrequency: 110, duration: 0.18, wave: 'triangle', volume: 0.06 }),
  ]),
  boss: Object.freeze([
    Object.freeze({ type: 'note', frequency: 92, endFrequency: 68, duration: 0.34, wave: 'sawtooth', volume: 0.13 }),
    Object.freeze({ type: 'noise', duration: 0.24, delay: 0.1, volume: 0.055 }),
  ]),
  victory: Object.freeze([
    Object.freeze({ type: 'note', frequency: 523.25, duration: 0.15, wave: 'triangle', volume: 0.09 }),
    Object.freeze({ type: 'note', frequency: 659.25, duration: 0.15, delay: 0.1, wave: 'triangle', volume: 0.09 }),
    Object.freeze({ type: 'note', frequency: 783.99, duration: 0.18, delay: 0.2, wave: 'triangle', volume: 0.1 }),
  ]),
  defeat: Object.freeze([
    Object.freeze({ type: 'note', frequency: 311.13, duration: 0.18, wave: 'sine', volume: 0.08 }),
    Object.freeze({ type: 'note', frequency: 233.08, duration: 0.18, delay: 0.12, wave: 'sine', volume: 0.08 }),
    Object.freeze({ type: 'note', frequency: 155.56, duration: 0.18, delay: 0.24, wave: 'sine', volume: 0.09 }),
  ]),
});

export function cueDefinition(name) {
  const cue = cueFor(name);
  return cue ? cue.map((instruction) => ({ ...instruction })) : [];
}

export function createSfx(
  AudioContextClass = globalThis.AudioContext || globalThis.webkitAudioContext,
) {
  let context = null;
  let contextAttempted = false;

  function getContext() {
    if (context || contextAttempted) return context;
    contextAttempted = true;

    try {
      context = typeof AudioContextClass === 'function' ? new AudioContextClass() : null;
    } catch {
      context = null;
    }

    return context;
  }

  async function unlock() {
    try {
      const audioContext = getContext();
      if (audioContext?.state === 'suspended' && typeof audioContext.resume === 'function') {
        await audioContext.resume();
      }
    } catch {
      // Sound must never interrupt gameplay.
    }
  }

  function play(name) {
    try {
      const cue = cueFor(name);
      if (!cue) return false;

      const audioContext = getContext();
      if (!audioContext) return false;

      if (!resumeWithoutWaiting(audioContext)) return false;
      const now = finiteNumber(audioContext.currentTime, 0);
      let scheduled = false;

      for (const instruction of cue) {
        scheduled = playInstruction(audioContext, instruction, now) || scheduled;
      }

      return scheduled;
    } catch {
      return false;
    }
  }

  return { unlock, play };
}

function cueFor(name) {
  return Object.hasOwn(CUES, name) ? CUES[name] : null;
}

function resumeWithoutWaiting(audioContext) {
  if (audioContext.state === 'running') return true;
  if (audioContext.state !== 'suspended' || typeof audioContext.resume !== 'function') return false;

  try {
    const result = audioContext.resume();
    result?.catch?.(() => {});
  } catch {
    return false;
  }

  return audioContext.state === 'running';
}

function playInstruction(audioContext, instruction, now) {
  let source = null;
  let gain = null;

  try {
    const delay = Math.max(0, finiteNumber(instruction.delay, 0));
    const startAt = now + delay;
    const endAt = startAt + instruction.duration;

    gain = audioContext.createGain();
    configureEnvelope(gain.gain, startAt, endAt, instruction.volume);
    gain.connect(audioContext.destination);

    source = instruction.type === 'noise'
      ? createNoiseSource(audioContext, instruction.duration)
      : createNoteSource(audioContext, instruction, startAt, endAt);
    source.connect(gain);

    source.onended = () => disconnect(source, gain);
    source.start(startAt);
    source.stop(endAt + 0.01);
    return true;
  } catch {
    try {
      source?.stop?.();
    } catch {
      // The source may not have started.
    }
    disconnect(source, gain);
    return false;
  }
}

function createNoteSource(audioContext, instruction, startAt, endAt) {
  const oscillator = audioContext.createOscillator();
  oscillator.type = instruction.wave || 'sine';
  oscillator.frequency.setValueAtTime(instruction.frequency, startAt);

  if (Number.isFinite(instruction.endFrequency)) {
    oscillator.frequency.exponentialRampToValueAtTime(
      Math.max(1, instruction.endFrequency),
      endAt,
    );
  }

  return oscillator;
}

function createNoiseSource(audioContext, duration) {
  const sampleRate = Math.max(1, finiteNumber(audioContext.sampleRate, 44_100));
  const frameCount = Math.max(1, Math.ceil(sampleRate * duration));
  const buffer = audioContext.createBuffer(1, frameCount, sampleRate);
  const samples = buffer.getChannelData(0);

  for (let index = 0; index < samples.length; index += 1) {
    samples[index] = Math.random() * 2 - 1;
  }

  const source = audioContext.createBufferSource();
  source.buffer = buffer;
  return source;
}

function configureEnvelope(audioParam, startAt, endAt, requestedVolume) {
  const floor = 0.0001;
  const volume = Math.max(floor, finiteNumber(requestedVolume, 0.08));
  const attackEnd = Math.min(endAt, startAt + 0.012);

  audioParam.setValueAtTime(floor, startAt);
  audioParam.exponentialRampToValueAtTime(volume, attackEnd);
  audioParam.exponentialRampToValueAtTime(floor, endAt);
}

function disconnect(source, gain) {
  try {
    source?.disconnect?.();
  } catch {
    // Nodes can already be disconnected by the browser.
  }

  try {
    gain?.disconnect?.();
  } catch {
    // Nodes can already be disconnected by the browser.
  }
}

function finiteNumber(value, fallback) {
  return Number.isFinite(value) ? value : fallback;
}
