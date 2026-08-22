import test from "node:test";
import assert from "node:assert/strict";

import { AUDIO_ASSETS, MUSIC_ASSETS, createGameAudio } from "../src/audio.mjs";

test("maps the reference pack to gameplay and music events", () => {
  assert.ok(Object.keys(AUDIO_ASSETS).length >= 10);
  assert.deepEqual(Object.keys(MUSIC_ASSETS), ["meadow", "wave", "boss", "victory", "defeat"]);
  for (const source of [...Object.values(AUDIO_ASSETS), ...Object.values(MUSIC_ASSETS)]) {
    assert.match(source, /^\.\/assets\/audio\/.+\.mp3$/);
  }
});

test("plays overlapping samples and changes music without leaking the previous loop", () => {
  const instances = [];
  class FakeAudio {
    constructor(src) {
      this.src = src;
      this.playCalls = 0;
      this.pauseCalls = 0;
      instances.push(this);
    }
    cloneNode() { return new FakeAudio(this.src); }
    play() { this.playCalls += 1; return Promise.resolve(); }
    pause() { this.pauseCalls += 1; }
  }
  const fallback = { unlock: async () => {}, play: () => false };
  const audio = createGameAudio({ AudioClass: FakeAudio, fallback });

  assert.equal(audio.play("shot"), true);
  assert.equal(audio.play("shot"), true);
  assert.equal(audio.setMusic("meadow"), true);
  const meadow = instances.at(-1);
  assert.equal(meadow.loop, true);
  assert.equal(audio.setMusic("boss"), true);
  assert.equal(meadow.pauseCalls, 1);
});

test("falls back safely when browser Audio is unavailable", async () => {
  const calls = [];
  const fallback = { unlock: async () => calls.push("unlock"), play: (name) => calls.push(name) };
  const audio = createGameAudio({ AudioClass: undefined, fallback });
  await audio.unlock();
  audio.play("hitPower");
  assert.deepEqual(calls, ["unlock", "hit"]);
  assert.equal(audio.setMusic("meadow"), false);
});
