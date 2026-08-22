/*
 * =================================================================================================
 * [PROJECT] NU-40 DK Full Hardware All-in-One Reference Demo
 * [FEATURES]
 *  1. TFT 1 (좌측)  : MPU-9250 9축 IMU 자세 텔레메트리 & Pan/Tilt 서보 각도 시각화
 *  2. TFT 2 (가운데): VL53L8CX 8x8 Grayscale 원근감 깊이 맵 (Wire1: P0.26/P0.27 전용 버스)
 *  3. TFT 3 (우측)  : FSR-402 압력 센서 HUD (3단계 판정) & I2S 사운드 엔진 출력 상태
 *  4. SG90 서보 2EA : Pan(P1.10 / Pin 42), Tilt(P1.11 / Pin 43) 자동 왕복 스캔
 *  5. I2S 오디오    : MAX98357A DMA 사운드 (방아쇠 압력 단계별 효과음 피드백)
 * =================================================================================================
 */

#include <Adafruit_TinyUSB.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ST7735.h>
#include <SPI.h>
#include <Wire.h>
#include <Servo.h>
#include <MPU9250_WE.h>
#include <vl53l8cx.h>
#include <nrf.h>
#include <math.h>

// =================================================================
// 🎨 1. 표준 RGB565 컬러 매크로 정의
// =================================================================
#define COLOR_BLACK      0x0000
#define COLOR_WHITE      0xFFFF
#define COLOR_RED        0xF800
#define COLOR_GREEN      0x07E0
#define COLOR_BLUE       0x001F
#define COLOR_YELLOW     0xFFE0
#define COLOR_CYAN       0x07FF
#define COLOR_MAGENTA    0xF81F
#define COLOR_ORANGE     0xFD20
#define COLOR_DARKGREY   0x3186
#define COLOR_LIGHTGREY  0x8410

// =================================================================
// 📌 2. 하드웨어 핀 배선 매핑 (NU-40 DK)
// =================================================================
// SPI 디스플레이 공통 신호선
#define TFT_SCLK         19  // P0.19 (SCL)
#define TFT_MOSI         20  // P0.20 (SDA)
#define TFT_DC           23  // P0.23 (Data/Command)
#define TFT_MISO         22  // P0.22 (더미 핀)

// 3개 디스플레이 개별 CS & RST
#define TFT_CS1          38  // P1.06 (화면 1: 좌측 IMU/서보)
#define TFT_RST1         15  // P0.15

#define TFT_CS2          39  // P1.07 (화면 2: 가운데 ToF 8x8 음영 맵)
#define TFT_RST2         16  // P0.16

#define TFT_CS3          25  // P0.25 (화면 3: 우측 압력 HUD/사운드)
#define TFT_RST3         17  // P0.17

// 서보모터 (SG90)
#define PIN_SERVO_PAN    42  // P1.10 (좌우 팬 서보)
#define PIN_SERVO_TILT   43  // P1.11 (상하 틸트 서보)

// 기본 I2C (Wire) - MPU-9250 전용
#define MPU_I2C_SDA      30  // P0.30
#define MPU_I2C_SCL      31  // P0.31
#define MPU_ADDR         0x68

// ★ [수정] ToF 전용 독립 I2C (Wire1) - VL53L8CX_ToF_8x8_Grayscale_Depth_Map 규격 반영
#define TOF_I2C_SDA      26  // P0.26
#define TOF_I2C_SCL      27  // P0.27
#define TOF_LPN_PIN      -1

// MAX98357A I2S 오디오
#define PIN_I2S_MCK      0xFFFFFFFF
#define PIN_I2S_LRCK     11  // P0.11 (WS)
#define PIN_I2S_SCK      12  // P0.12 (BCLK)
#define PIN_I2S_SDOUT    13  // P0.13 (DIN)

#define SAMPLE_RATE      16000
#define BUFFER_SIZE      256

// =================================================================
// 🛠️ 3. 객체 인스턴스 생성 및 전역 변수
// =================================================================
Adafruit_ST7735 tft1 = Adafruit_ST7735(TFT_CS1, TFT_DC, TFT_RST1);
Adafruit_ST7735 tft2 = Adafruit_ST7735(TFT_CS2, TFT_DC, TFT_RST2);
Adafruit_ST7735 tft3 = Adafruit_ST7735(TFT_CS3, TFT_DC, TFT_RST3);

