/*
 * =================================================================================================
 * [PROJECT] NU-40 DK 3-TFT Gyro Aiming Arrow & FSR-402 3-Stage Trigger HUD
 *  - MPU6050_light 라이브러리 사용 (6축 전용)
 *  - 위/아래(Pitch) 조준 방향 반전 보정 적용
 *  - FSR-402 3단계 감마/EMA 필터 판정 (IDLE: 0~24% / HOLD: 25~74% / ACTION: 75~100%)
 * =================================================================================================
 */

#include <Adafruit_TinyUSB.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ST7735.h>
#include <SPI.h>
#include <Wire.h>
#include <MPU6050_light.h>
#include <nrf.h>
#include <math.h>

// =================================================================
// 🎨 1. RGB565 컬러 매크로 정의
// =================================================================
#define COLOR_BLACK     0x0000
#define COLOR_WHITE     0xFFFF
#define COLOR_RED       0xF800
#define COLOR_GREEN     0x07E0
#define COLOR_BLUE      0x001F
#define COLOR_YELLOW    0xFFE0
#define COLOR_CYAN      0x07FF
#define COLOR_DARKGREY  0x3186

// =================================================================
// 📌 2. 하드웨어 핀 배선 매핑 (NU-40 DK)
// =================================================================
// SPI 디스플레이 공통 버스
#define TFT_SCLK        19  // P0.19 (SCL)
#define TFT_MOSI        20  // P0.20 (SDA)
#define TFT_DC          23  // P0.23 (Data / Command)
#define TFT_MISO        22  // P0.22 (더미)

// 3개 디스플레이 개별 CS & RST
#define TFT_CS1         38  // P1.06 (화면 1: 좌측 텔레메트리)
#define TFT_RST1        15  // P0.15

#define TFT_CS2         39  // P1.07 (화면 2: 가운데 조준 화살표)
#define TFT_RST2        16  // P0.16

#define TFT_CS3         25  // P0.25 (화면 3: 우측 방아쇠 압력 HUD)
#define TFT_RST3        17  // P0.17

// MPU-6050/9250 I2C 핀
#define I2C_SDA_PIN     30  // P0.30
#define I2C_SCL_PIN     31  // P0.31

// =================================================================
// 🛠️ 3. FSR-402 압력 센서 파라미터 & 등급 정의
// =================================================================
#define RAW_MIN           80     // 미입력 노이즈 컷 (데드존)
#define RAW_MAX           3750   // 3.3V 최대 가압 시 도달 ADC 수치
#define GAMMA_CURVE       2.8f   // 곡선 완만도
#define EMA_ALPHA         0.25f  // 노이즈 평활화 계수

#define THRESHOLD_HOLD    25     // Level 1 (살짝 쥠) 임계값 (%)
#define THRESHOLD_ACTION  75     // Level 2 (강하게 쥠) 임계값 (%)

enum PressureLevel {
  LEVEL_IDLE = 0,   // 대기 상태 (0 ~ 24%)
  LEVEL_HOLD,       // 살짝 쥔 상태 (25 ~ 74%)
  LEVEL_ACTION      // 세게 쥔 상태 (75 ~ 100%)
};

// =================================================================
// 🛠️ 4. 객체 생성 및 전역 변수
// =================================================================
Adafruit_ST7735 tft1 = Adafruit_ST7735(&SPI, TFT_CS1, TFT_DC, TFT_RST1);
Adafruit_ST7735 tft2 = Adafruit_ST7735(&SPI, TFT_CS2, TFT_DC, TFT_RST2);
Adafruit_ST7735 tft3 = Adafruit_ST7735(&SPI, TFT_CS3, TFT_DC, TFT_RST3);

MPU6050 mpu(Wire);

bool mpu_ready = false;
float aimPitch = 0.0f;
float aimRoll  = 0.0f;
float aimYaw   = 0.0f;
unsigned long prevTime = 0;

// 화면 2 잔상 제거용 화살표 이전 프레임 좌표
int prev_arrow_tip_x = 64, prev_arrow_tip_y = 80;
int prev_arrow_b1_x  = 64, prev_arrow_b1_y  = 80;
int prev_arrow_b2_x  = 64, prev_arrow_b2_y  = 80;

