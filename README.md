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

게임 흐름은 타이틀 → 스토리 인트로 → 60초 전투 → 결과 → 재시작입니다. 현재 번들 영상은 `game/assets/video/h3-meadow-intro.mp4`이며, `game_image` 캐릭터를 재구성한 세 장면을 1344×768, 24fps, 9초 H.264/AAC 영상으로 구성합니다. 실제 캐릭터 동작을 다시 생성하는 KT AI Nexus H3 ComfyUI 공식 MCP/API 실행기는 `tools/run_h3_meadow.py`에 포함되어 있고, 생성 결과는 같은 영상 경로에 교체할 수 있습니다.

발사, 명중, 빗나감, 보스 등장, 승리, 패배 효과음은 브라우저의 Web Audio API로 실시간 합성합니다. 명중 시 꽃잎 파편, 충격파, 히트마커, 화면 섬광, 짧은 진동이 에임 위치에서 발생합니다. 별도 음원 파일이나 키보드 조작은 필요하지 않습니다.

## 펌웨어

`firmware/ble_hid_mouse_controller.ino`는 NU-40/nRF52840용 최소 BLE HID 검증 스케치입니다. `all_parts_activated.ino`의 MPU I2C 핀과 FSR 입력을 재사용하며 TFT, ToF, 서보, 오디오, 키보드 HID는 제외했습니다. `toyton_basic_components.ino`는 MPU-6050, 3개 TFT, FSR 기본 부품을 확인하는 별도 스케치입니다. 보드 설치, 라이브러리 버전, 현재 컴파일 상태는 `firmware/README.md`를 참고하세요.

## 테스트

```powershell
node --test game/tests/*.test.mjs
```
