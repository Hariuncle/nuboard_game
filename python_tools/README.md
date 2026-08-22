# NU-40 Python 도구

NU-40 총 콘솔에서 전송하는 조준·방아쇠·게임 상태 데이터를 확인하는 보조 도구입니다.
게임은 운영체제의 마우스 입력을 직접 받으므로, 총 콘솔이 Bluetooth HID 마우스로 동작하면 별도 프로그램 없이 `BlossomBreach.exe`를 실행할 수 있습니다. 아래 도구는 BLE Nordic UART Service 또는 USB 시리얼 텔레메트리를 점검할 때 사용합니다.

## 빠른 실행

Python 3.10 이상에서 이 폴더를 터미널로 연 뒤 의존성을 설치합니다.

```powershell
python -m pip install -r requirements.txt
```

데스크톱 통합 모니터:

```powershell
python nu40_desktop_app.py
```

BLE 터미널 모니터:

```powershell
python nu40_ble_monitor.py
```

USB 시리얼 터미널 모니터:

```powershell
python nu40_serial_monitor.py
```

설치 없이 브라우저에서 확인하려면 Chrome 또는 Edge로 `nu40_web_monitor.html`을 열고 BLE/USB 연결 버튼을 누릅니다. 브라우저가 Bluetooth 또는 Serial 권한을 요청하면 NU-40 장치를 선택합니다.

## 연결 규격

- BLE 장치 이름: `savethe earth`
- BLE 서비스: Nordic UART Service (`6e400001-b5a3-f393-e0a9-e50e24dcca9e`)
- BLE 수신 특성: `6e400003-b5a3-f393-e0a9-e50e24dcca9e`
- USB 시리얼: 115200 bps
- 메시지: 줄바꿈으로 구분된 JSON

주요 필드는 `gx`, `gy`(조준), `trg`(방아쇠), `prs`(압력), `hp`, `stg`, `scr`, `amo`, `rld`, `a`(적 목록)입니다.

## 게임과 함께 사용

1. `BlossomBreach.exe`를 실행합니다.
2. 총 콘솔의 커서를 화면 중앙의 조준점으로 움직입니다.
3. 인트로 화면의 `여기를 쏘세요` 표적을 방아쇠로 쏘면 게임이 시작됩니다.
4. 데이터 확인이 필요하면 게임과 동시에 `nu40_desktop_app.py`를 실행합니다.

키보드는 게임 진행에 필요하지 않습니다.
