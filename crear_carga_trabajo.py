from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import cm
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak


OUT = "output/pdf/carga_trabajo_personas_2_3_4.pdf"

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(
    name="TitleCustom", parent=styles["Title"], alignment=TA_CENTER,
    fontName="Helvetica-Bold", fontSize=20, leading=25, textColor=colors.HexColor("#0C3B5E"),
    spaceAfter=14,
))
styles.add(ParagraphStyle(
    name="Subtitle", parent=styles["Normal"], alignment=TA_CENTER,
    fontSize=11, leading=15, textColor=colors.HexColor("#444444"), spaceAfter=18,
))
styles.add(ParagraphStyle(
    name="H2Custom", parent=styles["Heading2"], fontName="Helvetica-Bold",
    fontSize=14, leading=18, textColor=colors.HexColor("#0C3B5E"), spaceBefore=8, spaceAfter=8,
))
styles.add(ParagraphStyle(
    name="BodyCustom", parent=styles["BodyText"], fontSize=9.5, leading=13,
    spaceAfter=5,
))
styles.add(ParagraphStyle(
    name="Small", parent=styles["BodyText"], fontSize=8.5, leading=11,
))


def P(text, style="BodyCustom"):
    return Paragraph(text, styles[style])


def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(colors.HexColor("#D8D8D8"))
    canvas.line(1.6 * cm, 1.35 * cm, A4[0] - 1.6 * cm, 1.35 * cm)
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(colors.HexColor("#666666"))
    canvas.drawString(1.6 * cm, 0.9 * cm, "SIVCR - Carga de trabajo de implementación visual")
    canvas.drawRightString(A4[0] - 1.6 * cm, 0.9 * cm, f"Página {doc.page}")
    canvas.restoreState()


doc = SimpleDocTemplate(
    OUT, pagesize=A4, rightMargin=1.6 * cm, leftMargin=1.6 * cm,
    topMargin=1.5 * cm, bottomMargin=1.8 * cm,
    title="Carga de trabajo SIVCR - Personas 2, 3 y 4",
    author="Equipo SIVCR",
)

story = []
story += [
    P("Carga de trabajo del proyecto SIVCR", "TitleCustom"),
    P("Implementación de vistas según el prototipo de Figma y el SAD", "Subtitle"),
    P("Alcance de este documento", "H2Custom"),
    P("Este documento asigna únicamente el trabajo de las personas 2, 3 y 4. Se excluyen la persona responsable del layout general y la persona responsable del dashboard y los filtros base. La asignación contempla la implementación visual de las pantallas, sus estados normales, alternativos y de error, dejando preparadas las vistas para su conexión posterior con datos y servicios.", "BodyCustom"),
    Spacer(1, 8),
]

summary = [
    [P("Persona", "Small"), P("Módulo", "Small"), P("Pantallas asignadas", "Small")],
    [P("Persona 2", "Small"), P("Explorador y reportes", "Small"), P("CU-03 · Explorador de registros<br/>CU-03 · Diálogo de exportación<br/>CU-03 · Exportar consulta sin resultados (flujo alternativo 2)", "Small")],
    [P("Persona 3", "Small"), P("Carga y administración de datos", "Small"), P("CU-01 · Carga de datos viales<br/>CU-01 · Carga completada<br/>CU-01 · Archivo incompatible (excepción 1)", "Small")],
    [P("Persona 4", "Small"), P("Filtros guardados, roles y predicción", "Small"), P("CU-02 · Filtros guardados e historial (RF-5)<br/>CU-04 · Predicción de riesgo vial<br/>CU-04 · Fuera de cobertura (flujo alternativo 2)<br/>CU-04 · Datos insuficientes (excepción 1)", "Small")],
]
tbl = Table(summary, colWidths=[2.3*cm, 4.2*cm, 10.0*cm], repeatRows=1)
tbl.setStyle(TableStyle([
    ("BACKGROUND", (0,0), (-1,0), colors.HexColor("#0C3B5E")),
    ("TEXTCOLOR", (0,0), (-1,0), colors.white),
    ("GRID", (0,0), (-1,-1), 0.4, colors.HexColor("#B8C5CF")),
    ("VALIGN", (0,0), (-1,-1), "TOP"),
    ("BACKGROUND", (0,1), (-1,-1), colors.HexColor("#F7FAFC")),
    ("ROWBACKGROUNDS", (0,1), (-1,-1), [colors.white, colors.HexColor("#F2F6F9")]),
    ("LEFTPADDING", (0,0), (-1,-1), 7),
    ("RIGHTPADDING", (0,0), (-1,-1), 7),
    ("TOPPADDING", (0,0), (-1,-1), 7),
    ("BOTTOMPADDING", (0,0), (-1,-1), 7),
]))
story.append(tbl)
story.append(PageBreak())

