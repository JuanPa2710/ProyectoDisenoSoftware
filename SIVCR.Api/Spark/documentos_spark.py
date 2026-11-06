from pyspark.sql import functions as F

from perfiles_cosevi import COLUMNAS_VIALES


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
        F.struct(*[F.col(c) for c in COLUMNAS_VIALES]).alias("atributos")
    )


def documentos_accidentes(validos):
    return validos.select(
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


def documentos_personas(validos):
    return validos.select(
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
            F.col("anio"),
            F.col("mes_numero").alias("mes"),
            F.col("dia_semana_numero").alias("dia_semana")
        ).alias("tiempo"),
        F.struct(
            F.col("Provincia").alias("provincia"),
            F.col("Cantón").alias("canton"),
            F.col("Distrito").alias("distrito")
        ).alias("ubicacion")
    )