Servo panServo;
Servo tiltServo;

MPU9250_WE myMPU = MPU9250_WE(&Wire, MPU_ADDR);

// ToF 전용 Wire1 생성
TwoWire Wire1(NRF_TWIM1, NRF_TWIS1, SPIM1_SPIS1_TWIM1_TWIS1_SPI1_TWI1_IRQn, TOF_I2C_SDA, TOF_I2C_SCL);
VL53L8CX sensor(&Wire1, TOF_LPN_PIN);

static VL53L8CX_ResultsData measurementData;
static uint32_t i2s_tx_buffer[BUFFER_SIZE];

// 시스템 상태 변수
bool mpu_ready = false;
bool tof_ready = false;
float aimPitch = 0.0f;
float aimRoll = 0.0f;
float aimYaw = 0.0f;
unsigned long prevTime = 0;

// 서보 스캔 파라미터
int panAngle = 90;
int tiltAngle = 90;
int panDirection = 1;
int tiltDirection = 1;
unsigned long lastServoTime = 0;

// 압력 센서 등급
enum PressureState {
  STATE_IDLE = 0,
  STATE_HOLD,
  STATE_ACTION
};
PressureState currentPState = STATE_IDLE;
PressureState prevPState = STATE_IDLE;

// =================================================================
// 🔌 4. 저수준 하드웨어 제어 (I2C 복구, SAADC, I2S)
// =================================================================
void recoverI2CBuses() {
  // 1. 기본 I2C(Wire) 버스 복구
  pinMode(MPU_I2C_SCL, OUTPUT);
  pinMode(MPU_I2C_SDA, INPUT_PULLUP);
  for (int i = 0; i < 9; i++) {
    digitalWrite(MPU_I2C_SCL, HIGH); delayMicroseconds(5);
    digitalWrite(MPU_I2C_SCL, LOW);  delayMicroseconds(5);
  }
  digitalWrite(MPU_I2C_SCL, HIGH);

  // 2. ToF I2C(Wire1) 버스 복구
  pinMode(TOF_I2C_SCL, OUTPUT);
  pinMode(TOF_I2C_SDA, INPUT_PULLUP);
  for (int i = 0; i < 9; i++) {
    digitalWrite(TOF_I2C_SCL, HIGH); delayMicroseconds(5);
    digitalWrite(TOF_I2C_SCL, LOW);  delayMicroseconds(5);
  }
  digitalWrite(TOF_I2C_SCL, HIGH);
  delay(10);
}

// nRF52840 SAADC 레지스터 직접 제어 (P0.02 = AIN0 = Channel 1)
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

// MAX98357A I2S 오디오 엔진
void initI2S() {
  NRF_I2S->PSEL.MCK   = PIN_I2S_MCK;
  NRF_I2S->PSEL.SCK   = PIN_I2S_SCK;
  NRF_I2S->PSEL.LRCK  = PIN_I2S_LRCK;
  NRF_I2S->PSEL.SDOUT = PIN_I2S_SDOUT;

  NRF_I2S->CONFIG.MODE    = I2S_CONFIG_MODE_MODE_Master;
  NRF_I2S->CONFIG.TXEN    = I2S_CONFIG_TXEN_TXEN_Enabled;
  NRF_I2S->CONFIG.MCKFREQ = I2S_CONFIG_MCKFREQ_MCKFREQ_32MDIV31;
  NRF_I2S->CONFIG.RATIO   = I2S_CONFIG_RATIO_RATIO_64X;
  NRF_I2S->CONFIG.SWIDTH  = I2S_CONFIG_SWIDTH_SWIDTH_16Bit;
  NRF_I2S->CONFIG.ALIGN   = I2S_CONFIG_ALIGN_ALIGN_Left;
  NRF_I2S->CONFIG.FORMAT  = I2S_CONFIG_FORMAT_FORMAT_I2S;

  NRF_I2S->TXD.PTR = (uint32_t)i2s_tx_buffer;
  NRF_I2S->RXTXD.MAXCNT = BUFFER_SIZE;

  NRF_I2S->ENABLE = 1;
  NRF_I2S->TASKS_START = 1;
}

