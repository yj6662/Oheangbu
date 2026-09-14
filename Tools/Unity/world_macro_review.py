"""Build an offline Korean world review from the Unity export, never inferred test results.

Run: python Tools/Unity/world_macro_review.py [--out Art/World/WorldMacro]
Required: sheet.json, terrain_grid.json. Optional: captures.json, measurements.json,
flow_audit.json, return_routes.json (matching revision only).
The map and cross sections sample the exact exported terrain; no procedural detail is added.
"""
from __future__ import annotations

import argparse
import base64
import html
import hashlib
import io
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
# Existing RealmId serialization: Cheongrim, Hwanggyeong, Jeokro, Cheolong, Hyeongang.
PALETTE = ["#4f7659", "#8b7854", "#9b6656", "#697681", "#4c7887"]


def field(obj, key, default=None):
    if not isinstance(obj, dict):
        return default
    return next((v for k, v in obj.items() if k.lower() == key.lower()), default)


def esc(value):
    return html.escape(str(value), quote=True)


def read_json(path, fallback=None):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else fallback


def xz(p):
    if isinstance(p, (list, tuple)):
        return float(p[0]), float(p[2] if len(p) > 2 else p[1])
    return float(field(p, "x", 0)), float(field(p, "z", field(p, "y", 0)))


def label(obj):
    return field(obj, "Label", None) or field(obj, "Id", "")


def is_return_route(route):
    return str(field(route, 'Id', '')).startswith('Return_') or '황경 귀환' in str(field(route, 'Progression', ''))


def realm_color(obj):
    realm = field(obj, "Realm", 0)
    try:
        return PALETTE[int(realm) % len(PALETTE)]
    except (ValueError, TypeError):
        return PALETTE[0]


class Terrain:
    def __init__(self, data):
        self.x = float(field(data, "minX", 0))
        self.z = float(field(data, "minZ", 0))
        self.step = float(field(data, "step", 80))
        self.cols = int(field(data, "cols", 0))
        self.rows = int(field(data, "rows", 0))
        self.heights = np.asarray(field(data, "heights", []), dtype=np.float64).reshape(self.rows, self.cols)
        self.inside = np.asarray(field(data, "inside", [True] * self.heights.size), dtype=bool).reshape(self.rows, self.cols)
        self.min_h = float(np.min(self.heights[self.inside]))
        self.max_h = float(np.max(self.heights[self.inside]))

    def sample(self, x, z):
        tx, tz = (x - self.x) / self.step, (z - self.z) / self.step
        if not (0 <= tx <= self.cols - 1 and 0 <= tz <= self.rows - 1):
            return None
        ix, iz = min(self.cols - 2, int(tx)), min(self.rows - 2, int(tz))
        if not self.inside[iz:iz + 2, ix:ix + 2].any():
            return None
        fx, fz = tx - ix, tz - iz
        a = self.heights[iz, ix] * (1 - fx) + self.heights[iz, ix + 1] * fx
        b = self.heights[iz + 1, ix] * (1 - fx) + self.heights[iz + 1, ix + 1] * fx
        return float(a * (1 - fz) + b * fz)

    def relief(self):
        gz, gx = np.gradient(self.heights, self.step)
        normals = np.stack([-gx, np.ones_like(gx), -gz], axis=-1)
        normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
        light = np.array([-0.45, 0.78, 0.43])
        light /= np.linalg.norm(light)
        shade = np.clip(np.sum(normals * light, axis=-1), 0, 1)
        elevation = np.clip((self.heights - self.min_h) / max(1, self.max_h - self.min_h), 0, 1)
        low, high = np.array([212, 211, 190]), np.array([115, 116, 104])
        rgb = low + (high - low) * elevation[:, :, None] ** 0.70
        rgb *= (0.57 + 0.55 * shade[:, :, None])
        # The exact authored outline clips the image. A coarse 80m alpha mask here
        # would add a false staircase / light fringe around that same outline.
        rgba = np.concatenate([np.clip(rgb, 0, 255).astype(np.uint8), np.full((*self.heights.shape, 1), 255, dtype=np.uint8)], axis=-1)
        return Image.fromarray(np.flipud(rgba))


