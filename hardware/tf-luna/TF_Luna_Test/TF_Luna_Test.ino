#include <Arduino.h>
#include "LunaData.h"

// All wiring and test choices are here. Distances are centimeters.
constexpr int LIDAR_RX = 16;  // ESP32 input, connected to sensor TX (pin 3).
constexpr int LIDAR_TX = 17;  // ESP32 output, connected to sensor RX (pin 2).
constexpr uint32_t BAUD = 115200;
constexpr uint32_t WINDOW_MS = 10000;
constexpr uint32_t STALE_MS = 1000;
constexpr uint16_t MIN_CM = 20, MAX_CM = 800;
constexpr uint16_t MIN_STRENGTH = 100, MAX_STRENGTH = 30000;
// Set to your measured target distance to report bias; zero disables comparison.
constexpr double REFERENCE_CM = 0;
// Plot mode emits only fresh distance/validity points (at most 10 per second).
// Keep false for diagnostics and the timed test summaries.
constexpr bool PLOT_MODE = false;

HardwareSerial lidar(2);
LunaParser parser;
LunaStats stats;
uint32_t startMs, lastFrameMs, lastDisplayMs = 0, previousErrors = 0;
uint32_t frames = 0, rejected = 0;
bool haveFrame = false, staleReported = false;

void printSummary(uint32_t now) {
  const double seconds = (now - startMs) / 1000.0;
  Serial.printf("TEST seconds=%.2f frames=%lu hz=%.2f usable=%lu rejected=%lu checksum_candidates=%lu\n",
    seconds, (unsigned long)frames, frames / seconds, (unsigned long)stats.n,
    (unsigned long)rejected, (unsigned long)(parser.checksumErrors - previousErrors));
  if (stats.n) {
    Serial.printf("DIST mean_cm=%.3f min_cm=%u max_cm=%u sample_sd_cm=%.3f",
      stats.mean, unsigned(stats.minimum), unsigned(stats.maximum), stats.sampleStdDev());
    if (REFERENCE_CM > 0) Serial.printf(" bias_cm=%.3f", stats.mean - REFERENCE_CM);
    Serial.println();
  } else Serial.println("DIST no usable samples; statistics unavailable");
  previousErrors = parser.checksumErrors;
  frames = rejected = 0;
  stats = LunaStats{};
  startMs = now;
}

void setup() {
  Serial.begin(BAUD);
  lidar.setRxBufferSize(2048);
  lidar.begin(BAUD, SERIAL_8N1, LIDAR_RX, LIDAR_TX);
  startMs = millis();
  lastFrameMs = startMs;
  // Receive only: do not change or save sensor settings.
  if (!PLOT_MODE) Serial.println("TF-Luna bench test: assumes default UART 115200, 9-byte/cm output. Reset restarts tests.");
}

void loop() {
  // Bound each drain so continuous input cannot starve timeout/report handling.
  for (int budget = 0; budget < 512 && lidar.available(); ++budget) {
    LunaReading reading;
    if (!parser.push(uint8_t(lidar.read()), reading)) continue;
    const uint32_t now = millis(); // Host receipt time, not sensor acquisition time.
    lastFrameMs = now;
    haveFrame = true;
    staleReported = false;
    ++frames;
    const bool valid = usable(reading, MIN_CM, MAX_CM, MIN_STRENGTH, MAX_STRENGTH);
    if (valid) stats.add(reading.distanceCm); else ++rejected;
    if (now - lastDisplayMs >= 100) {
      lastDisplayMs = now;
      if (PLOT_MODE) {
        // nan leaves invalid ranges undefined, rather than inventing zero distance.
        Serial.printf("distance_cm:%.2f\tvalid:%d\n", valid ? double(reading.distanceCm) : NAN, valid ? 1 : 0);
      } else {
        Serial.printf("READ t_ms=%lu raw_cm=%u strength=%u chip_C=%.2f usable=%d\n",
          (unsigned long)now, unsigned(reading.distanceCm), unsigned(reading.strength),
          reading.temperatureC(), valid ? 1 : 0);
      }
    }
  }
  const uint32_t now = millis();
  if (now - lastFrameMs >= STALE_MS && !staleReported) {
    staleReported = true;
    if (!PLOT_MODE) Serial.println(haveFrame ? "STALE: no checksum-valid frame for 1 second" : "NO DATA: no checksum-valid frame received");
  }
  if (now - startMs >= WINDOW_MS) {
    if (!PLOT_MODE) printSummary(now);
    else { startMs = now; frames = rejected = 0; stats = LunaStats{}; }
  }
}