sections = [
    ("Persona 2 - Explorador y reportes", "CU-03", [
        "CU-03 · Explorador de registros",
        "CU-03 · Diálogo de exportación",
        "CU-03 · Exportar consulta sin resultados (flujo alternativo 2)",
    ], [
        "Implementar la tabla visual de incidentes viales.",
        "Agregar columnas, filas, indicadores de severidad y estados visuales.",
        "Construir búsqueda, ordenamiento y paginación visual.",
        "Crear el diálogo de exportación con las opciones CSV, Excel y PDF.",
        "Mostrar mensajes cuando no existan registros para exportar.",
        "Preparar estados de carga, error y operación completada.",
        "Dejar la vista preparada para exportar todos los registros de una consulta, no solamente la página visible.",
    ], "Este módulo corresponde al explorador tabular y a la generación de reportes definidos en el SAD. En la primera implementación puede utilizar datos simulados."),
    ("Persona 3 - Carga y administración de datos", "CU-01", [
        "CU-01 · Carga de datos viales",
        "CU-01 · Carga completada",
        "CU-01 · Archivo incompatible (excepción 1)",
    ], [
        "Construir la zona para seleccionar o arrastrar archivos.",
        "Mostrar los formatos permitidos: CSV, JSON y Excel.",
        "Mostrar nombre, tipo y tamaño del archivo seleccionado.",
        "Diseñar el indicador de procesamiento.",
        "Mostrar el resumen de registros válidos e inválidos.",
        "Crear el estado de carga completada y su reporte de calidad.",
        "Crear el estado de archivo incompatible o ilegible, con acciones para cancelar y reintentar.",
        "Dejar la vista lista para conectarse posteriormente con la validación y la ingesta real.",
    ], "Aunque la ingesta real se implemente después, estas pantallas deben quedar terminadas visualmente y con datos simulados para demostrar sus flujos."),
    ("Persona 4 - Filtros guardados, roles y predicción", "CU-02 / CU-04", [
        "CU-02 · Filtros guardados e historial (RF-5)",
        "CU-04 · Predicción de riesgo vial",
        "CU-04 · Fuera de cobertura (flujo alternativo 2)",
        "CU-04 · Datos insuficientes (excepción 1)",
    ], [
        "Construir la lista de filtros guardados y el historial de consultas.",
        "Agregar acciones visuales para reutilizar y eliminar filtros.",
        "Construir el formulario de predicción con ubicación, período, clima, franja horaria y estado de calzada.",
        "Mostrar el resultado de probabilidad de riesgo con niveles bajo, medio y alto.",
        "Mostrar las condiciones utilizadas en la estimación.",
        "Crear los estados de ubicación fuera de cobertura y datos insuficientes.",
        "Preparar la vista para control de acceso según los roles definidos en el SAD.",
        "Dejar la pantalla lista para conectarse posteriormente con el modelo predictivo.",
    ], "La predicción debe presentarse como una estimación, no como certeza. Las pantallas de excepción son estados derivados de la vista principal y no módulos independientes."),
]

for idx, (title, code, screens, tasks, note) in enumerate(sections):
    story.append(P(title, "H2Custom"))
    story.append(P(f"Casos de uso relacionados: {code}", "Small"))
    story.append(Spacer(1, 4))
    story.append(P("Pantallas exactas de Figma", "BodyCustom"))
    screen_rows = [[P("Pantalla", "Small")]] + [[P(f"• {s}", "Small")] for s in screens]
    screen_tbl = Table(screen_rows, colWidths=[16.5*cm])
    screen_tbl.setStyle(TableStyle([
        ("BACKGROUND", (0,0), (-1,0), colors.HexColor("#E9F1F7")),
        ("GRID", (0,0), (-1,-1), 0.35, colors.HexColor("#B8C5CF")),
        ("LEFTPADDING", (0,0), (-1,-1), 7),
        ("RIGHTPADDING", (0,0), (-1,-1), 7),
        ("TOPPADDING", (0,0), (-1,-1), 5),
        ("BOTTOMPADDING", (0,0), (-1,-1), 5),
    ]))
    story.append(screen_tbl)
    story.append(Spacer(1, 8))
    story.append(P("Qué debe programar", "BodyCustom"))
    for task in tasks:
        story.append(P(f"• {task}", "Small"))
    story.append(Spacer(1, 4))
    story.append(P(f"Criterio del módulo: {note}", "Small"))
    if idx < len(sections) - 1:
        story.append(PageBreak())

story.append(Spacer(1, 12))
story.append(P("Dependencias entre integrantes", "H2Custom"))
story.append(P("Las tres personas deben reutilizar el layout, los colores, la tipografía y los componentes base definidos por el responsable general. Cada módulo debe poder mostrarse con datos ficticios antes de conectarse con MongoDB. Las pantallas normales y sus estados alternativos deben pertenecer al mismo módulo para evitar duplicación y mantener consistencia visual.", "BodyCustom"))

doc.build(story, onFirstPage=footer, onLaterPages=footer)
print(OUT)
