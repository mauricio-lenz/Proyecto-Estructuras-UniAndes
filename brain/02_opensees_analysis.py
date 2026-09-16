import json
import os
import math
import openseespy.opensees as ops

GAMMA = 25.0
E_CONC = 2.5e7
G_CONC = 1.0e7


def secciones():
    return {
        "PILAR-70x70": {"A": 0.49, "Iy": 0.0200083, "Iz": 0.0200083, "J": 0.03376, "b": 0.7, "h": 0.7},
        "P-70x70":     {"A": 0.49, "Iy": 0.0200083, "Iz": 0.0200083, "J": 0.03376, "b": 0.7, "h": 0.7},
        "V-60x80":     {"A": 0.48, "Iy": 0.0144, "Iz": 0.0256, "J": 0.0312, "b": 0.6, "h": 0.8},
        "V-45x30":     {"A": 0.135, "Iy": 0.002534, "Iz": 0.0010125, "J": 0.0022, "b": 0.45, "h": 0.3},
        "V-40x80":     {"A": 0.32, "Iy": 0.009600, "Iz": 0.017067, "J": 0.0212, "b": 0.4, "h": 0.8},
    }


def seccion_muro(L):
    t = 0.20
    return {"A": t * L, "Iy": L * t ** 3 / 12.0, "Iz": t * L ** 3 / 12.0,
            "J": 0.5 * t ** 4, "b": t, "h": L}


def transf_for(nodes, edata):
    if edata["type"] == "column":
        return 1
    n1, n2 = edata["nodes"]
    dy = nodes[n2]["y"] - nodes[n1]["y"]
    return 3 if abs(dy) > 1e-9 else 2


def construir_modelo(data):
    nodes, elements = data["nodes"], data["elements"]
    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)
    for nid, ndata in nodes.items():
        ops.node(int(nid), ndata["x"], ndata["y"], ndata["z"])
        fix = ndata.get("fix", [0, 0, 0, 0, 0, 0])
        if sum(fix) > 0:
            ops.fix(int(nid), *fix)
    for lvl, mid in data["masters"].items():
        mid_floor = nodes[str(mid)]["floor"]
        lvl_node_ids = [nid for nid, nd in nodes.items()
                        if nd["floor"] == mid_floor]
        slaves = [int(nid) for nid in lvl_node_ids if nid != str(mid)]
        if slaves:
            ops.rigidDiaphragm(3, int(mid), *slaves)
    ops.geomTransf("Linear", 1, 1.0, 0.0, 0.0)
    ops.geomTransf("Linear", 2, 0.0, 0.0, 1.0)
    ops.geomTransf("Linear", 3, 0.0, 0.0, 1.0)
    secc = secciones()
    for eid, edata in elements.items():
        transf = transf_for(nodes, edata)
        if edata["type"] == "wall":
            p = seccion_muro(edata["length"])
        else:
            p = secc.get(edata["sectionTag"], secc["V-60x80"])
        ops.element("elasticBeamColumn", int(eid),
                    *[int(n) for n in edata["nodes"]],
                    p["A"], E_CONC, G_CONC, p["J"], p["Iy"], p["Iz"], transf)


def aplicar_gravedad(data, q_lvl, tag_patron=1, tag_series=1, escala=1.0):
    elements = data["elements"]
    ops.timeSeries("Linear", tag_series)
    ops.pattern("Plain", tag_patron, tag_series)
    total = 0.0
    for eid, edata in elements.items():
        if edata["type"] != "beam":
            continue
        w = edata["w_G"] * escala if q_lvl == "G" else edata["w_Q"] * escala
        if abs(w) < 1e-12:
            continue
        ops.eleLoad("-ele", int(eid), "-type", "-beamUniform", 0.0, -w)
        total += w * edata["length"]
    return total


