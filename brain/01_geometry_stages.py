import json
import os
import math

GAMMA = 25.0

MONKEY_DIMS = {
    "PILAR-70x70": {"b": 0.70, "h": 0.70},
    "P-70x70":     {"b": 0.70, "h": 0.70},
    "V-60x80":     {"b": 0.60, "h": 0.80},
    "V-45x30":     {"b": 0.45, "h": 0.30},
    "V-40x80":     {"b": 0.40, "h": 0.80},
    "M-20":        {"t": 0.20},
}

BUILDS = [
    ("brain/edificio1_config.json",   100_000),
    ("brain/edificio_config_real.json", 200_000),
]


def load_config(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def build_one(cfg, offset):
    xs = [(g["eje"], g["x"]) for g in cfg["grilla_X"]]
    ys = [(g["eje"], g["y"]) for g in cfg["grilla_Y"]]
    nx, ny = len(xs), len(ys)
    levels = cfg["niveles"]
    elev = {lv["id"]: lv["elevation"] for lv in levels}
    phase_of = {lv["id"]: lv.get("phase", 1) for lv in levels}
    slab_lvls = cfg["niveles_losa"]

    col_crosses = cfg["columnas_cruces"]
    if col_crosses == "all":
        col_set = {(ix, iy) for ix in range(nx) for iy in range(ny)}
    else:
        col_set = set()
        for ex, ey in col_crosses:
            ix = next(i for i, (e, _) in enumerate(xs) if e == ex)
            iy = next(i for i, (e, _) in enumerate(ys) if e == ey)
            col_set.add((ix, iy))

    def nid(li, iy, ix):
        return str(offset + li * 100 + iy * 10 + ix)

    nodes = {}
    for li, lv in enumerate(levels):
        lid = lv["id"]
        for iy in range(ny):
            for ix in range(nx):
                if li == 0 and (ix, iy) not in col_set:
                    continue
                k = nid(li, iy, ix)
                nodes[k] = {
                    "x": xs[ix][1], "y": ys[iy][1], "z": elev[lid],
                    "floor": li, "phase": phase_of[lid],
                    "fix": [1, 1, 1, 1, 1, 1] if li == 0 else [0, 0, 0, 0, 0, 0],
                }

    elements = {}
    eid_counter = offset
    n_col = n_viga = n_muro = 0

    x_cm = (xs[0][1] + xs[-1][1]) / 2.0
    y_cm = (ys[0][1] + ys[-1][1]) / 2.0
    best = min(col_set, key=lambda c: ((xs[c[0]][1] - x_cm) ** 2
                                       + (ys[c[1]][1] - y_cm) ** 2))
    mi, mj = best

    def add_elem(kind, i, j, seccion, lvl, orient, cad):
        nonlocal eid_counter, n_col, n_viga, n_muro
        eid_counter += 1
        p1 = nodes[i]; p2 = nodes[j]
        L = math.hypot(p2["x"] - p1["x"], p2["y"] - p1["y"], p2["z"] - p1["z"])
        elements[str(eid_counter)] = {
            "type": kind, "nodes": [i, j], "sectionTag": seccion,
            "cad_id": cad, "phase": phase_of[lvl],
            "length": round(L, 4), "orient": orient,
            "lvl": lvl, "trib_area": 0.0, "w_G": 0.0, "w_Q": 0.0,
        }
        if kind == "column": n_col += 1
        elif kind == "beam": n_viga += 1
        else: n_muro += 1

    for k in range(len(levels) - 1):
        l_up = levels[k + 1]["id"]
        for (ix, iy) in sorted(col_set):
            add_elem("column", nid(k, iy, ix),
                     nid(k + 1, iy, ix),
                     cfg["seccion_columna"], l_up, "Z",
                     f"COL-{xs[ix][0]}-{ys[iy][0]}-{l_up}")

    for lvl in slab_lvls:
        li = next(i for i, lv in enumerate(levels) if lv["id"] == lvl)
        for iy in range(ny):
            for ix in range(nx - 1):
                add_elem("beam", nid(li, iy, ix), nid(li, iy, ix + 1),
                         cfg["seccion_viga"], lvl, "X",
                         f"V-{ys[iy][0]}-{xs[ix][0]}{xs[ix + 1][0]}-{lvl}")
        for ix in range(nx):
            for iy in range(ny - 1):
                add_elem("beam", nid(li, iy, ix), nid(li, iy + 1, ix),
                         cfg["seccion_viga"], lvl, "Y",
                         f"V-{xs[ix][0]}-{ys[iy][0]}{ys[iy + 1][0]}-{lvl}")

    for run in cfg["muros_ejes"]:
        if run["direccion"] == "Y":
            ix = next(i for i, (e, _) in enumerate(xs) if e == run["eje"])
            for lvl in slab_lvls:
                li = next(i for i, lv in enumerate(levels) if lv["id"] == lvl)
                for iy in range(ny - 1):
                    add_elem("wall", nid(li, iy, ix), nid(li, iy + 1, ix),
                             cfg["seccion_muro"], lvl, "Y",
                             f"MURO-{run['eje']}-{lvl}")
        else:
            iy = next(i for i, (e, _) in enumerate(ys) if e == run["eje"])
            for lvl in slab_lvls:
                li = next(i for i, lv in enumerate(levels) if lv["id"] == lvl)
                for ix in range(nx - 1):
                    add_elem("wall", nid(li, iy, ix), nid(li, iy, ix + 1),
                             cfg["seccion_muro"], lvl, "X",
                             f"MURO-{run['eje']}-{lvl}")

    qG = {lv["id"]: lv["qG"] for lv in levels if lv["id"] in slab_lvls}
    qQ = {lv["id"]: lv["qQ"] for lv in levels if lv["id"] in slab_lvls}
    xv = [x for (_, x) in xs]
    yv = [y for (_, y) in ys]
    dx = [xv[i + 1] - xv[i] for i in range(nx - 1)]
    dy = [yv[j + 1] - yv[j] for j in range(ny - 1)]

    def beam_id(tipo, i, j, lvl):
        for eid, ed in elements.items():
            if ed["type"] != "beam" or ed["lvl"] != lvl or ed["orient"] != tipo:
                continue
            n1n = ed["nodes"]
            x1 = nodes[n1n[0]]; x2 = nodes[n1n[1]]
            if tipo == "X":
                if abs(x1["y"] - yv[j]) < 1e-9 and abs(x2["y"] - yv[j]) < 1e-9 \
                        and min(x1["x"], x2["x"]) == xv[i]:
                    return eid
            else:
                if abs(x1["x"] - xv[i]) < 1e-9 and abs(x2["x"] - xv[i]) < 1e-9 \
                        and min(x1["y"], x2["y"]) == yv[j]:
                    return eid
        return None

    trib_area = {}
    area_losa_lvl = {}
    for lvl in slab_lvls:
        a_lvl = sum(dx) * sum(dy)
        area_losa_lvl[lvl] = round(a_lvl, 6)
        for i in range(nx - 1):
            for j in range(ny - 1):
                panel = dx[i] * dy[j]
                triang = panel / 4.0
                edges = [
                    beam_id("X", i, j, lvl),
                    beam_id("X", i, j + 1, lvl),
                    beam_id("Y", i, j, lvl),
                    beam_id("Y", i + 1, j, lvl),
                ]
                for eid_f in edges:
                    if eid_f is None:
                        continue
                    trib_area[eid_f] = trib_area.get(eid_f, 0.0) + triang

    for eid_f, area in trib_area.items():
        ed = elements[eid_f]
        ed["trib_area"] = round(area, 6)
        L = ed["length"]
        qGv = qG[ed["lvl"]]; qQv = qQ[ed["lvl"]]
        if L > 1e-9:
            ed["w_G"] = round((qGv * area) / L, 6) if qGv else 0.0
            ed["w_Q"] = round((qQv * area) / L, 6) if qQv else 0.0

    secc_area = {
        "PILAR-70x70": 0.49, "P-70x70": 0.49,
        "V-60x80": 0.48, "V-45x30": 0.135,
        "V-40x80": 0.32, "M-20": cfg["muros_e"],
    }
    peso_piso = {lv["id"]: 0.0 for lv in levels}
    for ed in elements.values():
        L = ed["length"]
        a_sec = secc_area.get(ed["sectionTag"], 0.0)
        peso_piso[ed["lvl"]] += GAMMA * a_sec * L
    peso_piso = {k: round(v, 2) for k, v in peso_piso.items()}
    area_losa_total = sum(area_losa_lvl.values())
    carga_losa_G = sum(qG[l] * area_losa_lvl[l] for l in slab_lvls if qG[l])
    carga_losa_Q = sum(qQ[l] * area_losa_lvl[l] for l in slab_lvls if qQ[l])

    masters = {
        lv["id"]: nid(li, mj, mi)
        for li, lv in enumerate(levels)
        if lv["id"] in slab_lvls
    }

    resumen = {
        "nodos": len(nodes), "columnas": n_col, "vigas": n_viga,
        "muros": n_muro, "losa_area_m2": round(area_losa_total, 2),
        "carga_losa_G_kN": round(carga_losa_G, 2),
        "carga_losa_Q_kN": round(carga_losa_Q, 2),
        "peso_propio_kN": round(sum(peso_piso.values()), 2),
    }

    return {
        "nodes": nodes, "elements": elements, "masters": masters,
        "area_losa_lvl": area_losa_lvl, "peso_piso": peso_piso,
        "resumen": resumen,
    }


def build_all():
    print("=== GEOMETRIA REAL ED1 + ED2 (PASO 2) ===")
    buildings = {}
    configs_used = []
    for cfg_path, offset in BUILDS:
        cfg = load_config(cfg_path)
        bid = cfg["id"]
        cfgs_short = os.path.basename(cfg_path)
        configs_used.append(cfg_path)
        bld = build_one(cfg, offset)
        bld["config"] = cfg_path
        buildings[bid] = bld
        r = bld["resumen"]
        print(f"  {bid} ({cfg['nombre']}): "
              f"{r['nodos']} nodos, {r['columnas']} col, {r['vigas']} vigas, "
              f"{r['muros']} muros, losa={r['losa_area_m2']} m2")

    all_n = sum(b["resumen"]["nodos"] for b in buildings.values())
    all_c = sum(b["resumen"]["columnas"] for b in buildings.values())
    all_v = sum(b["resumen"]["vigas"] for b in buildings.values())
    all_m = sum(b["resumen"]["muros"] for b in buildings.values())
    print(f"\n[QA] Total ED1+ED2: {all_n} nodos, {all_c} col, {all_v} vigas, {all_m} muros")

    out = {
        "schema": "geometry_stages/3.0",
        "configs": configs_used,
        "buildings": buildings,
    }
    path = "brain/geometry_stages.json"
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)
    print(f"[OK] Exportado a: {path}")


if __name__ == "__main__":
    build_all()
