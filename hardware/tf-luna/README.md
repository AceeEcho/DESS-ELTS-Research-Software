# TF-Luna + your ESP32: Arduino bench test

Open `TF_Luna_Test/TF_Luna_Test.ino` in Arduino IDE. Keep `LunaData.h` in the same folder: it is part of the sketch, not a package to install.

Your photos show an SDATEKIT ESP32-DevkitC-32E board and a TF-Luna sensor. Select **ESP32 Dev Module** for this classic ESP32 board. The USB bridge chip marking and sensor connector orientation cannot be confirmed from these photos. The board headers appear unpopulated: use properly soldered headers or secure soldered leads, not wires loosely inserted in holes.

## Wiring diagram

Unplug USB before wiring. This is a functional diagram, not a physical connector view.

```text
Computer USB ── USB-C data cable ── ESP32 DevkitC-32E

ESP32 (read printed pin labels)           TF-Luna (manufacturer pin numbers)

5V  ───────────────────────────────────► 1  +5V
GND ──────────────────────────────────── 4  GND
16  ◄─────────────────────────────────── 3  TXD   sensor sends readings
17  ───────────────────────────────────► 2  RXD   sensor receives commands
                                        5  Leave disconnected (UART mode)
                                        6  Leave disconnected
```

| TF-Luna pin | Connect to ESP32 |
|---|---|
| 1: +5V | 5V |
| 2: RXD | GPIO17, printed `17` |
| 3: TXD | GPIO16, printed `16` |
| 4: GND | Any GND |
| 5: interface selection | Leave disconnected |
| 6: output | Leave disconnected |

Use the sensor's matching six-pin cable. Identify pin 1 using **Figure 4, printed page 8 (PDF page 12)** of the [Benewake manual](https://en.benewake.com/uploadfiles/2025/04/20250430174515390.pdf#page=12). Do not infer orientation from this table, cable colors, or the exposed PCB test pads in Photo 2. If your connector/cable cannot be matched to that drawing, identify it before applying power.