PressureLevel currentLevel = LEVEL_IDLE;
float filteredPercent = 0.0f;

// =================================================================
// 🔌 5. 저수준 하드웨어 제어 (I2C 버스 복구 & SAADC)
// =================================================================
void recoverI2CBus() {
  pinMode(I2C_SCL_PIN, OUTPUT);
  pinMode(I2C_SDA_PIN, INPUT_PULLUP);
  for (int i = 0; i < 9; i++) {
    digitalWrite(I2C_SCL_PIN, HIGH); delayMicroseconds(10);
    digitalWrite(I2C_SCL_PIN, LOW);  delayMicroseconds(10);
  }
  digitalWrite(I2C_SCL_PIN, HIGH);
  delay(10);
}

// nRF52840 SAADC 레지스터 직접 제어 (P0.02 = AIN0)
int readPressureDirect(uint8_t ainChannel = 1) {
  NRF_SAADC->ENABLE = (SAADC_ENABLE_ENABLE_Disabled << SAADC_ENABLE_ENABLE_Pos);

  NRF_SAADC->CH[0].PSELP = ainChannel;
  NRF_SAADC->CH[0].PSELN = SAADC_CH_PSELN_PSELN_NC;
  NRF_SAADC->CH[0].CONFIG = (SAADC_CH_CONFIG_GAIN_Gain1_6 << SAADC_CH_CONFIG_GAIN_Pos) |
                            (SAADC_CH_CONFIG_REFSEL_Internal << SAADC_CH_CONFIG_REFSEL_Pos) |
                            (SAADC_CH_CONFIG_TACQ_10us << SAADC_CH_CONFIG_TACQ_Pos) |
                            (SAADC_CH_CONFIG_MODE_SE << SAADC_CH_CONFIG_MODE_Pos);

  NRF_SAADC->RESOLUTION = SAADC_RESOLUTION_VAL_12bit;

  volatile int16_t result = 0;
  NRF_SAADC->RESULT.PTR = (uint32_t)&result;
  NRF_SAADC->RESULT.MAXCNT = 1;

  NRF_SAADC->ENABLE = (SAADC_ENABLE_ENABLE_Enabled << SAADC_ENABLE_ENABLE_Pos);
  NRF_SAADC->TASKS_START = 1;
  while (!NRF_SAADC->EVENTS_STARTED);
  NRF_SAADC->EVENTS_STARTED = 0;

  NRF_SAADC->TASKS_SAMPLE = 1;
  while (!NRF_SAADC->EVENTS_END);
  NRF_SAADC->EVENTS_END = 0;

  NRF_SAADC->TASKS_STOP = 1;
  while (!NRF_SAADC->EVENTS_STOPPED);
  NRF_SAADC->EVENTS_STOPPED = 0;

  NRF_SAADC->ENABLE = (SAADC_ENABLE_ENABLE_Disabled << SAADC_ENABLE_ENABLE_Pos);
  return (result < 0) ? 0 : result;
}

