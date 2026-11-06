(() => {
    "use strict";
    const raiz = document.getElementById("cargas-lote");
    if (!raiz) return;
    const formulario = document.getElementById("lote-formulario");
    const selector = document.getElementById("lote-archivos");
    const tabla = document.getElementById("lote-resultados");
    const aviso = document.getElementById("lote-aviso");
    const importar = document.getElementById("lote-importar");
    const nuevo = document.getElementById("lote-nuevo");
    const clave = "sivcr-lote-cosevi-v1";
    let filas = [];
    let ocupado = false;

    try {
        const guardadas = JSON.parse(sessionStorage.getItem(clave) || "[]");
        if (Array.isArray(guardadas)) {
            filas = guardadas.filter(f => f && typeof f.nombre === "string");
            for (const f of filas) {
                if (["Subiendo", "Procesando", "Importando"].includes(f.estado)) {
                    f.estado = "Operación interrumpida: verificar antes de reintentar";
                }
            }
        }
    } catch { filas = []; }

    function guardar() {
        try {
            sessionStorage.setItem(clave, JSON.stringify(
                filas.map(({ archivo, ...fila }) => fila)));
        } catch { /* La carga funciona sin almacenamiento del navegador. */ }
    }

    function pintar() {
        tabla.replaceChildren();
        for (const f of filas) {
            const tr = document.createElement("tr");
            const valores = [f.nombre, f.reporte?.perfil ?? "Pendiente",
                f.reporte?.registros_aceptados ?? "—",
                f.reporte?.registros_rechazados ?? "—", f.estado];
            for (const valor of valores) {
                const td = document.createElement("td");
                td.textContent = String(valor);
                tr.append(td);
            }
            const enlaces = document.createElement("td");
            if (f.cargaId && f.carpetaResultado) {
                for (const [tipo, texto] of [["reporte", "Reporte JSON"], ["rechazados", "Rechazados ZIP"]]) {
                    if (tipo === "rechazados" && !(f.reporte?.registros_rechazados > 0)) continue;
                    const a = document.createElement("a");
                    const url = new URL(raiz.dataset[tipo], location.origin);
                    url.searchParams.set("cargaId", f.cargaId);
                    url.searchParams.set("carpetaResultado", f.carpetaResultado);
                    a.href = url.href;
                    a.textContent = texto;
                    enlaces.append(a, document.createElement("br"));
                }
            }
            tr.append(enlaces);
            tabla.append(tr);
        }
        for (const boton of formulario.querySelectorAll("button")) boton.disabled = ocupado;
        selector.disabled = ocupado || filas.length > 0;
        nuevo.disabled = ocupado;
        importar.disabled = ocupado || !filas.some(f => f.reporte?.puede_importar && !f.importada);
        guardar();
    }

    async function enviar(url, campos) {
        const datos = new FormData();
        datos.append("__RequestVerificationToken", formulario.querySelector(
            "input[name='__RequestVerificationToken']").value);
        for (const [nombre, valor] of Object.entries(campos)) datos.append(nombre, valor);
        const respuesta = await fetch(url, { method: "POST", body: datos, credentials: "same-origin" });
        let contenido;
        try { contenido = await respuesta.json(); }
        catch { throw new Error(`Respuesta no confirmada (HTTP ${respuesta.status}). Verificá el servidor antes de reintentar.`); }
        if (!respuesta.ok) throw new Error(contenido.mensaje || `Error HTTP ${respuesta.status}`);
        return contenido;
    }

    formulario.addEventListener("submit", async evento => {
        evento.preventDefault();
        if (ocupado) return;
        if (!filas.length) {
            const archivos = Array.from(selector.files);
            if (!archivos.length || archivos.length > 10) {
                aviso.textContent = "Seleccioná entre 1 y 10 archivos.";
                return;
            }
            if (archivos.some(f => f.size === 0 || f.size > 50 * 1024 * 1024 || !/\.(csv|json|xlsx)$/i.test(f.name))) {
                aviso.textContent = "Cada archivo debe tener contenido, un formato permitido y como máximo 50 MiB.";
                return;
            }
            filas = archivos.map(archivo => ({ archivo, nombre: archivo.name, estado: "Pendiente" }));
        }
        ocupado = true;
        aviso.textContent = "Procesando en secuencia. Mantené abierta esta pestaña.";
        pintar();
        try {
            for (const f of filas) {
                if (f.reporte || f.importada) continue;
                try {
                    if (!f.cargaId) {
                        if (!f.archivo) throw new Error("No se conserva el archivo tras recargar. Verificá la carga anterior antes de iniciar otro lote.");
                        f.estado = "Subiendo";
                        pintar();
                        const subida = await enviar(raiz.dataset.subir, { archivo: f.archivo });
                        if (!subida.cargaId) throw new Error("No se recibió el identificador de carga.");
                        f.cargaId = subida.cargaId;
                        guardar();
                    }
                    f.estado = "Procesando";
                    pintar();
                    const procesada = await enviar(raiz.dataset.procesar, {
                        cargaId: f.cargaId, nombreArchivo: f.nombre
                    });
                    if (!procesada.reporte || !procesada.carpetaResultado)
                        throw new Error("No se recibió el reporte completo.");
                    f.carpetaResultado = procesada.carpetaResultado;
                    f.reporte = procesada.reporte;
                    f.estado = f.reporte.puede_importar ? "Lista para revisión" : "Sin registros importables";
                } catch (error) { f.estado = error.message; }
                pintar();
            }
        } finally {
            ocupado = false;
            aviso.textContent = "Revisá los reportes. Solo se importarán los registros aceptados de los archivos válidos.";
            pintar();
        }
    });

    importar.addEventListener("click", async () => {
        if (ocupado) return;
        const pendientes = filas.filter(f => f.reporte?.puede_importar && !f.importada);
        if (!pendientes.length) return;
        const cantidad = pendientes.reduce((n, f) => n + f.reporte.registros_aceptados, 0);
        if (!window.confirm(`¿Importar ${cantidad} registros aceptados de ${pendientes.length} archivos? Los rechazados se excluyen. Si hubo un fallo previo, este intento reutiliza la misma carga.`)) return;
        ocupado = true;
        aviso.textContent = "Importando en secuencia. Esperá la confirmación de cada archivo.";
        pintar();
        try {
            for (const f of pendientes) {
                f.estado = "Importando";
                pintar();
                try {
                    const resultado = await enviar(raiz.dataset.importar, {
                        cargaId: f.cargaId, carpetaResultado: f.carpetaResultado,
                        nombreArchivo: f.nombre
                    });
                    if (resultado.importada !== true) throw new Error("La API no confirmó la importación.");
                    f.importada = true;
                    f.estado = "Importada";
                } catch (error) { f.estado = `Sin confirmación: ${error.message}`; }
                pintar();
            }
        } finally {
            ocupado = false;
            aviso.textContent = "Finalizó el intento. Revisá el estado de cada archivo; el lote no es una transacción única.";
            pintar();
        }
    });

    nuevo.addEventListener("click", () => {
        if (ocupado) return;
        if (filas.length && !window.confirm("¿Limpiar la lista de esta pestaña? Los datos ya importados no se borran. Conservá los reportes necesarios.")) return;
        filas = [];
        selector.value = "";
        aviso.textContent = "";
        pintar();
    });
    window.addEventListener("beforeunload", evento => {
        if (!ocupado) return;
        evento.preventDefault();
        evento.returnValue = "";
    });
    pintar();
})();