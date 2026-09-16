"""
CAPACIDAD P-M POR COMPATIBILIDAD + BLOQUE DE WHITNEY (P1L4)
===========================================================
Genera los 5 puntos caracteristicos del diagrama de interaccion P-M para
secciones rectangulares de hormigon armado (columnas y muros), usando la
misma metodologia de P1L3 (parte D):

    P_o  = compresion axial pura     (0.85 fc' (Ag-As) + fy As,  M = 0)
    cfm  = compresion de flexion menor (NA en cara traccionada, c = h)
    bal  = condicion balanceada      (eps_ext = ey = fy/Es)
    flex = flexion pura              (P = 0)
    ten  = tension axial pura        (M = 0)

Convencion: P positivo = compresion, M positivo = flexion. Unidades SI m, kN.

Secciones previstas:
  - PILAR/P-70x70  : columna 0.70x0.70, 8 phi25, recubrimiento 0.05 m
  - M-20 muro      : espesor 0.20 x largo representativo, malla 2phi16@0.20
"""

EC_U = 0.003  # deformacion ultima concreto no confinado


def esfuerzos_whitney(b, h, cover, capas, fc_kPa, fy_kPa, Es_kPa, As_bar, c):
    """(P, M) por compatibilidad con bloque rectangular (eje fuerte)."""
    a = min(0.85 * c, h)
    C = 0.85 * fc_kPa * b * a
    P = C
    M = C * (h / 2.0 - a / 2.0)
    for depth, n in capas:
        eps = EC_U * (1.0 - depth / c)
        fs = min(max(Es_kPa * eps, -fy_kPa), fy_kPa)
        F = n * As_bar * fs
        P += F
        M += F * (h / 2.0 - depth)
    return P, M


def c_por_eps_t(d, eps_t):
    return d * EC_U / (EC_U + eps_t)


def puntos_interaccion(b, h, cover, capas, As_total, n_barras,
                       fc_MPa, fy_MPa, Es_kPa, nombre):
    """5 puntos caracteristicos P-M {nombre, P[kN], M[kN-m]} (P+ compresion)."""
    fc_kPa = fc_MPa * 1e3
    fy_kPa = fy_MPa * 1e3
    ey = fy_kPa / Es_kPa
    As_bar = As_total / n_barras
    Ag = b * h
    d = h - cover

    P_axial = 0.85 * fc_kPa * (Ag - As_total) + fy_kPa * As_total
    P_cfm, M_cfm = esfuerzos_whitney(b, h, cover, capas, fc_kPa, fy_kPa,
                                     Es_kPa, As_bar, h)
    P_bal, M_bal = esfuerzos_whitney(b, h, cover, capas, fc_kPa, fy_kPa,
                                     Es_kPa, As_bar, c_por_eps_t(d, ey))
    lo, hi = 1e-4, c_por_eps_t(d, ey)
    for _ in range(300):
        c = 0.5 * (lo + hi)
        if esfuerzos_whitney(b, h, cover, capas, fc_kPa, fy_kPa,
                             Es_kPa, As_bar, c)[0] > 0:
            hi = c
        else:
            lo = c
    P_flex, M_flex = esfuerzos_whitney(b, h, cover, capas, fc_kPa, fy_kPa,
                                       Es_kPa, As_bar, 0.5 * (lo + hi))
    P_tension = -fy_kPa * As_total

    return {
        "nombre": nombre,
        "P": [round(P_axial, 1), round(P_cfm, 1), round(P_bal, 1),
              round(P_flex, 1), round(P_tension, 1)],
        "M": [0.0, round(M_cfm, 1), round(M_bal, 1),
              round(M_flex, 1), 0.0],
    }


def cap_columna_70x70(fc_MPa=25.0):
    """Columna 0.70x0.70, 8 phi25, recubrimiento 0.05 (P1L3)."""
    b = h = 0.70
    cover = 0.05
    diam = 0.025
    As_total = 8 * 3.14159265 * diam ** 2 / 4.0
    capas = [(cover, 3), (h / 2.0, 2), (h - cover, 3)]
    return puntos_interaccion(b, h, cover, capas, As_total, 8, fc_MPa, 420.0,
                              200.0e6, "PILAR-70x70")


def cap_muro_20(width_m=2.90, fc_MPa=25.0):
    """Muro M-20 e=0.20, malla 2phi16@0.20 distribuida en 5 capas."""
    t = 0.20
    b = t                       # flexion fuera del plano (espesor)
    h = width_m                 # largo considerado como altura de seccion
    cover = 0.03
    diam = 0.016
    As_por_m = 2 * 2 * 3.14159265 * diam ** 2 / 4.0   # 2phi16@0.20 doble malla
    As_total = As_por_m * h
    n_barras = 10
    capas = [(cover, 2), (h / 4.0, 2), (h / 2.0, 2),
             (3 * h / 4.0, 2), (h - cover, 2)]
    return puntos_interaccion(t * 1.0, h, cover, capas, As_total, n_barras,
                              fc_MPa, 420.0, 200.0e6, "M-20")


if __name__ == "__main__":
    import json
    out = {
        "PILAR-70x70": cap_columna_70x70(),
        "M-20": cap_muro_20(),
        "M-20_ancho_3": cap_muro_20(3.0),
    }
    for k, v in out.items():
        print(f"{k}: P={v['P']}  M={v['M']}")
    with open("brain/cap_pm.json", "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)
    print("[OK] brain/cap_pm.json")