# CU-01 — Carga y administración de datos

## Alcance
Permite subir, procesar, revisar y confirmar la importación de
archivos de accidentes con la estructura del perfil COSEVI.

Formatos admitidos:
- CSV UTF-8 separado por punto y coma.
- JSON con campos simples equivalentes a las columnas del perfil.
- Excel .xlsx con una sola hoja, encabezados en la primera fila,
  sin fórmulas ni celdas con errores.

Tamaño máximo del archivo: 50 MiB.

Aceptar estos formatos no significa aceptar cualquier estructura
ni cualquier fuente de datos. Otros perfiles requieren adaptación.

## Procesamiento
La Web recibe el archivo y se comunica con la API.
La API ejecuta el procesamiento de Python y Apache Spark.
Los documentos aceptados se importan en MongoDB únicamente
después de confirmar la importación.

Se valida año, mes, día de semana, provincia, clase y tipo de accidente.
Cada registro rechazado informa únicamente el primer fallo detectado.
Los registros rechazados no se importan.

El día representa el día de la semana, no el día del mes.
Este perfil no proporciona fecha exacta ni coordenadas.
Un kilómetro no numérico se conserva como texto original y se
representa como null en el campo numérico; no causa rechazo.

## Resultados
- Resumen de registros leídos, aceptados y rechazados.
- Reporte de calidad descargable en JSON.
- Descarga completa de rechazados en ZIP cuando existen.
- Confirmación de importación y pantalla de carga completada.
- Mensajes de archivo incompatible o estructura incorrecta.
- Bloqueo de importación cuando no existen registros aceptados.

MongoDB:
- Base: proyecto.
- Incidencias: documentos aceptados.
- Cargas: historial y reporte de validación de la carga.

Reintentar la misma carga conserva sus identificadores.
Subir nuevamente el archivo crea otra carga: no existe
deduplicación global entre cargas distintas.

## Verificaciones realizadas
- CSV, JSON y XLSX válidos.
- Extensión incompatible y columnas incorrectas.
- Archivo con encabezados pero sin registros.
- Excel con varias hojas o con fórmulas.
- Cuatro registros: dos aceptados y dos rechazados.
- Todos los registros rechazados: importación bloqueada.
- Importación de aceptados y consulta en MongoDB.
- Descarga de reporte y de registros rechazados.
- Solicitudes simultáneas para una misma carga: HTTP 200 y 409.
- Archivo completo: 104898 leídos, 104898 aceptados, 0 rechazados.
- El archivo completo presentó 36643 kilómetros no numéricos,
  admitidos según la regla del perfil.

## Operación
Servicios: sivcr-web, sivcr-api, mongod y nginx.

Nginx recibe las solicitudes públicas y las dirige a la Web.
Web: 127.0.0.1:5002.
API: 127.0.0.1:5001.
MongoDB: 127.0.0.1:27017.

Entorno Python: /home/ubuntu/apps/sivcr-spark.
Datos: /home/ubuntu/apps/datos.
Contraseña MongoDB: variable SIVCR_MONGO_PASSWORD,
configurada fuera del repositorio.

No se deben subir contraseñas, tokens, llaves privadas,
archivos de datos ni entornos virtuales a Git.

## Límites conocidos
La protección de concurrencia corresponde a una misma carga
dentro de la instancia actual de la API.
La importación por lotes no es una transacción global.
Duplicar o recargar una página resultado de un formulario POST
puede provocar una solicitud de reenvío del navegador.
La autenticación y los roles del sistema deben integrarse
con el módulo del equipo encargado.
