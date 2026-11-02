(function () {
    const datosElemento = document.getElementById("datos_registros");
    const todos = JSON.parse(datosElemento.textContent);
    
    //estado de columna actual
    let filtrados = [...todos];
    let ordenCampo = "Fecha";
    let ascendente = false;
    let pagina = 1;
    const porPagina = 8;

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
            return ascendente ? resultado : -resultado;
        });

        const totalPaginas = Math.max(1, Math.ceil(filtrados.length / porPagina));
        pagina = Math.min(pagina, totalPaginas);
        const inicio = (pagina - 1) * porPagina;
        const registrosPagina = filtrados.slice(inicio, inicio + porPagina);

        filas.innerHTML = registrosPagina.map((registro) => `
            <tr>
                <td><strong>${escapar(registro.Id)}</strong></td>
                <td>${escapar(registro.Fecha)}</td>
                <td>${escapar(registro.Hora)}</td>
                <td>${escapar(registro.Provincia)}</td>
                <td>${escapar(registro.Canton)}</td>
                <td>${escapar(registro.Ruta)}</td>
                <td>${escapar(registro.Vehiculo)}</td>
                <td>${escapar(registro.Franja)}</td>
                <td>${escapar(registro.Clima)}</td>
                <td>${escapar(registro.Calzada)}</td>
                <td>${escapar(registro.Tpr)}</td>
                <td><span class="explorador_severidad ${claseSeveridad(registro.Severidad)}">${escapar(registro.Severidad)}</span></td>
            </tr>
        `).join("");

        totalConsulta.textContent = `${filtrados.length} registro${filtrados.length === 1 ? "" : "s"} encontrados`;
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
            boton.className = numero === pagina ? "activa" : "";
            boton.addEventListener("click", () => {
                pagina = numero;
                renderizar();
            });
            numeros.appendChild(boton);
        }

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
            boton.querySelector(".orden").textContent = ascendente ? " ▲" : " ▼";
            const nombre = boton.textContent.trim().replace(/[▲▼]/, "").trim();
            detalleOrden.textContent = `Orden: 1) ${nombre} ${ascendente ? "ascendente" : "descendente"}`;
            renderizar();
        });
    });

    // invertir campo seleccionado
    document.getElementById("ordenar_consulta").addEventListener("click", () => {
        ascendente = !ascendente;
        document.querySelectorAll(".orden").forEach((indicador) => indicador.textContent = "");
        const botonActivo = document.querySelector(`[data-sort="${ordenCampo}"]`);
        if (botonActivo) {
            botonActivo.querySelector(".orden").textContent = ascendente ? " ▲" : " ▼";
            const nombre = botonActivo.textContent.trim().replace(/[▲▼]/, "").trim();
            detalleOrden.textContent = `Orden: 1) ${nombre} ${ascendente ? "ascendente" : "descendente"}`;
        }
        renderizar();
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

    // controles de avance y retroceso entre páginas.
    anterior.addEventListener("click", () => { pagina--; renderizar(); });
    siguiente.addEventListener("click", () => { pagina++; renderizar(); });
    document.getElementById("reintentar").addEventListener("click", iniciarCarga);

    // Cuando no existen resultados, el botón informa que no hay datos para exportar.
    document.getElementById("abrir_exportacion").addEventListener("click", () => {
        if (filtrados.length !== 0) return;
        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("dialogo_exportacion")
        ).show();
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
