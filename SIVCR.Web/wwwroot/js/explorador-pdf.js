(function () {
    window.exportarConsultaPdf = function (registros, columnasConsulta) {
        if (!window.jspdf || !window.jspdf.jsPDF) {
            throw new Error("La librería jsPDF no está disponible.");
        }
        const { jsPDF } = window.jspdf;
        const documento = new jsPDF({
            orientation: "landscape",
            unit: "mm",
            format: "a4"
        });

        let columnas = [];
        if (columnasConsulta !== null && columnasConsulta !== undefined) {
            if (columnasConsulta.length > 0) {
                columnas = columnasConsulta;
            }
        }

        const encabezados = columnas.map((columna) => {
            if (columna.Etiqueta !== null && columna.Etiqueta !== undefined) {
                return columna.Etiqueta;
            }
            return columna.etiqueta;
        });
        const campos = columnas.map((columna) => {
            if (columna.Campo !== null && columna.Campo !== undefined) {
                return columna.Campo;
            }
            return columna.campo;
        });

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
        let etiquetaRegistros = " registros";
        if (registros.length === 1) {
            etiquetaRegistros = " registro";
        }
        documento.text(
            `${registros.length}${etiquetaRegistros} encontrados`,
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
                let valor = "";
                if (registro[campo] !== null && registro[campo] !== undefined) {
                    valor = registro[campo];
                }
                const textoValor = String(valor);
                const x = margenIzquierdo + (columna * anchoColumna);
                let fondoFila = 255;
                if (indice % 2 === 0) {
                    fondoFila = 248;
                }
                documento.setFillColor(fondoFila, fondoFila, fondoFila);
                documento.rect(x, y, anchoColumna, altoFila, "FD");
                documento.text(textoValor, x + 1.5, y + 5.5, {
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
