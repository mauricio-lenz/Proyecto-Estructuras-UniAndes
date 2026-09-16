import json
import os
import math

GAMMA = 25.0  # kN/m3

MONKEY_DIMS = {
    "PILAR-70x70": {"b": 0.70, "h": 0.70},
    "V-60x80": {"b": 0.60, "h": 0.80},
    "V-45x30": {"b": 0.45, "h": 0.30},
    "V-40x80": {"b": 0.40, "h": 0.80},
    "M-20": {"t": 0.20},
}


def load_config(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def build_real_geometry(cfg_path="brain/edificio_config_real.json"):
    print("=== PROCESANDO GEOMETRÍA REAL (PASO 2) ===")
    cfg = load_config(cfg_path)
    xs = [(g["eje"], g["x"]) for g in cfg["grilla_X"]]
    ys = [(g["eje"], g["y"]) for g in cfg["grilla_Y"]]
    nx, ny = len(xs), len(ys)
    levels = cfg["niveles"]
    elev = {lv["id"]: lv["elevation"] for lv in levels}
    phase_of = {lv["id"]: lv["phase"] for lv in levels}
    slab_lvls = cfg["niveles_losa"]

    col_crosses = cfg["columnas_cruces"]
    col_set = set()
    for ex, ey in col_crosses:
        ix = next(i for i, (e, _) in enumerate(xs) if e == ex)
        iy = next(i for i, (e, _) in enumerate(ys) if e == ey)
        col_set.add((ix, iy))

    # ---------- NODOS ----------
    nodes = {}
    for li, lv in enumerate(levels):
        lid = lv["id"]
        for iy in range(ny):
            for ix in range(nx):
                if li == 0 and (ix, iy) not in col_set:
                    continue  # base solo en cruces de columna
                nid = str(li * 100 + iy * 10 + ix)
                nodes[nid] = {
                    "x": xs[ix][1], "y": ys[iy][1], "z": elev[lid],
                    "floor": li, "phase": phase_of[lid],
                    "fix": [1, 1, 1, 1, 1, 1] if li == 0 else [0, 0, 0, 0, 0, 0],
                }

    # ---------- ELEMENTOS ----------
    elements = {}
    eid = 0
    n_col = n_viga = n_muro = 0

    # Master de diafragma = cruce de columna real mas cercano al CM de losa
    x_cm = (min(x for _, x in xs) + max(x for _, x in xs)) / 2.0
    y_cm = (min(y for _, y in ys) + max(y for _, y in ys)) / 2.0
    best = min(col_set, key=lambda c: ((xs[c[0]][1] - x_cm) ** 2
                                       + (ys[c[1]][1] - y_cm) ** 2))
    mi, mj = best

    def add_elem(kind, i, j, seccion, lvl, orient, cad):
        nonlocal eid, n_col, n_viga, n_muro
        eid += 1
        p1 = nodes[i]; p2 = nodes[j]
        L = math.hypot(p2["x"] - p1["x"], p2["y"] - p1["y"], p2["z"] - p1["z"])
        elements[str(eid)] = {
            "type": kind, "nodes": [i, j], "sectionTag": seccion,
            "cad_id": cad, "phase": phase_of[lvl],
            "length": round(L, 4), "orient": orient,
            "lvl": lvl, "trib_area": 0.0, "w_G": 0.0, "w_Q": 0.0,
        }
        if kind == "column": n_col += 1
        elif kind == "beam": n_viga += 1
        else: n_muro += 1

    # Columnas: pares de niveles consecutivos en cruces
    for k in range(len(levels) - 1):
        l_up = levels[k + 1]["id"]
        for (ix, iy) in sorted(col_set):
            add_elem("column", str(k * 100 + iy * 10 + ix),
                     str((k + 1) * 100 + iy * 10 + ix),
                     cfg["seccion_columna"], l_up, "Z",
                     f"COL-{xs[ix][0]}-{ys[iy][0]}-{l_up}")

    # Vigas: retícula completa en cada nivel de losa
    for lvl in slab_lvls:
        li = next(i for i, lv in enumerate(levels) if lv["id"] == lvl)
        for iy in range(ny):
            for ix in range(nx - 1):
                add_elem("beam", str(li * 100 + iy * 10 + ix),
                         str(li * 100 + iy * 10 + ix + 1),
                         cfg["seccion_viga"], lvl, "X",
                         f"V-{ys[iy][0]}-{xs[ix][0]}{xs[ix + 1][0]}-{lvl}")
        for ix in range(nx):
            for iy in range(ny - 1):
                add_elem("beam", str(li * 100 + iy * 10 + ix),
                         str(li * 100 + (iy + 1) * 10 + ix),
                         cfg["seccion_viga"], lvl, "Y",
                         f"V-{xs[ix][0]}-{ys[iy][0]}{ys[iy + 1][0]}-{lvl}")

    # Muros: recorridos sobre ejes del núcleo
    for run in cfg["muros_ejes"]:
        if run["direccion"] == "Y":
            ix = next(i for i, (e, _) in enumerate(xs) if e == run["eje"])
            for lvl in slab_lvls:
                li = next(i for i, lv in enumerate(levels) if lv["id"] == lvl)
                for iy in range(ny - 1):
                    add_elem("wall", str(li * 100 + iy * 10 + ix),
                             str(li * 100 + (iy + 1) * 10 + ix),
                             cfg["seccion_muro"], lvl, "Y",
                             f"MURO-{run['eje']}-{lvl}")
        else:
            iy = next(i for i, (e, _) in enumerate(ys) if e == run["eje"])
            for lvl in slab_lvls:
                li = next(i for i, lv in enumerate(levels) if lv["id"] == lvl)
                for ix in range(nx - 1):
                    add_elem("wall", str(li * 100 + iy * 10 + ix),
                             str(li * 100 + iy * 10 + ix + 1),
                             cfg["seccion_muro"], lvl, "X",
                             f"MURO-{run['eje']}-{lvl}")

    # ---------- ÁREAS TRIBUTARIAS Y CARGAS (igual que P1L1/P1L3) ----------
    qG = {lv["id"]: lv["qG"] for lv in levels if lv["id"] in slab_lvls}
    qQ = {lv["id"]: lv["qQ"] for lv in levels if lv["id"] in slab_lvls}
    xv = [x for (_, x) in xs]
    yv = [y for (_, y) in ys]
    dx = [xv[i + 1] - xv[i] for i in range(nx - 1)]
    dy = [yv[j + 1] - yv[j] for j in range(ny - 1)]

    # identificar vigas por (tipo, i, j, lvl); X(i,j)=seg en xv[i]..xv[i+1] a yv[j],
    # Y(i,j)=seg en yv[j]..yv[j+1] en xv[i]
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

    # ---------- PESO PROPIO POR PISO (para masa sísmica) ----------
    secc_area = {
        "PILAR-70x70": 0.49, "V-60x80": 0.48, "V-45x30": 0.135,
        "V-40x80": 0.32, "M-20": cfg["muros_e"],
    }
    peso_piso = {lv["id"]: 0.0 for lv in levels}
    for ed in elements.values():
        L = ed["length"]
        a_sec = secc_area.get(ed["sectionTag"], 0.0)
        peso_piso[ed["lvl"]] += GAMMA * a_sec * L
    peso_piso = {k: round(v, 2) for k, v in peso_piso.items()}
    area_losa_total = sum(area_losa_lvl.values())
    carga_losa_G = sum(qG[l] * area_losa_lvl[l] for l in slab_lvls)
    carga_losa_Q = sum(qQ[l] * area_losa_lvl[l] for l in slab_lvls)

    out = {
        "schema": "geometry_stages/2.0",
        "config": cfg_path,
        "nodes": nodes,
        "elements": elements,
        "masters": {lv["id"]: str(li * 100 + mj * 10 + mi)
                    for li, lv in enumerate(levels)
                    if lv["id"] in slab_lvls},
        "area_losa_lvl": area_losa_lvl,
        "peso_piso": peso_piso,
        "resumen": {
            "nodos": len(nodes), "columnas": n_col, "vigas": n_viga,
            "muros": n_muro,
            "losa_area_m2": round(area_losa_total, 2),
            "carga_losa_G_kN": round(carga_losa_G, 2),
            "carga_losa_Q_kN": round(carga_losa_Q, 2),
            "peso_propio_kN": round(sum(peso_piso.values()), 2),
        },
    }

    path = "brain/geometry_stages.json"
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)

    # ---------- RESUMEN POR PISO ----------
    print("\n=== RESUMEN POR PISO (geometría real ED2) ===")
    for lvl in slab_lvls:
        cols_p = n_vigas = n_muros = 0
        for ed in elements.values():
            if ed["lvl"] != lvl:
                continue
            if ed["type"] == "column": cols_p += 1
            elif ed["type"] == "beam": n_vigas += 1
            else: n_muros += 1
        g = qG[lvl] * area_losa_lvl[lvl]
        qq = qQ[lvl] * area_losa_lvl[lvl]
        level = next(lv for lv in levels if lv["id"] == lvl)
        print(f"  {lvl:7s} z={elev[lvl]:+6.2f} m | columnas={cols_p:2d} "
              f"vigas={n_vigas:2d} muros={n_muros:2d} | trib={area_losa_lvl[lvl]:7.1f} m2 "
              f"G={g:8.1f} kN Q={qq:6.1f} kN | fase={level['phase']}")

    r = out["resumen"]
    print(f"\n[QA] Total: {r['nodos']} nodos, {r['columnas']} col, "
          f"{r['vigas']} vigas, {r['muros']} muros")
    print(f"     losa={r['losa_area_m2']} m2, G_losa={r['carga_losa_G_kN']} kN, "
          f"Q={r['carga_losa_Q_kN']} kN, PP={r['peso_propio_kN']} kN")
    print(f"[OK] Geometría real exportada a: {path}")
    print("-------------------------------------------")


if __name__ == "__main__":
    build_real_geometry()