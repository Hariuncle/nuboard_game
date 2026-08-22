/*
 * NEON BREACH - minimal NU-40 / nRF52840 BLE HID mouse validation sketch
 *
 * MPU-9250 gyro -> relative mouse movement
 * FSR-402 trigger -> left mouse button
 *
 * Deliberately excludes TFT, ToF, servos, and audio from the all-parts demo.
 */

#include <bluefruit.h>
#include <Adafruit_TinyUSB.h>
#include "class/hid/hid_device.h"
#include <Wire.h>
#include <MPU9250_WE.h>
#include <nrf.h>
#include <math.h>

#define MPU_I2C_SDA 30  // P0.30, reused from all_parts_activated.ino
#define MPU_I2C_SCL 31  // P0.31, reused from all_parts_activated.ino
#define MPU_ADDR    0x68

constexpr uint8_t FSR_AIN_CHANNEL = 1;  // P0.02 / AIN0
constexpr int FSR_PRESS_THRESHOLD = 2830;
constexpr int FSR_RELEASE_THRESHOLD = 2500;

constexpr uint32_t REPORT_INTERVAL_MS = 10;
constexpr float GYRO_DEAD_ZONE_DPS = 0.6f;
constexpr float GYRO_TO_MOUSE = 0.55f;

enum { MOUSE_REPORT_ID = 1 };

const uint8_t mouseReportDescriptor[] = {
  TUD_HID_REPORT_DESC_MOUSE(HID_REPORT_ID(MOUSE_REPORT_ID))
};
uint16_t mouseInputReportLength[] = { sizeof(hid_mouse_report_t) };

BLEDis deviceInfo;
BLEHidGeneric bleHid(1, 0, 0);
MPU9250_WE imu(&Wire, MPU_ADDR);

bool imuReady = false;
bool triggerPressed = false;
bool sentTriggerPressed = false;
bool wasConnected = false;
uint32_t previousReportMs = 0;
float mouseXResidual = 0.0f;
float mouseYResidual = 0.0f;
uint8_t mouseButtons = 0;

void recoverImuBus() {
  pinMode(MPU_I2C_SDA, INPUT_PULLUP);
  pinMode(MPU_I2C_SCL, INPUT_PULLUP);

  for (int pulse = 0; pulse < 9; ++pulse) {
    // Emulate open-drain clocking: only drive LOW, release for HIGH.
    pinMode(MPU_I2C_SCL, OUTPUT);
    digitalWrite(MPU_I2C_SCL, LOW);
    delayMicroseconds(5);
    pinMode(MPU_I2C_SCL, INPUT_PULLUP);
    delayMicroseconds(5);
  }

  // Generate a STOP without ever driving either I2C line HIGH.
  pinMode(MPU_I2C_SDA, OUTPUT);
  digitalWrite(MPU_I2C_SDA, LOW);
  delayMicroseconds(5);
  pinMode(MPU_I2C_SCL, INPUT_PULLUP);
  delayMicroseconds(5);
  pinMode(MPU_I2C_SDA, INPUT_PULLUP);
  delay(10);
}

int readPressure() {
  NRF_SAADC->ENABLE = SAADC_ENABLE_ENABLE_Disabled << SAADC_ENABLE_ENABLE_Pos;
  NRF_SAADC->CH[0].PSELP = FSR_AIN_CHANNEL;
  NRF_SAADC->CH[0].PSELN = SAADC_CH_PSELN_PSELN_NC;
  NRF_SAADC->CH[0].CONFIG =
      (SAADC_CH_CONFIG_GAIN_Gain1_6 << SAADC_CH_CONFIG_GAIN_Pos) |
      (SAADC_CH_CONFIG_REFSEL_Internal << SAADC_CH_CONFIG_REFSEL_Pos) |
      (SAADC_CH_CONFIG_TACQ_10us << SAADC_CH_CONFIG_TACQ_Pos) |
      (SAADC_CH_CONFIG_MODE_SE << SAADC_CH_CONFIG_MODE_Pos);
  NRF_SAADC->RESOLUTION = SAADC_RESOLUTION_VAL_12bit;

  volatile int16_t result = 0;
  NRF_SAADC->RESULT.PTR = reinterpret_cast<uint32_t>(&result);
  NRF_SAADC->RESULT.MAXCNT = 1;
  NRF_SAADC->ENABLE = SAADC_ENABLE_ENABLE_Enabled << SAADC_ENABLE_ENABLE_Pos;

  NRF_SAADC->TASKS_START = 1;
  while (!NRF_SAADC->EVENTS_STARTED) {}
  NRF_SAADC->EVENTS_STARTED = 0;

  NRF_SAADC->TASKS_SAMPLE = 1;
  while (!NRF_SAADC->EVENTS_END) {}
  NRF_SAADC->EVENTS_END = 0;

  NRF_SAADC->TASKS_STOP = 1;
  while (!NRF_SAADC->EVENTS_STOPPED) {}
  NRF_SAADC->EVENTS_STOPPED = 0;
  NRF_SAADC->ENABLE = SAADC_ENABLE_ENABLE_Disabled << SAADC_ENABLE_ENABLE_Pos;

  return result < 0 ? 0 : result;
}