The sensor uses 5V power and 3.3V UART signaling, so these UART lines connect directly. Do not put 5V onto GPIO16/17 or power the sensor from 3V3. The manual specifies up to 150mA peak sensor current. Because this is a third-party board, verify its 5V pin is supplied from USB before relying on it. Use one USB power source for this setup; do not add an external 5V supply while USB is connected. [Electrical and pin specifications](https://en.benewake.com/uploadfiles/2025/04/20250430174515390.pdf#page=11).

## Install the software and packages on Windows

### 1. Arduino IDE

If already installed, open it. Otherwise download the Windows installer from [Arduino's official software page](https://www.arduino.cc/en/software), run it, complete the installer, and launch Arduino IDE 2.

### 2. Required board package: esp32 by Espressif Systems

1. Open **File > Preferences**.
2. In **Additional boards manager URLs**, add the following address. Preserve existing entries; use the list button to add another line.

   ```text
   https://espressif.github.io/arduino-esp32/package_esp32_index.json
   ```

3. Click **OK**.
4. Open **Tools > Board > Boards Manager** (or the Boards Manager sidebar).
5. Search `esp32`.
6. Find **esp32 by Espressif Systems** and install the current stable version. Record that version with your test notes.
7. Restart the IDE, then select **Tools > Board > esp32 > ESP32 Dev Module**.

This installs the compiler, upload tools and ESP32 Arduino core. `Arduino.h` and `HardwareSerial` come with it. **No Library Manager packages, TF-Luna library, Python, or Benewake GUI are required.** Follow [Espressif's official installation guide](https://docs.espressif.com/projects/arduino-esp32/en/latest/installing.html) if menu wording differs.

### 3. USB driver: only if Windows does not create a COM port

1. Connect the ESP32 using a USB-C **data** cable.
2. Right-click Windows Start > **Device Manager**. Expand **Ports (COM & LPT)**. Unplug/replug the board to identify its entry. If a COM port appears without an error icon, skip driver installation.
3. Otherwise inspect its entry under **Other devices** or USB devices. Open **Properties > Details > Hardware Ids** and check the device/chip identity. USB-C alone does not identify the driver.
4. Install only the matching driver:

| Identified USB bridge | Official download | Installation |
|---|---|---|
| Silicon Labs CP210x | [CP210x VCP drivers](https://www.silabs.com/software-and-tools/usb-to-uart-bridge-vcp-drivers?tab=downloads), choose **CP210x Universal Windows Driver** | Download ZIP, right-click > Extract All. In Device Manager, right-click the board > Update driver > Browse my computer for drivers > select the extracted folder, enable Include subfolders > Next. |
| WCH CH340/CH341 | [CH341SER.EXE](https://www.wch-ic.com/downloads/CH341SER_EXE.html) | Download the official installer, run it, select INSTALL, then reconnect. |
| WCH CH343/CH9102 | [CH343SER.ZIP](https://www.wch-ic.com/downloads/CH343SER_ZIP.html) | Download and Extract All; run the included SETUP installer and select INSTALL. If using the driver files directly, use Device Manager's Browse my computer workflow above. |

If the bridge remains unidentified, obtain its marking or Hardware Ids before choosing a driver. If absolutely no device appears/disappears, first try another data cable and USB port. Do not install random driver bundles.

## Upload and view readings

1. Make the connections above with USB unplugged, then reconnect USB.
2. Open **File > Open** and choose `TF_Luna_Test/TF_Luna_Test.ino`. The IDE should also show a `LunaData.h` tab.
3. Select **ESP32 Dev Module** and **Tools > Port > COM...** matching Device Manager. Leave other board options at defaults; set **Upload Speed = 115200** for initial setup.
4. Click **Verify** (checkmark), then **Upload** (right arrow).
5. If upload stays at `Connecting...`, hold **BOOT**; release it when writing starts. If necessary, hold BOOT, tap the reset/EN button, then retry uploading and release BOOT when writing begins.
6. Open **Tools > Serial Monitor**, select **115200 baud**, and tap reset/EN to restart the test. No monitor commands or line-ending selection are required.
7. Aim at a stationary, flat, matte target. Expect `READ` lines and a `TEST`/`DIST` summary every 10 seconds. If not, use the troubleshooting table below.

The sketch assumes factory UART settings: **115200, 8N1, 9-byte output in centimeters**. It does not change the sensor configuration. Previously configured millimeter output has the same header and cannot be identified automatically: confirm the units against a known distance. [Output format and defaults](https://en.benewake.com/uploadfiles/2025/04/20250430174515390.pdf#page=24).

## Run tests on the readings

1. **Communication:** Leave the target fixed for three 10-second windows. Record frame rate, usable count, rejected count and checksum candidates. Factory output is nominally 100Hz. Checksum candidates should ideally remain zero; startup may include a partial frame. A checksum failure counter counts candidate frames, not a proven number of lost measurements.
2. **Repeatability:** Hold the sensor and target still. Record mean, minimum, maximum and sample standard deviation. Lower standard deviation indicates less short-term variation; it does not establish accuracy.
3. **Reference comparison:** Measure from the sensor's front face to the target using an independent reference. Put that measured value into `REFERENCE_CM` near the top of the sketch and upload again. Bias is mean minus reference: positive means the sensor reads farther. Try, for example, targets at 50, 100 and 200cm if you can measure those positions. Record your reference uncertainty. Skip accuracy claims without a measured reference.
4. **Target response:** Move the target slowly closer and farther, then stop at each position. Check that readings follow the motion and settle. This does not measure sensor latency; timestamps are ESP32 receipt times.
5. **Signal and range rejection:** Try a dark target and a target closer than 20cm. Inspect the raw values and `usable` flag. The software conservatively accepts 20–800cm with strength 100–30000; this screening does not guarantee a correct measurement. [Range and signal guidance](https://en.benewake.com/uploadfiles/2025/04/20250430174515390.pdf).
6. **Loss of signal:** Disconnect USB before changing wiring. Remove only the sensor TX-to-GPIO16 lead, reconnect USB, and confirm `NO DATA` appears. Power off again before restoring the lead.

The first window includes sensor startup. Use subsequent stable windows for comparisons. Statistics include every checksum-valid, usable reading parsed in the window. Invalid readings are excluded from distance statistics and counted separately; no valid readings produces an explicit unavailable result. Standard deviation requires at least two samples.

`READ` output is limited to 10 lines/second for readability; it is **not a full-rate raw recording**. Keep monitor text and test notes together if you copy/save results. Record board-package version, sketch settings, target surface, lighting, geometry and reference measurement. No physical readings have been collected as part of creating this guide.

For Arduino **Serial Plotter**, set `PLOT_MODE = true`, upload, close Serial Monitor and open **Tools > Serial Plotter** at 115200. The plot has `distance_cm` and `valid`; invalid distance is `nan`, and no fresh frames means no new plot points. Some Plotter versions handle `nan` differently, so use Monitor mode for rejection and timeout tests. Restore `false` for summaries.

## Troubleshooting

| Symptom | Check |
|---|---|
| No COM port | Data cable, Device Manager and matching USB driver. |
| Upload cannot connect | Correct board/port, close other serial programs, BOOT/reset procedure above. |
| Gibberish in Monitor | Monitor must use 115200 baud. Brief boot output is separate from sketch output. |
| NO DATA / STALE | Power, shared ground, sensor TX to GPIO16, pin 5 floating, configured UART baud/output format. Disconnect power before rewiring. |
| Many checksum candidates | Loose leads, power integrity, noise or wrong sensor baud. Do not disable checksum checking. |
| Mostly rejected readings | Examine raw distance/strength, target size and position, surface and lighting. |
| A roughly tenfold distance error | Sensor may have been set to millimeter mode; this sketch assumes centimeters. |
| Reset loop / brownout | USB cable or supply capacity; inspect for shorts with power disconnected. |

## Software verification

`test_luna.cpp` exercises the actual shared parser/statistics code with synthetic frames: noise, damaged checksums, truncated frames, embedded header bytes, validity boundaries, temperature decoding and sample statistics. Compile/run from the repository root using a C++ compiler:

```text
g++ -std=c++11 -Wall -Wextra -pedantic hardware/tf-luna/test_luna.cpp -o test_luna.exe
./test_luna.exe
```

These host tests passed during preparation. ESP32 board compilation, upload, Serial Plotter behavior and physical measurements have not been verified. This standalone bench sketch does not control the ELTS apparatus or establish physical calibration or study readiness.