void clearI2SBuffer() {
  memset(i2s_tx_buffer, 0, sizeof(i2s_tx_buffer));
}

// 효과음 1: 준비/조준 비프음
void playSoundBeep() {
  float phase = 0.0f;
  for (int block = 0; block < 3; block++) {
    for (int i = 0; i < BUFFER_SIZE; i++) {
      int16_t sample = (int16_t)(sin(phase) * 20000.0f);
      i2s_tx_buffer[i] = ((uint32_t)(uint16_t)sample << 16) | (uint16_t)sample;
      phase += 2.0f * M_PI * 880.0f / SAMPLE_RATE;
      if (phase >= 2.0f * M_PI) phase -= 2.0f * M_PI;
    }
    delay(12);
  }
  clearI2SBuffer();
}

// 효과음 2: 강력한 액션/격발 레이저 사운드
void playSoundLaser() {
  float phase = 0.0f;
  float currentFreq = 3200.0f;
  for (int block = 0; block < 5; block++) {
    for (int i = 0; i < BUFFER_SIZE; i++) {
      float progress = (float)(block * BUFFER_SIZE + i) / (float)(5 * BUFFER_SIZE);
      float vol = 28000.0f * (1.0f - progress);
      int16_t sample = (int16_t)(sin(phase) * vol);
      i2s_tx_buffer[i] = ((uint32_t)(uint16_t)sample << 16) | (uint16_t)sample;
      phase += 2.0f * M_PI * currentFreq / SAMPLE_RATE;
      if (phase >= 2.0f * M_PI) phase -= 2.0f * M_PI;
      currentFreq *= 0.9980f;
    }
    delay(12);
  }
  clearI2SBuffer();
}

// =================================================================
// 🎨 5. 8x8 Grayscale 원근감 음영 색상 변환 함수
// =================================================================
// 가까운 곳 (<= 200mm): 밝은 흰색 (0xFFFF)
// 먼 곳 (>= 3000mm)    : 검정색 (0x0000)
uint16_t getGrayscaleColor(int16_t dist_mm, uint8_t status) {
  if (dist_mm <= 0 || !(status == 5 || status == 6 || status == 9 || status == 10)) {
    return COLOR_BLACK;
  }

  int brightness = map(dist_mm, 200, 3000, 255, 0);
  brightness = constrain(brightness, 0, 255);

  uint16_t r = (brightness >> 3) & 0x1F;
  uint16_t g = (brightness >> 2) & 0x3F;
  uint16_t b = (brightness >> 3) & 0x1F;

  return (r << 11) | (g << 5) | b;
}

