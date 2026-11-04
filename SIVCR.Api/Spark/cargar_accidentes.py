from tempfile import TemporaryDirectory
from zipfile import BadZipFile
from xml.etree.ElementTree import ParseError
from openpyxl import load_workbook
from openpyxl.utils.exceptions import InvalidFileException
import sys
import csv
import re
import unicodedata
import argparse
from pyspark.sql.types import ArrayType, MapType, StructType
from pathlib import Path
from pyspark.sql import SparkSession
from pyspark.sql import functions as F


def rechazar_archivo(codigo, mensaje):
    print(f"SIVCR_VALIDACION:{codigo}", file=sys.stderr, flush=True)
    print(mensaje, file=sys.stderr, flush=True)
    raise SystemExit(2)

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

# Columnas necesarias para transformar el perfil de accidentes COSEVI.
columnas_accidentes = [
    "Clase de accidente",
    "Tipo de accidente",
    "Año",
    "Hora",
    "Hora recodificada",
    "Provincia",
    "Cantón",
    "Distrito",
    "Ruta",
    "Kilómetro",
    "Rural o urbano",
    "Calzada vertical",
    "Calzada horizontal",
    "Tipo de calzada",
    "Tipo de circulación",
    "Estado del tiempo",
    "Estado de la calzada",
    "Región Mideplan",
    "Tipo ruta",
    "Día",
    "Mes",
]

columnas_personas = [
    "Rol", "Tipo de lesión", "Edad", "Sexo",
    "Vehiculo en el que viajaba", "Año", "Provincia", "Cantón",
    "Distrito", "Día", "Mes", "Edad quinquenal",
]

columnas_viales = [
    "Seccion", "Provincia", "Canton", "Distrito", "Nom_Provin",
    "Nom_Canton", "Nom_Distri", "Region_MOP", "Region_MID", "Zona_CONAV",
    "Shape_leng", "Ruta", "Seccion_1", "Descripcio", "Jerarquia",
    "Cod_Rve", "Rve_Descri", "Longitud_K", "Longitud_A", "N_Car",
    "Fecha_Inv", "Fecha_Rec", "Cod_Sup", "Tipo_Super", "Cod_EstSup",
    "Estado_Sup", "Hor_Bueno", "Hor_Regula", "Hor_Malo", "Total",
    "Ca_Buena", "Ca_Regular", "Ca_Mala", "Total1", "Ts_Bueno",
    "Ts_Regular", "Ts_Malo", "Total2", "Tsi_Bueno", "Tsi_Regula",
    "Tsi_Malo", "Total3", "Asfalto_To", "Las_Bueno", "Las_Regula",
    "Las_Malo", "Total4", "Tie_Buena", "Tie_Regula", "Tie_Malo",
    "Total5", "Otros_Buen", "Otros_Regu", "Otros_Malo", "Total6",
    "SA_Buena", "SA_Regular", "SA_Malo", "Total7", "Mp_Buena",
    "Mp_Regular", "Mp_Malo", "Total8", "Puentes_In", "Cod_Terren",
    "Tipo_Terre", "Cod_Espald", "Tipo_Espal", "Cod_EstEsp", "Est_Espald",
    "Anc_Espald", "Cod_Drenaj", "Drenaje", "Cod_Alinea", "Alineamien",
    "Cod_Pendie", "Pendiente", "Cod_Visibi", "Visibilida", "Cod_Senala",
    "Senalamien", "Cod_EstPav", "Estructura",
]

def preparar_red_vial(datos):
    from pyspark.sql.window import Window

    km = F.expr("try_cast(replace(`Longitud_K`, ',', '.') AS DOUBLE)")
    datos = datos.withColumn("longitud_km", km)
    repeticiones = F.count("*").over(Window.partitionBy("Seccion"))

    def error(condicion, campo, motivo):
        return F.when(condicion, F.struct(
            F.lit(campo).alias("campo"), F.col(campo).alias("valor"),
            F.lit(motivo).alias("problema")
        ))

    reglas = [
        error(F.col(campo).isNull() | (F.col(campo) == ""), campo, "Campo obligatorio.")
        for campo in ["Seccion", "Ruta", "Nom_Provin", "Nom_Canton", "Nom_Distri"]
    ]
    reglas.append(error(repeticiones > 1, "Seccion", "Sección repetida en esta carga."))
    longitud = F.col("longitud_km")
    reglas.append(error(
        longitud.isNull() | F.isnan(longitud) | (longitud < 0)
        | (longitud == float("inf")),
        "Longitud_K", "La longitud debe ser un número finito no negativo."
    ))
    return datos.withColumn("primer_error", F.coalesce(*reglas))


