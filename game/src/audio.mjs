import { createSfx } from "./sfx.mjs";

export const AUDIO_ASSETS = Object.freeze({
  shot: "./assets/audio/shot.mp3",
  hit: "./assets/audio/hit.mp3",
  hitPower: "./assets/audio/hit-power.mp3",
  miss: "./assets/audio/miss.mp3",
  spawn: "./assets/audio/spawn.mp3",
  fall: "./assets/audio/fall.mp3",
  exit: "./assets/audio/exit.mp3",
  boss: "./assets/audio/boss.mp3",
  bossPhase: "./assets/audio/boss-phase.mp3",
  bossDefeat: "./assets/audio/boss-defeat.mp3",
  combo: "./assets/audio/combo.mp3",
});

export const MUSIC_ASSETS = Object.freeze({
  meadow: "./assets/audio/bgm-meadow.mp3",
  wave: "./assets/audio/bgm-wave.mp3",
  boss: "./assets/audio/bgm-boss.mp3",
  victory: "./assets/audio/bgm-victory.mp3",
  defeat: "./assets/audio/bgm-defeat.mp3",
});

export function createGameAudio({
  AudioClass = globalThis.Audio,
  fallback = createSfx(),
} = {}) {
  const available = typeof AudioClass === "function";
  const prototypes = new Map();
  let music = null;
  let musicName = null;
  let requestedMusic = null;

  if (available) {
    for (const [name, source] of Object.entries(AUDIO_ASSETS)) {
      const audio = new AudioClass(source);
      audio.preload = "auto";
      audio.volume = 0.72;
      prototypes.set(name, audio);
    }
  }

  function unlock() {
    if (requestedMusic && !music) {
      startMusic(requestedMusic.name, requestedMusic.loop);
    }
    try {
      return Promise.resolve(fallback.unlock?.()).catch(() => {});
    } catch {
      return Promise.resolve();
    }
  }

  function play(name) {
    const prototype = prototypes.get(name);
    if (!prototype) return fallback.play?.(fallbackName(name)) ?? false;
    try {
      const voice = prototype.cloneNode?.(true) ?? new AudioClass(prototype.src);
      voice.volume = prototype.volume;
      const result = voice.play();
      result?.catch?.(() => fallback.play?.(fallbackName(name)));
      return true;
    } catch {
      return fallback.play?.(fallbackName(name)) ?? false;
    }
  }

  function setMusic(name, { loop = true } = {}) {
    if (!available || !Object.hasOwn(MUSIC_ASSETS, name)) return false;
    requestedMusic = { name, loop };
    if (musicName === name && music) return true;
    stopActiveMusic();
    return startMusic(name, loop);
  }

  function startMusic(name, loop) {
    try {
      const candidate = new AudioClass(MUSIC_ASSETS[name]);
      candidate.preload = "auto";
      candidate.loop = loop;
      candidate.volume = 0.32;
      music = candidate;
      musicName = name;
      const result = candidate.play();
      result?.catch?.(() => {
        if (music === candidate) {
          music = null;
          musicName = null;
        }
      });
      return true;
    } catch {
      music = null;
      musicName = null;
      return false;
    }
  }

  function stopMusic() {
    requestedMusic = null;
    stopActiveMusic();
  }

  function stopActiveMusic() {
    try {
      music?.pause?.();
      if (music) music.currentTime = 0;
    } catch {
      // Audio cleanup must never interrupt gameplay.
    }
    music = null;
    musicName = null;
  }

  return { unlock, play, setMusic, stopMusic };
}

function fallbackName(name) {
  if (name === "hitPower" || name === "fall" || name === "exit" || name === "combo") return "hit";
  if (name === "bossPhase") return "boss";
  if (name === "bossDefeat") return "victory";
  return name;
}