class Map:
    def __init__(self, sheet, terrain):
        self.sheet, self.terrain = sheet, terrain
        self.x0, self.z0 = xz(field(sheet, "BoundsMin", {"x": terrain.x, "y": terrain.z}))
        self.x1, self.z1 = xz(field(sheet, "BoundsMax", {"x": terrain.x + (terrain.cols - 1) * terrain.step, "y": terrain.z + (terrain.rows - 1) * terrain.step}))
        self.padding, self.scale = 50, 1100 / max(1, self.z1 - self.z0)
        self.w, self.h = (self.x1 - self.x0) * self.scale + 100, 1200
        self.outline = field(sheet, "Outline", [])
        self.regions = field(sheet, "Regions", [])
        self.rivers = field(sheet, "Rivers", [])
        self.routes = field(sheet, "Routes", [])
        self.sites = field(sheet, "Sites", [])
        self.ridges = field(sheet, "Ridges", [])
        self.relief_image = terrain.relief()
        self.label_layouts = self.place_labels()

    def xy(self, p):
        x, z = xz(p)
        return self.padding + (x - self.x0) * self.scale, self.padding + (self.z1 - z) * self.scale

    def points(self, values):
        return " ".join(f"{x:.2f},{y:.2f}" for x, y in map(self.xy, values))

    def place_labels(self):
        """Deterministic cartographic annotation; never moves the actual site."""
        font_path = Path("C:/Windows/Fonts/malgun.ttf")
        font = ImageFont.truetype(str(font_path), 40) if font_path.exists() else ImageFont.load_default()
        major_ids = {"Hwanggyeong", "Cheongrim", "Jeokro", "Cheolong", "Hyeongang"}
        sites = sorted(enumerate(self.sites), key=lambda item: field(item[1], 'Id') not in major_ids)
        occupied = []
        markers = [self.xy(field(s, 'Position', {})) for s in self.sites]
        result = {}
        def overlap(a, b):
            return max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(0, min(a[3], b[3]) - max(a[1], b[1]))
        for i, site in sites:
            name = label(site).replace(' 예약', '')
            size = 13 if field(site, 'Id') in major_ids else 10.5
            width = font.getlength(name) / 40 * size
            x, y = markers[i]
            options = []
            for dx, dy in [(9, -7), (9, 16), (-width - 9, -7), (-width - 9, 16), (-width / 2, -15), (-width / 2, 26), (18, -29), (18, 36), (-width - 18, -29), (-width - 18, 36), (25, -43), (25, 49), (-width - 25, -43), (-width - 25, 49)]:
                lx, ly = x + dx, y + dy
                box = (lx - 3, ly - size - 2, lx + width + 3, ly + 3)
                cost = sum(overlap(box, b) for b in occupied) * 100
                cost += sum(overlap(box, (px - 4, py - 4, px + 4, py + 4)) for j, (px, py) in enumerate(markers) if j != i) * 45
                cost += (max(0, 15 - box[0]) + max(0, box[2] - (self.w - 15))) * 1000
                cost += abs(dy) + min(abs(dx), abs(dx + width)) * .3
                options.append((cost, lx, ly, box))
            _, lx, ly, box = min(options, key=lambda v: v[0])
            occupied.append(box)
            result[i] = {'x': lx, 'y': ly, 'size': size, 'text': name, 'width': width, 'leader': abs(ly - y) > 20}
        return result

    def svg(self):
        buf = io.BytesIO()
        self.relief_image.save(buf, format="PNG")
        encoded = base64.b64encode(buf.getvalue()).decode("ascii")
        t = self.terrain
        grid_top = self.xy({"x": t.x, "z": t.z + (t.rows - 1) * t.step})
        grid_w, grid_h = (t.cols - 1) * t.step * self.scale, (t.rows - 1) * t.step * self.scale
        outline = self.points(self.outline)
        s = [f'<svg xmlns="http://www.w3.org/2000/svg" id="world-map" role="img" aria-label="전체 지형 지도" viewBox="0 0 {self.w:.2f} {self.h}" data-base="0 0 {self.w:.2f} {self.h}">',
             '<style>text{font-family:"Malgun Gothic",sans-serif}.site{cursor:pointer}.site:hover circle{fill:#ad7040}.map-label{paint-order:stroke;stroke:#eeece1;stroke-width:3px;stroke-linejoin:round}.site:focus{outline:none}.site:focus circle{stroke-width:4}.route-line.return-selected{stroke:#b56539;stroke-width:5;stroke-dasharray:none}</style>',
             f'<defs><clipPath id="world-outline"><polygon points="{outline}"/></clipPath></defs>',
             f'<rect x="-10000" y="-10000" width="30000" height="30000" fill="#e8e5d9"/>',
             f'<polygon points="{outline}" fill="#d4d3be" stroke="#71766a" stroke-width="1.5"/>',
             '<g id="layer-relief" clip-path="url(#world-outline)">',
             f'<image x="{grid_top[0]:.2f}" y="{grid_top[1]:.2f}" width="{grid_w:.2f}" height="{grid_h:.2f}" href="data:image/png;base64,{encoded}"/>', '</g>',
             '<g id="layer-regions" clip-path="url(#world-outline)">']
        for region in self.regions:
            polygon = field(region, "Polygon", [])
            if not polygon:
                continue
            color = realm_color(region)
            s += [f'<polygon points="{self.points(polygon)}" fill="{color}" fill-opacity=".12" stroke="{color}" stroke-opacity=".55" stroke-dasharray="7 6" stroke-width="1.3"/>']
            positions = [self.xy(p) for p in polygon]
            cx, cy = np.mean(positions, axis=0)
            s += [f'<text x="{cx:.1f}" y="{cy:.1f}" text-anchor="middle" font-size="24" fill="{color}" class="map-label" opacity=".88">{esc(label(region))}</text>']
        s += ['</g><g id="layer-ridges" style="display:none">']
        for ridge in self.ridges:
            s += [f'<polyline points="{self.points(field(ridge, "Points", []))}" stroke="#555c4f" fill="none" stroke-width="2" stroke-dasharray="2 5"><title>{esc(label(ridge))} · 주능선 저작선</title></polyline>']
        s += ['</g><g id="layer-rivers">']
        for river in self.rivers:
            width = max(2, min(8, float(field(river, "Width", 20)) * self.scale))
            s += [f'<polyline points="{self.points(field(river, "Points", []))}" stroke="#eff1e9" stroke-width="{width + 1.5:.1f}" fill="none" stroke-linejoin="round"/>',
                  f'<polyline points="{self.points(field(river, "Points", []))}" stroke="#688c95" stroke-width="{width:.1f}" fill="none" stroke-linejoin="round"><title>{esc(label(river))}</title></polyline>']
        s += ['</g>']
        for returning, group_id in [(False, 'roads'), (True, 'returns')]:
            s += [f'<g id="layer-{group_id}">']
            for route in self.routes:
                if is_return_route(route) != returning:
                    continue
                carriage = bool(field(route, "Carriage", False))
                points = self.points(field(route, "Points", []))
                color = '#805b75' if returning else '#795a3c' if carriage else '#526c4c'
                dash = '' if carriage else 'stroke-dasharray="4 3"'
                s += [f'<polyline points="{points}" stroke="#f2e8d1" stroke-width="{5 if carriage or returning else 3}" fill="none" stroke-linecap="round"/>',
                      f'<polyline class="route-line" data-route="{esc(field(route, "Id", ""))}" points="{points}" stroke="{color}" stroke-width="{2.7 if carriage or returning else 1.8}" {dash} fill="none" stroke-linejoin="round"><title>{esc(label(route))} · {"황경 귀환 추가 구간 / " if returning else ""}{"가도" if carriage else "도보"}</title></polyline>']
            s += ['</g>']
        s += ['<g id="layer-sites">']
        for i, site in enumerate(self.sites):
            x, y = self.xy(field(site, "Position", {}))
            name = label(site)
            layout = self.label_layouts[i]
            major = layout['size'] == 13
            leader = f'<path d="M{x:.1f} {y:.1f}L{layout["x"] + layout["width"] / 2:.1f} {layout["y"] - layout["size"] / 2:.1f}" stroke="#626f5b" stroke-opacity=".65" stroke-width=".7"/>' if layout['leader'] else ''
            s += [f'<g class="site" tabindex="0" data-site="{i}" role="button" aria-label="{esc(name)}">{leader}<circle cx="{x:.1f}" cy="{y:.1f}" r="{5 if major else 3.3}" fill="{realm_color(site)}" stroke="#f4eee0" stroke-width="1.5"/><text x="{layout["x"]:.1f}" y="{layout["y"]:.1f}" font-size="{layout["size"]}" fill="#373d35" class="map-label">{esc(layout["text"])}</text><title>{esc(name)} · {esc(field(site, "Kind", ""))}</title></g>']
        s += ['</g>',
              f'<path d="M{self.w - 43:.1f} 78v-35m-7 11 7-11 7 11" fill="none" stroke="#435147" stroke-width="2"/><text x="{self.w - 43:.1f}" y="35" text-anchor="middle" fill="#435147" font-size="13">N</text>',
              f'<path d="M58 {self.h - 32}h{1000 * self.scale:.1f}" stroke="#435147" stroke-width="3"/><text x="58" y="{self.h - 43}" fill="#435147" font-size="13">1 km</text>', '</svg>']
        return ''.join(s)

    def png(self, path):
        factor = 1.6
        w, h = int(self.w * factor), int(self.h * factor)
        image = Image.new("RGB", (w, h), "#e8e5d9")
        draw = ImageDraw.Draw(image)
        xy = lambda p: tuple(v * factor for v in self.xy(p))
        outline = [xy(p) for p in self.outline]
        mask = Image.new("L", (w, h))
        ImageDraw.Draw(mask).polygon(outline, fill=255)
        t = self.terrain
        origin = xy({"x": t.x, "z": t.z + (t.rows - 1) * t.step})
        tw, th = round((t.cols - 1) * t.step * self.scale * factor), round((t.rows - 1) * t.step * self.scale * factor)
        background = Image.new("RGBA", (w, h), "#d4d3be")
        relief = self.relief_image.resize((tw, th), Image.Resampling.BICUBIC)
        background.paste(relief, (round(origin[0]), round(origin[1])), relief)
        image.paste(background.convert("RGB"), (0, 0), mask)
        draw = ImageDraw.Draw(image)
        for river in self.rivers:
            points = [xy(p) for p in field(river, "Points", [])]
            if len(points) >= 2:
                draw.line(points, fill="#71919a", width=max(3, round(float(field(river, "Width", 20)) * self.scale * factor)), joint="curve")
        for route in self.routes:
            points = [xy(p) for p in field(route, "Points", [])]
            if len(points) >= 2:
                draw.line(points, fill="#eee5cf", width=7, joint="curve")
                draw.line(points, fill="#805b75" if is_return_route(route) else "#795a3c" if field(route, "Carriage", False) else "#526c4c", width=4 if is_return_route(route) else 3, joint="curve")
        font_path = Path("C:/Windows/Fonts/malgun.ttf")
        for i, site in enumerate(self.sites):
            x, y = xy(field(site, "Position", {}))
            layout = self.label_layouts[i]
            font = ImageFont.truetype(str(font_path), round(layout['size'] * factor)) if font_path.exists() else ImageFont.load_default()
            if layout['leader']:
                draw.line([(x, y), ((layout['x'] + layout['width'] / 2) * factor, (layout['y'] - layout['size'] / 2) * factor)], fill="#74806c", width=1)
            draw.ellipse((x - 5, y - 5, x + 5, y + 5), fill=realm_color(site), outline="#f4eee0", width=2)
            draw.text((layout['x'] * factor, layout['y'] * factor), layout['text'], anchor='ls', font=font, fill="#373d35", stroke_width=2, stroke_fill="#eeece1")
        draw.line(outline + outline[:1], fill="#71766a", width=2)
        image.save(path)


