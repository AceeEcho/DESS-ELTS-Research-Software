"""Rebuild the original library props and their painted surfaces with Python 3."""
from math import cos, exp, pi, sin, sqrt
from pathlib import Path
import random
import struct
import zlib


ART = Path(__file__).resolve().parents[2] / "unity/Assets/Resources/ELTS/Library"


class Model:
    """Small Wavefront writer. Unity imports these static meshes as ordinary art assets."""

    def __init__(self):
        self.vertices = []
        self.faces = []

    def vertex(self, x, y, z, u, v):
        self.vertices.append((x, y, z, u, v))
        return len(self.vertices)

    def bridge(self, bottom, top):
        for i in range(len(bottom)):
            j = (i + 1) % len(bottom)
            self.faces.append((bottom[i], top[i], top[j], bottom[j]))

    def save(self, name, smooth=False):
        lines = [f"o {name}", "# Geometry in virtual metres"]
        lines.extend("v %.5f %.5f %.5f" % p[:3] for p in self.vertices)
        lines.extend("vt %.5f %.5f" % p[3:] for p in self.vertices)
        lines.append("s 1" if smooth else "s off")
        lines.extend("f " + " ".join(f"{i}/{i}" for i in face) for face in self.faces)
        (ART / (name + ".obj")).write_text("\n".join(lines) + "\n", encoding="ascii")


def rounded_panel(name, width, height, depth, radius, bevel, upright=False):
    """Rounded outline plus three bevel rings on each face. The chair back bows in depth."""
    model = Model()
    rings = []
    for angle in (-90, -55, -25, 0, 0, 25, 55, 90):
        a = angle * pi / 180
        inset = bevel * (1 - cos(a))
        upper = len(rings) >= 4
        y = (1 if upper else -1) * (height / 2 - bevel) + bevel * sin(a)
        ring = []
        for corner, (sx, sz) in enumerate(((1, 1), (-1, 1), (-1, -1), (1, -1))):
            for step in range(6):
                t = (corner + step / 5) * pi / 2
                x = sx * (width / 2 - radius) + (radius - inset) * cos(t)
                z = sz * (depth / 2 - radius) + (radius - inset) * sin(t)
                u, v = x / width + .5, z / depth + .5
                if upright:
                    # Rotate the cushion upright. The wings wrap slightly around the reader.
                    point = (x, z, -y + .012 - .025 * (2 * x / width) ** 2)
                else:
                    point = (x, y, z)
                ring.append(model.vertex(*point, u, v))
        if rings:
            model.bridge(rings[-1], ring)
        rings.append(ring)
    model.faces.extend((tuple(rings[0]), tuple(reversed(rings[-1]))))
    model.save(name, smooth=True)


def png(name, width, height, pixel):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    rows = bytearray()
    for j in range(height):
        rows.append(0)
        for i in range(width):
            rows.extend(max(0, min(255, round(c))) for c in pixel(i, j))
    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    (ART / (name + ".png")).write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
                                         + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def bindings_and_foliage():
    colors = [(96, 115, 112), (131, 77, 61), (153, 133, 90), (73, 89, 109),
              (120, 127, 100), (167, 154, 132), (101, 69, 72), (175, 165, 133),
              (80, 76, 60), (88, 107, 76), (106, 125, 79), (132, 145, 92),
              (97, 117, 83), (153, 159, 106), (109, 126, 95), (126, 138, 90)]

    def pixel(x, y):
        section, local = divmod(x, 32)
        base = colors[section]
        if section < 8 and 5 < local < 26:
            if (section % 3 == 0 and 82 < y < 100) or (section % 3 == 1 and y in (20, 21, 85)):
                return (178, 165, 133)
        grain = ((x * 31 + y * 43) % 11 - 5) * .45
        return [c + grain for c in base]

    png("library-palette", 512, 128, pixel)


def books(seed):
    rng = random.Random(seed)
    model = Model()
    x = -.5
    while x < .45:
        width = rng.uniform(.014, .034)
        height = rng.uniform(.68, .99)
        z = rng.uniform(-.49, -.38)
        color = rng.randrange(8)
        left, right = (color + .1) / 16, (color + .9) / 16
        corners = [(x + width, .49), (x, .49), (x, z), (x + width, z)]
        rings = []
        for y in (0, height):
            rings.append([model.vertex(px, y, pz, right if px > x else left, .05 if y == 0 else .95)
                          for px, pz in corners])
        model.bridge(*rings)
        model.faces.append(tuple(reversed(rings[1])))
        x += width + rng.uniform(.002, .006)
        if rng.random() < .12:
            x += rng.uniform(.017, .045)
    model.save(f"CollectionRow{seed}")


