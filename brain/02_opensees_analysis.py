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
    fuerzas = {}
    for eid in data["elements"]:
        f = ops.basicForce(int(eid))
        fuerzas[eid] = {
            "N": [f[0], -f[0]], "Vy": [f[1], -f[1]], "Mz": [f[2], -f[2]],
            "N_s": f[0], "Vy_s": f[1], "Mz_s": f[2],
        }
    reac = {}
    for nid, ndata in data["nodes"].items():
        if ndata.get("fix") and ndata["fix"][0] == 1:
            reac[nid] = ops.nodeReaction(int(nid))
    return fuerzas, reac


def analyze_building(bid, bdata, cfg):
    """Analiza un edificio completo: G, Q, EX, EY + superposicion. Devuelve cases."""
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
    fG, rG = extraer_resultados(bdata)
    cases["G"] = (fG, rG)
    print(f"    [G] OK reacciones Fz={sum(r[2] for r in rG.values()):.1f} kN")

    construir_modelo(bdata)
    aplicar_gravedad(bdata, "Q")
    configurar_y_analizar()
    fQ, rQ = extraer_resultados(bdata)
    cases["Q"] = (fQ, rQ)

    construir_modelo(bdata)
    aplicar_sismo(bdata, "EX", fuerzas_EX)
    configurar_y_analizar()
    fEX, rEX = extraer_resultados(bdata)
    cases["EX"] = (fEX, rEX)
    corte_EX = sum(r[0] for r in rEX.values())
    print(f"    [EX] corte={corte_EX:.1f} kN vs {lateral_EX:.1f} kN")

    construir_modelo(bdata)
    aplicar_sismo(bdata, "EY", fuerzas_EY)
    configurar_y_analizar()
    fEY, rEY = extraer_resultados(bdata)
    cases["EY"] = (fEY, rEY)
    corte_EY = sum(r[1] for r in rEY.values())
    print(f"    [EY] corte={corte_EY:.1f} kN vs {lateral_EX:.1f} kN")

    results = {}
    for eid in bdata["elements"]:
        R = {}
        for compo in ["N_s", "Vy_s", "Mz_s"]:
            val = (lambdas["G"] * cases["G"][0][eid][compo]
                   + lambdas["Q"] * cases["Q"][0][eid][compo]
                   + lambdas["EX"] * cases["EX"][0][eid][compo]
                   + lambdas["EY"] * cases["EY"][0][eid][compo])
            R[compo] = val
        results[eid] = {"N": [round(R["N_s"], 2), round(-R["N_s"], 2)],
                        "Vy": [round(R["Vy_s"], 2), round(-R["Vy_s"], 2)],
                        "Mz": [round(R["Mz_s"], 2), round(-R["Mz_s"], 2)]}
    return results, ws


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

    for bid, bdata in buildings.items():
        cfg = configs[bid]
        print(f"\n=== {bid}: {cfg['nombre']} ===")
        results, ws = analyze_building(bid, bdata, cfg)
        r = bdata["resumen"]
        print(f"  [QA] {bid}: nodos={r['nodos']} col={r['columnas']} "
              f"vigas={r['vigas']} muros={r['muros']} losa={r['losa_area_m2']} m2")

        for nid, nd in bdata["nodes"].items():
            all_nodes.append({
                "id": nid, "building": bid,
                "x": nd["x"], "y": nd["y"], "z": nd["z"],
                "floor": nd["floor"], "phase": nd["phase"],
                "fix": nd.get("fix", [0] * 6),
            })
        for eid, ed in bdata["elements"].items():
            all_elements.append({
                "id": eid, "building": bid,
                "type": ed["type"], "nodes": ed["nodes"],
                "sectionTag": ed["sectionTag"], "cad_id": ed["cad_id"],
                "phase": ed["phase"], "trib_area": ed["trib_area"],
                "w_G": ed["w_G"], "w_Q": ed["w_Q"],
                "length": ed["length"], "orient": ed["orient"], "lvl": ed["lvl"],
            })
        for eid, r in results.items():
            all_results.append({
                "id": eid, "building": bid,
                "N": r["N"], "Vy": r["Vy"], "Mz": r["Mz"],
            })

    pm_capacity = {
        "P": [11978.4, 9826.8, 4884.2, 0.0, -1649.3],
        "M": [0.0, 631.3, 1277.5, 513.2, 0.0],
    }
    final_output = {
        "nodes": all_nodes,
        "elements": all_elements,
        "results": all_results,
        "pm_capacity": pm_capacity,
    }
    unity_json_path = "visualization/Assets/StreamingAssets/structural_data.json"
    os.makedirs(os.path.dirname(unity_json_path), exist_ok=True)
    with open(unity_json_path, "w") as f:
        json.dump(final_output, f, indent=2)

    total_n = sum(b["resumen"]["nodos"] for b in buildings.values())
    total_c = sum(b["resumen"]["columnas"] for b in buildings.values())
    total_v = sum(b["resumen"]["vigas"] for b in buildings.values())
    total_m = sum(b["resumen"]["muros"] for b in buildings.values())
    print(f"\n=== TOTAL ED1+ED2 ===")
    print(f"  nodos={total_n} col={total_c} vigas={total_v} muros={total_m}")
    print(f"  elementos exportados: {len(all_elements)}")
    print(f"\n[OK] JSON exportado a: {unity_json_path}")
    print("\n-------------------------------------------")
    print(">>> RESULTADO: PASO 3 COMPLETADO CON EXITO <<<")
    print("-------------------------------------------")


if __name__ == "__main__":
    run_staged_analysis()