void startAdvertising() {
  Bluefruit.Advertising.addFlags(BLE_GAP_ADV_FLAGS_LE_ONLY_GENERAL_DISC_MODE);
  Bluefruit.Advertising.addTxPower();
  Bluefruit.Advertising.addAppearance(BLE_APPEARANCE_HID_MOUSE);
  Bluefruit.Advertising.addService(bleHid);
  Bluefruit.Advertising.addName();
  Bluefruit.Advertising.restartOnDisconnect(true);
  Bluefruit.Advertising.setInterval(32, 244);
  Bluefruit.Advertising.setFastTimeout(30);
  Bluefruit.Advertising.start(0);
}

bool sendMouseReport(int8_t x, int8_t y) {
  hid_mouse_report_t report = {};
  report.buttons = mouseButtons;
  report.x = x;
  report.y = y;
  report.wheel = 0;
  if (bleHid.isBootMode()) {
    return bleHid.bootMouseReport(&report, sizeof(report));
  }
  return bleHid.inputReport(MOUSE_REPORT_ID, &report, sizeof(report));
}

bool sendLeftButton(bool pressed) {
  const uint8_t previousButtons = mouseButtons;
  mouseButtons = pressed ? MOUSE_BUTTON_LEFT : 0;
  if (sendMouseReport(0, 0)) return true;
  mouseButtons = previousButtons;
  return false;
}

void setup() {
  Serial.begin(115200);

  recoverImuBus();
  Wire.setPins(MPU_I2C_SDA, MPU_I2C_SCL);
  Wire.begin();
  Wire.setClock(100000);

  for (uint8_t attempt = 0; attempt < 5 && !imuReady; ++attempt) {
    imuReady = imu.init();
    if (!imuReady) delay(50);
  }

  if (imuReady) {
    imu.setGyrRange(MPU9250_GYRO_RANGE_500);
    imu.setAccRange(MPU9250_ACC_RANGE_4G);
  }

  Bluefruit.begin();
  Bluefruit.setName("NEON BREACH Gun");
  Bluefruit.setTxPower(4);
  Bluefruit.Periph.setConnInterval(9, 16);

  deviceInfo.setManufacturer("NEON BREACH");
  deviceInfo.setModel("NU-40 Gun Controller");
  deviceInfo.begin();

  bleHid.setReportLen(mouseInputReportLength);
  bleHid.enableMouse(true);
  bleHid.setReportMap(mouseReportDescriptor, sizeof(mouseReportDescriptor));
  bleHid.begin();
  startAdvertising();

  previousReportMs = millis();
}

void loop() {
  const uint32_t now = millis();
  if (now - previousReportMs < REPORT_INTERVAL_MS) return;

  const float dt = (now - previousReportMs) / 1000.0f;
  previousReportMs = now;

  const int pressure = readPressure();
  if (!triggerPressed && pressure >= FSR_PRESS_THRESHOLD) {
    triggerPressed = true;
  } else if (triggerPressed && pressure <= FSR_RELEASE_THRESHOLD) {
    triggerPressed = false;
  }

  const bool connected = Bluefruit.connected();
  if (!connected) {
    wasConnected = false;
    sentTriggerPressed = false;
    return;
  }

  if (!wasConnected) {
    // Clear any button state retained by the HID helper across a reconnect.
    if (!sendLeftButton(false)) return;
    sentTriggerPressed = false;
    wasConnected = true;
  }

  if (triggerPressed != sentTriggerPressed) {
    if (sendLeftButton(triggerPressed)) {
      sentTriggerPressed = triggerPressed;
    }
  }

  if (!imuReady) return;

  const xyzFloat gyro = imu.getGyrValues();
  const float yawRate = fabsf(gyro.z) >= GYRO_DEAD_ZONE_DPS ? -gyro.z : 0.0f;
  const float pitchRate = fabsf(gyro.x) >= GYRO_DEAD_ZONE_DPS ? gyro.x : 0.0f;
  mouseXResidual += yawRate * dt * GYRO_TO_MOUSE;
  mouseYResidual += pitchRate * dt * GYRO_TO_MOUSE;

  const int x = constrain(static_cast<int>(mouseXResidual), -127, 127);
  const int y = constrain(static_cast<int>(mouseYResidual), -127, 127);

  if (x != 0 || y != 0) {
    if (sendMouseReport(static_cast<int8_t>(x), static_cast<int8_t>(y))) {
      mouseXResidual -= x;
      mouseYResidual -= y;
    }
  }
}