def tree():
    rng = random.Random(44)
    model = Model()

    def ring(center, rx, rz, color, sides=7):
        x, y, z = center
        return [model.vertex(x + rx * cos(i * 2*pi/sides), y, z + rz * sin(i * 2*pi/sides),
                             (color + .5) / 16, .5) for i in range(sides)]

    def branch(start, end, base, tip):
        model.bridge(ring(start, base, base, 8), ring(end, tip, tip, 8))

    branch((0, 0, 0), (.06, 2.85, .03), .15, .065)
    for limb in range(16):
        angle = limb * 2.39996
        radius = rng.uniform(.9, 1.9)
        tip = (cos(angle)*radius, rng.uniform(2.9, 4.85), sin(angle)*radius)
        branch((.04, 1.55 + limb * .07, .02), tip, .065, .018)
        for twig in range(7):
            heading = angle + twig * 2.39996
            cx = tip[0] + .57*cos(heading)
            cy = tip[1] + rng.uniform(-.15, .55)
            cz = tip[2] + .57*sin(heading)
            branch(tip, (cx, cy, cz), .022, .007)
            # Small overlapping leaf sprays leave sky between the limbs. Unity draws
            # both sides of these cards, so each leaf needs just two triangles.
            for _ in range(24):
                theta = rng.uniform(0, 2*pi)
                reach = .60 * sqrt(rng.random())
                x, z = cx + reach*cos(theta), cz + reach*sin(theta)
                y = cy + rng.uniform(-.23,.23)
                length, width = rng.uniform(.13,.26), rng.uniform(.055,.104)
                dx, dz = length*cos(theta), length*sin(theta)
                wx, wz = -width*sin(theta), width*cos(theta)
                color_u = (rng.randrange(9,16) + .5)/16
                points = [(x-dx,y+.03,z-dz), (x+wx,y+.08,z+wz),
                          (x+dx,y-.035,z+dz), (x-wx,y+.025,z-wz)]
                leaf = tuple(model.vertex(*point, color_u, .5) for point in points)
                model.faces.append(leaf)
    model.save("CourtyardTree", smooth=True)


def ceiling():
    # Explicit UVs on a downward-facing plane align the painted light with the real pendants.
    # Unity's OBJ importer mirrors X; mirror source X here to retain the room's U direction.
    model = Model()
    for x, z in ((-.5, -.5), (-.5, .5), (.5, .5), (.5, -.5)):
        model.vertex(x, 0, z, .5 - x, z + .5)
    model.faces.append((4, 3, 2, 1))
    model.save("AcousticCeiling")
    width, height = 768, 1024
    x_lights = [sum(exp(-((x - lamp)/1.42)**2) for lamp in (-5.4, 1, 6.8))
                for x in (22*((i+.5)/width-.5) for i in range(width))]
    z_lights = [sum(exp(-((z - lamp)/.82)**2) for lamp in (2, 7.5, 14.4, 21, 26))
                for z in (28-38.5*(j+.5)/height for j in range(height))]

    def pixel(i, j):
        x = 22*((i+.5)/width-.5)
        z = 28-38.5*(j+.5)/height
        glow = x_lights[i] * z_lights[j]
        sky = max(0, (x-2) / 9)
        # Fine joints and light acoustic speckle are intentionally quiet at a distance.
        joint = min(abs(x/1.2-round(x/1.2))*1.2, abs(z/2.4-round(z/2.4))*2.4)
        shade = 14*max(0, 1-joint/.022)
        grain = ((i*53+j*29) % 13 - 6)*.42
        return (163+70*glow+14*sky-shade+grain, 166+58*glow+17*sky-shade+grain,
                164+39*glow+21*sky-shade+grain)

    png("acoustic-ceiling", width, height, pixel)


if __name__ == "__main__":
    rounded_panel("CounterLip", 3.4, .065, 1.15, .13, .018)
    rounded_panel("ChairSeat", .43, .07, .43, .078, .019)
    rounded_panel("ChairBack", .43, .040, .43, .09, .013, upright=True)
    for variation in range(3):
        books(variation)
    bindings_and_foliage()
    tree()
    ceiling()
