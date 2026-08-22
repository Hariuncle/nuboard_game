# NU-40 BLE HID mouse validation firmware

`ble_hid_mouse_controller.ino` turns the gun console into a standard Bluetooth Low Energy HID mouse:

- MPU-9250 gyro yaw/pitch produces relative mouse X/Y movement.
- The FSR-402 mounted as the trigger produces left-button press/release events.
- The device advertises as `NEON BREACH Gun` with the HID mouse appearance.

This is intentionally a minimal hardware validation sketch. It excludes the three TFT displays, VL53L8CX ToF sensor, SG90 servos, and MAX98357A audio used by `all_parts_activated.ino`.

## Assumed hardware and pins

The sketch reuses the verified mappings and low-level initialization from `all_parts_activated.ino`:

| Part | Connection |
| --- | --- |
| MPU-9250 | I2C address `0x68`; the NU-40 variant must map Arduino indices 30/31 to physical P0.30/P0.31 |
| FSR-402 divider output | nRF52840 P0.02 / AIN0, SAADC positive input channel value `1` |

The FSR voltage divider must never drive P0.02 above the board's permitted analog-input voltage. The default click thresholds are raw 12-bit SAADC values 2830 for press and 2500 for release. The gap provides hysteresis; calibrate both constants for the actual trigger mechanics.

## Software assumptions

- Arduino CLI 1.5.1.
- NU-40 board core `nucode:nrf52@1.0.2`; select FQBN `nucode:nrf52:nu40dk` with options `softdevice=s140v6,debug=l0,debug_output=serial`.
- The BLE validation sketch relies on the board core's Adafruit nRF52-compatible `bluefruit.h`, `BLEHidGeneric`, `Wire.setPins(...)`, and bundled `Bluefruit52Lib` APIs. A generic Arduino Mbed nRF52840 core is not API-compatible.
- `MPU6050_light` 1.2.1.
- `Adafruit TinyUSB Library` 3.7.7.
- `Adafruit GFX Library` 1.12.6.
- `Adafruit ST7735 and ST7789 Library` 1.11.0.
- `MPU9250_WE` 1.2.17 by Wolfgang Ewald.
- Nordic nRF52840 register definitions supplied by the selected board core through `nrf.h`.

Do not assume another nRF52840 board variant has the same pin-number mapping; confirm that Arduino pins 30 and 31 resolve to P0.30 and P0.31 first.

## Pairing and validation

1. Build and flash `ble_hid_mouse_controller.ino` with the assumptions above.
2. In the desktop or mobile Bluetooth settings, pair with `NEON BREACH Gun`.
3. Rotate the gun to check relative pointer movement.
4. Squeeze and release the FSR trigger to check left-button down/up.
5. If aim drifts, increase `GYRO_DEAD_ZONE_DPS`. If movement is too slow or fast, adjust `GYRO_TO_MOUSE`.
6. If the trigger chatters or never clicks, log/calibrate the raw FSR range and adjust `FSR_PRESS_THRESHOLD` and `FSR_RELEASE_THRESHOLD`.

The host game should consume ordinary mouse movement and primary-button input. No custom BLE application protocol is required for this validation path.

## Verification status

The local toolchain has Arduino CLI 1.5.1, `nucode:nrf52@1.0.2`, and all user libraries listed above installed.

`toyton_basic_components.ino` compiles successfully for `nucode:nrf52:nu40dk` with `softdevice=s140v6,debug=l0,debug_output=serial`. The build uses 102,856 of 815,104 bytes of flash (12%) and 12,864 of 237,568 bytes of RAM (5%). Its only observed diagnostic is the board core's duplicate `USE_TINYUSB` definition warning.

This result validates the MPU-6050, three ST7735 displays, and FSR-oriented basic-components source at compile time only; it does not validate BLE pairing or physical hardware behavior. `ble_hid_mouse_controller.ino` still requires a board-specific compile, flash, and real pairing test.

`all_parts_activated.ino` has not completed a full compile verification. It additionally depends on the VL53L8CX library (`vl53l8cx.h`) and board-compatible Servo, Wire1, and low-level I2S support for its ToF, dual-servo, and MAX98357A paths. Do not treat the successful basic-components build as validation of the all-parts sketch.