// =================================================================
// 🖥️ 6. 3개 디스플레이 정적 UI 레이아웃
// =================================================================
void drawStaticFrames() {
  // [TFT 1] IMU 텔레메트리 & 서보
  tft1.fillScreen(COLOR_BLACK);
  tft1.drawRect(0, 0, 128, 160, COLOR_CYAN);
  tft1.drawFastHLine(0, 20, 128, COLOR_CYAN);
  tft1.setTextColor(COLOR_CYAN, COLOR_BLACK);
  tft1.setTextSize(1);
  tft1.setCursor(10, 6);
  tft1.print("IMU & SERVO [1]");

  tft1.setTextColor(COLOR_WHITE, COLOR_BLACK);
  tft1.setCursor(8, 26);  tft1.print("PITCH : ");
  tft1.setCursor(8, 40);  tft1.print("ROLL  : ");
  tft1.setCursor(8, 54);  tft1.print("YAW   : ");
  tft1.drawFastHLine(4, 68, 120, COLOR_DARKGREY);

  tft1.setCursor(8, 74);  tft1.print("PAN   : ");
  tft1.setCursor(8, 88);  tft1.print("TILT  : ");
  tft1.drawFastHLine(4, 102, 120, COLOR_DARKGREY);

  tft1.drawRect(14, 108, 100, 46, COLOR_DARKGREY);
  tft1.drawFastHLine(14, 131, 100, COLOR_DARKGREY);
  tft1.drawFastVLine(64, 108, 46, COLOR_DARKGREY);

  // [TFT 2] VL53L8CX 8x8 음영 깊이 맵
  tft2.fillScreen(COLOR_BLACK);
  tft2.drawRect(0, 0, 128, 160, COLOR_GREEN);
  tft2.drawFastHLine(0, 20, 128, COLOR_GREEN);
  tft2.setTextColor(COLOR_GREEN, COLOR_BLACK);
  tft2.setTextSize(1);
  tft2.setCursor(8, 6);
  tft2.print("ToF 8x8 DEPTH [2]");
  tft2.drawRect(6, 22, 116, 116, COLOR_DARKGREY);

  tft2.drawFastHLine(0, 140, 128, COLOR_GREEN);
  tft2.setTextColor(COLOR_WHITE, COLOR_BLACK);
  tft2.setCursor(6, 146);
  tft2.print("NEAR:WHT | FAR:BLK");

  // [TFT 3] FSR-402 압력 센서 HUD & 오디오
  tft3.fillScreen(COLOR_BLACK);
  tft3.drawRect(0, 0, 128, 160, COLOR_YELLOW);
  tft3.drawFastHLine(0, 20, 128, COLOR_YELLOW);
  tft3.setTextColor(COLOR_YELLOW, COLOR_BLACK);
  tft3.setTextSize(1);
  tft3.setCursor(10, 6);
  tft3.print("TRIGGER & AUDIO [3]");

  tft3.drawRect(8, 70, 112, 18, COLOR_WHITE); // 압력 게이지 바 테두리

  tft3.drawFastHLine(4, 96, 120, COLOR_DARKGREY);
  tft3.setTextColor(COLOR_WHITE, COLOR_BLACK);
  tft3.setCursor(8, 104); tft3.print("RAW ADC : ");
  tft3.setCursor(8, 118); tft3.print("VOLTAGE : ");
  tft3.setCursor(8, 132); tft3.print("I2S DAC : ACTIVE");
  tft3.drawFastHLine(4, 144, 120, COLOR_DARKGREY);
  tft3.setTextColor(COLOR_CYAN, COLOR_BLACK);
  tft3.setCursor(8, 148); tft3.print("P0.02 SAADC 12BIT");
}

