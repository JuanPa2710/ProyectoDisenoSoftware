(() => {
    const form = document.querySelector(".dashboard-filters");
    if (!form) return;
    form.addEventListener("change", event => {
        if (!(event.target instanceof HTMLSelectElement)) return;
        const clear = field => { form.elements.namedItem("Filtros." + field).value = ""; };
        if (event.target.name === "Filtros.Provincia") {
            clear("Canton");
            clear("Distrito");
        } else if (event.target.name === "Filtros.Canton") {
            clear("Distrito");
        }
        form.setAttribute("aria-busy", "true");
        form.requestSubmit();
    });
})();


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