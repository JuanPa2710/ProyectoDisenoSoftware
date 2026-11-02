(() => {
    "use strict";

    const formularios = document.querySelectorAll(
        "form[data-carga-accion]"
    );
    const aviso = document.getElementById("cargas-espera");

    if (!formularios.length || !aviso) return;

    const mensajes = {
        subir: "Subiendo el archivo. Esperá a que termine el envío.",
        procesar: "Procesando y validando el archivo. Esto puede tardar unos minutos.",
        importar: "Importando los registros aceptados. Esperá la confirmación antes de volver a intentar."
    };

    let ocupado = false;
    let botonesDesactivados = [];

    function restaurar() {
        ocupado = false;

        for (const boton of botonesDesactivados) {
            boton.disabled = false;
        }

        botonesDesactivados = [];
        aviso.hidden = true;
        aviso.textContent = "";
    }

    for (const formulario of formularios) {
        formulario.addEventListener("submit", (evento) => {
            if (evento.defaultPrevented) return;

            if (ocupado) {
                evento.preventDefault();
                return;
            }

            ocupado = true;

            aviso.textContent =
                mensajes[formulario.dataset.cargaAccion]
                ?? "Realizando la operación. Esperá un momento.";

            aviso.hidden = false;

            botonesDesactivados = Array.from(
                document.querySelectorAll(
                    "form[data-carga-accion] button[type='submit']"
                )
            ).filter(boton => !boton.disabled);

            for (const boton of botonesDesactivados) {
                boton.disabled = true;
            }

            aviso.scrollIntoView({
                behavior: "smooth",
                block: "center"
            });
        });
    }

    window.addEventListener("pageshow", restaurar);
})();