// =================================================================
// 🖥️ 6. 3개 디스플레이 정적 UI 레이아웃
// =================================================================
void drawStaticUI() {
  // [TFT 1] 좌측: 텔레메트리 HUD
  tft1.fillScreen(COLOR_BLACK);
  tft1.drawRect(0, 0, 128, 160, COLOR_YELLOW);
  tft1.drawFastHLine(0, 22, 128, COLOR_YELLOW);
  tft1.setTextColor(COLOR_YELLOW);
  tft1.setTextSize(1);
  tft1.setCursor(14, 7);
  tft1.print("IMU TELEMETRY [1]");

  tft1.setTextColor(COLOR_WHITE);
  tft1.setCursor(10, 36);  tft1.print("PITCH :");
  tft1.setCursor(10, 60);  tft1.print("ROLL  :");
  tft1.setCursor(10, 84);  tft1.print("YAW   :");

  tft1.drawFastHLine(4, 112, 120, COLOR_DARKGREY);
  tft1.setTextColor(COLOR_GREEN);
  tft1.setCursor(10, 122); tft1.print("LIB : MPU_LIGHT");
  tft1.setCursor(10, 138); tft1.print("DEV : 0x68 READY");

  // [TFT 2] 가운데: 에임 화살표 가이드
  tft2.fillScreen(COLOR_BLACK);
  tft2.drawRect(0, 0, 128, 160, COLOR_GREEN);
  tft2.drawFastHLine(0, 22, 128, COLOR_GREEN);
  tft2.setTextColor(COLOR_WHITE);
  tft2.setTextSize(1);
  tft2.setCursor(18, 7);
  tft2.print("AIMING ARROW [2]");

  tft2.drawCircle(64, 80, 46, COLOR_DARKGREY);
  tft2.drawCircle(64, 80, 22, COLOR_DARKGREY);
  tft2.drawFastHLine(10, 80, 108, COLOR_DARKGREY);
  tft2.drawFastVLine(64, 26, 108, COLOR_DARKGREY);

  tft2.drawFastHLine(4, 138, 120, COLOR_DARKGREY);
  tft2.setTextColor(COLOR_CYAN);
  tft2.setCursor(8, 145);  tft2.print("P:");
  tft2.setCursor(68, 145); tft2.print("R:");

  // [TFT 3] 우측: 방아쇠 압력 HUD
  tft3.fillScreen(COLOR_BLACK);
  tft3.drawRect(0, 0, 128, 160, COLOR_CYAN);
  tft3.drawFastHLine(0, 22, 128, COLOR_CYAN);
  tft3.setTextColor(COLOR_CYAN);
  tft3.setTextSize(1);
  tft3.setCursor(14, 7);
  tft3.print("TRIGGER HUD [3]");

  tft3.drawRect(8, 74, 112, 18, COLOR_WHITE); // 압력 게이지 틀

  tft3.drawFastHLine(4, 98, 120, COLOR_DARKGREY);
  tft3.setTextColor(COLOR_WHITE);
  tft3.setCursor(8, 106); tft3.print("RAW ADC : ");
  tft3.setCursor(8, 122); tft3.print("VOLTAGE : ");
  tft3.drawFastHLine(4, 138, 120, COLOR_DARKGREY);
  tft3.setTextColor(COLOR_YELLOW);
  tft3.setCursor(8, 145); tft3.print("FSR-402 ACTIVE");
}

// =================================================================
// 🚀 7. SETUP
// =================================================================
void setup() {
  Serial.begin(115200);

  // 1. SPI 통신선 및 CS/RST 핀 설정
  pinMode(TFT_CS1, OUTPUT); digitalWrite(TFT_CS1, HIGH);
  pinMode(TFT_CS2, OUTPUT); digitalWrite(TFT_CS2, HIGH);
  pinMode(TFT_CS3, OUTPUT); digitalWrite(TFT_CS3, HIGH);
  pinMode(TFT_RST1, OUTPUT); digitalWrite(TFT_RST1, HIGH);
  pinMode(TFT_RST2, OUTPUT); digitalWrite(TFT_RST2, HIGH);
  pinMode(TFT_RST3, OUTPUT); digitalWrite(TFT_RST3, HIGH);

  SPI.setPins(TFT_MISO, TFT_SCLK, TFT_MOSI);
  SPI.begin();

  // 2. 3개 TFT 디스플레이 초기화
  tft1.initR(INITR_BLACKTAB); tft1.setSPISpeed(4000000); tft1.setRotation(0);
  tft2.initR(INITR_BLACKTAB); tft2.setSPISpeed(4000000); tft2.setRotation(0);
  tft3.initR(INITR_BLACKTAB); tft3.setSPISpeed(4000000); tft3.setRotation(0);

  drawStaticUI();

  // 3. I2C 복구 및 MPU6050_light 초기화
  recoverI2CBus();
  Wire.setPins(I2C_SDA_PIN, I2C_SCL_PIN);
  Wire.begin();
  Wire.setClock(100000);
  delay(50);

  byte status = mpu.begin();
  if (status == 0) {
    mpu_ready = true;
    mpu.calcOffsets();
  } else {
    tft1.setTextColor(COLOR_RED);
    tft1.setCursor(10, 122);
    tft1.print("MPU INIT FAIL!");
  }

  prevTime = millis();
}

