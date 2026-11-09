# Ampliación COSEVI: personas, lotes y comparación estadística

## Datos y alcance

- `accidentes_cosevi` conserva el esquema e importación de `Incidencias`.
- `personas_cosevi` guarda registros en `PersonasAccidentes`, con `perfil: personas_cosevi`.
- Las estadísticas se calculan por separado y luego se combinan por año, mes,
  provincia, cantón y distrito. No hay relación individual persona–accidente.
- La comparación está disponible en `/EstadisticasCosevi`.
- La selección múltiple está en `/Cargas/Lote`; tiene enlaces desde `/Cargas`.
- El dashboard anterior no cambia sus indicadores. La comparación es una pantalla
  adicional para no aplicar a personas filtros que solo existen en accidentes.

## Perfiles

Accidentes: se conservan las 21 columnas, transformaciones, reglas y salida anteriores.

Personas: Rol, Tipo de lesión, Edad, Sexo, Vehiculo en el que viajaba, Año, Provincia,
Cantón, Distrito, Día, Mes y Edad quinquenal.

Se normalizan Unicode NFC y espacios de encabezados. CSV admite UTF-8 y Windows-1252
con separador `;`; la conversión se realiza en un temporal y no modifica el origen.
JSON y XLSX siguen las restricciones anteriores; se reconoce el perfil por sus columnas.
No se aceptan archivos ambiguos que contengan las columnas completas de ambos perfiles.

La edad vacía o Desconocida produce null y conserva el valor original. Un valor informado
debe ser entero no negativo representable como INT. Rol, lesión, sexo y vehículo no pueden
estar vacíos. Año, mes, día de semana y provincia conservan las validaciones comunes.
Estas son reglas del cargador, no un catálogo oficial de edades o roles de COSEVI.

## Lotes

Hasta 10 archivos por selección; máximo 50 MiB por archivo. El navegador envía un archivo
por petición y procesa en secuencia. No se aumenta el límite por petición de Web, API o Nginx.
Cada archivo conserva cargaId, carpeta de resultado y reporte propios. Se confirma la importación
después de revisar reportes; los rechazados se excluyen y pueden descargarse por archivo.
Un fallo no cancela los otros archivos. No existe una transacción global del lote ni ejecución
en segundo plano independiente del navegador. Otros usuarios aún pueden iniciar sus propias cargas.

El estado y los identificadores se conservan en sessionStorage de la pestaña; los archivos
seleccionados no. No cerrar la pestaña durante el proceso. Si la respuesta de una subida se pierde,
puede quedar una carga sin reporte visible. Verificar antes de subir otra vez.
Un fallo de importación puede haber escrito parte de sus lotes. Reintentar desde la misma lista
conserva el identificador; no crea automáticamente una carga nueva.

## Conteos y límites

No hay deduplicación entre cargas distintas: volver a subir un archivo ya importado puede duplicarlo.
No eliminar filas de personas solo porque sus atributos sean iguales: pueden ser personas distintas.
La nueva pantalla cuenta solo personas con el marcador `perfil: personas_cosevi`; no migra ni
presupone el esquema de datos preexistentes en PersonasAccidentes.
Los accidentes existentes no necesitan recargarse para aparecer en la comparación.

Las fuentes entregadas abarcan accidentes 2018–2024 y personas 2020–2025.
Comparar por defecto 2024; el intervalo común es 2020–2024.
La ausencia de registros importados no demuestra un cero real. Los conteos son registros,
no personas únicas. Personas incluye ilesos: no se etiqueta el total como víctimas.
No se calculan tasas de personas por accidente ni se presupone igualdad de cobertura entre fuentes.
No se infiere fecha exacta: Día es día de semana.

## Verificación realizada en la preparación

- API y Web compilan en Release, sin errores. Ocho advertencias previas de nulabilidad en
  UbicacionGeografica.cs de Web.
- Sintaxis Python y JavaScript verificadas.
- Funciones reales de normalización, detección de perfiles y codificación verificadas con
  ambos CSV; también rechazan encabezados vacíos, duplicados, desconocidos y perfiles ambiguos.
- Lectura completa: 104898 filas de accidentes, 21 columnas, UTF-8; 199142 filas de personas,
  12 columnas, Windows-1252. Son filas leídas, no conteos confirmados de aceptados en Spark.
- El archivo de personas contiene 11690 edades Desconocida.

Pendiente en el entorno de ejecución: Spark completo, importación a una base de pruebas,
validación de consultas MongoDB, interacción en navegador y conexión de la VM. No se escribieron
datos en MongoDB ni se desplegó esta ampliación.

## Prueba antes de subirlo a la web

1. Compilar Web y API en Release.
2. Procesar primero muestras de ambos perfiles con Python/Spark en una carpeta nueva.
3. Incluir una edad desconocida, una negativa, un año inválido, un encabezado con espacios
   dobles y un CSV Windows-1252.
4. En una base de prueba, importar y comprobar que cada perfil llega a su colección.
5. Reintentar una misma carga y comprobar que no aumenta su conteo.
6. En /Cargas/Lote seleccionar ambos perfiles y un archivo incorrecto: el error de uno no debe
   ocultar los reportes de los otros. Confirmar únicamente las cargas con aceptados.
7. Comprobar que la suma de cada columna de la comparación, sobre todas sus páginas,
   coincide con el conteo independiente de esa colección para los mismos filtros.
8. Probar 2019 y 2025: la ausencia de una fuente no debe borrar los grupos de la otra ni
   presentarse como ausencia real de accidentes o personas.
9. Revisar los permisos MongoDB de lectura/escritura sobre PersonasAccidentes.

Si Spark tarda más que la espera de Nginx, agregar `proxy_read_timeout 720s;` dentro del
location que contiene `proxy_pass http://127.0.0.1:5002;`. Validar con `sudo nginx -t`
y recargar solo si la validación pasa. El cliente CargaDatosApi actual espera 11 minutos;
la API limita cada operación Python a 10 minutos. La carga múltiple mantiene una petición
por operación y archivo, no una petición que deba esperar todo el lote.