def aplicar_sismo(data, direccion, fuerzas, tag_patron=2, tag_series=2):
    cm = centros_de_masa(data)
    nodes, masters = data["nodes"], data["masters"]
    ops.timeSeries("Linear", tag_series)
    ops.pattern("Plain", tag_patron, tag_series)
    for lvl, f in fuerzas.items():
        if abs(f) < 1e-12:
            continue
        nm = int(masters[lvl])
        xm, ym = nodes[str(nm)]["x"], nodes[str(nm)]["y"]
        _a, xc, yc = cm[lvl]
        dx_cm, dy_cm = xc - xm, yc - ym
        if direccion == "EX":
            ops.load(nm, f, 0.0, 0.0, 0.0, 0.0, -f * dy_cm)
        else:
            ops.load(nm, 0.0, f, 0.0, 0.0, 0.0, f * dx_cm)


def centros_de_masa(data):
    xs = sorted({nd["x"] for nd in data["nodes"].values()})
    ys = sorted({nd["y"] for nd in data["nodes"].values()})
    area_main = (max(xs) - min(xs)) * (max(ys) - min(ys))
    cx_main = (min(xs) + max(xs)) / 2.0
    cy_main = (min(ys) + max(ys)) / 2.0
    return {lvl: (area_main, cx_main, cy_main) for lvl in data["masters"]}


def pesos_sismicos(cfg, data):
    area = centros_de_masa(data)
    qG = {lv["id"]: lv["qG"] for lv in cfg["niveles"]}
    qQ = {lv["id"]: lv["qQ"] for lv in cfg["niveles"]}
    frac = cfg["cargas"]["sismo"]["fraccion_sobrecarga_masa"]
    pp_est = data["peso_piso"]
    out = {}
    for lvl in data["masters"]:
        a = area[lvl][0]
        pp = qG[lvl] * a + pp_est[lvl]
        qsid = qQ[lvl] * a
        out[lvl] = {"W_kN": pp + frac * qsid, "PP_kN": pp, "Q_kN": qsid,
                    "area_m2": a, "qG": qG[lvl], "qQ": qQ[lvl]}
    return out


def configurar_y_analizar():
    ops.constraints("Transformation")
    ops.numberer("RCM")
    ops.system("BandGeneral")
    ops.test("NormDispIncr", 1.0e-8, 20)
    ops.algorithm("Linear")
    ops.integrator("LoadControl", 1.0)
    ops.analysis("Static")
    ok = ops.analyze(1)
    if ok != 0:
        raise RuntimeError("Analisis estatico fallo (codigo {})".format(ok))
    ops.reactions()


def extraer_resultados(data):
    """Fuerzas de extremo (6 componentes) en convencion basic force.

    Orden documentado de basicForce para elasticBeamColumn 3D:
    [N, Vy, Mz, T, My, Vz] (axial, corte local y, flexion local z, torsion,
    flexion local y, corte local z). Los arrays de cada componente se
    guardan como [extremo_i, extremo_j] = [v, -v] por simetria (igual a
    versiones anteriores validadas).
    """
    fuerzas = {}
    for eid in data["elements"]:
        b = ops.basicForce(int(eid))
        fuerzas[eid] = {
            "N": [b[0], -b[0]], "Vy": [b[1], -b[1]], "Mz": [b[2], -b[2]],
            "T": [b[3], -b[3]], "My": [b[4], -b[4]], "Vz": [b[5], -b[5]],
            "N_s": b[0], "Vy_s": b[1], "Mz_s": b[2],
            "T_s": b[3], "My_s": b[4], "Vz_s": b[5],
        }
    reac = {}
    desp = {}
    for nid, ndata in data["nodes"].items():
        desp[nid] = list(ops.nodeDisp(int(nid)))
        if ndata.get("fix") and ndata["fix"][0] == 1:
            reac[nid] = ops.nodeReaction(int(nid))
    return fuerzas, reac, desp


