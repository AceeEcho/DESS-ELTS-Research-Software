#pragma once
#include <stdint.h>
#include <math.h>

// Default TF-Luna 9-byte/cm frames. This protocol contains no unit tag.
struct LunaReading {
  uint16_t distanceCm, strength, temperatureRaw;
  float temperatureC() const { return temperatureRaw / 8.0f - 256.0f; }
};

class LunaParser {
 public:
  uint32_t checksumErrors = 0;
  bool push(uint8_t byte, LunaReading &out) {
    bytes[count++] = byte;
    if (count < 9) return false;
    uint8_t sum = 0;
    for (int i = 0; i < 8; ++i) sum += bytes[i];
    if (bytes[0] == 0x59 && bytes[1] == 0x59) {
      if (sum == bytes[8]) {
        out = {word(2), word(4), word(6)};
        count = 0;
        return true;
      }
      ++checksumErrors;
    }
    // Slide one byte after noise/corruption to recover overlapping headers.
    for (int i = 0; i < 8; ++i) bytes[i] = bytes[i + 1];
    count = 8;
    return false;
  }
 private:
  uint8_t bytes[9] = {}, count = 0;
  uint16_t word(int offset) const {
    return uint16_t(bytes[offset]) | (uint16_t(bytes[offset + 1]) << 8);
  }
};

inline bool usable(const LunaReading &r, uint16_t minCm, uint16_t maxCm,
                   uint16_t minStrength, uint16_t maxStrength) {
  return r.distanceCm >= minCm && r.distanceCm <= maxCm &&
         r.strength >= minStrength && r.strength <= maxStrength;
}

struct LunaStats {
  uint32_t n = 0;
  double mean = 0, m2 = 0;
  uint16_t minimum = 65535, maximum = 0;
  void add(uint16_t cm) {
    ++n;
    // Welford's update avoids subtracting large nearly equal sums.
    const double delta = cm - mean;
    mean += delta / n;
    m2 += delta * (cm - mean);
    if (cm < minimum) minimum = cm;
    if (cm > maximum) maximum = cm;
  }
  double sampleStdDev() const { return n > 1 ? sqrt(m2 / (n - 1)) : NAN; }
};
