"""
===================================================================================
[NU-40 DK] USB 시리얼 텔레메트리 모니터 (Python pyserial 기반)
 - USB 케이블 연결 시 자동 포트 탐색 및 실시간 데이터 출력
===================================================================================
설치 방법:
  pip install pyserial
실행 방법:
  python nu40_serial_monitor.py
===================================================================================
"""

import sys
import time
import json
import serial
import serial.tools.list_ports

def find_nu40_port():
    ports = list(serial.tools.list_ports.comports())
    for p in ports:
        if "nRF" in p.description or "Adafruit" in p.description or "J-Link" in p.description or "USB" in p.description:
            return p.device
    if ports:
        return ports[0].device
    return None

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

        fire_badge = "🔥 [BANG! SHOOT!]" if trg == 1 else "  [READY]        "
        reload_str = "RELOADING..." if rld == 1 else f"{amo}/10"

        sys.stdout.write("\033[H\033[J")
        print("=" * 65)
        print(f" 🔌  [NU-40 USB SERIAL DIGITAL TWIN MONITOR]")
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
    except Exception:
        pass

def main():
    port = find_nu40_port()
    if not port:
        print("❌ 연결된 시리얼 포트를 찾을 수 없습니다. USB 케이블을 확인하세요.")
        return

    print(f"🔗 포트 {port} 에 115200 bps 로 연결하는 중...")
    try:
        ser = serial.Serial(port, 115200, timeout=1)
        time.sleep(1)
        print("✅ 연결 완료! 실시간 수신 대기 중...\n")

        while True:
            line = ser.readline().decode('utf-8', errors='ignore').strip()
            if line.startswith("{") and line.endswith("}"):
                parse_and_display(line)

    except serial.SerialException as e:
        print(f"❌ 시리얼 포트 에러: {e}")
    except KeyboardInterrupt:
        print("\n👋 모니터링을 종료합니다.")
    finally:
        if 'ser' in locals() and ser.is_open:
            ser.close()

if __name__ == "__main__":
    main()
