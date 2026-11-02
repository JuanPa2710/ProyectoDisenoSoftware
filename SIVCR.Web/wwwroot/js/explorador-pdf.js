(function () {
    window.exportarConsultaPdf = function (registros) {
        if (!window.jspdf || !window.jspdf.jsPDF) {
            throw new Error("La librería jsPDF no está disponible.");
        }
        const { jsPDF } = window.jspdf;
        const documento = new jsPDF({
            orientation: "landscape",
            unit: "mm",
            format: "a4"
        });

        const encabezados = [
            "ID", "Fecha", "Hora", "Provincia", "Canton", "Ruta",
            "Vehiculo", "Franja", "Clima", "Calzada", "TPR", "Severidad"
        ];
        const campos = [
            "Id", "Fecha", "Hora", "Provincia", "Canton", "Ruta",
            "Vehiculo", "Franja", "Clima", "Calzada", "Tpr", "Severidad"
        ];

        const margenIzquierdo = 8;
        const margenSuperior = 16;
        const anchoPagina = documento.internal.pageSize.getWidth();
        const altoPagina = documento.internal.pageSize.getHeight();
        const anchoContenido = anchoPagina - (margenIzquierdo * 2);
        const anchoColumna = anchoContenido / encabezados.length;
        const altoFila = 9;

        function dibujarEncabezados(y) {
            documento.setFont("helvetica", "bold");
            documento.setFontSize(7);

            encabezados.forEach((encabezado, indice) => {
                const x = margenIzquierdo + (indice * anchoColumna);
                documento.setFillColor(37, 99, 235);
                documento.setDrawColor(30, 64, 175);
                documento.setTextColor(255, 255, 255);
                documento.rect(x, y, anchoColumna, altoFila, "FD");
                documento.text(
                    encabezado,
                    x + 1.5,
                    y + 6,
                    { maxWidth: anchoColumna - 3 }
                );
            });
        }

        documento.setFillColor(255, 255, 255);
        documento.rect(0, 0, anchoPagina, altoPagina, "F");
        documento.setFont("helvetica", "bold");
        documento.setFontSize(14);
        documento.setTextColor(20, 31, 48);
        documento.text("Consulta de incidentes viales", margenIzquierdo, 10);
        documento.setFont("helvetica", "normal");
        documento.setFontSize(9);
        documento.setTextColor(71, 85, 105);
        documento.text(
            `${registros.length} registro${registros.length === 1 ? "" : "s"} encontrados`,
            anchoPagina - margenIzquierdo,
            10,
            { align: "right" }
        );

        let y = margenSuperior;
        dibujarEncabezados(y);
        y += altoFila;

        registros.forEach((registro, indice) => {
            if (y + altoFila > altoPagina - 10) {
                documento.addPage();
                y = margenSuperior;
                dibujarEncabezados(y);
                y += altoFila;
            }

            documento.setDrawColor(190, 195, 200);
            documento.setFont("helvetica", "normal");
            documento.setFontSize(6.5);
            documento.setTextColor(20, 31, 48);

            campos.forEach((campo, columna) => {
                const valor = String(registro[campo] ?? "");
                const x = margenIzquierdo + (columna * anchoColumna);
                const fondoFila = indice % 2 === 0 ? 248 : 255;
                documento.setFillColor(fondoFila, fondoFila, fondoFila);
                documento.rect(x, y, anchoColumna, altoFila, "FD");
                documento.text(valor, x + 1.5, y + 5.5, {
                    maxWidth: anchoColumna - 3
                });
            });

            if (indice + 1 === registros.length || y + (altoFila * 2) <= altoPagina - 10) {
                y += altoFila;
            } else {
                y += altoFila;
            }
        });

        const archivoPdf = documento.output("blob");
        const url = URL.createObjectURL(archivoPdf);
        const enlace = document.createElement("a");
        enlace.href = url;
        enlace.download = "consulta-incidentes.pdf";
        enlace.style.display = "none";
        document.body.appendChild(enlace);
        enlace.click();
        enlace.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
})();
