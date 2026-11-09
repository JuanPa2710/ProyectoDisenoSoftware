(function () {
    const datosElemento = document.getElementById("datos_registros");
    const consulta = JSON.parse(datosElemento.textContent);
    let todos = [];
    if (consulta.Filas !== null && consulta.Filas !== undefined) {
        todos = consulta.Filas;
    } else if (consulta.filas !== null && consulta.filas !== undefined) {
        todos = consulta.filas;
    }

    let columnas = [];
    if (consulta.Columnas !== null && consulta.Columnas !== undefined) {
        columnas = consulta.Columnas;
    } else if (consulta.columnas !== null && consulta.columnas !== undefined) {
        columnas = consulta.columnas;
    }

    let coleccion = "";
    if (consulta.Coleccion !== null && consulta.Coleccion !== undefined) {
        coleccion = consulta.Coleccion;
    } else if (consulta.coleccion !== null && consulta.coleccion !== undefined) {
        coleccion = consulta.coleccion;
    }
    
    //estado de columna actual
    let filtrados = [...todos];
    let ordenes = [];
    let pagina = 1;
    const porPagina = 10;

    const filas = document.getElementById("filas_registros");
    const rango = document.getElementById("rango_registros");
    const anterior = document.getElementById("pagina_anterior");
    const siguiente = document.getElementById("pagina_siguiente");
    const numeros = document.getElementById("numeros_pagina");
    const sinResultados = document.getElementById("sin_resultados");
    const tablaContenedor = document.querySelector(".explorador_tabla_contenedor");
    const carga = document.getElementById("estado_carga");
    const contenido = document.getElementById("contenido_tabla");
    const error = document.getElementById("estado_error");

    function campoColumna(columna) {
        if (columna.Campo !== null && columna.Campo !== undefined) {
            return columna.Campo;
        }
        return columna.campo;
    }

    function etiquetaColumna(columna) {
        if (columna.Etiqueta !== null && columna.Etiqueta !== undefined) {
            return columna.Etiqueta;
        }
        return columna.etiqueta;
    }

    function prepararEncabezados() {
        const filaEncabezados = document.querySelector(".explorador_tabla thead tr");
        const menuColumnas = document.getElementById("menu_columnas");
        filaEncabezados.innerHTML = columnas.map((columna, indice) => `
            <th scope="col" data-column-index="${indice}">
                <button type="button" data-sort="${campoColumna(columna)}">
                    ${escapar(etiquetaColumna(columna))}
                    <span class="orden"></span>
                </button>
            </th>
        `).join("");

        menuColumnas.innerHTML = columnas.map((columna, indice) => `
            <label>
                <input type="checkbox"
                       data-column="${indice}"
                       ${columnaOcultaPorDefecto(columna) ? "" : "checked"} />
                ${escapar(etiquetaColumna(columna))}
            </label>
        `).join("");

        document.querySelector(".explorador_tabla").setAttribute(
            "aria-label",
            `Registros de ${coleccion}`
        );
    }

    prepararEncabezados();

    function columnaOcultaPorDefecto(columna) {
        const campo = campoColumna(columna).toLowerCase();
        const etiqueta = etiquetaColumna(columna).toLowerCase();
        const campoNormalizado = campo.replace(/[\s._-]+/g, "");
        const etiquetaNormalizada = etiqueta.replace(/[\s._-]+/g, "");

        if (campoNormalizado === "_id") return true;
        if (campoNormalizado === "id" || etiquetaNormalizada === "id") return true;
        if (campo.endsWith("._id") || campo === "_id") return true;
        if (campoNormalizado.includes("cargaid")) return true;
        if (etiquetaNormalizada.includes("cargaid")) return true;
        if (campoNormalizado.endsWith("idobjeto")) return true;
        if (etiquetaNormalizada.includes("idobjeto")) return true;
        return false;
    }

    function obtenerColumnasVisibles() {
        const columnasVisibles = [];
        document.querySelectorAll("#menu_columnas input[data-column]").forEach((checkbox) => {
            if (checkbox.checked) {
                const indice = Number(checkbox.dataset.column);
                if (indice >= 0 && indice < columnas.length) {
                    columnasVisibles.push(columnas[indice]);
                }
            }
        });
        return columnasVisibles;
    }

    function aplicarVisibilidadColumnas() {
        document.querySelectorAll("#menu_columnas input[data-column]").forEach((checkbox) => {
            const indice = Number(checkbox.dataset.column);
            document.querySelectorAll(".explorador_tabla tr").forEach((fila) => {
                const celda = fila.children[indice];
                if (celda) {
                    celda.hidden = !checkbox.checked;
                }
            });
        });
    }

    // asignar color según severidad
    function claseSeveridad(valor) {
        if (valor === "Fallecidos") return "severidad_fallecidos";
        if (valor === "Grave" || valor === "Heridos graves") return "severidad_graves";
        if (valor === "Leve" || valor === "Heridos leves") return "severidad_leves";
        if (valor === "Materiales") return "severidad_danos";
        return "";
    }

    function paginasVisibles(totalPaginas, paginaActual) {
        if (totalPaginas <= 7) {
            const paginas = [];
            for (let numero = 1; numero <= totalPaginas; numero++) {
                paginas.push(numero);
            }
            return paginas;
        }

        if (paginaActual <= 4) {
            return [1, 2, 3, 4, 5, null, totalPaginas];
        }

        if (paginaActual >= totalPaginas - 3) {
            const paginas = [];
            paginas.push(1, null);
            for (let numero = totalPaginas - 4; numero <= totalPaginas; numero++) {
                paginas.push(numero);
            }
            return paginas;
        }

        return [
            1,
            null,
            paginaActual - 2,
            paginaActual - 1,
            paginaActual,
            paginaActual + 1,
            paginaActual + 2,
            null,
            totalPaginas
        ];
    }

    function escapar(valor) {
        const elemento = document.createElement("span");
        elemento.textContent = valor;
        return elemento.innerHTML;
    }

    function renderizar() {
        filtrados = [...todos];

        // ordena una copia de los registros, sin modificar la fuente original.
        filtrados.sort((a, b) => {
            for (const orden of ordenes) {
                const resultado = String(a[orden.campo] || "")
                    .localeCompare(String(b[orden.campo] || ""), "es", { numeric: true });
                if (resultado !== 0) {
                    if (orden.ascendente) {
                        return resultado;
                    } else {
                        return -resultado;
                    }
                }
            }
            return 0;
        });

        const totalPaginas = Math.max(1, Math.ceil(filtrados.length / porPagina));
        pagina = Math.min(pagina, totalPaginas);
        const inicio = (pagina - 1) * porPagina;
        const registrosPagina = filtrados.slice(inicio, inicio + porPagina);

        filas.innerHTML = registrosPagina.map((registro) => `
            <tr>
                ${columnas.map((columna) => {
                    const campo = campoColumna(columna);
                    let valor = "";
                    if (registro[campo] !== null && registro[campo] !== undefined) {
                        valor = registro[campo];
                    }
                    return `<td>${escapar(valor)}</td>`;
                }).join("")}
            </tr>
        `).join("");
        aplicarVisibilidadColumnas();

        const hayResultados = filtrados.length > 0;
        sinResultados.hidden = hayResultados;
        tablaContenedor.hidden = !hayResultados;
        document.querySelector(".explorador_paginacion").hidden = !hayResultados;

        if (hayResultados) {
            rango.textContent = `${inicio + 1}-${Math.min(inicio + porPagina, filtrados.length)} de ${filtrados.length}`;
        } else {
            rango.textContent = "0 registros";
        }

        anterior.disabled = pagina <= 1 || !hayResultados;
        siguiente.disabled = pagina >= totalPaginas || !hayResultados;
        numeros.innerHTML = "";
        for (const numero of paginasVisibles(totalPaginas, pagina)) {
            if (numero === null) {
                const separador = document.createElement("span");
                separador.className = "explorador_numeros_separador";
                separador.textContent = "…";
                separador.setAttribute("aria-hidden", "true");
                numeros.appendChild(separador);
                continue;
            }

            const boton = document.createElement("button");
            boton.type = "button";
            boton.textContent = numero;
            if (numero === pagina) {
                boton.className = "activa";
            } else {
                boton.className = "";
            }
            boton.addEventListener("click", () => {
                pagina = numero;
                renderizar();
            });
            numeros.appendChild(boton);
        }

        let etiquetaCantidad = " registros";
        if (filtrados.length === 1) {
            etiquetaCantidad = " registro";
        }
        document.getElementById("cantidad_exportar").textContent =
            `${filtrados.length}${etiquetaCantidad}`;
    }

    // para ordenar la tabla
    function actualizarIndicadoresOrden() {
        document.querySelectorAll(".orden").forEach((indicador) => {
            indicador.textContent = "";
        });

        ordenes.forEach((orden) => {
            const botonOrden = document.querySelector(`[data-sort="${orden.campo}"]`);
            if (botonOrden) {
                const indicador = orden.ascendente ? "▲" : "▼";
                botonOrden.querySelector(".orden").textContent = ` ${indicador}`;
            }
        });
    }

    document.querySelectorAll("[data-sort]").forEach((boton) => {
        boton.addEventListener("click", () => {
            const campo = boton.dataset.sort;
            const indiceOrden = ordenes.findIndex((orden) => orden.campo === campo);

            if (indiceOrden >= 0) {
                if (ordenes[indiceOrden].ascendente) {
                    ordenes[indiceOrden].ascendente = false;
                } else {
                    ordenes = ordenes.filter((orden) => orden.campo !== campo);
                }
            } else {
                ordenes.push({
                    campo,
                    ascendente: true
                });
            }

            actualizarIndicadoresOrden();
            renderizar();
        });
    });


    // muestrar u ocultar columnas en base a su indice
    document.querySelectorAll("#menu_columnas input[data-column]").forEach((checkbox) => {
        checkbox.addEventListener("change", () => {
            const columna = Number(checkbox.dataset.column);
            document.querySelectorAll(".explorador_tabla tr").forEach((fila) => {
                const celda = fila.children[columna];
                if (celda) celda.hidden = !checkbox.checked;
            });
        });
    });

    // controles de avance y retroceso entre páginas
    anterior.addEventListener("click", () => {
        if (pagina > 1) {
            pagina--;
            renderizar();
        }
    });
    siguiente.addEventListener("click", () => {
        const totalPaginas = Math.max(1, Math.ceil(filtrados.length / porPagina));
        if (pagina < totalPaginas) {
            pagina++;
            renderizar();
        }
    });
    document.getElementById("reintentar").addEventListener("click", iniciarCarga);

    // abre las opciones o el aviso según los resultados de la consulta
    document.getElementById("abrir_exportacion").addEventListener("click", () => {
        const vacio = filtrados.length === 0;
        document.getElementById("exportacion_sin_resultados").hidden = !vacio;
        document.getElementById("exportacion_con_resultados").hidden = vacio;
        document.getElementById("exportacion_completada").hidden = true;
        document.getElementById("confirmar_exportacion").hidden = vacio;

        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("dialogo_exportacion")
        ).show();
    });

    function descargarCsv(separador, extension, registrosExportacion, columnasExportacion) {
        const encabezados = columnasExportacion.map(etiquetaColumna);
        const campos = columnasExportacion.map(campoColumna);
        const filasCsv = registrosExportacion.map((registro) =>
            campos.map((campo) => {
                let valor = "";
                if (registro[campo] !== null && registro[campo] !== undefined) {
                    valor = registro[campo];
                }
                return `"${String(valor).replaceAll('"', '""')}"`;
            }).join(separador)
        );
        const contenidoCsv =
            "\ufeff" + encabezados.join(separador) + "\n"
            + filasCsv.join("\n");
        let tipo = "text/csv;charset=utf-8";
        if (extension === "xls") {
            tipo = "application/vnd.ms-excel;charset=utf-8";
        }
        const blob = new Blob([contenidoCsv], { type: tipo });
        const enlace = document.createElement("a");
        enlace.href = URL.createObjectURL(blob);
        enlace.download = `consulta-incidentes.${extension}`;
        enlace.style.display = "none";
        document.body.appendChild(enlace);
        enlace.click();
        enlace.remove();
        window.setTimeout(() => URL.revokeObjectURL(enlace.href), 1000);
    }

    async function descargarXlsx(columnasExportacion) {
        const parametros = new URLSearchParams(window.location.search);
        parametros.set("collection", coleccion);

        const respuesta = await fetch(
            `/Explorador/ExportarXlsx?${parametros.toString()}`,
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                },
                body: JSON.stringify(columnasExportacion.map((columna) => ({
                    Campo: campoColumna(columna),
                    Etiqueta: etiquetaColumna(columna)
                })))
            }
        );

        if (!respuesta.ok) {
            let detalle = "No fue posible generar el archivo Excel.";
            try {
                const respuestaError = await respuesta.text();
                if (respuestaError !== null
                    && respuestaError.trim() !== "") {
                    detalle = respuestaError;
                }
            } catch {
                detalle = "No fue posible generar el archivo Excel.";
            }
            throw new Error(detalle);
        }

        const archivo = await respuesta.blob();
        const url = URL.createObjectURL(archivo);
        const enlace = document.createElement("a");
        enlace.href = url;
        enlace.download = "consulta-incidentes.xlsx";
        enlace.style.display = "none";
        document.body.appendChild(enlace);
        enlace.click();
        enlace.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    }


    // coneccion a Mongo
    // solicita al servidor todos los registros para exportarlos
    async function obtenerDatosExportacion() {
        const parametros = new URLSearchParams(window.location.search);
        parametros.set("collection", coleccion);
        const respuesta = await fetch(`/Explorador/DatosExportacion?${parametros.toString()}`, {
            headers: { "Accept": "application/json" }
        });
        if (!respuesta.ok) throw new Error("No fue posible obtener los datos para exportar.");
        return respuesta.json();
    }

    document.getElementById("confirmar_exportacion").addEventListener("click", async () => {
        const formato = document.querySelector(
            'input[name="formato_exportacion"]:checked'
        ).value;
        const columnasVisibles = obtenerColumnasVisibles();

        document.getElementById("exportacion_sin_resultados").hidden = true;
        document.getElementById("exportacion_con_resultados").hidden = true;
        document.getElementById("confirmar_exportacion").hidden = true;
        document.getElementById("exportacion_completada").hidden = false;

        try {
            const consultaExportacion = await obtenerDatosExportacion();
            let registrosExportacion = [];
            if (consultaExportacion.Filas !== null && consultaExportacion.Filas !== undefined) {
                registrosExportacion = consultaExportacion.Filas;
            } else if (consultaExportacion.filas !== null && consultaExportacion.filas !== undefined) {
                registrosExportacion = consultaExportacion.filas;
            }

            if (formato === "csv") descargarCsv(",", "csv", registrosExportacion, columnasVisibles);
            if (formato === "excel") await descargarXlsx(columnasVisibles);
            if (formato === "pdf") {
                let columnasExportacion = columnasVisibles;
                if (consultaExportacion.Columnas !== null && consultaExportacion.Columnas !== undefined) {
                    columnasExportacion = consultaExportacion.Columnas;
                } else if (consultaExportacion.columnas !== null && consultaExportacion.columnas !== undefined) {
                    columnasExportacion = consultaExportacion.columnas;
                }
                const camposVisibles = new Set(columnasVisibles.map(campoColumna));
                columnasExportacion = columnasExportacion.filter((columna) =>
                    camposVisibles.has(campoColumna(columna))
                );
                window.exportarConsultaPdf(registrosExportacion, columnasExportacion);
            }
        } catch (errorExportacion) {
            document.getElementById("exportacion_con_resultados").hidden = false;
            document.getElementById("confirmar_exportacion").hidden = false;
            document.getElementById("exportacion_completada").hidden = true;
            alert(errorExportacion.message);
        }
    });

    function iniciarCarga() {
        carga.hidden = false;
        contenido.hidden = true;
        error.hidden = true;
        window.setTimeout(() => {
            carga.hidden = true;
            contenido.hidden = false;
            renderizar();
        }, 350);
    }

    actualizarIndicadoresOrden();
    iniciarCarga();
})();
