# BLOSSOM BREACH 제작 과정 발표 자료 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ImageGen, Unity MCP, 3D 에셋·애니메이션, H3 영상 제작 과정을 설명하는 이미지 중심 3장 PowerPoint를 만든다.

**Architecture:** 기존 프로젝트의 ImageGen 콘셉트 시트와 H3 영상 프레임을 재사용하고, Unity 프로젝트의 실제 구성은 소스·에셋 구조와 게임 화면으로 증명한다. `@oai/artifact-tool` 기반 단일 ES 모듈이 세 슬라이드를 만들고 PPTX로 내보내며, 렌더·오버플로 검사로 결과를 검증한다.

**Tech Stack:** JavaScript ES modules, `@oai/artifact-tool`, PowerPoint PPTX, bundled presentation render/test helpers

---

### Task 1: 제작 이미지 선별

**Files:**
- Read: `game_image/*.png`
- Read: `game/assets/video/h3-meadow-intro.mp4`
- Read: `unity/BlossomBreach/Assets/Resources/Meshy/**`
- Create: `.ppt_build/source-notes.txt`

- [ ] **Step 1: ImageGen 콘셉트 시트에서 세계관·캐릭터 이미지를 고른다**

선정 기준은 첫 장의 16:9 큰 이미지에서 캐릭터와 환경을 동시에 설명할 수 있는가이다.

- [ ] **Step 2: H3 인트로 영상에서 대표 프레임을 추출한다**

```powershell
ffmpeg -ss 00:00:01.5 -i game/assets/video/h3-meadow-intro.mp4 -frames:v 1 .ppt_build/h3-frame.png
```

Expected: `.ppt_build/h3-frame.png`가 생성되고 화면이 정상적으로 보인다.

- [ ] **Step 3: 출처 메모를 기록한다**

`source-notes.txt`에 모든 시각 자료가 이 저장소의 사용자 제작 자산임을 기록하고, 외부 출처가 없음을 명시한다.

### Task 2: 3장 PPTX 제작

**Files:**
- Create: `.ppt_build/build_game_making_deck.mjs`
- Create: `BLOSSOM_BREACH_제작과정.pptx`

- [ ] **Step 1: 프레젠테이션 빌더를 작성한다**

빌더는 16:9 프레젠테이션을 만들고, 다음 고정 구성을 사용한다.

```text
Slide 1 — 아이디어를 이미지로
Codex ImageGen으로 세계관·캐릭터·배경 콘셉트를 시각화했다.

Slide 2 — 이미지를 플레이 가능한 게임으로
Unity MCP로 제작 흐름을 연결하고 3D 에셋, 게임 로직, 애니메이션을 구현했다.

Slide 3 — 움직이는 이야기로 완성
H3로 인트로 영상을 만들고 Unity 게임과 결합해 하나의 플레이 경험으로 완성했다.
```

각 장에는 서로 다른 실제 프로젝트 이미지를 사용하고, 하단에 `01 / 02 / 03` 페이지 마커를 배치한다.

- [ ] **Step 2: PPTX를 내보낸다**

```powershell
& $env:RUNTIME_NODE .ppt_build/build_game_making_deck.mjs
```

Expected: `BLOSSOM_BREACH_제작과정.pptx`가 생성된다.

### Task 3: 렌더링 및 품질 검증

**Files:**
- Read: `BLOSSOM_BREACH_제작과정.pptx`
- Create: `.ppt_build/rendered/slide-1.png`
- Create: `.ppt_build/rendered/slide-2.png`
- Create: `.ppt_build/rendered/slide-3.png`

- [ ] **Step 1: 전체 슬라이드를 PNG로 렌더링한다**

```powershell
python $SKILL_DIR/container_tools/render_slides.py BLOSSOM_BREACH_제작과정.pptx
```

Expected: 세 장의 PNG가 생성된다.

- [ ] **Step 2: 각 슬라이드를 전체 크기로 확인한다**

제목 줄바꿈, 이미지 왜곡, 텍스트 잘림, 대비, 일관된 여백을 확인하고 발견된 문제를 빌더에서 수정한다.

- [ ] **Step 3: 오버플로 검사를 실행한다**

```powershell
python $SKILL_DIR/container_tools/slides_test.py BLOSSOM_BREACH_제작과정.pptx
```

Expected: 슬라이드 캔버스를 벗어난 요소가 없고, 결과가 PASS다.

- [ ] **Step 4: 최종 산출물을 전달한다**

최종 PPTX 한 개만 사용자에게 제공하고, 사용한 시각 자료가 프로젝트 내부 제작 자산임을 짧게 설명한다.
