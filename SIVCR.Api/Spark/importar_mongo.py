import hashlib
import argparse
import os
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from getpass import getpass
from pymongo import MongoClient, ReplaceOne

# Recibir la carpeta que generó Spark.
parser = argparse.ArgumentParser(
    description="Importar los JSON procesados a MongoDB."
)

parser.add_argument(
    "carpeta",
    type=Path,
    help="Carpeta de resultados generada por Spark."
)

parser.add_argument(
    "--carga-id",
    help="Identificador de la carga asignado por la API."
)

argumentos = parser.parse_args()
carpeta = argumentos.carpeta.expanduser().resolve()
archivos = sorted(carpeta.glob("part-*.json"))

if not archivos or not (carpeta / "_SUCCESS").exists():
    raise SystemExit("No se encontró una exportación completa de Spark.")

# Validar el reporte antes de modificar MongoDB.
try:
    reporte = json.loads(
        (carpeta / "reporte.json").read_text(encoding="utf-8")
    )
except (OSError, ValueError) as error:
    raise SystemExit(
        "No se pudo leer el reporte. Volvé a procesar el archivo."
    ) from error

if not isinstance(reporte, dict):
    raise SystemExit("El reporte debe ser un objeto JSON.")

campos_conteo = (
    "registros_leidos",
    "registros_aceptados",
    "registros_rechazados",
    "documentos_generados",
)

for campo in campos_conteo:
    valor = reporte.get(campo)
    if type(valor) is not int or valor < 0:
        raise SystemExit(f"Conteo inválido en el reporte: {campo}.")

leidos = reporte["registros_leidos"]
aceptados = reporte["registros_aceptados"]
rechazados = reporte["registros_rechazados"]
generados = reporte["documentos_generados"]

if leidos != aceptados + rechazados or generados != aceptados:
    raise SystemExit("Los conteos del reporte no coinciden.")

if aceptados == 0:
    raise SystemExit("No hay registros válidos para importar.")

if reporte.get("puede_importar") is not True:
    raise SystemExit("El reporte no permite importar esta carga.")

# Revisar todos los documentos antes de comenzar la escritura.
documentos_verificados = 0

for archivo in archivos:
    with archivo.open(encoding="utf-8") as entrada:
        for numero_linea, linea in enumerate(entrada, start=1):
            if not linea.strip():
                continue

            try:
                documento = json.loads(linea)
                if not isinstance(documento, dict):
                    raise ValueError("Se esperaba un objeto JSON.")

                json.dumps(documento, allow_nan=False)
            except (ValueError, TypeError) as error:
                raise SystemExit(
                    f"Documento inválido en {archivo.name}, "
                    f"línea {numero_linea}."
                ) from error

            documentos_verificados += 1

if documentos_verificados != aceptados:
    raise SystemExit(
        "La cantidad de documentos no coincide con los aceptados "
        "del reporte. No se inició la importación."
    )

carga_id = argumentos.carga_id or ("cosevi-" + carpeta.name)
clave = os.environ.get("SIVCR_MONGO_PASSWORD")

if not clave:
    if sys.stdin.isatty():
        clave = getpass("Contraseña de desarrollador: ")
    else:
        raise SystemExit(
            "Falta configurar SIVCR_MONGO_PASSWORD."
        )

with MongoClient(
    host="127.0.0.1",
    port=27017,
    username="desarrollador",
    password=clave,
    authSource="proyecto",
    serverSelectionTimeoutMS=5000,
) as cliente:
    cliente.admin.command("ping")
    coleccion = cliente["proyecto"]["Incidencias"]

    lote = []
    total = 0
    ocurrencias = {}
    for archivo in archivos:
        with archivo.open(encoding="utf-8") as entrada:
            for numero_linea, linea in enumerate(entrada, start=1):
                if not linea.strip():
                    continue

                documento = json.loads(linea)

                # Identificar cada registro por su posición en esta exportación.
                if argumentos.carga_id:
                    contenido = json.dumps(
                        documento,
                        sort_keys=True,
                        ensure_ascii=False,
                        separators=(",", ":"),
                        allow_nan=False,
                    )

                    huella = hashlib.sha256(
                        contenido.encode("utf-8")
                    ).hexdigest()

                    repeticion = ocurrencias.get(huella, 0) + 1
                    ocurrencias[huella] = repeticion

                    identificador = (
                        f"{carga_id}:{huella}:{repeticion}"
                    )
                else:
                    # Conservar los identificadores de la carga manual anterior.
                    identificador = (
                        f"{carga_id}:{archivo.name}:{numero_linea}"
                    )

                documento["_id"] = identificador
                documento["carga_id"] = carga_id

                lote.append(
                    ReplaceOne(
                        {"_id": identificador},
                        documento,
                        upsert=True,
                    )
                )

                total += 1

                if len(lote) == 500:
                    coleccion.bulk_write(lote)
                    lote.clear()
                    print(f"Procesados: {total}", flush=True)

    if lote:
        coleccion.bulk_write(lote)

    guardados = coleccion.count_documents({"carga_id": carga_id})

    print("Documentos leídos:", total)
    print("Documentos guardados en esta carga:", guardados)

    if total != aceptados or guardados != aceptados:
        raise SystemExit(
            "El conteo final no coincide con los aceptados. "
            "El historial no se marcará como completado."
        )

    ahora = datetime.now(timezone.utc)

    cliente["proyecto"]["Cargas"].update_one(
        {"_id": carga_id},
        {
            "$set": {
                "fuente": "COSEVI",
                "coleccion_destino": "Incidencias",
                "carpeta_json": str(carpeta),
                "estado": "Importada",
                "documentos_leidos": total,
                "documentos_guardados": guardados,
                "registros_leidos": leidos,
                "registros_aceptados": aceptados,
                "registros_rechazados": rechazados,
                "reporte_validacion": reporte,
                "ultima_verificacion": ahora,
            },
            "$setOnInsert": {
                "fecha_registro": ahora,
            },
        },
        upsert=True,
    )

    print("Importación completada.")
    print("Historial guardado en Cargas:", carga_id)