def section_svg(world, out):
    """Three explicit geographic transects, sampled from exported height grid."""
    t = world.terrain
    sites = world.sites
    def find(word):
        return next((s for s in sites if word in str(label(s))), None)
    transects = []
    for a, b in [("황경", "청림"), ("황경", "현강"), ("적로", "철옹")]:
        sa, sb = find(a), find(b)
        if sa and sb:
            transects.append((f'{label(sa)} → {label(sb)}', xz(field(sa, "Position")), xz(field(sb, "Position"))))
    if not transects:
        cx, cz = (world.x0 + world.x1) / 2, (world.z0 + world.z1) / 2
        transects = [("중앙 동서 단면", (world.x0, cz), (world.x1, cz)), ("중앙 남북 단면", (cx, world.z0), (cx, world.z1))]
    W, H = 1300, 300 * len(transects)
    svg = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}"><rect width="{W}" height="{H}" fill="#eeece2"/><style>text{{font-family:"Malgun Gothic",sans-serif;fill:#3f463d}}</style>']
    records = []
    ymax = max(200, math.ceil(t.max_h / 200) * 200)
    for n, (title, a, b) in enumerate(transects):
        y0 = 58 + n * 300
        distance = math.dist(a, b)
        samples = []
        for k in range(301):
            ratio = k / 300
            px, pz = a[0] + (b[0] - a[0]) * ratio, a[1] + (b[1] - a[1]) * ratio
            samples.append((distance * ratio, t.sample(px, pz)))
        svg += [f'<text x="60" y="{y0 - 20}" font-size="20">{esc(title)}</text>']
        for tick in range(0, ymax + 1, 200):
            y = y0 + 170 - (tick / ymax) * 160
            svg += [f'<path d="M65 {y:.2f}H1240" stroke="#d5d8cb"/><text x="55" y="{y + 4:.2f}" text-anchor="end" font-size="11">{tick}m</text>']
        segments, current = [], []
        for d, height in samples:
            if height is None:
                if current:
                    segments.append(current)
                    current = []
            else:
                current.append((65 + d / max(1, distance) * 1175, y0 + 170 - height / ymax * 160))
        if current:
            segments.append(current)
        for segment in segments:
            p = ' '.join(f'{x:.2f},{y:.2f}' for x, y in segment)
            bottom = f'{segment[-1][0]:.2f},{y0 + 170} {segment[0][0]:.2f},{y0 + 170}'
            svg += [f'<polygon points="{p} {bottom}" fill="#bdc2ae"/><polyline points="{p}" stroke="#566451" fill="none" stroke-width="2"/>']
        svg += [f'<text x="65" y="{y0 + 195}" font-size="12">0 km</text><text x="1240" y="{y0 + 195}" text-anchor="end" font-size="12">{distance / 1000:.2f} km · 직선 단면 / 이동 경로 아님</text>']
        records.append({"label": title, "from": list(a), "to": list(b), "straightDistanceM": round(distance, 2), "samples": [{"distanceM": round(d, 2), "heightM": None if h is None else round(h, 2)} for d, h in samples]})
    svg.append('</svg>')
    (out / 'sections.svg').write_text(''.join(svg), encoding='utf-8')
    (out / 'section_samples.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')


def route_table(world, measurements=None):
    rows = []
    measured = {field(m, 'id'): m for m in field(measurements, 'routes', [])}
    sites = {field(s, 'Id'): label(s).replace(' 예약', '') for s in world.sites}
    for route in world.routes:
        pts = field(route, "Points", [])
        if len(pts) < 2:
            continue
        carriage = bool(field(route, "Carriage", False))
        d3, dh, max_grade, grades = 0, 0, 0, []
        for a, b in zip(pts, pts[1:]):
            horizontal = math.dist(xz(a), xz(b))
            rise = float(field(b, "y", 0)) - float(field(a, "y", 0))
            d3 += math.hypot(horizontal, rise)
            dh += horizontal
            if horizontal > .01:
                grade = math.degrees(math.atan2(abs(rise), horizontal))
                grades.append(grade)
                max_grade = max(max_grade, grade)
        speed = 14 if carriage else 4.5
        minutes = d3 / speed / 60
        m = measured.get(field(route, 'Id'), {})
        length = float(field(m, 'lengthM', d3))
        width = float(field(m, 'widthM', field(route, 'Width', 0)))
        maximum = float(field(m, 'maxGradeDeg', max_grade))
        p95 = float(field(m, 'p95GradeDeg', np.percentile(grades, 95) if grades else 0))
        seconds = float(field(m, 'travelSecondsAtConstantSpeed', d3 / speed))
        radius = field(m, 'minTurnRadiusM', None)
        radius_text = '미측정' if radius is None else f'{radius:.1f} m' if radius < 1e8 else '직선'
        bridge_grade = field(m, 'maxBridgeApproachGradeDeg', None)
        bridge_text = '미측정' if bridge_grade is None else f'{bridge_grade:.1f}°'
        start, end = field(route, 'From', ''), field(route, 'To', '')
        rows.append(f'<tr><td>{esc(sites.get(start, start))} → {esc(sites.get(end, end))}<small>{esc(label(route))}</small></td><td>{"가도" if carriage else "도보"}</td><td>{length / 1000:.2f} km</td><td>{width:.1f} m</td><td>{maximum:.1f}° / {p95:.1f}°</td><td>{bridge_text}</td><td>{radius_text}</td><td>{seconds / 60:.1f}분</td></tr>')
    return ''.join(rows)


def return_section(world, data):
    if not data or not field(data, 'rows', []):
        return '<p class="pending">황경 귀환 경로의 최단거리 검사 결과가 아직 제공되지 않았습니다.</p>'
    sites = {field(s, 'Id'): label(s).replace(' 예약', '') for s in world.sites}
    rows = []
    changed_anchor = False
    for route in field(data, 'rows', []):
        start, end = field(route, 'from', ''), field(route, 'to', 'Hwanggyeong')
        before_anchor = field(route, 'beforeAnchor', start)
        before = field(route, 'beforeLengthM', None)
        status = field(route, 'status', 'UNVERIFIED')
        route_ids = field(route, 'routeIds', []) or []
        site_ids = field(route, 'siteIds', []) or []
        route_text = ' → '.join(sites.get(s, s) for s in site_ids)
        before_text = '미연결 / 미측정' if before is None or before < 0 else f'{before / 1000:.2f} km'
        before_text += f'<small>{esc(sites.get(before_anchor, before_anchor))} 출발</small>'
        length = float(field(route, 'lengthM', 0))
        after_text = f'{length / 1000:.2f} km' if route_ids else '미연결'
        if before is not None and before >= 0 and before_anchor == start and route_ids:
            delta = float(field(route, 'shorterByM', before - length))
            after_text += '<small>이전과 동일</small>' if abs(delta) < 0.5 else f'<small>{abs(delta) / 1000:.2f} km {"감소" if delta >= 0 else "증가"}</small>'
        elif before_anchor != start:
            changed_anchor = True
            after_text += '<small>이전과 출발점 다름</small>'
        walk = float(field(route, 'walkSeconds', 0)) / 60
        mixed = float(field(route, 'mixedSeconds', 0)) / 60
        trails = float(field(route, 'trailLengthM', 0)) / 1000
        roads = float(field(route, 'carriageLengthM', 0)) / 1000
        route_attribute = esc(json.dumps(route_ids, ensure_ascii=False))
        rows.append(f'<tr><td><button class="return-select" data-return="{route_attribute}">{esc(sites.get(start, start))} → {esc(sites.get(end, end))}</button><small>{esc(status)} · 지도에서 경로 보기</small></td><td>{before_text}</td><td>{after_text}</td><td>가도 {roads:.2f} km<br>도보길 {trails:.2f} km</td><td>{walk:.1f}분<small>전체 구간 4.5m/s</small></td><td>{mixed:.1f}분<small>가도14 / 도보4.5m/s</small></td></tr><tr class="return-detail"><td colspan="6">{esc(route_text)}<small>{esc(" · ".join(route_ids))}</small></td></tr>')
    revision = field(data, 'revision', {})
    revision_text = ''
    if revision:
        before = field(revision, 'ridgeCountBefore', None)
        after = field(revision, 'ridgeCountAfter', None)
        raised = field(revision, 'raisedArea25mKm2', None)
        rise = field(revision, 'maxRiseM', None)
        if before is not None and after is not None:
            revision_text = f'<p class="caption">산계 저작선 {before} → {after}개'
            if raised is not None:
                revision_text += f' · 이전보다25m 이상 높아진 표본 영역 {raised:.2f}km²'
            if rise is not None:
                revision_text += f' · 표본 최대 상승 {rise:.0f}m'
            revision_text += '. 지도 높이 표본 비교이며 실제 플레이 구간 면적이나 산봉우리 개수를 의미하지 않습니다.</p>'
        if str(field(data, 'baselineVersion', '')).startswith('v3_'):
            revision_text += '<p class="caption">V4는 기존 산 높이를 유지하고 급사면을 피하도록 길을 옮겼습니다. 적로·현강 귀환 거리가 길어져 우회 구간의 호흡과 편안함은 추가 검토가 필요합니다. 연결 PASS가 가도 폭 전체의 주행 합격을 의미하지 않습니다.</p>'
    anchor_note = ' 청림의 이전값은 금표 주막을 대리 출발점으로 사용하므로 현재 청림 외림 출발값과 단축량을 직접 비교하지 않습니다.' if changed_anchor else ' 두 버전의 동일한 강토 중심을 출발점으로 사용합니다.'
    return ('<div class="table-wrap"><table class="return-table"><thead><tr><th>출발 강토 → 황경</th><th>이전 최단거리</th><th>현재 최단거리</th><th>길의 구성</th><th>전부 도보</th><th>가도에서 가마 이용</th></tr></thead><tbody>'
            + ''.join(rows) + '</tbody></table></div><p class="caption">거리 합계가 가장 짧은 연결 경로를 먼저 고른 뒤, 그 동일 경로의 시간을 환산했습니다. 속도가 가장 빠른 경로를 별도로 탐색한 값이 아닙니다. 가마 이용 시간은 가도 구간만14m/s, 도보길은4.5m/s이며 실제 가마 기능·진행 게이트·주행 검증을 뜻하지 않습니다. </p>'
            + '<p class="caption">' + anchor_note + '</p>' + revision_text)



