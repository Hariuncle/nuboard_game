# BLOSSOM BREACH

물리 총 콘솔을 BLE HID 마우스로 연결해 조준하고 발사하는 1인칭 웹 슈팅 프로토타입입니다. 전투 중 배경 카메라는 고정되어 있고 화면 안의 에임이 움직입니다. 키보드 플레이 입력은 사용하지 않습니다.

## 실행

```powershell
python -m http.server 4173 --directory game
```

Chrome에서 `http://127.0.0.1:4173/`을 열고 총 콘솔을 운영체제의 Bluetooth 설정에서 `NEON BREACH Gun` 마우스로 페어링합니다.

- 총의 MPU-9250 자이로: BLE HID 상대 마우스 이동 / 에임 조준
- FSR 방아쇠: BLE HID 마우스 왼쪽 버튼 / 발사
- 데스크톱 마우스: Pointer Lock 상대 이동 / 왼쪽 클릭
- 모바일: 화면 드래그 조준 / 탭 발사

게임 흐름은 타이틀 → 스토리 인트로 → 60초 전투 → 결과 → 재시작입니다. 현재 번들된 `game/assets/video/meadow-animatic-preview.mp4`는 세 장의 정지 키프레임에 FFmpeg pan/zoom과 합성 오디오를 적용한 임시 애니매틱이며 H3 생성 결과가 아닙니다.

실제 애니메이션 생성은 `tools/run_h3_meadow.py`를 KT AI Nexus H3 머신에서 실행합니다. 이 실행기는 공식 `video_minimax_h3_i2v` 템플릿을 SHA-256으로 고정하고, 세 키프레임을 각각 `MiniMaxH3ImageToVideo.first_frame`에 넣어 1344×768, 24fps, 5초(내부 124프레임)로 생성합니다. 정지 guide 영상을 `ref_video`/`previous_tail`에 중복하던 잘못된 방식은 `tools/run_h3_meadow_r2v_experimental.py`로 격리했습니다.

```bash
/home/work/media-lab-data/minimax-h3/env/comfy-mcp/bin/python \
  tools/run_h3_meadow.py
```

MCP 실행 순서는 `server_info → upload_file → validate_workflow → run_workflow → job(wait) → fetch_outputs`입니다. 검증된 세 H3 결과를 `game/assets/video/h3-meadow-intro.mp4`로 합성한 뒤 `game/index.html`의 영상 경로를 교체합니다.

발사, 명중, 빗나감, 보스 등장, 승리, 패배 효과음은 아래의 최적화된 오디오 팩을 우선 사용하고, 재생이 불가능한 환경에서는 Web Audio API 합성음으로 폴백합니다. 명중 시 꽃잎 파편, 충격파, 히트마커, 화면 섬광, 짧은 진동이 에임 위치에서 발생합니다. 키보드 조작은 필요하지 않습니다.

`Pomora_Audio_Reference_Pack`은 인트로와 분리해 게임 본편에 적용했습니다. 원본 51.18MB WAV 중 실제 플레이에 필요한 BGM 5개와 SFX 11개만 48kHz MP3(총 약 1.88MB)로 최적화해 `game/assets/audio/`에 포함했습니다. 1막 들판, 2막 블룸 게이트, 3막 보스에 따라 음악이 바뀌며 발사·명중·경직·쓰러짐·퇴장·보스 페이즈 사운드가 재생됩니다. 브라우저 Audio를 쓸 수 없으면 기존 Web Audio 합성음으로 안전하게 폴백합니다.

> 배포 참고: 원본 팩은 프로토타입 레퍼런스로 표시되어 있지만 별도 `LICENSE` 파일은 없습니다. 공개 출시·상업 배포 전 음원 권리와 재배포 범위를 확인해야 합니다. 원본 51MB 팩 자체는 저장소에서 제외하고 게임에 필요한 최적화본만 포함합니다.

## 펌웨어

`firmware/ble_hid_mouse_controller.ino`는 NU-40/nRF52840용 최소 BLE HID 검증 스케치입니다. `all_parts_activated.ino`의 MPU I2C 핀과 FSR 입력을 재사용하며 TFT, ToF, 서보, 오디오, 키보드 HID는 제외했습니다. `toyton_basic_components.ino`는 MPU-6050, 3개 TFT, FSR 기본 부품을 확인하는 별도 스케치입니다. 보드 설치, 라이브러리 버전, 현재 컴파일 상태는 `firmware/README.md`를 참고하세요.

## 테스트

```powershell
node --test game/tests/*.test.mjs
```