def analyze_building(bid, bdata, cfg):
    """Analiza un edificio completo: G, Q, EX, EY + superposicion.
    Devuelve (results_combo, ws, desplazamientos, cases_export, desplazamientos_cases).
    """
    lambdas = cfg["cargas"]["superposicion_lambdas"]
    orden = cfg["niveles_losa"]

    ws = pesos_sismicos(cfg, bdata)
    frac = cfg["cargas"]["sismo"]["fraccion_g"]
    fuerzas_EX = {lvl: ws[lvl]["W_kN"] * frac for lvl in orden}
    fuerzas_EY = {lvl: ws[lvl]["W_kN"] * frac for lvl in orden}
    lateral_EX = sum(fuerzas_EX.values())

    print(f"\n  --- {bid} pesos sismicos ---")
    for lvl in orden:
        print(f"    {lvl:12s} A={ws[lvl]['area_m2']:7.1f} m2  W={ws[lvl]['W_kN']:8.1f} kN  F={fuerzas_EX[lvl]:7.1f} kN")
    print(f"    V_basal total: EX={lateral_EX:.1f} kN")

    cases = {}

    construir_modelo(bdata)
    aplicar_gravedad(bdata, "G")
    configurar_y_analizar()
    fG, rG, dG = extraer_resultados(bdata)
    cases["G"] = (fG, rG, dG)
    print(f"    [G] OK reacciones Fz={sum(r[2] for r in rG.values()):.1f} kN")

    construir_modelo(bdata)
    aplicar_gravedad(bdata, "Q")
    configurar_y_analizar()
    fQ, rQ, dQ = extraer_resultados(bdata)
    cases["Q"] = (fQ, rQ, dQ)

    construir_modelo(bdata)
    aplicar_sismo(bdata, "EX", fuerzas_EX)
    configurar_y_analizar()
    fEX, rEX, dEX = extraer_resultados(bdata)
    cases["EX"] = (fEX, rEX, dEX)
    corte_EX = sum(r[0] for r in rEX.values())
    print(f"    [EX] corte={corte_EX:.1f} kN vs {lateral_EX:.1f} kN")

    construir_modelo(bdata)
    aplicar_sismo(bdata, "EY", fuerzas_EY)
    configurar_y_analizar()
    fEY, rEY, dEY = extraer_resultados(bdata)
    cases["EY"] = (fEY, rEY, dEY)
    corte_EY = sum(r[1] for r in rEY.values())
    print(f"    [EY] corte={corte_EY:.1f} kN vs {lateral_EX:.1f} kN")

    combo = ["G", "Q", "EX", "EY"]
    comps = ["N", "Vy", "Mz", "T", "My", "Vz"]

    def superp(field, eid, comp=None):
        if comp is None:
            return sum(lambdas[c] * cases[c][0][eid][field] for c in combo)
        vals = [cases[c][0][eid][field][comp] for c in combo]
        return sum(lambdas[c] * v for c, v in zip(combo, vals))

    results = {}
    for eid in bdata["elements"]:
        R = {}
        for compo in comps:
            R[compo] = [round(superp(compo + "_s", eid), 2),
                        round(-superp(compo + "_s", eid), 2)]
        results[eid] = R

    # Desplazamientos nodales superpuestos (para la deformada Unity)
    desplazamientos = {}
    for nid in bdata["nodes"]:
        d = [0.0, 0.0, 0.0]
        for c in combo:
            val = cases[c][2][nid]
            d[0] += lambdas[c] * val[0]
            d[1] += lambdas[c] * val[1]
            d[2] += lambdas[c] * val[2]
        desplazamientos[nid] = [round(v, 6) for v in d]

    # ---- Exportacion por caso individual ----
    # cases_export: {caso: {eid: {N:[..], Vy:[..], sz, Ry, ...}}} con
    # convencion [extremo_i, extremo_j] = [v, -v] igual a la combinacion.
    comps_l = ["N_s", "Vy_s", "Vz_s", "T_s", "My_s", "Mz_s"]
    comps_o = ["N", "Vy", "Vz", "T", "My", "Mz"]
    cases_export = {}
    desplazamientos_cases = {}
    for c in combo:
        fe, re, de = cases[c]
        casos_f = {}
        for eid in bdata["elements"]:
            raw = fe[eid]
            casos_f[eid] = {o: [round(raw[s], 2), round(-raw[s], 2)]
                            for s, o in zip(comps_l, comps_o)}
        cases_export[c] = casos_f
        disp_c = {}
        for nid in bdata["nodes"]:
            v = de[nid]
            disp_c[nid] = [round(v[0], 6), round(v[1], 6), round(v[2], 6)]
        desplazamientos_cases[c] = disp_c

    return results, ws, desplazamientos, cases_export, desplazamientos_cases


