import argparse
import csv
import json
from datetime import datetime
from pathlib import Path
from tempfile import TemporaryDirectory

from pyspark.sql import SparkSession
from pyspark.sql import functions as F
from pyspark.sql.types import ArrayType, MapType, StructType

from documentos_spark import (
    documentos_accidentes,
    documentos_personas,
    documentos_red_vial,
)
from transformaciones_spark import (
    crear_fallo,
    esta_vacio,
    preparar_red_vial,
    preparar_registros_generales,
)
from validacion_archivos import (
    convertir_excel_a_csv,
    detectar_codificacion_csv,
    normalizar_encabezado,
    rechazar_archivo,
    validar_columnas,
)


parser = argparse.ArgumentParser(
    description="Procesar accidentes o personas de COSEVI con Spark."
)
parser.add_argument(
    "--entrada",
    required=True,
    type=Path,
    help="Ruta del archivo CSV, JSON o Excel (.xlsx) de COSEVI."
)
parser.add_argument(
    "--salida",
    type=Path,
    help="Carpeta nueva donde guardar los JSON procesados."
)
argumentos = parser.parse_args()
ruta_entrada = argumentos.entrada.expanduser().resolve()

if not ruta_entrada.is_file():
    parser.error(f"No se encontró el archivo: {ruta_entrada}")

formato = ruta_entrada.suffix.lower()
if formato not in {".csv", ".json", ".xlsx"}:
    parser.error("Se permiten archivos CSV, JSON y Excel (.xlsx).")

codificacion_csv = "utf-8-sig"
separador_csv = ";"

# revisar el encabezado antes de que spark lea el CSV
if formato == ".csv":
    try:
        codificacion_csv = detectar_codificacion_csv(ruta_entrada)
        with ruta_entrada.open(
            encoding=codificacion_csv,
            newline=""
        ) as entrada:
            separador_csv = ";"
            encabezado = next(
                csv.reader(
                    entrada,
                    delimiter=separador_csv,
                    strict=True
                ),
                None
            )
            if encabezado and len(encabezado) == 1:
                entrada.seek(0)
                separador_csv = ","
                encabezado = next(
                    csv.reader(
                        entrada,
                        delimiter=separador_csv,
                        strict=True
                    ),
                    None
                )
    except (UnicodeError, csv.Error):
        parser.error(
            "Se requiere un CSV UTF-8 o Windows-1252 "
            "delimitado por coma o punto y coma."
        )
    except OSError:
        parser.error("No se pudo abrir el archivo de entrada.")

    if not encabezado:
        rechazar_archivo(
            "SIN_REGISTROS",
            "El archivo está vacío o no contiene encabezados."
        )

    validar_columnas(encabezado)

spark = (
    SparkSession.builder
    .appName("CargaAccidentesSIVCR")
    .master("local[2]")
    .config("spark.ui.enabled", "false")
    .getOrCreate()
)
spark.sparkContext.setLogLevel("ERROR")
temporal = None

