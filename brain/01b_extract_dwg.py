"""
QA de Planos DWG/DXF (Paso 2b) - Verificacion contra config real ED2
====================================================================
Ya no modela geometrica (eso lo hace 01_geometry_stages.py a partir de
brain/edificio_config_real.json). Este script solo valida que los planos
DXF (plantas 100/101/102) contienen la grilla de ejes del edificio real
y muestra coincidencias/divergencias con el config, para control de
calidad de la fuente CAD.

Fuentes (conversion ODA DWG->DXF en cad_files/):
  - 2024_22-100  PLANTA FUNDACIONES  -> layout base (-7.97)
  - 2024_22-101  PLANTA CIOELO 1SUB a PISO 3 -> layout tipico
  - 2024_22-102  PLANTA CIELO PISO 4 -> layout superior (p4)
Unidades dibujo = cm -> metros (factor 0.01).

Salida: compara ejes X/Y detectados en cada planta con la grilla real
del config y reporta diferencias > tolerancia.
"""
import os
import math
import json
import ezdxf

CM = 0.01
LAYERS_EJES = ["RLE-EJES", "RLE-EJE"]

SRC = {
    "base": "cad_files/dxf_calculo/LT2_CAL_Planos/2024_22-100.dxf",
    "tipico": "cad_files/dxf_calculo/LT2_CAL_Planos/2024_22-101.dxf",
    "p4": "cad_files/dxf_calculo/LT2_CAL_Planos/2024_22-102.dxf",
}


def load_segs(path, layers):
    doc = ezdxf.readfile(path)
    out = []
    for e in doc.modelspace():
        if e.dxf.layer not in layers:
            continue
        t = e.dxftype()
        if t == "LINE":
            s, en = e.dxf.start, e.dxf.end
            out.append((s.x * CM, s.y * CM, en.x * CM, en.y * CM))
        elif t == "LWPOLYLINE":
            pts = [(p[0] * CM, p[1] * CM) for p in e.get_points()]
            for i in range(len(pts) - 1):
                out.append((pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1]))
    return out


def axis_lines(segs, minlen=1.2):
    H, V = {}, {}
    for (x1, y1, x2, y2) in segs:
        L = math.hypot(x2 - x1, y2 - y1)
        if L < minlen:
            continue
        if abs(y2 - y1) < 0.03:
            H.setdefault(round(y1, 1), []).append((x1, x2))
        elif abs(x2 - x1) < 0.03:
            V.setdefault(round(x1, 1), []).append((y1, y2))
    return H, V


def span(vals):
    return max(max(a, b) for a, b in vals) - min(min(a, b) for a, b in vals)


def detect_grid(path):
    segs = load_segs(path, LAYERS_EJES)
    if not segs:
        return [], [], 0
    H, V = axis_lines(segs)
    maxH = max((span(v) for v in V.values()), default=1)
    maxV = max((span(v) for v in H.values()), default=1)
    cols = sorted(x for x, v in V.items() if span(v) >= 0.6 * maxH)
    rows = sorted(y for y, v in H.items() if span(v) >= 0.6 * maxV)
    return cols, rows, len(segs)


def qa_grillas():
    print("=== QA PLANOS DXF vs CONFIG REAL ED2 (PASO 2b) ===")
    cfg_path = "brain/edificio_config_real.json"
    if not os.path.exists(cfg_path):
        print("[ERROR] Falta " + cfg_path)
        return
    with open(cfg_path, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    x_real = sorted(g["x"] for g in cfg["grilla_X"])
    y_real = sorted(g["y"] for g in cfg["grilla_Y"])

    tol = 0.8  # m (ejes de los DXF tienen ruido/lineas auxiliares)

    for nombre, path in SRC.items():
        if not os.path.exists(path):
            print(f"  [{nombre}] DXF no encontrado: {path}")
            continue
        cols, rows, nseg = detect_grid(path)
        print(f"\n  [{nombre}] {os.path.basename(path)}  (segmentos ejes={nseg})")
        print(f"    X detectados ({len(cols)}): {[round(x,2) for x in cols]}")
        print(f"    X real      ({len(x_real)}): {x_real}")
        faltan_x = [x for x in x_real if not any(abs(x - c) < tol for c in cols)]
        sobran_x = [c for c in cols if not any(abs(c - x) < tol for x in x_real)]
        print(f"    X: faltan={len(faltan_x)} {[round(x,2) for x in faltan_x]} "
              f"sobran={len(sobran_x)}")
        print(f"    Y detectados ({len(rows)}): {[round(y,2) for y in rows]}")
        print(f"    Y real      ({len(y_real)}): {y_real}")
        faltan_y = [y for y in y_real if not any(abs(y - r) < tol for r in rows)]
        print(f"    Y: faltan={len(faltan_y)} {[round(y,2) for y in faltan_y]}")

    print("\n[OK] QA de grillas completado. La geometria real se genera con "
          "01_geometry_stages.py (no con DXF).")


if __name__ == "__main__":
    qa_grillas()