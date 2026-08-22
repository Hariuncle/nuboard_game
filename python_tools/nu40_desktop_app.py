"""
===================================================================================================
[NU-40 DK] SAVE THE EARTH - PYTHON DESKTOP GUI DIGITAL TWIN APP (파이썬 독립 데스크톱 앱)
===================================================================================================
- 기능:
  1. 블루투스 LE ("savethe earth") 무선 연결 & USB 시리얼 유선 연결 듀얼 지원
  2. 실시간 60FPS 사이버펑크 2D 전장 그래픽 캔버스 미러링 (에임, 몬스터 3마리, 격발 이펙트)
  3. 방아쇠 압력 HUD 게이지, 플레이어 HP, 탄약, 점수, 스테이지 실시간 대시보드
===================================================================================================
실행 방법:
  pip install bleak pyserial
  python nu40_desktop_app.py
===================================================================================================
"""

import sys
import time
import json
import asyncio
import threading
import tkinter as tk
from tkinter import ttk, messagebox

# Bleak & Serial 모듈 임포트
try:
    from bleak import BleakScanner, BleakClient
    BLEAK_AVAILABLE = True
except ImportError:
    BLEAK_AVAILABLE = False

try:
    import serial
    import serial.tools.list_ports
    SERIAL_AVAILABLE = True
except ImportError:
    SERIAL_AVAILABLE = False

DEVICE_NAME = "savethe earth"
NUS_TX_CHAR_UUID = "6e400003-b5a3-f393-e0a9-e50e24dcca9e"