def build_combinacion_nombre(configs):
    """Descripcion textual de la combinacion usada (de ED1, igual en ambas)."""
    for cfg in configs.values():
        lam = cfg.get("cargas", {}).get("superposicion_lambdas", {})
        return "1.0G + 1.0Q + {:.2f}EX + {:.2f}EY".format(
            lam.get("EX", 1.0), lam.get("EY", 1.0))
    return "1.0G + 1.0Q + 0.9EX + 0.75EY"


def _interp_mcap(P_curve, M_curve, p):
    """M capacidad interpolada en la demanda P (mismo criterio que Unity)."""
    if not P_curve or not M_curve or len(P_curve) < 2:
        return None
    for i in range(len(P_curve) - 1):
        p0, p1 = P_curve[i], P_curve[i + 1]
        m0, m1 = M_curve[i], M_curve[i + 1]
        lo, hi = min(p0, p1), max(p0, p1)
        if lo - 1e-9 <= p <= hi + 1e-9:
            if abs(p1 - p0) < 1e-9:
                return m0
            t = (p - p0) / (p1 - p0)
            return m0 + (m1 - m0) * t
    idx = 0 if abs(P_curve[0] - p) <= abs(P_curve[-1] - p) else len(P_curve) - 1
    return M_curve[idx]


