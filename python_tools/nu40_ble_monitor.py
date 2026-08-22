"""
===================================================================================
[NU-40 DK] BLE 무선 텔레메트리 모니터 (Python Bleak 기반)
 - 장치 이름: "savethe earth" 자동 검색 및 무선 연결
 - 실시간 에임 좌표, 방아쇠 격발/압력, 플레이어 HP, 3마리 몬스터 좌표/체력 터미널 출력
===================================================================================
설치 방법:
  pip install bleak
실행 방법:
  python nu40_ble_monitor.py
===================================================================================
"""

import asyncio
import json
import sys
from bleak import BleakScanner, BleakClient

DEVICE_NAME = "savethe earth"

# Nordic UART Service (NUS) 표준 UUID
NUS_SERVICE_UUID = "6e400001-b5a3-f393-e0a9-e50e24dcca9e"
NUS_TX_CHAR_UUID = "6e400003-b5a3-f393-e0a9-e50e24dcca9e"  # NU-40 -> PC 수신용

buffer = ""

def parse_and_display(json_str):
    try:
        data = json.loads(json_str.strip())
        gx = data.get("gx", 0)
        gy = data.get("gy", 0)
        trg = data.get("trg", 0)
        prs = data.get("prs", 0)
        hp = data.get("hp", 0)
        stg = data.get("stg", 0)
        scr = data.get("scr", 0)
        amo = data.get("amo", 0)
        rld = data.get("rld", 0)
        aliens = data.get("a", [])

        # 터미널 실시간 대시보드 출력
        fire_badge = "🔥 [BANG! SHOOT!]" if trg == 1 else "  [READY]        "
        reload_str = "RELOADING..." if rld == 1 else f"{amo}/10"

        sys.stdout.write("\033[H\033[J") # 화면 클리어 (ANSI)
        print("=" * 65)
        print(f" 🛰️  [NU-40 BLE DIGITAL TWIN MONITOR]  Device: '{DEVICE_NAME}'")
        print("=" * 65)
        print(f" 🎯 에임 위치   : X = {gx:3d} / 128 , Y = {gy:3d} / 160")
        print(f" 🔫 방아쇠 상태 : {fire_badge} | 압력: {prs:3d}%")
        print(f" ❤️ 플레이어 HP : {hp:3d}%  | 🏆 STAGE: {stg} | 🌟 SCORE: {scr}")
        print(f" 📦 잔여 탄약   : {reload_str}")
        print("-" * 65)
        print(" 👾 [외계인 3마리 실시간 데이터]")
        print("  ID | 상태 | 타입       | 위치 (X, Y)     | 속도 (Vx, Vy)  | 체력(HP)")
        print(" ----+------+------------+-----------------+----------------+---------")

        type_names = ["A (정찰형)", "B (전투형)", "C (요새형)"]
        for aln in aliens:
            aid = aln.get("id", 0)
            act = aln.get("act", 0)
            atype = aln.get("t", 0)
            ax = aln.get("x", 0.0)
            ay = aln.get("y", 0.0)
            vx = aln.get("vx", 0.0)
            vy = aln.get("vy", 0.0)
            chp = aln.get("h", 0)
            mhp = aln.get("m", 0)
            die = aln.get("d", 0)

            t_name = type_names[atype] if atype < len(type_names) else f"Type {atype}"
            if act == 1:
                status_str = "생존"
                hp_bar = f"{chp}/{mhp}"
            else:
                status_str = f"폭발({die})" if die > 0 else "사망"
                hp_bar = "0/0"

            print(f"  #{aid} | {status_str:4s} | {t_name:10s} | ({ax:5.1f}, {ay:5.1f}) | ({vx:+5.2f}, {vy:+5.2f}) | {hp_bar}")

        print("=" * 65)
        print(" ※ 종료하려면 Ctrl + C 를 누르세요.")
        sys.stdout.flush()

    except json.JSONDecodeError:
        pass
    except Exception as e:
        pass

def handle_rx(sender, data: bytearray):
    global buffer
    try:
        text = data.decode("utf-8", errors="ignore")
        buffer += text
        while "\n" in buffer:
            line, buffer = buffer.split("\n", 1)
            if line.strip().startswith("{"):
                parse_and_display(line)
    except Exception as e:
        pass

async def main():
    print(f"🔍 [BLE] '{DEVICE_NAME}' 블루투스 기기를 검색하는 중입니다...")
    device = await BleakScanner.find_device_by_name(DEVICE_NAME, timeout=10.0)

    if not device:
        print(f"❌ '{DEVICE_NAME}' 기기를 찾을 수 없습니다.")
        print("   1. NU-40 보드의 전원이 켜져 있고 코드가 업로드되었는지 확인하세요.")
        print("   2. 노트북의 블루투스가 켜져 있는지 확인하세요.")
        return

    print(f"✅ 기기 발견! MAC 주소: {device.address}")
    print(f"🔗 '{DEVICE_NAME}'에 무선 연결 중...")

    async with BleakClient(device) as client:
        print(f"🎉 블루투스 무선 연결 성공! (MTU: {client.mtu_size})")
        print("📡 실시간 텔레메트리 스트리밍을 시작합니다...\n")

        await client.start_notify(NUS_TX_CHAR_UUID, handle_rx)

        # 연결 유지 루프
        while client.is_connected:
            await asyncio.sleep(1.0)

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n👋 모니터링 프로그램을 종료합니다.")