class NU40App:
    def __init__(self, root):
        self.root = root
        self.root.title("🛸 NU-40 Digital Twin Controller - Save The Earth")
        self.root.geometry("860x560")
        self.root.configure(bg="#12131a")
        self.root.resizable(False, False)

        # 텔레메트리 데이터 저장소
        self.latest_data = {
            "gx": 64, "gy": 80, "trg": 0, "prs": 0, "hp": 100,
            "stg": 1, "scr": 0, "amo": 10, "rld": 0,
            "a": [
                {"id": 0, "act": 0, "t": 0, "x": 64, "y": 40, "vx": 0, "vy": 0, "h": 1, "m": 1, "d": 0},
                {"id": 1, "act": 0, "t": 1, "x": 40, "y": 60, "vx": 0, "vy": 0, "h": 2, "m": 2, "d": 0},
                {"id": 2, "act": 0, "t": 2, "x": 88, "y": 60, "vx": 0, "vy": 0, "h": 2, "m": 2, "d": 0}
            ]
        }
        self.data_lock = threading.Lock()
        self.is_connected = False
        self.packet_count = 0
        self.last_fps_time = time.time()
        self.current_fps = 0

        # 백그라운드 워커 스레드 관리
        self.ble_thread = None
        self.ble_loop = None
        self.serial_thread = None
        self.stop_event = threading.Event()
        self.serial_port_obj = None

        self.setup_ui()
        self.root.after(20, self.update_gui_loop)
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)

    def setup_ui(self):
        # 스타일 설정
        style = ttk.Style()
        style.theme_use('clam')
        style.configure("TProgressbar", thickness=14, troughcolor="#222430", background="#00ffcc")

        # 1. 상단 타이틀 & 연결 툴바
        top_frame = tk.Frame(self.root, bg="#1a1c26", height=60, bd=0, highlightthickness=1, highlightbackground="#2e3247")
        top_frame.pack(fill="x", padx=15, pady=(15, 10))

        title_lbl = tk.Label(top_frame, text="🛸 SAVE THE EARTH", font=("Segoe UI", 16, "bold"), fg="#00ffcc", bg="#1a1c26")
        title_lbl.pack(side="left", padx=15, pady=12)

        sub_lbl = tk.Label(top_frame, text="|  NU-40 DK 3-TFT DIGITAL TWIN", font=("Segoe UI", 10), fg="#7b819d", bg="#1a1c26")
        sub_lbl.pack(side="left", pady=12)

        # 연결 모드 버튼들
        self.btn_ble = tk.Button(top_frame, text="📡 BLE 무선 연결", font=("Segoe UI", 10, "bold"),
                                 bg="#0066ff", fg="white", activebackground="#0052cc", activeforeground="white",
                                 relief="flat", padx=14, pady=5, cursor="hand2", command=self.toggle_ble_connect)
        self.btn_ble.pack(side="right", padx=12, pady=12)

        self.btn_serial = tk.Button(top_frame, text="🔌 USB 시리얼 연결", font=("Segoe UI", 10, "bold"),
                                    bg="#2e3247", fg="#c4c9e2", activebackground="#3d425f", activeforeground="white",
                                    relief="flat", padx=14, pady=5, cursor="hand2", command=self.toggle_serial_connect)
        self.btn_serial.pack(side="right", padx=5, pady=12)

        self.lbl_status = tk.Label(top_frame, text="● 연결 대기 중", font=("Segoe UI", 9, "bold"), fg="#ff4466", bg="#1a1c26")
        self.lbl_status.pack(side="right", padx=15, pady=12)

        # 2. 메인 컨텐츠 (3단 분할 레이아웃)
        content_frame = tk.Frame(self.root, bg="#12131a")
        content_frame.pack(fill="both", expand=True, padx=15, pady=5)

        # [좌측 패널] 방아쇠 압력 & 플레이어 스탯
        left_panel = tk.Frame(content_frame, bg="#1a1c26", width=220, bd=0, highlightthickness=1, highlightbackground="#2e3247")
        left_panel.pack(side="left", fill="y", padx=(0, 10), pady=0)
        left_panel.pack_propagate(False)

        tk.Label(left_panel, text="🎛️ TRIGGER & HP", font=("Segoe UI", 11, "bold"), fg="#ffcc00", bg="#1a1c26").pack(anchor="w", padx=15, pady=(15, 10))

        # 방아쇠 압력 게이지
        tk.Label(left_panel, text="방아쇠 압력 (FSR-402)", font=("Segoe UI", 9), fg="#9aa0bd", bg="#1a1c26").pack(anchor="w", padx=15)
        self.lbl_pressure_val = tk.Label(left_panel, text="0 %", font=("Segoe UI", 18, "bold"), fg="#00ffcc", bg="#1a1c26")
        self.lbl_pressure_val.pack(anchor="w", padx=15)

        self.pb_pressure = ttk.Progressbar(left_panel, style="TProgressbar", length=190, mode="determinate")
        self.pb_pressure.pack(padx=15, pady=(2, 10))

        self.lbl_trigger_state = tk.Label(left_panel, text="[IDLE]", font=("Segoe UI", 10, "bold"), fg="#7b819d", bg="#1a1c26")
        self.lbl_trigger_state.pack(anchor="w", padx=15, pady=(0, 15))

        # 플레이어 체력
        tk.Label(left_panel, text="플레이어 체력 (HP)", font=("Segoe UI", 9), fg="#9aa0bd", bg="#1a1c26").pack(anchor="w", padx=15)
        self.lbl_hp_val = tk.Label(left_panel, text="100 %", font=("Segoe UI", 16, "bold"), fg="#00ff88", bg="#1a1c26")
        self.lbl_hp_val.pack(anchor="w", padx=15)

        self.pb_hp = ttk.Progressbar(left_panel, length=190, mode="determinate")
        self.pb_hp.pack(padx=15, pady=(2, 15))

        # 탄약 게이지
        tk.Label(left_panel, text="잔여 탄약 (AMMO)", font=("Segoe UI", 9), fg="#9aa0bd", bg="#1a1c26").pack(anchor="w", padx=15)
        self.lbl_ammo_val = tk.Label(left_panel, text="10 / 10", font=("Segoe UI", 14, "bold"), fg="#ff77aa", bg="#1a1c26")
        self.lbl_ammo_val.pack(anchor="w", padx=15, pady=(0, 5))

        # [중앙 패널] 2D 전장 그래픽 캔버스 미러링 (256 x 320 px)
        center_panel = tk.Frame(content_frame, bg="#1a1c26", bd=0, highlightthickness=1, highlightbackground="#2e3247")
        center_panel.pack(side="left", fill="both", expand=True, padx=0, pady=0)

        tk.Label(center_panel, text="🎮 BATTLEFIELD MIRROR (NU-40)", font=("Segoe UI", 11, "bold"), fg="#00ffcc", bg="#1a1c26").pack(pady=(10, 5))

        self.canvas_w = 256
        self.canvas_h = 320
        self.canvas = tk.Canvas(center_panel, width=self.canvas_w, height=self.canvas_h, bg="#05060a",
                               highlightthickness=2, highlightbackground="#00ff88")
        self.canvas.pack(pady=5)

        # [우측 패널] 스테이지 & 외계인 3마리 상태
        right_panel = tk.Frame(content_frame, bg="#1a1c26", width=240, bd=0, highlightthickness=1, highlightbackground="#2e3247")
        right_panel.pack(side="right", fill="y", padx=(10, 0), pady=0)
        right_panel.pack_propagate(False)

        tk.Label(right_panel, text="🏆 MISSION STATUS", font=("Segoe UI", 11, "bold"), fg="#ffcc00", bg="#1a1c26").pack(anchor="w", padx=15, pady=(15, 10))

        self.lbl_stage = tk.Label(right_panel, text="STAGE : 1", font=("Segoe UI", 11, "bold"), fg="#ffffff", bg="#1a1c26")
        self.lbl_stage.pack(anchor="w", padx=15, pady=2)

        self.lbl_score = tk.Label(right_panel, text="SCORE : 0", font=("Segoe UI", 13, "bold"), fg="#ffcc00", bg="#1a1c26")
        self.lbl_score.pack(anchor="w", padx=15, pady=2)

        tk.Label(right_panel, text="-" * 30, fg="#2e3247", bg="#1a1c26").pack(padx=15, pady=5)

        tk.Label(right_panel, text="👾 ALIENS (3마리 상태)", font=("Segoe UI", 10, "bold"), fg="#00ffcc", bg="#1a1c26").pack(anchor="w", padx=15, pady=2)

        self.lbl_alien0 = tk.Label(right_panel, text="#0 Scout    : 대기", font=("Segoe UI", 9), fg="#00ffff", bg="#1a1c26")
        self.lbl_alien0.pack(anchor="w", padx=15, pady=3)

        self.lbl_alien1 = tk.Label(right_panel, text="#1 Warrior  : 대기", font=("Segoe UI", 9), fg="#ff00ff", bg="#1a1c26")
        self.lbl_alien1.pack(anchor="w", padx=15, pady=3)

        self.lbl_alien2 = tk.Label(right_panel, text="#2 Juggernaut: 대기", font=("Segoe UI", 9), fg="#ffcc00", bg="#1a1c26")
        self.lbl_alien2.pack(anchor="w", padx=15, pady=3)

        # 하단 상태바
        self.lbl_fps = tk.Label(right_panel, text="수신 속도: 0 FPS", font=("Segoe UI", 8), fg="#7b819d", bg="#1a1c26")
        self.lbl_fps.pack(side="bottom", anchor="w", padx=15, pady=10)

    # =========================================================================
    # 📡 1. 블루투스 LE 무선 연결 로직 (Bleak 비동기)
    # =========================================================================
    def toggle_ble_connect(self):
        if self.is_connected:
            self.disconnect_all()
            return

        if not BLEAK_AVAILABLE:
            messagebox.showerror("라이브러리 필요", "Bleak 라이브러리가 설치되지 않았습니다.\n터미널에서 'pip install bleak' 를 실행하세요.")
            return

        self.lbl_status.config(text="🔍 BLE 검색 중...", fg="#ffaa00")
        self.btn_ble.config(state="disabled")
        self.btn_serial.config(state="disabled")

        self.stop_event.clear()
        self.ble_thread = threading.Thread(target=self.run_ble_async, daemon=True)
        self.ble_thread.start()

    def run_ble_async(self):
        self.ble_loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.ble_loop)
        try:
            self.ble_loop.run_until_complete(self.ble_worker())
        except Exception as e:
            self.update_status_safe(f"❌ 연결 에러: {e}", "#ff4466", False)

    async def ble_worker(self):
        self.update_status_safe("🔍 'savethe earth' 탐색 중...", "#ffaa00", False)
        device = await BleakScanner.find_device_by_name(DEVICE_NAME, timeout=8.0)
        if not device:
            self.update_status_safe("❌ 'savethe earth' 기기 미발견", "#ff4466", False)
            self.enable_buttons_safe()
            return

        self.update_status_safe(f"🔗 연결 중... ({device.address})", "#00ffcc", False)
        async with BleakClient(device) as client:
            self.is_connected = True
            self.update_status_safe("● BLE 무선 연결됨", "#00ff88", True)

            def notification_handler(sender, data: bytearray):
                self.process_raw_text(data.decode('utf-8', errors='ignore'))

            await client.start_notify(NUS_TX_CHAR_UUID, notification_handler)

            while not self.stop_event.is_set() and client.is_connected:
                await asyncio.sleep(0.1)

            self.is_connected = False
            self.update_status_safe("● 연결 해제됨", "#ff4466", False)
            self.enable_buttons_safe()

    # =========================================================================
    # 🔌 2. USB 시리얼 연결 로직
    # =========================================================================
    def toggle_serial_connect(self):
        if self.is_connected:
            self.disconnect_all()
            return

        if not SERIAL_AVAILABLE:
            messagebox.showerror("라이브러리 필요", "pyserial 라이브러리가 필요합니다.\n'pip install pyserial' 을 실행하세요.")
            return

        ports = list(serial.tools.list_ports.comports())
        target_port = None
        for p in ports:
            if "nRF" in p.description or "Adafruit" in p.description or "USB" in p.description or "J-Link" in p.description:
                target_port = p.device
                break
        if not target_port and ports:
            target_port = ports[0].device

        if not target_port:
            messagebox.showwarning("포트 없음", "연결된 USB 시리얼 포트를 찾을 수 없습니다.")
            return

        try:
            self.serial_port_obj = serial.Serial(target_port, 115200, timeout=0.1)
            self.is_connected = True
            self.update_status_safe(f"● USB 연결됨 ({target_port})", "#00ff88", True)
            self.stop_event.clear()
            self.serial_thread = threading.Thread(target=self.serial_worker, daemon=True)
            self.serial_thread.start()
        except Exception as e:
            messagebox.showerror("연결 실패", f"시리얼 포트 열기 실패: {e}")

    def serial_worker(self):
        while not self.stop_event.is_set() and self.serial_port_obj and self.serial_port_obj.is_open:
            try:
                chunk = self.serial_port_obj.read(256).decode('utf-8', errors='ignore')
                if chunk:
                    self.process_raw_text(chunk)
            except Exception:
                break
        self.is_connected = False
        self.update_status_safe("● 연결 해제됨", "#ff4466", False)
        self.enable_buttons_safe()

    # =========================================================================
    # 📦 3. 데이터 파싱 & 60FPS 그래픽 렌더링 루프
    # =========================================================================
    def process_raw_text(self, text):
        if not hasattr(self, '_stream_buf'):
            self._stream_buf = ""
        self._stream_buf += text
        while "\n" in self._stream_buf:
            line, self._stream_buf = self._stream_buf.split("\n", 1)
            line = line.strip()
            if line.startswith("{") and line.endswith("}"):
                try:
                    d = json.loads(line)
                    with self.data_lock:
                        self.latest_data = d
                        self.packet_count += 1
                except json.JSONDecodeError:
                    pass

    def update_gui_loop(self):
        # FPS 계산
        now = time.time()
        if now - self.last_fps_time >= 1.0:
            self.current_fps = self.packet_count
            self.packet_count = 0
            self.last_fps_time = now
            self.lbl_fps.config(text=f"수신 속도: {self.current_fps} FPS")

        with self.data_lock:
            data = dict(self.latest_data)

        # 1. 방아쇠 압력 & 플레이어 HP 갱신
        prs = data.get("prs", 0)
        self.lbl_pressure_val.config(text=f"{prs} %")
        self.pb_pressure["value"] = prs

        trg = data.get("trg", 0)
        if trg == 1:
            self.lbl_trigger_state.config(text="🔥 [BANG! SHOOT!]", fg="#ff0055")
        elif prs >= 25:
            self.lbl_trigger_state.config(text="⚠️ [HOLDING]", fg="#ffcc00")
        else:
            self.lbl_trigger_state.config(text="[IDLE]", fg="#7b819d")

        hp = data.get("hp", 100)
        self.lbl_hp_val.config(text=f"{hp} %", fg="#00ff88" if hp > 50 else ("#ffcc00" if hp > 25 else "#ff3366"))
        self.pb_hp["value"] = hp

        amo = data.get("amo", 10)
        rld = data.get("rld", 0)
        self.lbl_ammo_val.config(text="RELOAD..." if rld == 1 else f"{amo} / 10", fg="#ff3366" if rld == 1 else "#ff77aa")

        # 2. 스테이지 & 스코어
        self.lbl_stage.config(text=f"STAGE : {data.get('stg', 1)}")
        self.lbl_score.config(text=f"SCORE : {data.get('scr', 0)}")

        # 3. 외계인 3마리 텍스트 라벨
        aliens = data.get("a", [])
        labels = [self.lbl_alien0, self.lbl_alien1, self.lbl_alien2]
        type_names = ["Scout", "Warrior", "Juggernaut"]
        for i in range(min(3, len(aliens))):
            aln = aliens[i]
            act = aln.get("act", 0)
            chp = aln.get("h", 0)
            mhp = aln.get("m", 0)
            die = aln.get("d", 0)
            if act == 1:
                labels[i].config(text=f"#{i} {type_names[i]:10s}: 생존 ({chp}/{mhp} HP)")
            else:
                labels[i].config(text=f"#{i} {type_names[i]:10s}: " + (f"폭발 중 ({die})" if die > 0 else "사망 (소멸)"))

        # 4. 캔버스 2D 전장 그래픽 렌더링 (128x160 -> 256x320 2배 스케일링)
        self.canvas.delete("all")

        # 위험 저지선 라인
        self.canvas.create_line(8, 256, self.canvas_w - 8, 256, fill="#780000", width=1, dash=(4, 4))

        # 외계인 3마리 그리기
        colors = ["#00ffff", "#ff00ff", "#ffcc00"]
        for i, aln in enumerate(aliens):
            act = aln.get("act", 0)
            die = aln.get("d", 0)
            ax = (aln.get("x", 64) / 128.0) * self.canvas_w
            ay = (aln.get("y", 80) / 160.0) * self.canvas_h
            s = 12

            if act == 1:
                col = "#ffffff" if aln.get("h", 1) == 1 and aln.get("m", 2) > 1 else colors[i % 3]
                if i == 0:  # Scout Box
                    self.canvas.create_rectangle(ax - s, ay - s, ax + s, ay + s, fill=col, outline="#ffffff")
                elif i == 1: # Warrior Diamond
                    self.canvas.create_polygon(ax, ay - s - 2, ax + s + 2, ay, ax, ay + s + 2, ax - s - 2, ay, fill=col, outline="#ffffff")
                else: # Juggernaut Shield
                    self.canvas.create_rectangle(ax - s - 2, ay - s, ax + s + 2, ay + s, fill=col, outline="#ffcc00", width=2)
                # 미니 체력바
                h_ratio = aln.get("h", 1) / max(1, aln.get("m", 1))
                self.canvas.create_rectangle(ax - 14, ay - s - 6, ax + 14, ay - s - 3, fill="#222222", outline="")
                self.canvas.create_rectangle(ax - 14, ay - s - 6, ax - 14 + (28 * h_ratio), ay - s - 3, fill="#00ff88", outline="")

            elif die > 0:
                # 💥 사망 폭발 연출
                radius = (7 - die) * 7
                self.canvas.create_oval(ax - radius, ay - radius, ax + radius, ay + radius, outline="#ffff00", width=2)
                self.canvas.create_oval(ax - radius/2, ay - radius/2, ax + radius/2, ay + radius/2, outline="#ff3366", width=2)

        # 대형 샷건 에임 레티클 (Shotgun Circle Reticle)
        gx = (data.get("gx", 64) / 128.0) * self.canvas_w
        gy = (data.get("gy", 80) / 160.0) * self.canvas_h
        r = 28 # 반경 28px

        ret_col = "#ff0055" if trg == 1 else ("#ffcc00" if prs >= 25 else "#00ffff")
        self.canvas.create_oval(gx - r, gy - r, gx + r, gy + r, outline=ret_col, width=2)
        self.canvas.create_line(gx - r - 6, gy, gx - r, gy, fill=ret_col, width=2)
        self.canvas.create_line(gx + r, gy, gx + r + 6, gy, fill=ret_col, width=2)
        self.canvas.create_line(gx, gy - r - 6, gx, gy - r, fill=ret_col, width=2)
        self.canvas.create_line(gx, gy + r, gx, gy + r + 6, fill=ret_col, width=2)
        self.canvas.create_oval(gx - 2, gy - 2, gx + 2, gy + 2, fill="#ffffff", outline="")

        if trg == 1:
            self.canvas.create_rectangle(2, 2, self.canvas_w - 2, self.canvas_h - 2, outline="#ff0055", width=3)

        self.root.after(20, self.update_gui_loop)

    def update_status_safe(self, text, color, is_connected):
        self.root.after(0, lambda: self._apply_status(text, color, is_connected))

    def _apply_status(self, text, color, is_connected):
        self.lbl_status.config(text=text, fg=color)
        self.is_connected = is_connected
        if is_connected:
            self.btn_ble.config(text="연결 해제", bg="#ff3366", state="normal")
            self.btn_serial.config(state="disabled")
        else:
            self.btn_ble.config(text="📡 BLE 무선 연결", bg="#0066ff", state="normal")
            self.btn_serial.config(text="🔌 USB 시리얼 연결", state="normal")

    def enable_buttons_safe(self):
        self.root.after(0, lambda: [self.btn_ble.config(state="normal"), self.btn_serial.config(state="normal")])

    def disconnect_all(self):
        self.stop_event.set()
        if self.serial_port_obj and self.serial_port_obj.is_open:
            self.serial_port_obj.close()
            self.serial_port_obj = None
        self.is_connected = False
        self.update_status_safe("● 연결 해제됨", "#ff4466", False)

    def on_closing(self):
        self.disconnect_all()
        self.root.destroy()
        sys.exit(0)

if __name__ == "__main__":
    root = tk.Tk()
    app = NU40App(root)
    root.mainloop()