def export_reports(all_elements, all_results, all_results_cases,
                   buildings, configs, pm_capacity):
    """Escribe CSVs verificables: (1) cada elemento con todas sus fuerzas por
    caso (G/Q/EX/EY) y combinacion + D/C; (2) cargas por nivel (qG/qQ, area,
    peso y fuerza sismica)."""
    import csv
    os.makedirs("reports", exist_ok=True)
    reports = ["G", "Q", "EX", "EY"]
    comps = ["N", "Vy", "Vz", "T", "My", "Mz"]

    capP, capM = {}, {}
    for e in pm_capacity:
        capP[e["section"]] = e.get("P") or []
        capM[e["section"]] = e.get("M") or []

    elem_by_id = {}
    for en in all_elements:
        elem_by_id[en["id"]] = en
    if isinstance(all_results, list):
        all_results = {r["id"]: r for r in all_results}
    case_idx = {}
    for caso in reports:
        case_idx[caso] = {en["id"]: en for en in all_results_cases.get(caso, [])}

    header = ["building", "tag", "type", "section", "material", "lvl", "phase",
              "orient", "cad_id", "node_i", "node_j", "length_m", "trib_area_m2",
              "wG_kN_m", "wQ_kN_m"]
    for pre in reports + ["COMBO"]:
        for c in comps:
            header.append(f"{pre}_{c}_i")
            header.append(f"{pre}_{c}_j")
    header += ["COMBO_P_kN", "COMBO_M_kNm", "COMBO_Mcap_kNm", "COMBO_DC"]

    def section_pm(etype, section):
        if etype == "column":
            for k in (section, "PILAR-70x70", "P-70x70"):
                if k in capP:
                    return capP[k], capM[k]
        elif etype == "wall":
            for k in (section, "M-20"):
                if k in capP:
                    return capP[k], capM[k]
        return None, None

    rows = []
    ids = sorted(elem_by_id.keys(), key=lambda s: (elem_by_id[s]["building"], int(s)))
    for eid in ids:
        e = elem_by_id[eid]
        nod = e.get("nodes") or ["", ""]
        row = [e["building"], eid, e["type"], e["sectionTag"], e["material"],
               e["lvl"], e["phase"], e.get("orient", ""), e["cad_id"],
               nod[0] if len(nod) > 0 else "", nod[1] if len(nod) > 1 else "",
               round(e["length"], 3), round(e["trib_area"], 3),
               round(e["w_G"], 3), round(e["w_Q"], 3)]
        combo = None
        for pre in reports + ["COMBO"]:
            src = all_results[eid] if pre == "COMBO" else case_idx[pre].get(eid)
            if not src:
                row += [""] * (2 * len(comps))
                continue
            if pre == "COMBO":
                combo = src
            for c in comps:
                v = src.get(c)
                if v and len(v) >= 2:
                    row += [round(v[0], 3), round(v[1], 3)]
                else:
                    row += ["", ""]
        if combo is not None:
            row += [round(combo["P"], 2), round(combo["M"], 2)]
            Pc = combo["P"]
            Mcap = None
            capPP, capMM = section_pm(e["type"], e["sectionTag"])
            if capPP is not None:
                Mcap = _interp_mcap(capPP, capMM, Pc)
            if Mcap is not None and Mcap > 1e-6:
                row += [round(Mcap, 2), round(combo["M"] / Mcap, 3)]
            else:
                row += ["", ""]
        else:
            row += ["", "", "", ""]
        rows.append(row)

    with open("reports/resultados_elementos.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)

    # ---- Cargas por nivel (qG/qQ, area losa, W sismico, F sismica) ----
    rowsL = []
    for bid, bdata in buildings.items():
        cfg = configs[bid]
        elev_lvl = {lv["id"]: lv["elevation"] for lv in cfg["niveles"]}
        phase_lvl = {lv["id"]: lv["phase"] for lv in cfg["niveles"]}
        qG_lvl = {lv["id"]: lv.get("qG", 0.0) for lv in cfg["niveles"]}
        qQ_lvl = {lv["id"]: lv.get("qQ", 0.0) for lv in cfg["niveles"]}
        gx = [g["x"] for g in cfg["grilla_X"]]
        gy = [g["y"] for g in cfg["grilla_Y"]]
        xr = max(gx) - min(gx)
        yr = max(gy) - min(gy)
        ws = pesos_sismicos(cfg, bdata)
        frac = cfg["cargas"]["sismo"]["fraccion_g"]
        vbase = 0.0
        for lvl in cfg.get("niveles_losa", []):
            if lvl not in elev_lvl:
                continue
            W = ws[lvl]["W_kN"]
            F = W * frac
            vbase += F
            rowsL.append([bid, lvl, phase_lvl.get(lvl, 1), round(elev_lvl[lvl], 3),
                          qG_lvl.get(lvl, 0.0), qQ_lvl.get(lvl, 0.0),
                          round(xr * yr, 2), round(W, 1), round(F, 1)])
        vbase = round(vbase, 1)
        for r in rowsL:
            if r[0] == bid:
                r.append(vbase)

    with open("reports/resultados_cargas_niveles.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["building", "lvl", "phase", "z_m", "qG_kPa", "qQ_kPa",
                    "losa_area_m2", "W_sismico_kN", "F_sismica_kN", "V_basal_kN"])
        w.writerows(rowsL)

    print(f"[OK] reportes CSV: reports/resultados_elementos.csv "
          f"({len(rows)} filas), reports/resultados_cargas_niveles.csv "
          f"({len(rowsL)} filas)")