// =================================================================
// 🔄 8. MAIN LOOP
// =================================================================
void loop() {
  unsigned long now = millis();
  float dt = (now - prevTime) / 1000.0f;
  if (dt <= 0.0f || dt > 0.3f) dt = 0.033f;
  prevTime = now;

  // ----------------------------------------------------
  // 1. 자이로 데이터 갱신 및 각도 연산
  // ----------------------------------------------------
  if (mpu_ready) {
    mpu.update();

    aimPitch = mpu.getAngleY(); // 상하 각도 (Pitch)
    aimRoll  = mpu.getAngleX(); // 총신 비틀림 축 회전 (Roll)

    // Z축 각속도 적분 기반 좌우(Yaw) 에임 연산
    float gz = -mpu.getGyroZ();
    if (fabs(gz) > 0.6f) {
      aimYaw += gz * dt;
    }
    aimYaw = constrain(aimYaw, -45.0f, 45.0f);
  }

  // ----------------------------------------------------
  // 2. FSR-402 방아쇠 압력 감마/EMA 필터링 및 단계 판정
  // ----------------------------------------------------
  int rawADC = readPressureDirect(1); // P0.02 (AIN0)

  // 데드존 제거 및 0.0 ~ 1.0 선형 정규화
  float normalized = 0.0f;
  if (rawADC > RAW_MIN) {
    normalized = (float)(rawADC - RAW_MIN) / (float)(RAW_MAX - RAW_MIN);
    normalized = constrain(normalized, 0.0f, 1.0f);
  }

  // 비선형 감마 곡선 적용
  float curved = pow(normalized, GAMMA_CURVE);

  // EMA 필터링 및 백분율 환산
  float targetPercent = curved * 100.0f;
  filteredPercent = (filteredPercent * (1.0f - EMA_ALPHA)) + (targetPercent * EMA_ALPHA);
  int finalPercent = constrain((int)(filteredPercent + 0.5f), 0, 100);

  // 전압 환산
  float voltage = (rawADC * 3.6f) / 4095.0f;

  // 3단계 압력 등급 판정
  if (finalPercent >= THRESHOLD_ACTION) {
    currentLevel = LEVEL_ACTION; // 75% 이상
  } else if (finalPercent >= THRESHOLD_HOLD) {
    currentLevel = LEVEL_HOLD;   // 25% ~ 74%
  } else {
    currentLevel = LEVEL_IDLE;   // 25% 미만
  }

  // ----------------------------------------------------
  // 3. 3개 디스플레이 실시간 부분 갱신 (30ms 주기)
  // ----------------------------------------------------
  static unsigned long lastRenderTime = 0;
  if (now - lastRenderTime >= 30) {
    lastRenderTime = now;

    // 🖥️ [TFT 1] 좌측: 텔레메트리 수치
    tft1.setTextColor(COLOR_CYAN, COLOR_BLACK);
    tft1.setTextSize(1);
    tft1.setCursor(60, 36); tft1.print(aimPitch, 1); tft1.print(" deg  ");
    tft1.setCursor(60, 60); tft1.print(aimRoll, 1);  tft1.print(" deg  ");
    tft1.setCursor(60, 84); tft1.print(aimYaw, 1);   tft1.print(" deg  ");

    // 🖥️ [TFT 2] 가운데: 상/하 반전 보정 화살표 렌더링
    int arrowCenter_X = constrain(64 + (int)(aimYaw * 1.2f), 18, 110);
    
    // ★ [위/아래 방향 반전 보정]: 총구를 들면 위로(-Y), 내리면 아래로(+Y)
    int arrowCenter_Y = constrain(80 + (int)(aimPitch * 0.9f), 35, 125);
    
    float arrowAngleRad = (aimRoll - 90.0f) * PI / 180.0f;

    int arrowLen = 22;
    int tip_x = arrowCenter_X + cos(arrowAngleRad) * arrowLen;
    int tip_y = arrowCenter_Y + sin(arrowAngleRad) * arrowLen;

    int b1_x = arrowCenter_X + cos(arrowAngleRad + 2.5f) * 10;
    int b1_y = arrowCenter_Y + sin(arrowAngleRad + 2.5f) * 10;
    int b2_x = arrowCenter_X + cos(arrowAngleRad - 2.5f) * 10;
    int b2_y = arrowCenter_Y + sin(arrowAngleRad - 2.5f) * 10;

    // 이전 프레임 소거
    tft2.drawLine(prev_arrow_tip_x, prev_arrow_tip_y, prev_arrow_b1_x, prev_arrow_b1_y, COLOR_BLACK);
    tft2.drawLine(prev_arrow_tip_x, prev_arrow_tip_y, prev_arrow_b2_x, prev_arrow_b2_y, COLOR_BLACK);
    tft2.drawLine(prev_arrow_b1_x, prev_arrow_b1_y, prev_arrow_b2_x, prev_arrow_b2_y, COLOR_BLACK);

    // 신규 화살표 그리기
    tft2.drawLine(tip_x, tip_y, b1_x, b1_y, COLOR_GREEN);
    tft2.drawLine(tip_x, tip_y, b2_x, b2_y, COLOR_GREEN);
    tft2.drawLine(b1_x, b1_y, b2_x, b2_y, COLOR_GREEN);

    prev_arrow_tip_x = tip_x; prev_arrow_tip_y = tip_y;
    prev_arrow_b1_x  = b1_x;  prev_arrow_b1_y  = b1_y;
    prev_arrow_b2_x  = b2_x;  prev_arrow_b2_y  = b2_y;

    tft2.setTextColor(COLOR_WHITE, COLOR_BLACK);
    tft2.setCursor(22, 145); tft2.print(aimPitch, 0); tft2.print("d  ");
    tft2.setCursor(82, 145); tft2.print(aimRoll, 0);  tft2.print("d  ");

    // 🖥️ [TFT 3] 우측: 3단계 방아쇠 상태 카드 & 게이지 바
    static PressureLevel lastDrawnLevel = (PressureLevel)-1;
    if (currentLevel != lastDrawnLevel) {
      lastDrawnLevel = currentLevel;
      tft3.fillRect(8, 28, 112, 38, COLOR_BLACK);

      if (currentLevel == LEVEL_ACTION) {
        tft3.fillRoundRect(8, 28, 112, 38, 4, COLOR_RED);
        tft3.setTextColor(COLOR_WHITE);
        tft3.setTextSize(2);
        tft3.setCursor(24, 34); tft3.print("ACTION");
        tft3.setTextSize(1);
        tft3.setCursor(12, 52); tft3.print("FULL POWER (FIRE)");
      } else if (currentLevel == LEVEL_HOLD) {
        tft3.fillRoundRect(8, 28, 112, 38, 4, COLOR_BLUE);
        tft3.setTextColor(COLOR_YELLOW);
        tft3.setTextSize(2);
        tft3.setCursor(34, 34); tft3.print("HOLD");
        tft3.setTextColor(COLOR_WHITE);
        tft3.setTextSize(1);
        tft3.setCursor(20, 52); tft3.print("AIMING / READY");
      } else {
        tft3.fillRoundRect(8, 28, 112, 38, 4, COLOR_DARKGREY);
        tft3.setTextColor(COLOR_CYAN);
        tft3.setTextSize(2);
        tft3.setCursor(36, 34); tft3.print("IDLE");
        tft3.setTextColor(COLOR_WHITE);
        tft3.setTextSize(1);
        tft3.setCursor(22, 52); tft3.print("STANDBY MODE");
      }
      tft3.setTextSize(1);
    }

    // 게이지 바 갱신 (0 ~ 108px)
    int barW = (finalPercent * 108) / 100;
    uint16_t barCol = (currentLevel == LEVEL_ACTION) ? COLOR_RED : (currentLevel == LEVEL_HOLD ? COLOR_YELLOW : COLOR_CYAN);
    tft3.fillRect(10, 76, barW, 14, barCol);
    tft3.fillRect(10 + barW, 76, 108 - barW, 14, COLOR_BLACK);

    // 하단 수치 갱신
    tft3.setTextColor(COLOR_WHITE, COLOR_BLACK);
    tft3.setCursor(68, 106); tft3.print(rawADC); tft3.print("   ");
    tft3.setCursor(68, 122); tft3.print(voltage, 2); tft3.print("V ");
  }

  delay(5);
}