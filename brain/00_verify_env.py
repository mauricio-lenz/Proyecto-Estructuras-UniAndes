import os


def verify_environment():
    print("=== VERIFICACIÓN DE ENTORNO (PASO 1) ===")

    # 1. Validar Carpetas
    required_dirs = ["cad_files", "brain", "visualization/Assets/StreamingAssets", "reports"]
    dirs_ok = True
    for folder in required_dirs:
        if os.path.exists(folder):
            print(f"[OK] Carpeta detectada: {folder}")
        else:
            print(f"[ERROR] Falta crear la carpeta: {folder}")
            dirs_ok = False

    # 2. Validar Librerías
    libraries = ["openseespy", "ezdxf", "shapely", "numpy", "matplotlib"]
    libs_ok = True
    for lib in libraries:
        try:
            __import__(lib)
            print(f"[OK] Librería importada: {lib}")
        except ImportError:
            print(f"[ERROR] Librería faltante: {lib}")
            libs_ok = False

    # 3. Validar Git LFS
    lfs_ok = os.path.exists(".gitattributes")
    if lfs_ok:
        print("[OK] Archivo .gitattributes configurado correctamente.")
    else:
        print("[ERROR] No se encontró el archivo .gitattributes")

    print("\n-------------------------------------------")
    if dirs_ok and libs_ok and lfs_ok:
        print(">>> RESULTADO: PASO 1 COMPLETADO CON ÉXITO <<<")
    else:
        print(">>> RESULTADO: HAY ERRORES POR CORREGIR <<<")
    print("-------------------------------------------")


if __name__ == "__main__":
    verify_environment()