def run_staged_analysis():
    print("=== ANALISIS OPENSEES ED1 + ED2 (PASO 3) ===")
    geom_path = "brain/geometry_stages.json"
    if not os.path.exists(geom_path):
        print("[ERROR] No se encontro 'brain/geometry_stages.json'.")
        return
    with open(geom_path, "r") as f:
        data = json.load(f)
    buildings = data["buildings"]
    configs = {}
    for bid, bdata in buildings.items():
        cfg_path = bdata["config"]
        with open(cfg_path, "r", encoding="utf-8") as f:
            configs[bid] = json.load(f)

    all_nodes = []
    all_elements = []
    all_results = []
    all_results_cases = {}   # caso -> lista de resultados por elemento
    all_displacements_cases = {}  # caso -> nodo -> [dx, dy, dz]
    all_slabs = []
    all_voladizos = []
    all_apoyos = []

    CAP_URL = "brain/cap_pm.json"
    capstore = {}
    if os.path.exists(CAP_URL):
        with open(CAP_URL, "r", encoding="utf-8") as f:
            capstore = json.load(f)
    pm_capacity = []

    def add_pm(sec, curve):
        for entry in pm_capacity:
            if entry["section"] == sec:
                return
        pm_capacity.append({"section": sec, "P": curve["P"], "M": curve["M"]})

    def local_axes(n1, n2):
        """Ejes locales del elemento: L1 a lo largo, L2/L3 perpendiculares
        (convencion igual a la del viewer; en coords de modelo OS)."""
        dx = n2["x"] - n1["x"]; dy = n2["y"] - n1["y"]; dz = n2["z"] - n1["z"]
        L = math.sqrt(dx * dx + dy * dy + dz * dz)
        if L < 1e-12:
            return [0, 0, 1, 0, 1, 0, 1, 0, 0]
        l1 = [dx / L, dy / L, dz / L]
        if abs(l1[2]) > 0.99:  # vertical -> L2 en X
            cand = [1.0, 0.0, 0.0]
            dot = cand[0] * l1[0] + cand[1] * l1[1] + cand[2] * l1[2]
            l2 = [cand[0] - dot * l1[0], cand[1] - dot * l1[1],
                  cand[2] - dot * l1[2]]
            n2l = math.sqrt(sum(v * v for v in l2))
            l2 = [v / n2l for v in l2]
        else:
            l2 = [0.0, 0.0, 1.0]
            dot = l2[0] * l1[0] + l2[1] * l1[1] + l2[2] * l1[2]
            l2 = [l2[0] - dot * l1[0], l2[1] - dot * l1[1], l2[2] - dot * l1[2]]
            n2l = math.sqrt(sum(v * v for v in l2))
            l2 = [v / n2l for v in l2]
        l3 = [l1[1] * l2[2] - l1[2] * l2[1],
              l1[2] * l2[0] - l1[0] * l2[2],
              l1[0] * l2[1] - l1[1] * l2[0]]
        return l1 + l2 + l3

    for bid, bdata in buildings.items():
        cfg = configs[bid]
        print(f"\n=== {bid}: {cfg['nombre']} ===")
        results, ws, desplazamientos, cases_export, desplazamientos_cases = \
            analyze_building(bid, bdata, cfg)
        r = bdata["resumen"]
        print(f"  [QA] {bid}: nodos={r['nodos']} col={r['columnas']} "
              f"vigas={r['vigas']} muros={r['muros']} losa={r['losa_area_m2']} m2")

        material = cfg.get("material", "H30")
        elev_lvl = {lv["id"]: lv["elevation"] for lv in cfg["niveles"]}
        phase_lvl = {lv["id"]: lv["phase"] for lv in cfg["niveles"]}

        for nid, nd in bdata["nodes"].items():
            d = desplazamientos[nid]
            all_nodes.append({
                "id": nid, "building": bid,
                "x": nd["x"], "y": nd["y"], "z": nd["z"],
                "floor": nd["floor"], "phase": nd["phase"],
                "fix": nd.get("fix", [0] * 6),
                "dx": d[0], "dy": d[1], "dz": d[2],
            })
        for eid, ed in bdata["elements"].items():
            n1 = bdata["nodes"][ed["nodes"][0]]
            n2 = bdata["nodes"][ed["nodes"][1]]
            axes = local_axes(n1, n2)
            all_elements.append({
                "id": eid, "building": bid,
                "type": ed["type"], "nodes": ed["nodes"],
                "sectionTag": ed["sectionTag"], "cad_id": ed["cad_id"],
                "phase": ed["phase"], "trib_area": ed["trib_area"],
                "w_G": ed["w_G"], "w_Q": ed["w_Q"],
                "length": ed["length"], "orient": ed["orient"], "lvl": ed["lvl"],
                "material": material, "local": axes,
            })
        for eid, r in results.items():
            P_dem = -r["N"][0]
            M_dem = max(math.hypot(r["Mz"][0], r["My"][0]),
                        math.hypot(r["Mz"][1], r["My"][1]))
            all_results.append({
                "id": eid, "building": bid,
                "N": r["N"], "Vy": r["Vy"], "Mz": r["Mz"],
                "T": r["T"], "My": r["My"], "Vz": r["Vz"],
                "P": round(P_dem, 2), "M": round(M_dem, 2),
            })

        # --- Exportacion por caso individual (G, Q, EX, EY) ---
        # cases_export: {caso: {eid: {N:[..], Vy:[..], ...}}}, incluye P/M de demanda
        for caso, fdict in cases_export.items():
            for eid, fr in fdict.items():
                base = {
                    "N": fr["N"], "Vy": fr["Vy"], "Mz": fr["Mz"],
                    "T": fr["T"], "My": fr["My"], "Vz": fr["Vz"],
                }
                P_dem = -fr["N"][0]
                M_dem = max(math.hypot(fr["Mz"][0], fr["My"][0]),
                            math.hypot(fr["Mz"][1], fr["My"][1]))
                base["P"] = round(P_dem, 2)
                base["M"] = round(M_dem, 2)
                entry = {"id": eid, "building": bid, **base}
                all_results_cases.setdefault(caso, []).append(entry)

        # Desplazamientos por caso (para deformada por caso en Unity)
        for caso, disp_dict in desplazamientos_cases.items():
            all_displacements_cases[caso] = disp_dict

        if "PILAR-70x70" in capstore:
            add_pm("PILAR-70x70", capstore["PILAR-70x70"])
            add_pm("P-70x70", capstore["PILAR-70x70"])
        if "M-20" in capstore:
            add_pm("M-20", capstore["M-20"])

        # Losas: un rectangulo por nivel (poligono del trazado completo)
        gx = [g["x"] for g in cfg["grilla_X"]]
        gy = [g["y"] for g in cfg["grilla_Y"]]
        xmin_all, xmax_all = min(gx), max(gx)
        ymin_all, ymax_all = min(gy), max(gy)
        lay = cfg.get("modalidad_losa", "rect")
        for lvl in cfg.get("niveles_losa", []):
            if lvl not in elev_lvl:
                continue
            all_slabs.append({
                "building": bid, "lvl": lvl,
                "x": [xmin_all, xmax_all, xmax_all, xmin_all],
                "y": [ymin_all, ymin_all, ymax_all, ymax_all],
                "z": round(elev_lvl[lvl], 3),
                "e": cfg.get("losa_e", 0.15),
                "phase": phase_lvl.get(lvl, 1),
                "qG": next((lv["qG"] for lv in cfg["niveles"] if lv["id"] == lvl), 0.0) or 0.0,
                "qQ": next((lv["qQ"] for lv in cfg["niveles"] if lv["id"] == lvl), 0.0) or 0.0,
                "area": round((xmax_all - xmin_all) * (ymax_all - ymin_all), 2),
            })
        for vol in cfg.get("voladizos", []):
            lvl = vol["nivel"]
            if lvl not in elev_lvl:
                continue
            all_voladizos.append({
                "building": bid, "id": vol.get("id", "VOL"),
                "lvl": lvl,
                "x": [vol["x_min"], vol["x_max"], vol["x_max"], vol["x_min"]],
                "y": [vol["y_min"], vol["y_min"], vol["y_max"], vol["y_max"]],
                "z": round(elev_lvl[lvl], 3),
                "e": cfg.get("losa_e", 0.15),
                "phase": phase_lvl.get(lvl, 1),
                "area": round((vol["x_max"] - vol["x_min"]) * (vol["y_max"] - vol["y_min"]), 2),
            })
        for nid, nd in bdata["nodes"].items():
            fix = nd.get("fix", [0] * 6)
            if fix[0]:
                all_apoyos.append({
                    "node": nid, "building": bid,
                    "fix": fix, "z": nd["z"],
                })

    # JsonUtility en Unity no deserializa diccionarios: se emiten como
    # arrays de wrappers {name, entries} / {node, u} / {k, v}.
    results_cases_out = [
        {"name": caso, "entries": entries}
        for caso, entries in all_results_cases.items()
    ]
    displacements_cases_out = []
    for caso, disp_dict in all_displacements_cases.items():
        displacements_cases_out.append({
            "name": caso,
            "entries": [{"node": nid, "u": u} for nid, u in disp_dict.items()],
        })

    metadatos = {
        "fuente": "OpenSeesPy 3.5.2 - analisis elastico lineal (elasticBeamColumn 3D)",
        "generador": "brain/02_opensees_analysis.py",
        "materiales": [E_CONC, G_CONC, GAMMA],
        "controles": [
            {"k": "gravedad", "v": "eleLoad beamUniform w_G / w_Q (area tributaria x q)"},
            {"k": "sismo", "v": "fuerzas nodales en diafragma segun pesos sismicos W=C*Sigma(P)"},
            {"k": "superposicion", "v": "suma lineal de casos con lambdas de config"},
            {"k": "validacion", "v": "Sigma(reacciones)=V0 EX/EY; conservation Sigma(w*L)=Sigma(Rz)"},
        ],
        "trazabilidad": [
            {"k": "config_ED1", "v": "brain/edificio1_config.json"},
            {"k": "config_ED2", "v": "brain/edificio_config_real.json"},
            {"k": "planos_ED1", "v": "cad_files/dxf_L1/2017_67-*.dxf"},
            {"k": "planos_ED2", "v": "cad_files/dxf_calculo/LT2_CAL_Planos/2024_22-*.dxf"},
            {"k": "capacidad_pm", "v": "brain/cap_pm.json"},
        ],
    }

    final_output = {
        "schema": "structural_data/1.1",
        "nodes": all_nodes,
        "elements": all_elements,
        "results": all_results,
        "results_cases": results_cases_out,
        "displacements_cases": displacements_cases_out,
        "slabs": all_slabs,
        "voladizos": all_voladizos,
        "apoyos": all_apoyos,
        "pm_capacity": pm_capacity,
        "combinacion": build_combinacion_nombre(configs),
        "casos": ["G", "Q", "EX", "EY"],
        "metadata": metadatos,
    }
    unity_json_path = "visualization/Assets/StreamingAssets/structural_data.json"
    os.makedirs(os.path.dirname(unity_json_path), exist_ok=True)
    with open(unity_json_path, "w") as f:
        json.dump(final_output, f, indent=2)

    export_reports(all_elements, all_results, all_results_cases,
                   buildings, configs, pm_capacity)

    total_n = sum(b["resumen"]["nodos"] for b in buildings.values())
    total_c = sum(b["resumen"]["columnas"] for b in buildings.values())
    total_v = sum(b["resumen"]["vigas"] for b in buildings.values())
    total_m = sum(b["resumen"]["muros"] for b in buildings.values())
    print(f"\n=== TOTAL ED1+ED2 ===")
    print(f"  nodos={total_n} col={total_c} vigas={total_v} muros={total_m}")
    print(f"  elementos exportados: {len(all_elements)}")
    print(f"  losas exportadas: {len(all_slabs)}, voladizos: {len(all_voladizos)}")
    print(f"  casos individuales exportados: {list(all_results_cases.keys())}")
    print(f"\n[OK] JSON exportado a: {unity_json_path}")
    print("\n-------------------------------------------")
    print(">>> RESULTADO: PASO 3 COMPLETADO CON EXITO <<<")
    print("-------------------------------------------")


if __name__ == "__main__":
    run_staged_analysis()