def relief_section(data, world):
    if not data:
        return '<p class="pending">V2→V3의 길 양옆 지형 표본 비교가 아직 제공되지 않았습니다.</p>'
    baseline_branch = field(data, 'baseline', {})
    actual_audit = field(data, 'currentRouteAudit', None)
    current_branch = actual_audit or field(data, 'current', {})
    baseline = field(baseline_branch, 'overall', {})
    current = field(current_branch, 'overall', {})
    labels = [('leftReliefRatio', '왼쪽 산지'), ('rightReliefRatio', '오른쪽 산지'),
              ('bothReliefRatio', '양쪽 모두'), ('eitherReliefRatio', '한쪽 이상')]
    rows = []
    for key, title in labels:
        before, after = field(baseline, key), field(current, key)
        if before is None or after is None:
            values = '<td>미측정</td><td>미측정</td><td>미측정</td>'
        else:
            before, after = float(before), float(after)
            values = f'<td>{before * 100:.2f}%</td><td>{after * 100:.2f}%</td><td>{(after - before) * 100:+.2f}%p</td>'
        rows.append(f'<tr><td>{title}</td>{values}</tr>')
    metric = field(data, 'metric', {})
    step = field(metric, 'sampleSpacingM', '미측정')
    near, far = field(metric, 'distanceMinM', '미측정'), field(metric, 'distanceMaxM', '미측정')
    rise = field(metric, 'minRiseM', '미측정')
    angle = field(metric, 'minElevationDegrees', '미측정')
    coverage = field(metric, 'minSectorCoverage', None)
    covtext = '미측정' if coverage is None else f'{float(coverage) * 100:.0f}%'
    count = field(current, 'sampleCount', '미측정')
    before_count = field(baseline, 'sampleCount', '미측정')
    sampling_note = (f'V2와 V3 각 버전의 실제 경로를 {step}m 간격으로 따로 표본화했습니다({before_count}→{count}점). 경로 자체가 바뀌었으므로 동일 위치의 개선률이 아니며 경로 재배치와 지형 변화가 함께 반영됩니다.' if actual_audit else f'같은 V2 경로 위치와 진행 방향에서 {step}m 간격, {count}개 표본을 비교했습니다.')
    names = {field(site, 'Id'): label(site).replace(' 예약', '') for site in world.sites}
    names['OutsideDefinedRegions'] = '강토 구분 밖'
    regions_before = {field(r, 'id'): r for r in field(baseline_branch, 'regions', [])}
    regional_rows = []
    for region in field(current_branch, 'regions', []):
        rid = field(region, 'id', '')
        prior = regions_before.get(rid, {})
        before = float(field(prior, 'bothReliefRatio', 0))
        after = float(field(region, 'bothReliefRatio', 0))
        regional_rows.append(f'<tr><td>{esc(names.get(rid, rid))}</td><td>{field(prior, "sampleCount", 0)} → {field(region, "sampleCount", 0)}</td><td>{before * 100:.2f}% → {after * 100:.2f}%</td></tr>')
    route_names = {field(r, 'Id'): names.get(field(r, 'From'), field(r, 'From', '')) + ' → ' + names.get(field(r, 'To'), field(r, 'To', '')) for r in world.routes}
    gap_rows = []
    for route in field(current_branch, 'routes', []):
        gaps = field(route, 'gaps', {})
        largest = {side: max((float(field(g, 'estimatedLengthM', 0)) for g in field(gaps, side, [])), default=0) for side in ['left', 'right', 'bothAbsent']}
        if not any(largest.values()):
            continue
        rid = field(route, 'id', '')
        gap_rows.append(f'<tr><td>{esc(route_names.get(rid, rid))}<small>{esc(rid)}</small></td><td>{largest["left"]:.0f}m</td><td>{largest["right"]:.0f}m</td><td>{largest["bothAbsent"]:.0f}m</td></tr>')
    details = ('<details><summary>강토별 표본과 기준 미달 구간 확인</summary><h3>양쪽 조건을 만족한 표본 비율</h3><table><thead><tr><th>강토</th><th>표본 수 V2→V3</th><th>비율 V2→V3</th></tr></thead><tbody>'
               + ''.join(regional_rows) + '</tbody></table><h3>구간별 가장 긴 기준 미달 범위</h3><p class="caption">160m 간격 표본 중간점으로 추정한 길이입니다. 열린 도시 입지나 도하점일 수도 있으며 결함·통행 불가 판정이 아닙니다.</p><div class="table-wrap"><table><thead><tr><th>길</th><th>왼쪽</th><th>오른쪽</th><th>양쪽 동시</th></tr></thead><tbody>'
               + ''.join(gap_rows) + '</tbody></table></div><p><a href="route_relief.json">측정 조건·모든 표본 원문 ↗</a></p></details>')
    return ('<div class="table-wrap"><table><thead><tr><th>지형 표본 조건</th><th>V2</th><th>V3</th><th>변화</th></tr></thead><tbody>'
            + ''.join(rows) + '</tbody></table></div>'
            + f'<p class="caption">{esc(sampling_note)} 양옆 {esc(near)}~{esc(far)}m 범위에서 높이 차 {esc(rise)}m·고도각 {esc(angle)}° 이상인 지형이 방향 부채꼴의 {covtext} 이상을 채우는지 계산합니다. 이는 시각적 완성 기준보다 낮은 진단 하한입니다. 80m 지형 표본의 잠재 실루엣 지표이며, 앞산에 의한 가림·건물·숲·실제 렌더 시야를 계산한 가시성 검사는 아닙니다. 길의 위치가 바뀐 구간과 상세 조건은 원문에서 구분합니다.</p>' + details)


def revision_images(out):
    def pair(before, after, title, caption, before_label, after_label):
        if not (out / before).exists() or not (out / after).exists():
            return ''
        parts = []
        for name, tag in [(before, before_label), (after, after_label)]:
            parts.append(f'<figure class="capture"><a href="{esc(name)}" target="_blank"><img src="{esc(name)}" loading="lazy" alt="{esc(title)} · {esc(tag)}"></a><figcaption><strong>{esc(tag)}</strong></figcaption></figure>')
        return f'<h3>{esc(title)}</h3><p class="caption">{esc(caption)}</p><div class="capture-grid">{"".join(parts)}</div>'
    items = []
    for before, after, title in [('tone_before_eye_capital.png', 'eye_capital.png', '황경 · 더 짙어진 땅과 산'), ('tone_before_eye_jeokro.png', 'eye_jeokro.png', '적로 · 농도와 옅은 화색')]:
        items.append(pair(before, after, title, '같은 V4 지형과 카메라입니다. 이전은 V3의 지면 세 값을 복원하고 강토색을 끈 상태이며, 수정은 더 어두운 V4 지면과 강토색을 함께 적용했습니다. 하늘·조명·노출은 같습니다.', 'V3 지면값 복원 · 강토색 없음', 'V4 · 농도와 강토색 적용'))
    for before, after, title in [('tint_before_eye_inn.png', 'eye_inn.png', '청림 · 옅은 목색'), ('tint_before_eye_oldcapital.png', 'eye_oldcapital.png', '현강 · 옅은 수색')]:
        items.append(pair(before, after, title, '같은 V4 지형·카메라·어두운 지면에서 강토색만 끄고 켰습니다. 색 경계가 자연스럽고 지형의 먹 농도를 해치지 않는지 살펴봅니다.', 'V4 · 강토색 없음', 'V4 · 강토색 적용'))
    return ''.join(items) or '<p class="pending">V4 색조·강토색 비교 이미지가 아직 제공되지 않았습니다.</p>'


