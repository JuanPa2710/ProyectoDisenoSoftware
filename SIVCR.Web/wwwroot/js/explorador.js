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
    let ordenCampo = "";
    if (columnas.length > 0) {
        if (columnas[0].Campo !== null && columnas[0].Campo !== undefined) {
            ordenCampo = columnas[0].Campo;
        } else if (columnas[0].campo !== null && columnas[0].campo !== undefined) {
            ordenCampo = columnas[0].campo;
        }
    }
    let ascendente = false;
    let pagina = 1;
    const porPagina = 10;

    const filas = document.getElementById("filas_registros");
    const totalConsulta = document.getElementById("total_consulta");
    const rango = document.getElementById("rango_registros");
    const anterior = document.getElementById("pagina_anterior");
    const siguiente = document.getElementById("pagina_siguiente");
    const numeros = document.getElementById("numeros_pagina");
    const sinResultados = document.getElementById("sin_resultados");
    const tablaContenedor = document.querySelector(".explorador_tabla_contenedor");
    const carga = document.getElementById("estado_carga");
    const contenido = document.getElementById("contenido_tabla");
    const error = document.getElementById("estado_error");
    const detalleOrden = document.getElementById("detalle_orden");

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
                <input type="checkbox" data-column="${indice}" checked />
                ${escapar(etiquetaColumna(columna))}
            </label>
        `).join("");

        document.querySelector(".explorador_tabla").setAttribute(
            "aria-label",
            `Registros de ${coleccion}`
        );
    }

    prepararEncabezados();

    // asignar color según severidad
    function claseSeveridad(valor) {
        if (valor === "Fallecidos") return "severidad_fallecidos";
        if (valor === "Grave" || valor === "Heridos graves") return "severidad_graves";
        if (valor === "Leve" || valor === "Heridos leves") return "severidad_leves";
        if (valor === "Materiales") return "severidad_danos";
        return "";
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
            const resultado = String(a[ordenCampo]).localeCompare(String(b[ordenCampo]), "es", { numeric: true });
            if (ascendente) {
                return resultado;
            } else {
                return -resultado;
            }
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

        let etiquetaRegistros = " registros";
        if (filtrados.length === 1) {
            etiquetaRegistros = " registro";
        }
        totalConsulta.textContent = `${filtrados.length}${etiquetaRegistros} encontrados`;
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
        for (let numero = 1; numero <= totalPaginas; numero++) {
            if (totalPaginas > 7 && numero > 4 && numero < totalPaginas) continue;
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
    document.querySelectorAll("[data-sort]").forEach((boton) => {
        boton.addEventListener("click", () => {
            const campo = boton.dataset.sort;
            if (ordenCampo === campo) ascendente = !ascendente;
            else {
                ordenCampo = campo;
                ascendente = true;
            }
            document.querySelectorAll(".orden").forEach((indicador) => indicador.textContent = "");
            if (ascendente) {
                boton.querySelector(".orden").textContent = " ▲";
            } else {
                boton.querySelector(".orden").textContent = " ▼";
            }
            const nombre = boton.textContent.trim().replace(/[▲▼]/, "").trim();
            if (ascendente) {
                detalleOrden.textContent = `Orden: 1) ${nombre} ascendente`;
            } else {
                detalleOrden.textContent = `Orden: 1) ${nombre} descendente`;
            }
            renderizar();
        });
    });

    // invertir campo seleccionado
    /*
    document.getElementById("ordenar_consulta").addEventListener("click", () => {
        ascendente = !ascendente;
        document.querySelectorAll(".orden").forEach((indicador) => indicador.textContent = "");
        const botonActivo = document.querySelector(`[data-sort="${ordenCampo}"]`);
        if (botonActivo) {
            if (ascendente) {
                botonActivo.querySelector(".orden").textContent = " ▲";
            } else {
                botonActivo.querySelector(".orden").textContent = " ▼";
            }
            const nombre = botonActivo.textContent.trim().replace(/[▲▼]/, "").trim();
            if (ascendente) {
                detalleOrden.textContent = `Orden: 1) ${nombre} ascendente`;
            } else {
                detalleOrden.textContent = `Orden: 1) ${nombre} descendente`;
            }
        }
        renderizar();
    });
    */

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
    anterior.addEventListener("click", () => { pagina--; renderizar(); });
    siguiente.addEventListener("click", () => { pagina++; renderizar(); });
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

    function descargarCsv(separador, extension, registrosExportacion) {
        const encabezados = columnas.map(etiquetaColumna);
        const campos = columnas.map(campoColumna);
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


    // coneccion a Mongo
    // solicita al servidor todos los registros para exportarlos
    async function obtenerDatosExportacion() {
        const urlColeccion = encodeURIComponent(coleccion);
        const respuesta = await fetch(`/Explorador/DatosExportacion?collection=${urlColeccion}`, {
            headers: { "Accept": "application/json" }
        });
        if (!respuesta.ok) throw new Error("No fue posible obtener los datos para exportar.");
        return respuesta.json();
    }

    document.getElementById("confirmar_exportacion").addEventListener("click", async () => {
        const formato = document.querySelector(
            'input[name="formato_exportacion"]:checked'
        ).value;

        try {
            const consultaExportacion = await obtenerDatosExportacion();
            let registrosExportacion = [];
            if (consultaExportacion.Filas !== null && consultaExportacion.Filas !== undefined) {
                registrosExportacion = consultaExportacion.Filas;
            } else if (consultaExportacion.filas !== null && consultaExportacion.filas !== undefined) {
                registrosExportacion = consultaExportacion.filas;
            }

            if (formato === "csv") descargarCsv(",", "csv", registrosExportacion);
            if (formato === "excel") descargarCsv(";", "xls", registrosExportacion);
            if (formato === "pdf") {
                let columnasExportacion = columnas;
                if (consultaExportacion.Columnas !== null && consultaExportacion.Columnas !== undefined) {
                    columnasExportacion = consultaExportacion.Columnas;
                } else if (consultaExportacion.columnas !== null && consultaExportacion.columnas !== undefined) {
                    columnasExportacion = consultaExportacion.columnas;
                }
                window.exportarConsultaPdf(registrosExportacion, columnasExportacion);
            }

            document.getElementById("exportacion_con_resultados").hidden = true;
            document.getElementById("confirmar_exportacion").hidden = true;
            document.getElementById("exportacion_completada").hidden = false;
        } catch (errorExportacion) {
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

    iniciarCarga();
})();
