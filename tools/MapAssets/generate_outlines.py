"""Build clean silhouettes from the original 14.2.9 room sprite alpha masks.

Requires Pillow. Run from any directory; no game or player data is read.
"""
import collections
import math
from pathlib import Path
import struct
import zlib

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src/Methadrenaline.Server/Map"


def simplify(points, tolerance=1.25):
    if len(points) <= 2:
        return points
    a, b = points[0], points[-1]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    distances = [abs(dy * (x - a[0]) - dx * (y - a[1])) / length if length else math.dist(a, (x, y))
                 for x, y in points]
    pivot = max(range(1, len(points) - 1), key=distances.__getitem__)
    if distances[pivot] <= tolerance:
        return [a, b]
    return simplify(points[:pivot + 1], tolerance)[:-1] + simplify(points[pivot:], tolerance)


def trace(mask, size):
    edges = set()
    for y in range(size):
        for x in range(size):
            if not mask[y * size + x]:
                continue
            if y == 0 or not mask[(y - 1) * size + x]: edges.add(((x, y), (x + 1, y)))
            if x == size - 1 or not mask[y * size + x + 1]: edges.add(((x + 1, y), (x + 1, y + 1)))
            if y == size - 1 or not mask[(y + 1) * size + x]: edges.add(((x + 1, y + 1), (x, y + 1)))
            if x == 0 or not mask[y * size + x - 1]: edges.add(((x, y + 1), (x, y)))
    incident = collections.defaultdict(list)
    for a, b in sorted(edges): incident[a].append(b)
    loops = []
    while edges:
        a, b = min(edges)
        start = a
        points = []
        while True:
            edges.remove((a, b))
            points.append(a)
            if b == start: break
            candidates = [c for c in incident[b] if (b, c) in edges]
            dx, dy = b[0] - a[0], b[1] - a[1]
            def turn(c):
                ex, ey = c[0] - b[0], c[1] - b[1]
                cross = dx * ey - dy * ex
                return 2 if cross > 0 else 1 if dx * ex + dy * ey > 0 else 0
            a, b = b, max(candidates, key=turn)
        area = sum(p[0] * q[1] - q[0] * p[1] for p, q in zip(points, points[1:] + points[:1])) / 2
        if abs(area) < 4: continue
        pivot = max(range(1, len(points)), key=lambda i: math.dist(points[0], points[i]))
        points = simplify(points[:pivot + 1])[:-1] + simplify(points[pivot:] + points[:1])[:-1]
        loops.append((area, [(x * 256 / size - 128, 128 - y * 256 / size) for x, y in points]))
    return loops


def align(points, name):
    lines = {}
    half_corridor = 32 if name.startswith("LCZ_") else 40

    def snap(value):
        if abs(abs(value) - half_corridor) <= 10:
            return math.copysign(half_corridor, value)
        if abs(value) >= 124:
            return math.copysign(128, value)
        return round(value / 8) * 8

    incident = collections.defaultdict(list)
    for a in range(len(points)):
        b = (a + 1) % len(points)
        x1, y1 = points[a]
        x2, y2 = points[b]
        dx, dy = x2 - x1, y2 - y1
        aligned = False
        if abs(dx) >= 16 and abs(dy) <= abs(dx) * .1:
            line = (0, 1, snap((y1 + y2) / 2))
            aligned = True
        elif abs(dy) >= 16 and abs(dx) <= abs(dy) * .1:
            line = (1, 0, snap((x1 + x2) / 2))
            aligned = True
        else:
            line = (dy, -dx, dy * x1 - dx * y1)
        lines[(a, b)] = (line, aligned)
        incident[a].append((a, b))
        incident[b].append((a, b))
    result = list(points)
    for vertex, adjacent in incident.items():
        if len(adjacent) != 2:
            continue
        (a, b, c), aligned1 = lines[adjacent[0]]
        (d, e, f), aligned2 = lines[adjacent[1]]
        if not (aligned1 or aligned2):
            continue
        denominator = a * e - b * d
        if abs(denominator) > 1e-6:
            candidate = ((c * e - b * f) / denominator, (a * f - c * d) / denominator)
            if math.dist(candidate, points[vertex]) <= 12:
                result[vertex] = candidate
                continue
        # At a nearly tangent curve, keep its endpoint close to the source.
        x, y = points[vertex]
        if aligned1:
            distance = (a * x + b * y - c) / (a * a + b * b)
            x, y = x - a * distance, y - b * distance
        if aligned2:
            distance = (d * x + e * y - f) / (d * d + e * e)
            x, y = x - d * distance, y - e * distance
        result[vertex] = (x, y)
    return result


def contours(alpha, size, name):
    source = Image.frombytes("L", (size, size), alpha)
    image = Image.new("L", (512, 512))
    # Draw the silhouette once, then its substantial translucent interior areas.
    # Texture-edge antialiasing must not become extra floor bands along every
    # wall: that multiplies the number of quads without adding room detail.
    for lower, upper, opacity in ((24, 256, 255), (24, 64, 33), (64, 128, 94), (128, 192, 160)):
        mask = source.point(lambda value: 255 if lower <= value < upper else 0)
        if opacity == 255:
            mask = mask.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5))
        mask = mask.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))
        layer = Image.new("L", image.size)
        draw = ImageDraw.Draw(layer)
        for area, points in sorted(trace(mask.tobytes(), size), key=lambda loop: -abs(loop[0])):
            points = align(points, name)
            draw.polygon([((x + 128) * 2, (128 - y) * 2) for x, y in points], fill=255 if area > 0 else 0)
        image.paste(opacity, mask=layer)
    return image.tobytes()


def main():
    source = zlib.decompress((ASSETS / "native-room-icons.deflate").read_bytes(), -15)
    count, = struct.unpack_from("<I", source)
    offset = 4
    output = bytearray(struct.pack("<I", count))
    for _ in range(count):
        start = offset
        length = 0
        shift = 0
        while True:
            part = source[offset]
            offset += 1
            length |= (part & 127) << shift
            shift += 7
            if part < 128:
                break
        name = source[offset:offset + length].decode()
        output.extend(source[start:offset + length])
        offset += length
        size, label_x, label_y = struct.unpack_from("<Hff", source, offset)
        offset += 10
        alpha = source[offset:offset + size * size]
        offset += size * size
        output.extend(struct.pack("<Hff", 512, label_x * 512 / size, label_y * 512 / size))
        output.extend(contours(alpha, size, name))
    compressor = zlib.compressobj(9, wbits=-15)
    (ASSETS / "native-room-outlines.deflate").write_bytes(compressor.compress(output) + compressor.flush())
    print(f"Built {count} native room silhouettes with aligned straight walls.")


if __name__ == "__main__":
    main()
