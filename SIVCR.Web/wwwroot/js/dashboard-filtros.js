(() => {
    const form = document.querySelector(".dashboard-filters");
    if (!form) return;
    const carga = document.getElementById("dashboard-loading");
    const provincia = form.elements.namedItem("Filtros.Provincia");
    const canton = form.elements.namedItem("Filtros.Canton");
    const distrito = form.elements.namedItem("Filtros.Distrito");
    const ruta = form.elements.namedItem("Filtros.Ruta");
    const geographyUrl = form.dataset.geographyUrl;
    let geographyRequest = null;
    let geographySequence = 0;

    const reemplazarOpciones = (select, placeholder, opciones) => {
        select.replaceChildren(new Option(placeholder, ""));
        for (const opcion of opciones) select.add(new Option(opcion, opcion));
    };

    const cargarOpcionesGeograficas = async (origen) => {
        geographyRequest?.abort();
        const secuencia = ++geographySequence;
        geographyRequest = new AbortController();
        const params = new URLSearchParams();
        if (origen === "ruta") {
            if (ruta.value) params.set("ruta", ruta.value);
        } else {
            if (provincia.value) params.set("provincia", provincia.value);
            if (canton.value) params.set("canton", canton.value);
            if (distrito.value) params.set("distrito", distrito.value);
        }

        for (const combo of [provincia, canton, distrito, ruta])
            combo.setAttribute("aria-busy", "true");
        try {
            const respuesta = await fetch(`${geographyUrl}?${params}`, {
                signal: geographyRequest.signal,
                headers: { "Accept": "application/json" }
            });
            if (!respuesta.ok) throw new Error("No fue posible consultar la ubicación.");
            const datos = await respuesta.json();

            if (secuencia !== geographySequence) return;
            const conservar = (combo, opciones, placeholder) => {
                const valor = combo.value;
                reemplazarOpciones(combo, placeholder, opciones ?? []);
                combo.value = opciones?.includes(valor) ? valor : "";
            };

            if (origen === "ruta") {
                conservar(provincia, datos.provincias, "Todas");
                conservar(canton, datos.cantones, "Todos");
                conservar(distrito, datos.distritos, "Todos");
                provincia.disabled = false;
                canton.disabled = !provincia.value;
                distrito.disabled = !canton.value;
            } else {
                conservar(ruta, datos.rutas, "Todas");
                ruta.disabled = false;
                if (provincia.value) {
                    conservar(canton, datos.cantones, "Todos");
                    canton.disabled = false;
                } else {
                    reemplazarOpciones(canton, "Todos", []);
                    canton.disabled = true;
                }
                if (canton.value) {
                    conservar(distrito, datos.distritos, "Todos");
                    distrito.disabled = false;
                } else {
                    reemplazarOpciones(distrito, "Todos", []);
                    distrito.disabled = true;
                }
            }
        } catch (error) {
            if (error.name !== "AbortError" && secuencia === geographySequence)
                form.dataset.geographyError = "No fue posible actualizar las ubicaciones y rutas.";
        } finally {
            if (secuencia === geographySequence)
                for (const combo of [provincia, canton, distrito, ruta])
                    combo.removeAttribute("aria-busy");
        }
    };

    form.addEventListener("submit", () => {
        form.setAttribute("aria-busy", "true");
        if (carga) carga.hidden = false;
        document.body.classList.add("dashboard-is-loading");
    });

    form.querySelector(".filter-actions .text-button")?.addEventListener("click", (event) => {
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
            return;
        if (carga) carga.hidden = false;
        document.body.classList.add("dashboard-is-loading");
    });

    provincia?.addEventListener("change", () => {
        canton.value = "";
        distrito.value = "";
        distrito.disabled = true;
        cargarOpcionesGeograficas("ubicacion");
    });

    canton?.addEventListener("change", () => {
        distrito.value = "";
        if (!canton.value) {
            reemplazarOpciones(distrito, "Todos", []);
            distrito.disabled = true;
            return;
        }
        cargarOpcionesGeograficas("ubicacion");
    });

    distrito?.addEventListener("change", () =>
        cargarOpcionesGeograficas("ubicacion"));

    ruta?.addEventListener("change", () => {
        if (!ruta.value) {
            cargarOpcionesGeograficas("ubicacion");
            return;
        }
        provincia.value = "";
        canton.value = "";
        distrito.value = "";
        cargarOpcionesGeograficas("ruta");
    });
})();

// Guarda filtros y los inserta en la colección "Filtros" de MongoDB.
(() => {
    const form = document.querySelector(".dashboard-filters");
    const boton = document.getElementById("btn-guardar-filtros");
    const dialogo = document.getElementById("dialogo-guardar-filtros");
    const formGuardar = document.getElementById("form-guardar-filtros");
    if (!form || !boton || !dialogo || !formGuardar) return;

    const campoNombre = document.getElementById("nombre-filtros");
    const error = document.getElementById("guardar-filtros-error");
    const aviso = document.getElementById("guardar-filtros-aviso");
    const numericos = new Set(["Anio", "Mes", "DiaSemana"]);

    const leerFiltros = () => {
        const filtros = {};
        for (const select of form.querySelectorAll("select[name^='Filtros.']")) {
            const campo = select.name.replace("Filtros.", "");
            const clave = campo.charAt(0).toLowerCase() + campo.slice(1);
            const valor = select.value.trim();
            filtros[clave] = valor === "" ? null
                : numericos.has(campo) ? Number(valor) : valor;
        }
        return filtros;
    };

    const mostrarAviso = (texto, esError) => {
        aviso.textContent = texto;
        aviso.classList.toggle("dashboard-message--error", esError);
        aviso.classList.toggle("dashboard-message--ok", !esError);
        aviso.hidden = false;
    };

    boton.addEventListener("click", () => {
        const hayFiltros = Object.values(leerFiltros()).some(v => v !== null);
        if (!hayFiltros) {
            mostrarAviso("Selecciona al menos un filtro antes de guardar.", true);
            return;
        }
        aviso.hidden = true;
        error.hidden = true;
        campoNombre.value = "";
        dialogo.showModal();
        campoNombre.focus();
    });

    document.getElementById("cancelar-guardar-filtros")
        .addEventListener("click", () => dialogo.close());

    formGuardar.addEventListener("submit", async evento => {
        evento.preventDefault();
        const enviar = formGuardar.querySelector("button[type='submit']");
        enviar.disabled = true;
        error.hidden = true;

        try {
            const token = formGuardar.querySelector("input[name='__RequestVerificationToken']").value;
            const respuesta = await fetch(formGuardar.action, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": token
                },
                body: JSON.stringify({ nombre: campoNombre.value, filtros: leerFiltros() })
            });
            const datos = await respuesta.json().catch(() => ({}));

            if (!respuesta.ok) {
                error.textContent = datos.mensaje || "No fue posible guardar los filtros.";
                error.hidden = false;
                return;
            }
            dialogo.close();
            mostrarAviso(datos.mensaje, false);
        } catch {
            error.textContent = "No fue posible comunicarse con el servidor.";
            error.hidden = false;
        } finally {
            enviar.disabled = false;
        }
    });
})();