try:
    archivo = str(ruta_entrada)

    if formato == ".csv" and codificacion_csv == "cp1252":
        temporal = TemporaryDirectory(prefix="sivcr-csv-")
        archivo_temporal = Path(temporal.name) / "entrada.csv"
        with ruta_entrada.open(encoding="cp1252", newline="") as origen:
            with archivo_temporal.open(
                "w",
                encoding="utf-8",
                newline=""
            ) as destino:
                while True:
                    bloque = origen.read(1024 * 1024)
                    if not bloque:
                        break
                    destino.write(bloque)
        archivo = str(archivo_temporal)

    if formato == ".xlsx":
        temporal = TemporaryDirectory(prefix="sivcr-excel-")
        archivo_temporal = Path(temporal.name) / "entrada.csv"
        convertir_excel_a_csv(ruta_entrada, archivo_temporal)
        archivo = str(archivo_temporal)

    if formato in {".csv", ".xlsx"}:
        datos = (
            spark.read
            .option("header", "true")
            .option("sep", separador_csv if formato == ".csv" else ";")
            .option("escape", '"')
            .option("multiLine", "true")
            .option("encoding", "UTF-8")
            .csv(archivo)
        )
    else:
        datos = (
            spark.read
            .option("multiLine", "true")
            .option("mode", "FAILFAST")
            .option("primitivesAsString", "true")
            .json(archivo)
        )

    if not datos.take(1):
        rechazar_archivo(
            "SIN_REGISTROS",
            "El archivo no contiene registros para procesar."
        )

    perfil, columnas_requeridas = validar_columnas(datos.columns)
    datos = datos.toDF(*[
        normalizar_encabezado(nombre)
        for nombre in datos.columns
    ])
    datos = datos.select(*columnas_requeridas)

    campos_complejos = [
        campo.name
        for campo in datos.schema.fields
        if isinstance(campo.dataType, (ArrayType, MapType, StructType))
    ]
    if campos_complejos:
        rechazar_archivo(
            "COLUMNAS_INCORRECTAS",
            "Estos campos deben contener valores simples: "
            + ", ".join(campos_complejos)
        )

    datos = datos.select(*[
        F.trim(F.col(nombre)).alias(nombre)
        for nombre in datos.columns
    ])

    print("\nCOLUMNAS LIMPIAS:", datos.columns)
    print("\nTOTAL DE REGISTROS:", datos.count())

    if perfil == "red_vial_nacional":
        datos = preparar_red_vial(datos)
    else:
        datos.select(
            "Año", "Provincia", "Cantón", "Día", "Mes"
        ).show(5, truncate=False)

        datos = preparar_registros_generales(datos, perfil)

        if perfil == "accidentes_cosevi":
            print("\nEJEMPLOS DE CONVERSIÓN:")
            datos.select(
                "Kilómetro", "kilometro_numero"
            ).distinct().orderBy("Kilómetro").show(20, truncate=False)
        else:
            datos.select("Edad", "edad_numero").distinct().show(
                20,
                truncate=False
            )

        print("\nREGISTROS SIN AÑO O MES NUMÉRICO:")
        print(datos.filter(
            F.col("anio").isNull() | F.col("mes_numero").isNull()
        ).count())

        print("\nREGISTROS SIN DÍA RECONOCIDO:")
        print(datos.filter(
            F.col("dia_semana_numero").isNull()
        ).count())

        anio_actual = datetime.now().year
        provincias_validas = [
            "san josé",
            "alajuela",
            "cartago",
            "heredia",
            "guanacaste",
            "puntarenas",
            "limón",
        ]

        validaciones = [
            crear_fallo(
                F.col("anio").isNull()
                | ~F.col("anio").between(1900, anio_actual),
                "Año",
                f"Debe ser un año entero entre 1900 y {anio_actual}."
            ),
            crear_fallo(
                F.col("mes_numero").isNull(),
                "Mes",
                "Mes no reconocido."
            ),
            crear_fallo(
                F.col("dia_semana_numero").isNull(),
                "Día",
                "Día no reconocido."
            ),
            crear_fallo(
                esta_vacio("Provincia")
                | ~F.lower(F.col("Provincia")).isin(provincias_validas),
                "Provincia",
                "Debe indicar una de las siete provincias de Costa Rica."
            ),
        ]

        if perfil == "accidentes_cosevi":
            validaciones.extend([
                crear_fallo(
                    esta_vacio("Clase de accidente"),
                    "Clase de accidente",
                    "La clase es obligatoria."
                ),
                crear_fallo(
                    esta_vacio("Tipo de accidente"),
                    "Tipo de accidente",
                    "El tipo es obligatorio."
                ),
            ])
        else:
            desconocida = (
                esta_vacio("Edad")
                | (F.lower(F.col("Edad")) == "desconocida")
            )
            invalida = ~desconocida & (
                ~F.col("Edad").rlike(r"^\d+$")
                | F.col("edad_numero").isNull()
                | (F.col("edad_numero") < 0)
            )
            for campo in [
                "Rol",
                "Tipo de lesión",
                "Sexo",
                "Vehiculo en el que viajaba",
            ]:
                validaciones.append(crear_fallo(
                    esta_vacio(campo),
                    campo,
                    "El campo es obligatorio."
                ))
            validaciones.append(crear_fallo(
                invalida,
                "Edad",
                "Debe ser un entero no negativo o Desconocida."
            ))

        datos = datos.withColumn(
            "primer_error",
            F.coalesce(*validaciones)
        )

    print("\nRESUMEN DE VALIDACIÓN POR REGISTRO:")
    datos.select(
        F.when(
            F.col("primer_error").isNull(),
            F.lit("Válido")
        ).otherwise(F.lit("Rechazado")).alias("resultado")
    ).groupBy("resultado").count().show(truncate=False)

    print("\nPRIMER FALLO DE LOS REGISTROS RECHAZADOS:")
    datos.filter(F.col("primer_error").isNotNull()).select(
        "primer_error.campo",
        "primer_error.valor",
        "primer_error.problema"
    ).show(20, truncate=False)

    validos = datos.filter(F.col("primer_error").isNull())
    rechazados = datos.filter(F.col("primer_error").isNotNull())

    total_leidos = datos.count()
    total_aceptados = validos.count()
    total_rechazados = rechazados.count()

    if total_leidos != total_aceptados + total_rechazados:
        raise RuntimeError(
            "Los conteos de validación no coinciden."
        )

    if perfil == "red_vial_nacional":
        documentos = documentos_red_vial(validos)
    elif perfil == "accidentes_cosevi":
        documentos = documentos_accidentes(validos)
    else:
        documentos = documentos_personas(validos)

    print("\nEJEMPLO DE DOCUMENTO JSON:")
    ejemplos = documentos.limit(1).toJSON(use_unicode=True).collect()
    if ejemplos:
        print(json.dumps(
            json.loads(ejemplos[0]),
            indent=2,
            ensure_ascii=False
        ))
    else:
        print("No hay registros válidos para importar.")

    if argumentos.salida is not None:
        salida = str(argumentos.salida.expanduser().resolve())
    else:
        nombre_carga = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
        salida = str(
            Path.home() / "apps/datos/procesados" / nombre_carga
        )

    documentos.write.mode("errorifexists").option(
        "ignoreNullFields",
        "false"
    ).json(salida)

    guardados = (
        spark.read
        .schema(documentos.schema)
        .json(str(Path(salida) / "part-*.json"))
        .count()
    )

    if guardados != total_aceptados:
        raise RuntimeError(
            "La cantidad guardada no coincide con los registros aceptados."
        )

    detalle_rechazados = rechazados.select(
        F.col("primer_error.campo").alias("campo"),
        F.col("primer_error.valor").alias("valor"),
        F.col("primer_error.problema").alias("problema"),
        F.struct(*[
            F.col(nombre) for nombre in columnas_requeridas
        ]).alias("registro_original")
    )

    carpeta_rechazados = Path(salida) / "rechazados"
    if total_rechazados > 0:
        detalle_rechazados.write.mode("errorifexists").option(
            "ignoreNullFields",
            "false"
        ).json(str(carpeta_rechazados))

    errores_por_campo = {
        fila["campo"]: fila["count"]
        for fila in (
            detalle_rechazados
            .groupBy("campo")
            .count()
            .collect()
        )
    }

    muestra_errores = [
        fila.asDict()
        for fila in (
            detalle_rechazados
            .select("campo", "valor", "problema")
            .orderBy("campo", "valor", "problema")
            .limit(20)
            .collect()
        )
    ]

    reporte = {
        "perfil": perfil,
        "registros_leidos": total_leidos,
        "registros_aceptados": total_aceptados,
        "registros_rechazados": total_rechazados,
        "documentos_generados": guardados,
        "puede_importar": total_aceptados > 0,
        "errores_por_campo": errores_por_campo,
        "muestra_errores": muestra_errores,
        "carpeta_rechazados": (
            "rechazados" if total_rechazados > 0 else None
        ),
    }

    if perfil == "red_vial_nacional":
        reporte["notas"] = [
            "Cada fila aceptada representa una sección del inventario vial.",
            "Las fechas del inventario se conservan como datos de la fuente.",
            "Una ruta y un distrito pueden contener varias secciones."
        ]
    else:
        sin_anio_mes = datos.filter(
            F.col("anio").isNull() | F.col("mes_numero").isNull()
        ).count()
        sin_dia = datos.filter(
            F.col("dia_semana_numero").isNull()
        ).count()
        sin_kilometro = None
        sin_edad = None
        if perfil == "accidentes_cosevi":
            sin_kilometro = datos.filter(
                F.col("kilometro_numero").isNull()
            ).count()
        else:
            sin_edad = datos.filter(
                F.col("edad_numero").isNull()
            ).count()

        reporte.update({
            "registros_sin_anio_o_mes": sin_anio_mes,
            "registros_sin_dia_semana": sin_dia,
            "registros_sin_kilometro_numerico": sin_kilometro,
            "registros_sin_edad_numerica": sin_edad,
        })
        reporte["notas"] = [
            "Cada registro rechazado informa únicamente su primer fallo.",
            "El día representa el día de la semana, no el día del mes.",
            (
                "Un kilómetro no numérico no implica descartar el accidente."
                if perfil == "accidentes_cosevi"
                else "Una edad vacía o Desconocida se conserva como null."
            )
        ]

    ruta_reporte = Path(salida) / "reporte.json"
    ruta_reporte.write_text(
        json.dumps(reporte, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )

    print("\nRESUMEN FINAL:")
    print("REGISTROS LEÍDOS:", total_leidos)
    print("REGISTROS ACEPTADOS:", total_aceptados)
    print("REGISTROS RECHAZADOS:", total_rechazados)
    print("DOCUMENTOS GUARDADOS:", guardados)
    print("REPORTE GUARDADO EN:", ruta_reporte)

finally:
    try:
        spark.stop()
    finally:
        if temporal is not None:
            temporal.cleanup()