def documentos_red_vial(validos):
    return validos.select(
        F.lit("red_vial_nacional").alias("perfil"),
        F.lit("Red Vial Nacional").alias("fuente"),
        F.col("Seccion").alias("seccion"),
        F.struct(
            F.col("Ruta").alias("ruta"),
            F.col("Nom_Provin").alias("provincia"),
            F.col("Nom_Canton").alias("canton"),
            F.col("Nom_Distri").alias("distrito"),
            F.col("Provincia").alias("codigo_provincia"),
            F.col("Canton").alias("codigo_canton"),
            F.col("Distrito").alias("codigo_distrito")
        ).alias("ubicacion"),
        F.col("longitud_km"),
        F.col("Jerarquia").alias("jerarquia"),
        F.col("Tipo_Super").alias("tipo_superficie"),
        F.col("Estado_Sup").alias("estado_superficie"),
        F.col("Fecha_Inv").alias("fecha_inventario_original"),
        F.col("Fecha_Rec").alias("fecha_rec_original"),
        F.struct(*[F.col(c) for c in columnas_viales]).alias("atributos")
    )


perfiles = {
    "red_vial_nacional": columnas_viales,
    "accidentes_cosevi": columnas_accidentes,
    "personas_cosevi": columnas_personas,
}


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
        perfil for perfil, columnas in perfiles.items()
        if set(columnas).issubset(nombres)
    ]
    if len(coincidencias) != 1:
        rechazar_archivo(
            "COLUMNAS_INCORRECTAS",
            "Se requiere un único perfil: accidentes, personas o red vial nacional."
        )
    perfil = coincidencias[0]
    return perfil, perfiles[perfil]


def detectar_codificacion_csv(ruta):
    # Revisar todo el archivo: un encabezado ASCII no garantiza UTF-8.
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

                # Ignorar filas completamente vacías.
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

formato = ruta_entrada.suffix.lower()

if formato not in {".csv", ".json",".xlsx"}:
    parser.error("Se permiten archivos CSV, JSON y ·Excel (.xlsx).")

