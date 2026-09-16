import json
import os
import math
import openseespy.opensees as ops

GAMMA = 25.0  # kN/m3
E_CONC = 2.5e7  # kPa
G_CONC = 1.0e7  # kPa

# Todos positivos en compresion/traccion coo OpenSees (fuera local).
# (Se rellenan por seccion en secciones())
MONKEY_SECC = {}


def secciones():
    """Propiedades de seccion para los tags usados en ED2."""
    return {
        "PILAR-70x70": {"A": 0.49, "Iy": 0.0200083, "Iz": 0.0200083, "J": 0.03376, "b": 0.7, "h": 0.7},
        "V-60x80": {"A": 0.48, "Iy": 0.0144, "Iz": 0.0256, "J": 0.0312, "b": 0.6, "h": 0.8},
        "V-45x30": {"A": 0.135, "Iy": 0.002534, "Iz": 0.0010125, "J": 0.0022, "b": 0.45, "h": 0.3},
        "V-40x80": {"A": 0.32, "Iy": 0.009600, "Iz": 0.017067, "J": 0.0212, "b": 0.4, "h": 0.8},
    }


def seccion_muro(L):
    """M-20: muro e=0.20 como elemento de linea en su plano."""
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
    """Construye el modelo 3D con diafragmas rigidos por piso."""
    nodes, elements = data["nodes"], data["elements"]

    ops.wipe()
    ops.model("basic", "-ndm", 3, "-ndf", 6)

    for nid, ndata in nodes.items():
        ops.node(int(nid), ndata["x"], ndata["y"], ndata["z"])
        fix = ndata.get("fix", [0, 0, 0, 0, 0, 0])
        if sum(fix) > 0:
            ops.fix(int(nid), *fix)

    # Diafragmas rigidos por piso (constrain en plano X-Y de cada nivel)
    # Master = cruce de columna real (rigidez vertical asegurada), sin fix
    for lvl, mid in data["masters"].items():
        lvl_node_ids = [nid for nid, nd in nodes.items() if nd["floor"] == nodes[mid]["floor"]]
        slaves = [int(nid) for nid in lvl_node_ids if nid != mid]
        if slaves:
            ops.rigidDiaphragm(3, int(mid), *slaves)

    # Transformaciones geometricas
    ops.geomTransf("Linear", 1, 1.0, 0.0, 0.0)   # columnas (eje Z local)
    ops.geomTransf("Linear", 2, 0.0, 0.0, 1.0)   # vigas eje X
    ops.geomTransf("Linear", 3, 0.0, 0.0, 1.0)   # vigas eje Y

    secc = secciones()
    for eid, edata in elements.items():
        transf = transf_for(nodes, edata)
        if edata["type"] == "wall":
            p = seccion_muro(edata["length"])
        else:
            p = secc[edata["sectionTag"]]
        ops.element("elasticBeamColumn", int(eid),
                    *[int(n) for n in edata["nodes"]],
                    p["A"], E_CONC, G_CONC, p["J"], p["Iy"], p["Iz"], transf)


def aplicar_gravedad(data, q_lvl, tag_patron=1, tag_series=1, escala=1.0):
    """Aplica carga uniforme por piso (geometria tributaria). Devuelve total kN."""
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
    """Fuerzas laterales en el CM de cada piso con wrench (metodologia P1L3)."""
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
    """CM por piso = centroide del rectangulo de losa (ED2 sin voladizos)."""
    xs = sorted({nd["x"] for nd in data["nodes"].values()})
    ys = sorted({nd["y"] for nd in data["nodes"].values()})
    area_main = (max(xs) - min(xs)) * (max(ys) - min(ys))
    cx_main = (min(xs) + max(xs)) / 2.0
    cy_main = (min(ys) + max(ys)) / 2.0
    return {lvl: (area_main, cx_main, cy_main) for lvl in data["masters"]}


def pesos_sismicos(cfg, data):
    """W_i = PP_i + 0.5 Q_i ; PP = qG*A + peso estructura del piso."""
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
    """Fuerzas basicas por elemento (local) y reacciones + desplazamientos."""
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