// =================================================================
// 🚀 7. SETUP
// =================================================================
void setup() {
  Serial.begin(115200);

  // 1. SPI CS/RST 초기화 및 비활성화 (하이 레벨)
  pinMode(TFT_CS1, OUTPUT); digitalWrite(TFT_CS1, HIGH);
  pinMode(TFT_CS2, OUTPUT); digitalWrite(TFT_CS2, HIGH);
  pinMode(TFT_CS3, OUTPUT); digitalWrite(TFT_CS3, HIGH);
  pinMode(TFT_RST1, OUTPUT); digitalWrite(TFT_RST1, HIGH);
  pinMode(TFT_RST2, OUTPUT); digitalWrite(TFT_RST2, HIGH);
  pinMode(TFT_RST3, OUTPUT); digitalWrite(TFT_RST3, HIGH);

  SPI.setPins(TFT_MISO, TFT_SCLK, TFT_MOSI);
  SPI.begin();

  // 2. 3개 TFT 디스플레이 초기화
  tft1.initR(INITR_BLACKTAB); tft1.setRotation(0); tft1.fillScreen(COLOR_BLACK);
  tft2.initR(INITR_BLACKTAB); tft2.setRotation(0); tft2.fillScreen(COLOR_BLACK);
  tft3.initR(INITR_BLACKTAB); tft3.setRotation(0); tft3.fillScreen(COLOR_BLACK);

  drawStaticFrames();

  // 3. 서보모터 2개 초기화 (Pan: P1.10, Tilt: P1.11)
  panServo.attach(PIN_SERVO_PAN);
  tiltServo.attach(PIN_SERVO_TILT);
  panServo.write(panAngle);
  tiltServo.write(tiltAngle);

  // 4. I2C 버스 복구 및 듀얼 I2C 시작
  recoverI2CBuses();

  // 기본 Wire (MPU-9250: P0.30, P0.31)
  Wire.setPins(MPU_I2C_SDA, MPU_I2C_SCL);
  Wire.begin();
  Wire.setClock(100000);

  // 독립 Wire1 (VL53L8CX: P0.26, P0.27)
  Wire1.setPins(TOF_I2C_SDA, TOF_I2C_SCL);
  Wire1.begin();
  Wire1.setClock(100000);
  delay(50);

  // 5. MPU-9250 초기화
  for (int retry = 0; retry < 5; retry++) {
    if (myMPU.init()) {
      mpu_ready = true;
      myMPU.initMagnetometer();
      myMPU.setGyrRange(MPU9250_GYRO_RANGE_500);
      myMPU.setAccRange(MPU9250_ACC_RANGE_4G);
      break;
    }
    delay(50);
  }

  // 6. VL53L8CX ToF 센서 초기화 (8x8 해상도)
  sensor.begin();
  for (int retry = 0; retry < 3; retry++) {
    if (sensor.init() == VL53L8CX_STATUS_OK) {
      sensor.set_resolution(VL53L8CX_RESOLUTION_8X8);
      sensor.set_ranging_frequency_hz(15);
      sensor.start_ranging();
      tof_ready = true;
      break;
    }
    delay(100);
  }

  if (tof_ready) {
    Wire1.setClock(400000); // 정상 인식 후 400kHz 전환
  }

  // 7. MAX98357A I2S 사운드 엔진 기동
  initI2S();
  playSoundBeep();

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
  // 1. MPU-9250 데이터 취득 및 자세 계산
  // ----------------------------------------------------
  if (mpu_ready) {
    xyzFloat gyr = myMPU.getGyrValues();
    xyzFloat angles = myMPU.getAngles();
    aimPitch = angles.x;
    aimRoll  = angles.y;

    float gz = -gyr.z;
    if (fabs(gz) > 0.6f) {
      aimYaw += gz * dt;
    }
    aimYaw = constrain(aimYaw, -60.0f, 60.0f);
  }

  // ----------------------------------------------------
  // 2. SG90 서보모터 자동 스위프 (30ms 주기)
  // ----------------------------------------------------
  if (now - lastServoTime >= 30) {
    lastServoTime = now;
    
    // Pan 서보 (45 ~ 135도)
    panAngle += panDirection * 2;
    if (panAngle >= 135) { panAngle = 135; panDirection = -1; }
    else if (panAngle <= 45) { panAngle = 45; panDirection = 1; }
    panServo.write(panAngle);

    // Tilt 서보 (70 ~ 110도)
    tiltAngle += tiltDirection * 1;
    if (tiltAngle >= 110) { tiltAngle = 110; tiltDirection = -1; }
    else if (tiltAngle <= 70) { tiltAngle = 70; tiltDirection = 1; }
    tiltServo.write(tiltAngle);
  }

  // ----------------------------------------------------
  // 3. VL53L8CX ToF 데이터 수신 (Wire1)
  // ----------------------------------------------------
  if (tof_ready) {
    uint8_t isDataReady = 0;
    sensor.check_data_ready(&isDataReady);
    if (isDataReady) {
      sensor.get_ranging_data(&measurementData);
    }
  }

  // ----------------------------------------------------
  // 4. FSR-402 압력 센서 샘플링 & 사운드 피드백
  // ----------------------------------------------------
  int rawADC = readPressureDirect(1); // P0.02 (AIN0)
  float voltage = (rawADC * 3.6f) / 4095.0f;
  int percent = map(rawADC, 80, 3750, 0, 100);
  percent = constrain(percent, 0, 100);

  if (percent >= 75) {
    currentPState = STATE_ACTION;
  } else if (percent >= 25) {
    currentPState = STATE_HOLD;
  } else {
    currentPState = STATE_IDLE;
  }

  // 상태 전환 시 사운드 피드백
  if (currentPState != prevPState) {
    if (currentPState == STATE_HOLD) {
      playSoundBeep();
    } else if (currentPState == STATE_ACTION) {
      playSoundLaser();
    }
    prevPState = currentPState;
  }

  // ----------------------------------------------------
  // 5. 3개 디스플레이 실시간 부분 갱신 (60ms 주기)
  // ----------------------------------------------------
  static unsigned long lastRenderTime = 0;
  if (now - lastRenderTime >= 60) {
    lastRenderTime = now;

    // 🖥️ [TFT 1] IMU 및 서보 갱신
    tft1.setTextColor(COLOR_CYAN, COLOR_BLACK);
    tft1.setCursor(64, 26); tft1.print(aimPitch, 1); tft1.print(" deg  ");
    tft1.setCursor(64, 40); tft1.print(aimRoll, 1);  tft1.print(" deg  ");
    tft1.setCursor(64, 54); tft1.print(aimYaw, 1);   tft1.print(" deg  ");

    tft1.setTextColor(COLOR_YELLOW, COLOR_BLACK);
    tft1.setCursor(64, 74); tft1.print(panAngle);  tft1.print(" deg  ");
    tft1.setCursor(64, 88); tft1.print(tiltAngle); tft1.print(" deg  ");

    // 조준점 레티클 시각화
    tft1.fillRect(16, 110, 96, 42, COLOR_BLACK);
    int retX = constrain(64 + (int)(aimYaw * 0.7f), 18, 110);
    int retY = constrain(131 - (int)(aimPitch * 0.5f), 112, 150);
    tft1.drawCircle(retX, retY, 4, COLOR_YELLOW);
    tft1.drawPixel(retX, retY, COLOR_RED);

    // 🖥️ [TFT 2] VL53L8CX 8x8 Grayscale 깊이 맵 렌더링
    const int cellSize = 14;
    const int offsetX = 8;
    const int offsetY = 24;

    for (int y = 0; y < 8; y++) {
      for (int x = 0; x < 8; x++) {
        // ★ 전방 시점 기준 좌우 반전 매핑 (X Inverted)
        int sensorIndex = (7 - x) + (y * 8);

        int16_t dist = measurementData.distance_mm[sensorIndex];
        uint8_t stat = measurementData.target_status[sensorIndex];

        uint16_t color = getGrayscaleColor(dist, stat);
        tft2.fillRect(offsetX + (x * cellSize), offsetY + (y * cellSize), cellSize - 1, cellSize - 1, color);
      }
    }

    // 🖥️ [TFT 3] FSR-402 상태 카드 & 프로그레스 바
    tft3.fillRect(10, 26, 108, 38, COLOR_BLACK);
    if (currentPState == STATE_ACTION) {
      tft3.fillRoundRect(10, 26, 108, 38, 4, COLOR_RED);
      tft3.setTextColor(COLOR_WHITE);
      tft3.setTextSize(2);
      tft3.setCursor(18, 34); tft3.print("ACTION!");
      tft3.setTextSize(1);
      tft3.setCursor(24, 52); tft3.print("FULL POWER");
    } else if (currentPState == STATE_HOLD) {
      tft3.fillRoundRect(10, 26, 108, 38, 4, COLOR_BLUE);
      tft3.setTextColor(COLOR_YELLOW);
      tft3.setTextSize(2);
      tft3.setCursor(26, 34); tft3.print("HOLD");
      tft3.setTextColor(COLOR_WHITE);
      tft3.setTextSize(1);
      tft3.setCursor(24, 52); tft3.print("AIM / READY");
    } else {
      tft3.fillRoundRect(10, 26, 108, 38, 4, COLOR_DARKGREY);
      tft3.setTextColor(COLOR_CYAN);
      tft3.setTextSize(2);
      tft3.setCursor(28, 34); tft3.print("IDLE");
      tft3.setTextColor(COLOR_WHITE);
      tft3.setTextSize(1);
      tft3.setCursor(20, 52); tft3.print("STANDBY MODE");
    }
    tft3.setTextSize(1);

    // 게이지 바 갱신 (0 ~ 108px)
    int barW = (percent * 108) / 100;
    uint16_t barCol = (currentPState == STATE_ACTION) ? COLOR_RED : (currentPState == STATE_HOLD ? COLOR_YELLOW : COLOR_CYAN);
    tft3.fillRect(10, 72, barW, 14, barCol);
    tft3.fillRect(10 + barW, 72, 108 - barW, 14, COLOR_BLACK);

    // 하단 수치 갱신
    tft3.setTextColor(COLOR_WHITE, COLOR_BLACK);
    tft3.setCursor(70, 104); tft3.print(rawADC); tft3.print("   ");
    tft3.setCursor(70, 118); tft3.print(voltage, 2); tft3.print("V  ");
  }

  delay(5);
}