def flow_section(data, world, out):
    if not data:
        return '<p class="pending">이번 버전의 실제 지면·교량 콜라이더 검사 결과가 아직 제공되지 않았습니다.</p>'
    # Unity audit hashes the authored .asset YAML, not the JSON review export.
    authored_sheet = ROOT / 'Oheangbu/Assets/_Project/Art/World/WorldMacro/WorldMacroSheet.asset'
    scene_path = field(data, 'scenePath')
    authored_scene = ROOT / 'Oheangbu' / str(scene_path) if scene_path else None
    sources = [('sheetSha256', authored_sheet), ('sceneFileSha256', authored_scene)]
    if any(field(data, key) and path and path.exists() and field(data, key) != hashlib.sha256(path.read_bytes()).hexdigest() for key, path in sources):
        return '<p class="pending">이동 흐름 검사와 현재 Unity 장면·경로 에셋의 버전이 다릅니다. 새 검사 전까지 이전 수치를 현재 판정으로 표시하지 않습니다.</p>'
    names = {field(site, 'Id'): label(site).replace(' 예약', '') for site in world.sites}
    routes = field(data, 'routes', []) or []
    samples = field(data, 'sampleCount', 0)
    high, warning = field(data, 'highCount', 0), field(data, 'warningCount', 0)
    overflow = field(data, 'queryBufferOverflows', 0)
    rows = []
    for route in sorted(routes, key=lambda r: (-int(field(r, 'highCount', 0)), -int(field(r, 'warningCount', 0)), str(field(r, 'id', '')))):
        start, end = field(route, 'from', ''), field(route, 'to', '')
        title = names.get(start, start) + ' → ' + names.get(end, end)
        rows.append(f'<tr><td>{esc(title)}<small>{esc(field(route, "id", ""))} · {"가도" if field(route, "carriage", False) else "도보"}</small></td><td>{field(route, "sampleCount", 0)}</td><td>{field(route, "highCount", 0)} / {field(route, "warningCount", 0)}</td><td>{float(field(route, "maxLongitudinalGradeDeg", 0)):.1f}° / {float(field(route, "maxCrossSlopeDeg", 0)):.1f}°</td><td>{float(field(route, "maxStepResidualM", 0)):.3f}m</td><td>{field(route, "missingGround", 0)} / {field(route, "capsuleSupportMissing", 0)}</td><td>{field(route, "upperBodyBlocked", 0)}</td></tr>')
    capsule = field(data, 'capsule', {})
    criteria = field(data, 'criteria', {})
    spacing = field(criteria, 'stationSpacingM', '미측정')
    baseline = read_json(out / 'Versions/v3_before_realm_tint/flow_audit.json', None)
    if baseline:
        baseline.pop('samples', None)
        summary = f'<div class="table-wrap"><table><thead><tr><th>시점</th><th>콜라이더 표본</th><th>HIGH 기록</th><th>WARN 기록</th><th>버퍼 초과</th></tr></thead><tbody><tr><td>V3 초기 진단</td><td>{field(baseline, "sampleCount", 0):,}</td><td>{field(baseline, "highCount", 0):,}</td><td>{field(baseline, "warningCount", 0):,}</td><td>{field(baseline, "queryBufferOverflows", 0)}</td></tr><tr><td>V4 최신 진단</td><td>{samples:,}</td><td>{high:,}</td><td>{warning:,}</td><td>{overflow}</td></tr></tbody></table></div>'
    else:
        summary = f'<div class="table-wrap"><table><thead><tr><th>콜라이더 검사 표본</th><th>HIGH 기록</th><th>WARN 기록</th><th>쿼리 버퍼 초과</th></tr></thead><tbody><tr><td>{samples:,}</td><td>{high:,}</td><td>{warning:,}</td><td>{overflow}</td></tr></tbody></table></div>'
    note = f'<p class="caption">약 {esc(spacing)}m 간격의 실제 지면·교량 콜라이더 검사입니다. HIGH는 방해 가능성이 큰 기록, WARN은 편안한 이동 기준을 벗어난 기록입니다. 같은 문제 구간이 여러 표본·코드로 기록될 수 있고, 경로 수정으로 전후 표본 수도 달라집니다. 고위험 기록이0이어도 전체 플레이어 완주·가마 주행·실제 사용자 입력 검사가 통과한 뜻은 아닙니다.</p>'
    details = '<details><summary>구간별 지지·경사·단차 확인</summary><div class="table-wrap"><table><thead><tr><th>경로</th><th>표본</th><th>HIGH / WARN</th><th>종경사 / 횡경사 최대</th><th>단차 잔차 최대</th><th>지면 / 발밑 지지 누락</th><th>상체 막힘</th></tr></thead><tbody>' + ''.join(rows) + '</tbody></table></div>'
    settings = {"capsule": capsule, "criteria": criteria, "limitations": field(data, 'limitations', [])}
    details += '<p class="caption">캡슐 치수와 검사 기준은 아래 실제 기록을 사용했습니다. 경사·단차의 상세 판단과 잔여 문제가 원문에 있습니다.</p><pre>' + esc(json.dumps(settings, ensure_ascii=False, indent=2)) + '</pre><a href="flow_audit.json">이동 흐름 검사 전체 기록 ↗</a></details>'
    probe = ''
    probe_data = read_json(out / 'flow_move_probe.json', None)
    if probe_data and any(field(probe_data, key) and field(data, key) and field(probe_data, key) != field(data, key) for key in ('sheetSha256', 'sceneFileSha256')):
        probe = '<p class="pending">짧은 구간 이동 진단은 이전 장면·경로의 결과이므로 현재 결과로 표시하지 않습니다.</p>'
        probe_data = None
    if probe_data:
        segments = field(probe_data, 'segments', []) or []
        reached = sum(field(segment, 'status') == 'REACHED_WINDOW_END' for segment in segments)
        support_missing = sum(field(segment, 'finalHasSupport', None) is False for segment in segments)
        support_unknown = sum(field(segment, 'finalHasSupport', None) is None for segment in segments)
        state_labels = {'REACHED_WINDOW_END': '짧은 구간 끝 도달', 'STALLED': '이동 정체', 'LEFT_ROUTE': '경로 이탈', 'FELL_BELOW_START': '시작점 아래로 추락', 'WINDOW_NOT_REACHED': '구간 끝 미도달', 'GROUND_MISSING': '지면 없음'}
        probe_rows = []
        for segment in segments:
            status = field(segment, 'status', 'UNVERIFIED')
            support = field(segment, 'finalHasSupport', None)
            grounded = field(segment, 'finalGrounded', None)
            support_text = '미확인' if support is None else '있음' if support else '없음'
            ground_text = '미확인' if grounded is None else '접지' if grounded else '비접지'
            probe_rows.append(f'<tr><td>{esc(field(segment, "routeId", ""))}<small>{esc(field(segment, "selectionCode", ""))} · {esc(field(segment, "direction", ""))}</small></td><td>{esc(state_labels.get(status, status))}<small>{esc(field(segment, "reason", ""))}</small></td><td>{float(field(segment, "targetDistanceM", 0)):.1f}m / {float(field(segment, "actualPlanarTravelM", 0)):.1f}m</td><td>{support_text} / {ground_text}</td></tr>')
        probe = f'<h3>짧은 구간 이동 진단</h3><p class="caption">선택한 {len(segments)}구간 중 {reached}구간 끝 도달, 최종 지지 없음 {support_missing}구간·미확인 {support_unknown}구간. Editor에서 수동 시간 간격으로 CharacterController.Move를 호출한 진단이며 실제 사용자 입력·전체 완주·차량 주행이 아닙니다.</p><details><summary>이동 진단의 도달·정체·지지 결과</summary><p class="caption">{esc(field(probe_data, "selection", ""))}</p><div class="table-wrap"><table><thead><tr><th>경로 / 선택 이유</th><th>결과</th><th>목표 / 실제 수평 이동량</th><th>마지막 지지 / 접지</th></tr></thead><tbody>{"".join(probe_rows)}</tbody></table></div><a href="flow_move_probe.json">이동 진단 전체 원문 ↗</a></details>'
    return summary + note + details + probe