def run_staged_analysis():
    print("=== ANALISIS OPENSEES REAL (PASO 3) ===")

    geom_path = "brain/geometry_stages.json"
    cfg_path = "brain/edificio_config_real.json"
    if not os.path.exists(geom_path):
        print("[ERROR] No se encontró 'brain/geometry_stages.json'.")
        return
    with open(geom_path, "r") as f:
        data = json.load(f)
    with open(cfg_path, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    lambdas = cfg["cargas"]["superposicion_lambdas"]
    orden = cfg["niveles_losa"]

    # ============ PESOS Y FUERZAS SISMICAS ============
    ws = pesos_sismicos(cfg, data)
    frac = cfg["cargas"]["sismo"]["fraccion_g"]
    fuerzas_EX = {lvl: ws[lvl]["W_kN"] * frac for lvl in orden}
    fuerzas_EY = {lvl: ws[lvl]["W_kN"] * frac for lvl in orden}

    print("\n--- Pesos sismicos por piso ---")
    for lvl in orden:
        print(f"  {lvl:7s} A={ws[lvl]['area_m2']:6.1f} m2  PP={ws[lvl]['PP_kN']:8.1f} "
              f"Q={ws[lvl]['Q_kN']:7.1f}  W={ws[lvl]['W_kN']:8.1f} kN "
              f"  F={fuerzas_EX[lvl]:7.1f} kN")
    lateral_EX = sum(fuerzas_EX.values())
    lateral_EY = sum(fuerzas_EY.values())
    print(f"  Carga lateral total: EX={lateral_EX:.1f} kN  EY={lateral_EY:.1f} kN")

    # ============ CASOS BASE ============
    casos = {}
    data_Q_carga = None

    # Caso G
    construir_modelo(data)
    total_G = aplicar_gravedad(data, "G")
    configurar_y_analizar()
    fG, rG = extraer_resultados(data)
    casos["G"] = (fG, rG)
    qG_lvl = {lvl: next(lv["qG"] for lv in cfg["niveles"] if lv["id"] == lvl)
              for lvl in orden}
    g_total_losa = sum(qG_lvl[l] * data["area_losa_lvl"][l] for l in orden)
    print(f"\n[Caso G] Carga transferida por tributarias: {total_G:.2f} kN "
          f"(qG*A={g_total_losa:.2f} kN)")

    # Caso Q
    construir_modelo(data)
    total_Q = aplicar_gravedad(data, "Q")
    configurar_y_analizar()
    fQ, rQ = extraer_resultados(data)
    casos["Q"] = (fQ, rQ)
    q_total_por_piso = {lvl: ws[lvl]["qQ"] * ws[lvl]["area_m2"] for lvl in orden}
    print(f"\n[Caso Q] Carga transferida: {total_Q:.2f} kN  (qQ*A={sum(q_total_por_piso.values()):.2f} kN)")
    reac_fz_Q = sum(r[2] for r in rQ.values())
    print(f"         Reacciones Fz: {reac_fz_Q:.2f} kN | "
          f"rel={abs(reac_fz_Q - total_Q)/total_Q:.2e}")

    # Caso EX
    construir_modelo(data)
    aplicar_sismo(data, "EX", fuerzas_EX)
    configurar_y_analizar()
    fEX, rEX = extraer_resultados(data)
    casos["EX"] = (fEX, rEX)
    corte_EX = sum(r[0] for r in rEX.values())
    desp_EX = ops.nodeDisp(int(data["masters"]["piso4"]))[0]
    print(f"\n[Caso EX] corte basal={corte_EX:.1f} kN vs {lateral_EX:.1f} kN "
          f"| desp techo={desp_EX:.4f} m | sentido_ok={desp_EX >= 0}")

    # Caso EY
    construir_modelo(data)
    aplicar_sismo(data, "EY", fuerzas_EY)
    configurar_y_analizar()
    fEY, rEY = extraer_resultados(data)
    casos["EY"] = (fEY, rEY)
    corte_EY = sum(r[1] for r in rEY.values())
    desp_EY = ops.nodeDisp(int(data["masters"]["piso4"]))[1]
    print(f"\n[Caso EY] corte basal={corte_EY:.1f} kN vs {lateral_EY:.1f} kN "
          f"| desp techo={desp_EY:.4f} m | sentido_ok={desp_EY >= 0}")

    # ============ SUPERPOSICION R = lG*G + lQ*Q + lEX*EX + lEY*EY ============
    results = {}
    for eid in data["elements"]:
        R = {}
        for compo in ["N_s", "Vy_s", "Mz_s"]:
            val = (lambdas["G"] * casos["G"][0][eid][compo]
                   + lambdas["Q"] * casos["Q"][0][eid][compo]
                   + lambdas["EX"] * casos["EX"][0][eid][compo]
                   + lambdas["EY"] * casos["EY"][0][eid][compo])
            R[compo] = val
        results[eid] = {"N": [round(R["N_s"], 2), round(-R["N_s"], 2)],
                        "Vy": [round(R["Vy_s"], 2), round(-R["Vy_s"], 2)],
                        "Mz": [round(R["Mz_s"], 2), round(-R["Mz_s"], 2)]}

    # ============ RESULTADO / ENVOLVENTE ============
    resum = data["resumen"]
    print("\n=== RESUMEN FINAL (por piso) ===")
    for lvl in orden:
        cols = sum(1 for ed in data["elements"].values()
                   if ed["lvl"] == lvl and ed["type"] == "column")
        vigas = sum(1 for ed in data["elements"].values()
                    if ed["lvl"] == lvl and ed["type"] == "beam")
        muros = sum(1 for ed in data["elements"].values()
                    if ed["lvl"] == lvl and ed["type"] == "wall")
        a = ws[lvl]["area_m2"]
        print(f"  {lvl:7s} cols={cols:2d} vigas={vigas:2d} muros={muros:2d} | "
              f"trib={a:6.1f} m2 | G={ws[lvl]['qG']*a:8.1f} "
              f"Q={ws[lvl]['qQ']*a:7.1f} | W={ws[lvl]['W_kN']:8.1f} kN | corte_er={lateral_EX:.0f} kN")

    print(f"\n--- Total edificio ---")
    print(f"  nodos={resum['nodos']} columnas={resum['columnas']} "
          f"vigas={resum['vigas']} muros={resum['muros']}")
    print(f"  losa={resum['losa_area_m2']} m2 | G_losa={resum['carga_losa_G_kN']} "
          f"Q={resum['carga_losa_Q_kN']} | PP={resum['peso_propio_kN']} kN")
    print(f"  W_total={sum(v['W_kN'] for v in ws.values()):.1f} kN | "
          f"V_basal_EX={lateral_EX:.1f} kN  V_basal_EY={lateral_EY:.1f} kN")
    n_crit_rot = sum(1 for r in results.values()
                     if abs(r["Mz"][0]) > 250.0)
    print(f"  elementos con |Mz|>250 kN.m (revisar): {n_crit_rot}")

    # ============ EXPORTAR A UNITY ============
    elements_out = {}
    for eid, edata in data["elements"].items():
        elements_out[eid] = edata

    pm_capacity = {
        "P": [11978.4, 9826.8, 4884.2, 0.0, -1649.3],
        "M": [0.0, 631.3, 1277.5, 513.2, 0.0],
    }
    final_output = {
        "nodes": data["nodes"],
        "elements": elements_out,
        "results": results,
        "pm_capacity": pm_capacity,
    }

    unity_json_path = "visualization/Assets/StreamingAssets/structural_data.json"
    os.makedirs(os.path.dirname(unity_json_path), exist_ok=True)
    with open(unity_json_path, "w") as f:
        json.dump(final_output, f, indent=2)

    print(f"\n[OK] Análisis OpenSeesPy completado.")
    print(f"[OK] JSON exportado a Unity en: {unity_json_path}")
    print("\n-------------------------------------------")
    print(">>> RESULTADO: PASO 3 COMPLETADO CON ÉXITO <<<")
    print("-------------------------------------------")


if __name__ == "__main__":
    run_staged_analysis()