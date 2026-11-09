#include "TF_Luna_Test/LunaData.h"
#include <assert.h>
#include <stdio.h>
#include <vector>

std::vector<uint8_t> frame(uint16_t cm, uint16_t strength = 200) {
  std::vector<uint8_t> b = {0x59,0x59,uint8_t(cm),uint8_t(cm>>8),
    uint8_t(strength),uint8_t(strength>>8),0xC8,0x08,0}; // 25 C
  for (int i=0; i<8; ++i) b[8] += b[i];
  return b;
}
int main() {
  LunaParser p; LunaReading r{}; int received = 0;
  auto feed = [&](const std::vector<uint8_t> &b) { for (auto x:b) if(p.push(x,r)) ++received; };
  feed({0,1,0x59}); feed(frame(100));
  assert(received == 1 && r.distanceCm == 100 && r.temperatureC() == 25);
  auto bad = frame(250); bad[8] ^= 1; feed(bad); feed(frame(300));
  assert(received == 2 && r.distanceCm == 300 && p.checksumErrors >= 1);
  auto partial = frame(400); partial.resize(5); feed(partial); feed(frame(500));
  assert(received == 3 && r.distanceCm == 500);
  feed(frame(0x5959)); feed(frame(600));
  assert(received == 5 && r.distanceCm == 600);
  assert(usable({20,100,0},20,800,100,30000));
  assert(usable({800,30000,0},20,800,100,30000));
  for (auto v : {LunaReading{19,100,0}, LunaReading{801,100,0},
       LunaReading{100,99,0}, LunaReading{100,30001,0}, LunaReading{100,65535,0}})
    assert(!usable(v,20,800,100,30000));
  LunaStats s; assert(isnan(s.sampleStdDev()));
  s.add(100); assert(isnan(s.sampleStdDev())); s.add(102); s.add(104);
  assert(s.n == 3 && s.mean == 102 && s.sampleStdDev() == 2 && s.minimum == 100 && s.maximum == 104);
  puts("PASS: framing, noise, checksum rejection, truncation recovery, embedded headers, validity boundaries, temperature and statistics");
}