def captures_html(out, captures):
    if isinstance(captures, dict):
        captures = field(captures, 'captures', field(captures, 'items', []))
    captures = captures or []
    groups = {"overview": [], "ground": [], "other": []}
    for capture in captures:
        name = field(capture, 'file', '')
        path = Path(name)
        if path.is_absolute():
            try:
                name = path.relative_to(out).as_posix()
            except ValueError:
                name = path.as_posix()
        if not name or not (out / name).exists():
            continue
        kind = str(field(capture, 'kind', '')).lower()
        group = 'overview' if any(x in kind for x in ['overview', 'aerial', '조감']) else 'ground' if any(x in kind for x in ['ground', 'play', 'eye', '지상']) else 'other'
        pos = field(capture, 'position', {})
        position = f'X {field(pos, "x", 0):.0f} · Y {field(pos, "y", 0):.0f} · Z {field(pos, "z", 0):.0f} m' if pos else ''
        groups[group].append(f'<figure class="capture"><a href="{esc(name)}" target="_blank"><img src="{esc(name)}" loading="lazy" alt="{esc(field(capture, "label", field(capture, "id", name)))}"></a><figcaption><strong>{esc(field(capture, "label", field(capture, "id", name)))}</strong><span>{position}</span></figcaption></figure>')
    if not any(groups.values()):
        return '<p class="pending">아직 제공된 촬영 결과가 없습니다. 아래 지도는 높이 데이터 시각화이며 Unity 렌더 스크린샷이 아닙니다.</p>'
    output = []
    for key, title in [('overview', '전체와 지역의 조감'), ('ground', '플레이 시점에서 읽히는 풍경'), ('other', '추가 검토 시점')]:
        if groups[key]:
            output.append(f'<h3>{title}</h3><div class="capture-grid">{"".join(groups[key])}</div>')
    return ''.join(output)


