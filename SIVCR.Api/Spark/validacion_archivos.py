import csv
import re
import sys
import unicodedata
from xml.etree.ElementTree import ParseError
from zipfile import BadZipFile

from openpyxl import load_workbook
from openpyxl.utils.exceptions import InvalidFileException

from perfiles_cosevi import PERFILES


def rechazar_archivo(codigo, mensaje):
    print(f"SIVCR_VALIDACION:{codigo}", file=sys.stderr, flush=True)
    print(mensaje, file=sys.stderr, flush=True)
    raise SystemExit(2)


def normalizar_encabezado(nombre):
    nombre = unicodedata.normalize("NFC", nombre).lstrip("\ufeff")
    return re.sub(r"\s+", " ", nombre).strip()


def validar_columnas(nombres):
    nombres = [normalizar_encabezado(nombre) for nombre in nombres]
    if not nombres or any(not nombre for nombre in nombres):
        rechazar_archivo("COLUMNAS_INCORRECTAS", "Hay encabezados vacíos.")

    comparables = [nombre.casefold() for nombre in nombres]
    if len(comparables) != len(set(comparables)):
        rechazar_archivo("COLUMNAS_INCORRECTAS", "Hay encabezados repetidos.")

    coincidencias = [
        perfil for perfil, columnas in PERFILES.items()
        if set(columnas).issubset(nombres)
    ]
    if len(coincidencias) != 1:
        rechazar_archivo(
            "COLUMNAS_INCORRECTAS",
            "Se requiere un único perfil: accidentes, personas o red vial nacional."
        )
    perfil = coincidencias[0]
    return perfil, PERFILES[perfil]


def detectar_codificacion_csv(ruta):
    for codificacion in ("utf-8-sig", "cp1252"):
        try:
            with ruta.open(encoding=codificacion, newline="") as entrada:
                while entrada.read(1024 * 1024):
                    pass
            return codificacion
        except UnicodeError:
            continue
    rechazar_archivo(
        "COLUMNAS_INCORRECTAS",
        "No se pudo leer el CSV como UTF-8 ni Windows-1252."
    )


def convertir_excel_a_csv(origen, destino):
    try:
        libro = load_workbook(
            origen,
            read_only=True,
            data_only=False,
            keep_links=False
        )
    except (BadZipFile, InvalidFileException, ParseError, OSError, ValueError):
        rechazar_archivo(
            "EXCEL_INVALIDO",
            "No se pudo abrir el archivo Excel."
        )

    try:
        if len(libro.worksheets) != 1:
            rechazar_archivo(
                "EXCEL_INVALIDO",
                "El Excel debe contener exactamente una hoja."
            )

        hoja = libro.worksheets[0]
        filas = hoja.iter_rows()
        primera_fila = next(filas, None)

        if primera_fila is None:
            rechazar_archivo(
                "SIN_REGISTROS",
                "La hoja de Excel está vacía."
            )

        if any(celda.data_type in {"f", "e"} for celda in primera_fila):
            rechazar_archivo(
                "EXCEL_INVALIDO",
                "Los encabezados no pueden contener fórmulas ni errores."
            )

        encabezados = [
            "" if celda.value is None else str(celda.value).strip()
            for celda in primera_fila
        ]
        validar_columnas(encabezados)
        cantidad = 0

        with destino.open("w", encoding="utf-8", newline="") as salida:
            escritor = csv.writer(salida, delimiter=";")
            escritor.writerow(encabezados)

            for numero_fila, fila in enumerate(filas, start=2):
                valores = [celda.value for celda in fila]
                if all(
                    valor is None
                    or (isinstance(valor, str) and not valor.strip())
                    for valor in valores
                ):
                    continue

                if any(celda.data_type in {"f", "e"} for celda in fila):
                    rechazar_archivo(
                        "EXCEL_INVALIDO",
                        f"La fila {numero_fila} contiene fórmulas o errores. "
                        "Exportá el archivo con valores."
                    )

                escritor.writerow([
                    "" if valor is None else str(valor)
                    for valor in valores
                ])
                cantidad += 1

        if cantidad == 0:
            rechazar_archivo(
                "SIN_REGISTROS",
                "El Excel contiene encabezados, pero no tiene registros."
            )
    finally:
        libro.close()