# Revisar el encabezado original del CSV antes de que Spark lo lea.
if formato == ".csv":
    try:
        codificacion_csv = detectar_codificacion_csv(ruta_entrada)
        with ruta_entrada.open(
            encoding=codificacion_csv, newline=""
        ) as entrada:
            separador_csv = ";"
            encabezado = next(csv.reader(entrada, delimiter=separador_csv, strict=True), None)
            if encabezado and len(encabezado) == 1:
                entrada.seek(0)
                separador_csv = ","
                encabezado = next(csv.reader(entrada, delimiter=separador_csv, strict=True), None)
    except (UnicodeError, csv.Error):
        parser.error(
            "Se requiere un CSV UTF-8 o Windows-1252 delimitado por coma o punto y coma."
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
            with archivo_temporal.open("w", encoding="utf-8", newline="") as destino:
                while bloque := origen.read(1024 * 1024):
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
        normalizar_encabezado(nombre) for nombre in datos.columns
    ])
    datos = datos.select(*columnas_requeridas)


    # Este perfil espera campos simples, no objetos ni listas anidadas.
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


    # Quitar espacios al inicio y al final de cada valor.
    datos = datos.select(
        *[F.trim(F.col(nombre)).alias(nombre) for nombre in datos.columns]
    )

    print("\nCOLUMNAS LIMPIAS:", datos.columns)
    print("\nTOTAL DE REGISTROS:", datos.count())


    if perfil == "red_vial_nacional":
        datos = preparar_red_vial(datos)
    else:
        datos.select(
            "Año", "Provincia", "Cantón", "Día", "Mes"
        ).show(5, truncate=False)
        # Revisar los valores y su frecuencia antes de transformarlos.
        campos_revision = ["Año", "Mes", "Día"]
        campos_revision.append("Kilómetro" if perfil == "accidentes_cosevi" else "Edad")
        for campo in campos_revision:
            print(f"\nVALORES DE: {campo}")

            datos.groupBy(campo).count().orderBy(
                F.desc("count")
            ).show(15, truncate=False)


        if perfil == "accidentes_cosevi":
            # Conservar Kilómetro y crear una versión numérica aparte.
            datos = datos.withColumn(
                "kilometro_numero",
                F.expr("try_cast(replace(`Kilómetro`, ',', '.') AS DOUBLE)")
            )

            print("\nEJEMPLOS DE CONVERSIÓN:")
            datos.select(
                "Kilómetro", "kilometro_numero"
            ).distinct().orderBy("Kilómetro").show(20, truncate=False)

            print("\nVALORES QUE NO SE PUDIERON CONVERTIR:")
            datos.filter(
                F.col("kilometro_numero").isNull()
            ).groupBy("Kilómetro").count().orderBy(
                F.desc("count")
            ).show(30, truncate=False)

        else:
            datos = datos.withColumn("edad_numero", F.expr("try_cast(`Edad` AS INT)"))

        # Convertir el año a un número entero.
        datos = datos.withColumn(
            "anio",
            F.expr("try_cast(`Año` AS INT)")
        )

        # Relacionar las etiquetas del archivo con números de mes.
        meses = {
            "A. Enero": 1,
            "B. Febrero": 2,
            "C. Marzo": 3,
            "D. Abril": 4,
            "E. Mayo": 5,
            "F. Junio": 6,
            "G. Julio": 7,
            "H. Agosto": 8,
            "I. Setiembre": 9,
            "J. Octubre": 10,
            "K. Noviembre": 11,
            "L. Diciembre": 12,
        }

        pares = []
        for etiqueta, numero in meses.items():
            pares.extend([F.lit(etiqueta), F.lit(numero)])

        mapa_meses = F.create_map(*pares)

        datos = datos.withColumn(
            "mes_numero",
            F.element_at(mapa_meses, F.col("Mes"))
        )

        print("\nAÑO Y MES CONVERTIDOS:")
        datos.select(
            "Año", "anio", "Mes", "mes_numero"
        ).show(5, truncate=False)

        print("\nREGISTROS SIN AÑO O MES NUMÉRICO:")
        print(
            datos.filter(
                F.col("anio").isNull()
                | F.col("mes_numero").isNull()
            ).count()
        )

        # Traducir las etiquetas de COSEVI a números de día de semana.
        dias = {
            "1.Domingo": 1,
            "2.Lunes": 2,
            "3.Martes": 3,
            "4.Miércoles": 4,
            "5.Jueves": 5,
            "6.Viernes": 6,
            "7.Sábado": 7,
        }

        pares_dias = []
        for etiqueta, numero in dias.items():
            pares_dias.extend([F.lit(etiqueta), F.lit(numero)])

        mapa_dias = F.create_map(*pares_dias)

        datos = datos.withColumn(
            "dia_semana_numero",
            F.element_at(mapa_dias, F.col("Día"))
        )

        print("\nDÍAS CONVERTIDOS:")
        datos.select(
            "Día", "dia_semana_numero"
        ).distinct().orderBy("dia_semana_numero").show(
            10, truncate=False
        )

        print("\nREGISTROS SIN DÍA RECONOCIDO:")
        print(
            datos.filter(
                F.col("dia_semana_numero").isNull()
            ).count()
        )

        # Validaciones ordenadas: conservar solo el primer fallo.
        from datetime import datetime

        anio_actual = datetime.now().year

        def fallo(condicion, campo, mensaje):
            return F.when(
                condicion,
                F.struct(
                    F.lit(campo).alias("campo"),
                    F.col(campo).cast("string").alias("valor"),
                    F.lit(mensaje).alias("problema")
                )
            )

        def esta_vacio(campo):
            return (
                F.col(campo).isNull()
                | (F.trim(F.col(campo)) == "")
            )

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
            fallo(
                F.col("anio").isNull() | ~F.col("anio").between(1900, anio_actual),
                "Año", f"Debe ser un año entero entre 1900 y {anio_actual}."
            ),
            fallo(F.col("mes_numero").isNull(), "Mes", "Mes no reconocido."),
            fallo(F.col("dia_semana_numero").isNull(), "Día", "Día no reconocido."),
            fallo(
                esta_vacio("Provincia") | ~F.lower(F.col("Provincia")).isin(provincias_validas),
                "Provincia", "Debe indicar una de las siete provincias de Costa Rica."
            ),
        ]
        if perfil == "accidentes_cosevi":
            validaciones.extend([
                fallo(esta_vacio("Clase de accidente"), "Clase de accidente", "La clase es obligatoria."),
                fallo(esta_vacio("Tipo de accidente"), "Tipo de accidente", "El tipo es obligatorio."),
            ])
        else:
            desconocida = esta_vacio("Edad") | (F.lower(F.col("Edad")) == "desconocida")
            invalida = ~desconocida & (
                ~F.col("Edad").rlike(r"^\d+$")
                | F.col("edad_numero").isNull() | (F.col("edad_numero") < 0)
            )
            for campo in ["Rol", "Tipo de lesión", "Sexo", "Vehiculo en el que viajaba"]:
                validaciones.append(fallo(esta_vacio(campo), campo, "El campo es obligatorio."))
            validaciones.append(fallo(invalida, "Edad", "Debe ser un entero no negativo o Desconocida."))

        datos = datos.withColumn("primer_error", F.coalesce(*validaciones))

    print("\nRESUMEN DE VALIDACIÓN POR REGISTRO:")
    datos.select(
        F.when(
            F.col("primer_error").isNull(),
            F.lit("Válido")
        ).otherwise(F.lit("Rechazado")).alias("resultado")
    ).groupBy("resultado").count().show(truncate=False)

    print("\nPRIMER FALLO DE LOS REGISTROS RECHAZADOS:")
    datos.filter(
        F.col("primer_error").isNotNull()
    ).select(
        "primer_error.campo",
        "primer_error.valor",
        "primer_error.problema"
    ).show(20, truncate=False)

    # Separar los registros sin modificar el conjunto original.
    validos = datos.filter(F.col("primer_error").isNull())
    rechazados = datos.filter(F.col("primer_error").isNotNull())

    total_leidos = datos.count()
    total_aceptados = validos.count()
    total_rechazados = rechazados.count()

    if total_leidos != total_aceptados + total_rechazados:
        raise RuntimeError("Los conteos de validación no coinciden.")


    # Organizar los datos en documentos con campos agrupados.
    if perfil == "red_vial_nacional":
        documentos = documentos_red_vial(validos)
    elif perfil == "accidentes_cosevi":
        documentos = validos.select(
            F.lit("COSEVI").alias("fuente"),

            F.col("Clase de accidente").alias("clase_accidente"),
            F.col("Tipo de accidente").alias("tipo_accidente"),

            F.struct(
                F.col("anio"),
                F.col("mes_numero").alias("mes"),
                F.col("dia_semana_numero").alias("dia_semana"),
                F.col("Hora").alias("intervalo_hora")
            ).alias("tiempo"),

            F.struct(
                F.col("Provincia").alias("provincia"),
                F.col("Cantón").alias("canton"),
                F.col("Distrito").alias("distrito"),
                F.col("Ruta").alias("ruta"),
                F.col("kilometro_numero").alias("kilometro"),
                F.col("Kilómetro").alias("kilometro_original")
                    ).alias("ubicacion"),

            F.struct(
                F.col("Estado del tiempo").alias("clima"),
                F.col("Estado de la calzada").alias("estado_calzada"),
                F.col("Tipo de calzada").alias("tipo_calzada"),
                F.col("Calzada vertical").alias("calzada_vertical"),
                F.col("Calzada horizontal").alias("calzada_horizontal"),
                F.col("Tipo de circulación").alias("tipo_circulacion"),
                F.col("Rural o urbano").alias("zona")
            ).alias("condiciones"),

            F.col("Región Mideplan").alias("region_mideplan"),
            F.col("Tipo ruta").alias("tipo_ruta"),
            F.col("Hora recodificada").alias("franja_horaria")
        )

    else:
        documentos = validos.select(
            F.lit("COSEVI").alias("fuente"),
            F.lit("personas_cosevi").alias("perfil"),
            F.col("Rol").alias("rol"),
            F.col("Tipo de lesión").alias("tipo_lesion"),
            F.col("edad_numero").alias("edad"),
            F.col("Edad").alias("edad_original"),
            F.col("Sexo").alias("sexo"),
            F.col("Vehiculo en el que viajaba").alias("vehiculo"),
            F.col("Edad quinquenal").alias("edad_quinquenal"),
            F.struct(
                F.col("anio"), F.col("mes_numero").alias("mes"),
                F.col("dia_semana_numero").alias("dia_semana")
            ).alias("tiempo"),
            F.struct(
                F.col("Provincia").alias("provincia"),
                F.col("Cantón").alias("canton"),
                F.col("Distrito").alias("distrito")
            ).alias("ubicacion")
        )

    import json
    from datetime import datetime

    # Mostrar un ejemplo solo si existe algún documento válido.
    print("\nEJEMPLO DE DOCUMENTO JSON:")
    ejemplos = documentos.limit(1).toJSON(
        use_unicode=True
    ).collect()

    if ejemplos:
        print(json.dumps(
            json.loads(ejemplos[0]),
            indent=2,
            ensure_ascii=False
        ))
    else:
        print("No hay registros válidos para importar.")

    # Crear una carpeta distinta para cada ejecución.
    if argumentos.salida is not None:
        salida = str(argumentos.salida.expanduser().resolve())
    else:
        nombre_carga = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
        salida = str(
            Path.home() / "apps/datos/procesados" / nombre_carga
        )

    # En la raíz se guardan únicamente documentos válidos.
    (
        documentos.write
        .mode("errorifexists")
        .option("ignoreNullFields", "false")
        .json(salida)
    )

    # Usar el esquema conocido permite comprobar también una salida vacía.
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

    # Conservar cada registro rechazado junto con su primer fallo.
    detalle_rechazados = rechazados.select(
        F.col("primer_error.campo").alias("campo"),
        F.col("primer_error.valor").alias("valor"),
        F.col("primer_error.problema").alias("problema"),
        F.struct(
            *[F.col(nombre) for nombre in columnas_requeridas]
        ).alias("registro_original")
    )

    carpeta_rechazados = Path(salida) / "rechazados"

    if total_rechazados > 0:
        (
            detalle_rechazados.write
            .mode("errorifexists")
            .option("ignoreNullFields", "false")
            .json(str(carpeta_rechazados))
        )

    # Resumen por primer campo que falló.
    errores_por_campo = {
        fila["campo"]: fila["count"]
        for fila in (
            detalle_rechazados
            .groupBy("campo")
            .count()
            .collect()
        )
    }

    # Una muestra pequeña para mostrar posteriormente en la pantalla.
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

    # Estos indicadores describen conversiones sobre todos los registros.
    # Pueden superponerse; no son el total de rechazos.
    if perfil != "red_vial_nacional":
        sin_anio_mes = datos.filter(
            F.col("anio").isNull()
            | F.col("mes_numero").isNull()
        ).count()

        sin_dia = datos.filter(
            F.col("dia_semana_numero").isNull()
        ).count()

        sin_kilometro = None
        sin_edad = None
        if perfil == "accidentes_cosevi":
            sin_kilometro = datos.filter(F.col("kilometro_numero").isNull()).count()
        else:
            sin_edad = datos.filter(F.col("edad_numero").isNull()).count()

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
        reporte.update({
            "registros_sin_anio_o_mes": sin_anio_mes,
            "registros_sin_dia_semana": sin_dia,
            "registros_sin_kilometro_numerico": sin_kilometro,
            "registros_sin_edad_numerica": sin_edad,
        })
        reporte["notas"] = [
            "Cada registro rechazado informa únicamente su primer fallo.",
            "El día representa el día de la semana, no el día del mes.",
            ("Un kilómetro no numérico no implica descartar el accidente."
             if perfil == "accidentes_cosevi" else
             "Una edad vacía o Desconocida se conserva como null.")
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