def build(out):
    sheet = read_json(out / 'sheet.json')
    grid = read_json(out / 'terrain_grid.json')
    if not sheet or not grid:
        raise SystemExit(f'Unity export required: {out / "sheet.json"}, {out / "terrain_grid.json"}. No review was generated.')
    terrain = Terrain(grid)
    world = Map(sheet, terrain)
    svg = world.svg()
    (out / 'world_map.svg').write_text(svg, encoding='utf-8')
    world.relief_image.save(out / 'terrain_relief.png')
    world.png(out / 'world_map.png')
    section_svg(world, out)
    captures = read_json(out / 'captures.json', [])
    measurements = read_json(out / 'measurements.json', None)
    returns = read_json(out / 'return_routes.json', None)
    flow = read_json(out / 'flow_audit.json', None)
    if flow:
        flow.pop('samples', None)
    site_json = json.dumps(world.sites, ensure_ascii=False).replace('</', '<\\/')
    if measurements is None:
        measurement_block = '<p class="pending">별도의 기술 검증 결과가 아직 제공되지 않았습니다.</p>'
    else:
        checks = field(measurements, 'checks', [])
        states = {}
        for check in checks:
            status = field(check, 'status', 'UNVERIFIED')
            states[status] = states.get(status, 0) + 1
        findings = [check for check in checks if field(check, 'status') != 'PASS']
        summary = ' · '.join(f'{esc(k)} {v}' for k, v in states.items())
        measurement_block = f'<p class="caption">내보낸 기술 검사: {summary}. 형상과 시야의 미술적 승인을 의미하지 않습니다.</p>'
        for check in findings:
            measurement_block += f'<p class="pending"><strong>{esc(field(check, "status"))} · {esc(field(check, "name"))}</strong><br>{esc(field(check, "detail", ""))}</p>'
        measurement_block += '<details><summary>실제 내보낸 측정·검사 원문</summary><pre>' + esc(json.dumps(measurements, ensure_ascii=False, indent=2)) + '</pre></details>'
    report_link = '<a href="REPORT.md">설계·검증 보고서 ↗</a>' if (out / 'REPORT.md').exists() else '<span>설계·검증 보고서 정리 중</span>'
    sky_link = ''
    if (out / 'RegionalSky/REVIEW.html').exists():
        sky_link = '<p class="notice"><a href="RegionalSky/REVIEW.html">새 검토: 지역 이동에 따라 달라지는 수묵 하늘 ↗</a><br>이 페이지의17장은 V4 지면색·이동 흐름 검토 당시 화면입니다. 이후 추가한 지역별 하늘은 별도8장으로 확인합니다.</p>'
    if (out / 'WaterSurface/REVIEW.html').exists():
        sky_link = '<p class="notice"><a href="WaterSurface/REVIEW.html">최신 검토: 물가·합류부·다리의 수면 ↗</a><br>아래 V4 지면 이미지에는 새 수면을 소급 적용하지 않았습니다. 수면 전후와 시간별 정지 이미지는 별도 페이지에서 확인합니다.</p>' + sky_link
    prior = out / 'Versions/v1_before_mountain_returns'
    if (out / 'Versions/v3_before_realm_tint/REVIEW.html').exists():
        report_link += '<a href="Versions/v3_before_realm_tint/REVIEW.html">V3 산지·이전 지면 검토본 ↗</a>'
    if (out / 'Versions/v2_before_route_relief/REVIEW.html').exists():
        report_link += '<a href="Versions/v2_before_route_relief/REVIEW.html">V2 산지·귀환길 검토본 ↗</a>'
    if (prior / 'REVIEW.html').exists():
        report_link += '<a href="Versions/v1_before_mountain_returns/REVIEW.html">V1 첫 전체 지형 검토본 ↗</a>'
    if returns is not None:
        report_link += '<a href="return_routes.json">황경 귀환 경로·산지 변화 측정 ↗</a>'
    outline_xz = [xz(p) for p in world.outline]
    area = abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(outline_xz, outline_xz[1:] + outline_xz[:1]))) * .5 / 1e6 if outline_xz else 0
    scene = (ROOT / 'Oheangbu/Assets/_Project/Scenes/World/W_WorldMacro_Blockout.unity').as_posix()
    page = '''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>오행부 · 산과 물이 먼저인 세계</title>
<style>
:root{--ink:#303c33;--muted:#737c70;--paper:#f0eee6;--line:#d3d7c8;--brown:#886342}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.75 system-ui,"Malgun Gothic",sans-serif}a{color:#506e60;text-underline-offset:4px}main{max-width:1480px;padding:58px 48px 80px;margin:auto}header{display:grid;grid-template-columns:1.5fr 1fr;gap:50px;align-items:end;border-bottom:1px solid var(--line);padding-bottom:35px}.eyebrow{font-size:11px;letter-spacing:3px;color:var(--muted);margin:0 0 12px}h1{font-weight:500;letter-spacing:-2px;font-size:clamp(32px,4vw,54px);line-height:1.22;margin:0 0 18px}h2{font-size:27px;font-weight:500;letter-spacing:-.7px;margin:0}h3{font-size:19px;font-weight:500;margin:32px 0 15px}p{margin:9px 0 0}.intro{color:#596454;max-width:660px}.notice{padding:17px 20px;background:#e5e5d8;border-left:3px solid #7a886c;font-size:13px}.number{font-family:Georgia,serif;font-size:16px;color:#95a18b;margin-right:10px}.heading{display:flex;justify-content:space-between;align-items:baseline;gap:20px;margin:46px 0 20px}.heading>p{font-size:12px;color:var(--muted);text-align:right}.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:0;border-bottom:1px solid var(--line);padding:22px 0}.stat{padding:0 23px;border-left:1px solid var(--line)}.stat:first-child{padding-left:0;border:0}.stat strong{font-size:23px;font-weight:500}.stat small{display:block;font-size:11px;color:var(--muted);margin-top:3px}.map-layout{display:grid;grid-template-columns:minmax(0,1fr) 260px;border:1px solid var(--line);background:#e8e5d9}.canvas{height:780px;overflow:hidden;position:relative;touch-action:none}.canvas>svg{width:100%;height:100%;display:block;cursor:grab}.canvas>svg:active{cursor:grabbing}.map-tools{position:absolute;bottom:18px;left:18px;display:flex;gap:4px}.map-tools button{border:1px solid #b7c1ae;background:#f5f2e9d9;color:#3e5041;padding:7px 11px;cursor:pointer;font-size:13px}.map-side{border-left:1px solid var(--line);background:#ecece1;padding:23px}.map-side h3{font-size:13px;letter-spacing:1px;margin:0 0 15px}.layers{display:grid;gap:10px}.layers label{display:flex;align-items:center;gap:10px;cursor:pointer;font-size:13px}.layers input{accent-color:#65785c}.dot{width:15px;height:3px;display:inline-block}.site-card{margin-top:27px;border-top:1px solid #cad0bf;padding-top:20px;min-height:155px}.site-card strong{font-weight:500;font-size:18px}.site-card p{font-size:12px;color:#61705e}.map-meta{border-top:1px solid #cad0bf;padding-top:19px;font-size:11px;color:#74806c}.map-side a{display:block;font-size:12px;margin-top:10px}.caption{font-size:12px;color:var(--muted);margin:12px 0}.capture-grid{display:grid;grid-template-columns:1fr 1fr;gap:26px 22px}.capture{margin:0}.capture img{width:100%;aspect-ratio:16/9;object-fit:contain;background:#d2d5c9;display:block}.capture figcaption{padding-top:11px;display:flex;gap:12px;justify-content:space-between}.capture strong{font-size:13px;font-weight:500}.capture span{font-size:10px;color:var(--muted)}.section-img{width:100%;border:1px solid var(--line);display:block}.return-select{font:inherit;border:0;padding:0;background:transparent;color:#526e61;cursor:pointer;text-decoration:underline;text-underline-offset:4px}.return-detail td{padding-top:0;white-space:normal;color:#64725f;font-size:11px}.table-wrap{overflow:auto}table{width:100%;border-collapse:collapse;font-size:13px}th{text-align:left;font-size:11px;color:var(--muted);font-weight:400;border-bottom:1px solid var(--line);padding:10px}td{border-bottom:1px solid #dce0d1;padding:13px 10px;white-space:nowrap}td small{display:block;font-size:10px;color:var(--muted)}.pending{padding:22px;background:#e6e7db;font-size:13px}.two-col{display:grid;grid-template-columns:1fr 1fr;gap:34px}.two-col p{font-size:13px;color:#62705d}.two-col h3{margin-top:10px}details{margin-top:20px;border:1px solid var(--line);padding:15px 20px}summary{cursor:pointer;font-size:13px}pre{font:11px/1.7 Consolas,monospace;overflow:auto;max-height:470px;white-space:pre-wrap}footer{border-top:1px solid var(--line);margin-top:45px;padding-top:21px;display:flex;gap:25px;flex-wrap:wrap;font-size:12px;color:var(--muted)}@media(max-width:1000px){main{padding:30px 22px}.map-layout{grid-template-columns:1fr}.map-side{border-left:0;border-top:1px solid var(--line)}.layers{grid-template-columns:repeat(3,1fr)}.site-card{min-height:0}.canvas{height:720px}.map-meta{margin-top:15px}header{grid-template-columns:1fr;gap:20px}}@media(max-width:620px){.capture-grid,.two-col{grid-template-columns:1fr}.stats{grid-template-columns:1fr 1fr;gap:18px}.stat:nth-child(3){border:0;padding-left:0}.heading{display:block}.heading>p{text-align:left}.canvas{height:610px}.capture figcaption{display:block}.capture span{display:block}}
</style></head><body><main><header><div><p class="eyebrow">OHEANGBU / WORLD MACRO STUDY</p><h1>산과 물이 먼저인 세계</h1><p class="intro">땅과 산을 더 어둡게 하고, 강토의 오행색을 지면에 옅게 더했습니다. 실제 길의 지지·경사·교량 접근을 확인하면서 색과 이동 흐름을 함께 다듬습니다.</p></div><div class="notice">거친 3D 지형·배치 검토용입니다. 도시·숲·랜드마크는 대체 형상이며, 실제 가마·상세 콘텐츠·전투 검증을 포함하지 않습니다. 영상은 제작하지 않습니다.</div></header>__SKY_LINK__
<div class="stats"><div class="stat"><strong>__AREA__ km²</strong><small>불규칙한 저작 외곽의 평면 면적 · 전체가 플레이 콘텐츠는 아님</small></div><div class="stat"><strong>__SIZE__</strong><small>실제 데이터의 동서 × 남북 범위</small></div><div class="stat"><strong>__HEIGHT__ m</strong><small>내보낸 지형 표본의 최저–최고 고도</small></div><div class="stat"><strong>14 / 4.5 m/s</strong><small>가마 / 도보 검토용 속도 · 게임 규칙 미확정</small></div></div>
<section><div class="heading"><h2><span class="number">01</span>어두운 땅에 남는 강토색</h2><p>V3→V4 · 동일 V4 카메라 비교</p></div>__COMPARISONS__</section><section><div class="heading"><h2><span class="number">02</span>길의 이동 흐름</h2><p>실제 콜라이더의 표본 검사 · 완주와 구분</p></div>__FLOW__</section><section><div class="heading"><h2><span class="number">03</span>산계에서 길까지</h2><p>드래그로 이동 · 휠로 확대 · 거점을 누르면 위치 확인</p></div><div class="map-layout"><div class="canvas">__MAP__<div class="map-tools"><button id="zoom-in" aria-label="확대">＋</button><button id="zoom-out" aria-label="축소">－</button><button id="reset-map">전체 보기</button></div></div><aside class="map-side"><h3>지도 레이어</h3><div class="layers"><label><input type="checkbox" data-layer="relief" checked>지형 음영</label><label><input type="checkbox" data-layer="rivers" checked><i class="dot" style="background:#688c95"></i>물길</label><label><input type="checkbox" data-layer="regions" checked>강토 범위</label><label><input type="checkbox" data-layer="roads" checked><i class="dot" style="background:#795a3c"></i>가도·도보</label><label><input type="checkbox" data-layer="returns" checked><i class="dot" style="background:#805b75"></i>귀환 도보길</label><label><input type="checkbox" data-layer="sites" checked>도시·거점</label><label><input type="checkbox" data-layer="ridges">주능선 저작선</label></div><div class="site-card" id="site-card"><strong>자연 지형을 우선합니다</strong><p>갈색 실선은 가도, 녹색 점선은 도보 길, 자주색은 귀환용 도보길입니다. 강토 영역은 미술·서사의 대략적 구분이며 물리적인 경계벽이 아닙니다.</p></div><div class="map-meta">지도 음영은 실제 높이 표본을 사용하며 지면 색조를 재현한 렌더가 아닙니다. 실제 강토색은 위 스크린샷에서 확인합니다. 지도 기호의 선 폭은 가독성을 위한 것이며 실제 도로·하천 폭과 다릅니다.</div><a href="world_map.svg" target="_blank">전체 지도 SVG ↗</a><a href="world_map.png" target="_blank">전체 지도 PNG ↗</a></aside></div><p class="caption">불규칙한 외곽이 저작 범위이며, 외곽선 전체를 통과 불가능한 산벽으로 간주하지 않습니다. 직사각형 청크는 데이터 처리 단위입니다.</p></section>
<details><summary>이전 산지 검토 기록</summary><p class="caption">V2→V3의 상대고도 비율은 당시 경로의 자료입니다. 이번 콜라이더 흐름 검사와 새 경로의 가시성 판정으로 사용하지 않습니다. <a href="Versions/v3_before_realm_tint/REVIEW.html">V3 검토본 ↗</a></p></details><section><div class="heading"><h2><span class="number">04</span>황경으로 되돌아오는 길</h2><p>귀환 연결의 거리 기록 · 실제 완주 기록 아님</p></div>__RETURNS__</section><section><div class="heading"><h2><span class="number">05</span>높이와 공간의 관계</h2><p>같은 높이 표본을 사용한 직선 단면 · 수직축 확대</p></div><img class="section-img" src="sections.svg" alt="주요 지역 사이 지형 단면도"><p class="caption">수평축은 각 단면의 직선 거리, 수직축은 공통 고도입니다. 길을 따라 이동하는 단면이 아니며 표본 간격보다 작은 지형은 표현하지 않습니다.</p></section>
<section><div class="heading"><h2><span class="number">06</span>조감도와 플레이 구도</h2><p>정지 이미지 검토 · 영상 없음</p></div><p class="caption">조감도는 산세의 가독성을 위해 매크로 재질 사본의 거리 씻김을 일시적으로0.10으로 낮춰 촬영했습니다. 지상 시점은 기본0.58이며 촬영 후 복원합니다. 촬영용 거리 씻김을 복원하며 C2 원본은 보존했습니다.</p>__CAPTURES__</section>
<section><div class="heading"><h2><span class="number">07</span>이동 연결의 측정</h2><p>경로 표본의 기하 계산 · 실제 주행·보행 실측과 구분</p></div><div class="table-wrap"><table><thead><tr><th>경로</th><th>이동 분류</th><th>3D 길이</th><th>저작 폭</th><th>경사 최대 / 95%</th><th>교량 접근 최대</th><th>최소 곡률 반경</th><th>일정 속도 환산</th></tr></thead><tbody>__ROUTES__</tbody></table></div><p class="caption">가도 14m/s, 도보 4.5m/s의 일정 속도 환산입니다. 가감속·회전·정차를 포함하지 않습니다. 경사는 인접 경로 표본으로 계산합니다. 경사 최대/95%는 교량 예약점 반경150m 표본을 제외하며 그 표본의 최대 경사는 교량 접근 열에 따로 표시합니다. 충돌·접지 통과를 의미하지 않습니다.</p>__MEASUREMENTS__</section>
<section><div class="heading"><h2><span class="number">08</span>이번 검토에서 볼 것</h2></div><div class="two-col"><div><h3>어두운 지면에서도 길이 읽히는가</h3><p>산과 땅의 밝기가 충분히 낮아졌는지, 옅은 강토색이 지형의 농도와 길의 윤곽을 해치지 않는지 살펴봅니다. 지역 사이에 인위적인 색 경계가 보이지 않는지도 확인합니다.</p></div><div><h3>이동을 끊는 지형이 남았는가</h3><p>검사에서 드러난 급경사·단차·교량 접근과 경로의 연결을 확인합니다. 표본 지지 통과와 실제 캐릭터·가마의 연속 이동은 별도 단계로 판단합니다.</p></div></div></section>
<h3>Unity 검토 카메라</h3><p class="caption">Play Mode에서 1 자유 비행 · 2 지면 눈높이 · 3 선택 경로 이동 · [ / ] 경로 선택 · 오른쪽 마우스 드래그 시선 회전 · WASD 이동. 가도14m/s와 도보4.5m/s는 검토 카메라 속도이며 실제 플레이어·가마 물리가 아닙니다. <a href="motion_probe.json">V1 카메라 이동 기록 ↗</a> · 실제 사용자 입력 완주·가마 주행은 검증하지 않았습니다. Editor 이동 진단은 위 흐름 검사에서 구분합니다.</p><footer>__REPORT__<a href="__SCENE__">Unity 제작 씬 ↗</a><a href="sheet.json">공통 저작 데이터 ↗</a><a href="section_samples.json">단면 표본 ↗</a><span>지형 검토 후 폐광·금표 주막과 상세 구간 위치 확정</span></footer></main>
<script>
const sites=__SITES__;const svg=document.querySelector('#world-map'),base=svg.dataset.base.split(' ').map(Number);let box=base.slice(),drag=null;function draw(){svg.setAttribute('viewBox',box.join(' '))}function zoom(f,cx=.5,cy=.5){const nw=box[2]*f,nh=box[3]*f;if(nw<base[2]/12||nw>base[2]*1.2)return;box=[box[0]+(box[2]-nw)*cx,box[1]+(box[3]-nh)*cy,nw,nh];draw()}document.querySelectorAll('[data-layer]').forEach(el=>el.addEventListener('change',()=>document.querySelector('#layer-'+el.dataset.layer).style.display=el.checked?'':'none'));document.querySelector('#zoom-in').onclick=()=>zoom(.8);document.querySelector('#zoom-out').onclick=()=>zoom(1.25);document.querySelector('#reset-map').onclick=()=>{box=base.slice();draw()};svg.addEventListener('wheel',e=>{e.preventDefault();const r=svg.getBoundingClientRect();zoom(e.deltaY>0?1.12:.89,(e.clientX-r.left)/r.width,(e.clientY-r.top)/r.height)},{passive:false});svg.addEventListener('pointerdown',e=>{if(e.target.closest('.site'))return;svg.setPointerCapture(e.pointerId);drag={x:e.clientX,y:e.clientY,b:box.slice()}});svg.addEventListener('pointermove',e=>{if(!drag)return;const r=svg.getBoundingClientRect();box[0]=drag.b[0]-(e.clientX-drag.x)/r.width*drag.b[2];box[1]=drag.b[1]-(e.clientY-drag.y)/r.height*drag.b[3];draw()});svg.addEventListener('pointerup',()=>drag=null);svg.addEventListener('pointercancel',()=>drag=null);function f(o,k){const key=Object.keys(o).find(x=>x.toLowerCase()===k.toLowerCase());return key===undefined?undefined:o[key]}function showSite(el){const s=sites[+el.dataset.site],p=f(s,'Position')||{},card=document.querySelector('#site-card');card.replaceChildren();const title=document.createElement('strong'),info=document.createElement('p');title.textContent=f(s,'Label')||f(s,'Id');info.textContent=`${f(s,'Kind')||'예약 거점'} · X ${(+p.x||0).toFixed(0)} / Z ${(+p.z||0).toFixed(0)} m · 고도 ${(+p.y||0).toFixed(0)} m`;card.append(title,info)}svg.querySelectorAll('.site').forEach(el=>{el.addEventListener('click',()=>showSite(el));el.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();showSite(el)}})});
document.querySelectorAll('[data-return]').forEach(button=>button.addEventListener('click',()=>{const ids=JSON.parse(button.dataset.return);svg.querySelectorAll('.route-line').forEach(path=>path.classList.toggle('return-selected',ids.includes(path.dataset.route)));for(const name of ['roads','returns']){document.querySelector('#layer-'+name).style.display='';document.querySelector('[data-layer="'+name+'"]').checked=true}box=base.slice();draw();svg.scrollIntoView({behavior:'smooth',block:'center'})}));
</script></body></html>'''
    substitutions = {
        '__AREA__': f'{area:.1f}',
        '__SIZE__': f'{(world.x1 - world.x0) / 1000:.1f} × {(world.z1 - world.z0) / 1000:.1f} km',
        '__HEIGHT__': f'{terrain.min_h:.0f}–{terrain.max_h:.0f}',
        '__MAP__': svg,
        '__CAPTURES__': captures_html(out, captures),
        '__ROUTES__': route_table(world, measurements),
        '__RETURNS__': return_section(world, returns) if str(field(returns, 'baselineVersion', '')).startswith('v3_') else '<p class="caption">V4의 귀환 거리 비교는 아직 재계산되지 않았습니다. 이전 값은 <a href="Versions/v3_before_realm_tint/REVIEW.html">V3 보존 기록</a>에서 확인할 수 있습니다.</p>',
        '__FLOW__': flow_section(flow, world, out),
        '__COMPARISONS__': revision_images(out),
        '__MEASUREMENTS__': measurement_block,
        '__REPORT__': report_link,
        '__SKY_LINK__': sky_link,
        '__SCENE__': esc(scene),
        '__SITES__': site_json,
    }
    for key, value in substitutions.items():
        page = page.replace(key, value)
    (out / 'REVIEW.html').write_text(page, encoding='utf-8')
    print(json.dumps({"review": str(out / 'REVIEW.html'), "sites": len(world.sites), "routes": len(world.routes), "outlineAreaKm2": round(area, 3), "heightGridStepM": terrain.step, "hasMeasurements": measurements is not None}, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=ROOT / 'Art/World/WorldMacro')
    args = parser.parse_args()
    build(args.out.resolve())
