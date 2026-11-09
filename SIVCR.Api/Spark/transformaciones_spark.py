from pyspark.sql import functions as F
from pyspark.sql.window import Window


def preparar_red_vial(datos):
    kilometro = F.expr("try_cast(replace(`Longitud_K`, ',', '.') AS DOUBLE)")
    datos = datos.withColumn("longitud_km", kilometro)
    repeticiones = F.count("*").over(Window.partitionBy("Seccion"))

    def error(condicion, campo, motivo):
        return F.when(condicion, F.struct(
            F.lit(campo).alias("campo"),
            F.col(campo).alias("valor"),
            F.lit(motivo).alias("problema")
        ))

    reglas = [
        error(
            F.col(campo).isNull() | (F.col(campo) == ""),
            campo,
            "Campo obligatorio."
        )
        for campo in ["Seccion", "Ruta", "Nom_Provin", "Nom_Canton", "Nom_Distri"]
    ]
    reglas.append(error(
        repeticiones > 1,
        "Seccion",
        "Sección repetida en esta carga."
    ))

    longitud = F.col("longitud_km")
    reglas.append(error(
        longitud.isNull()
        | F.isnan(longitud)
        | (longitud < 0)
        | (longitud == float("inf")),
        "Longitud_K",
        "La longitud debe ser un número finito no negativo."
    ))
    return datos.withColumn("primer_error", F.coalesce(*reglas))


def crear_mapa(etiquetas):
    pares = []
    for etiqueta, numero in etiquetas.items():
        pares.extend([F.lit(etiqueta), F.lit(numero)])
    return F.create_map(*pares)


MESES_COSEVI = {
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

DIAS_COSEVI = {
    "1.Domingo": 1,
    "2.Lunes": 2,
    "3.Martes": 3,
    "4.Miércoles": 4,
    "5.Jueves": 5,
    "6.Viernes": 6,
    "7.Sábado": 7,
}


def preparar_registros_generales(datos, perfil):
    datos = datos.withColumn(
        "anio",
        F.expr("try_cast(`Año` AS INT)")
    )
    datos = datos.withColumn(
        "mes_numero",
        F.element_at(crear_mapa(MESES_COSEVI), F.col("Mes"))
    )
    datos = datos.withColumn(
        "dia_semana_numero",
        F.element_at(crear_mapa(DIAS_COSEVI), F.col("Día"))
    )

    if perfil == "accidentes_cosevi":
        datos = datos.withColumn(
            "kilometro_numero",
            F.expr("try_cast(replace(`Kilómetro`, ',', '.') AS DOUBLE)")
        )
    else:
        datos = datos.withColumn(
            "edad_numero",
            F.expr("try_cast(`Edad` AS INT)")
        )

    return datos


def crear_fallo(condicion, campo, mensaje):
    return F.when(
        condicion,
        F.struct(
            F.lit(campo).alias("campo"),
            F.col(campo).cast("string").alias("valor"),
            F.lit(mensaje).alias("problema")
        )
    )


def esta_vacio(campo):
    return F.col(campo).isNull() | (F.trim(F.col(campo)) == "")
