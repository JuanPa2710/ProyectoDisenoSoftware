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

    if guardados != total:
        raise SystemExit("El conteo no coincide; revisá la carga.")

        from datetime import datetime, timezone

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
