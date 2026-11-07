(() => {
    const mapa = document.querySelector("[data-province-map]");
    if (!mapa) return;

    const svg = mapa.querySelector(".province-map__shapes");
    const tooltip = mapa.querySelector(".province-map__tooltip");
    const filas = [...document.querySelectorAll(".province-ranking .bar-row[data-province]")];
    const conteos = new Map(filas.map(fila => [
        normalizar(fila.dataset.province),
        {
            nombre: fila.dataset.province,
            cantidad: Number(fila.dataset.count) || 0,
            porcentaje: Number(fila.dataset.percentage) || 0
        }
    ]));

    function normalizar(texto) {
        return (texto ?? "")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .trim()
            .toUpperCase();
    }

    function extraerPuntos(coordenadas, puntos = []) {
        if (typeof coordenadas?.[0] === "number") {
            puntos.push(coordenadas);
        } else if (Array.isArray(coordenadas)) {
            for (const coordenada of coordenadas) extraerPuntos(coordenada, puntos);
        }
        return puntos;
    }

    function geometriaAPath(geometria, proyectar) {
        const poligonos = geometria.type === "Polygon"
            ? [geometria.coordinates]
            : geometria.coordinates;
        return poligonos.flatMap(anillos => anillos.map(anillo => {
            const puntos = anillo.map(proyectar);
            return puntos.map(([x, y], indice) => `${indice === 0 ? "M" : "L"}${x.toFixed(2)},${y.toFixed(2)}`).join(" ") + " Z";
        })).join(" ");
    }

    function colorPara(cantidad, maximo) {
        const proporcion = maximo > 0 ? Math.min(1, cantidad / maximo) : 0;
        const bajo = [19, 59, 88];
        const alto = [71, 158, 245];
        const rgb = bajo.map((valor, indice) => Math.round(valor + (alto[indice] - valor) * proporcion));
        return `rgb(${rgb.join(",")})`;
    }

    function mostrarDetalle(elemento, dato) {
        tooltip.textContent = `${dato.nombre}: ${dato.cantidad.toLocaleString("es-CR")} incidentes (${dato.porcentaje.toLocaleString("es-CR", { maximumFractionDigits: 1 })} %)`;
        tooltip.hidden = false;
        elemento.classList.add("is-active");
    }

    function ocultarDetalle(elemento) {
        tooltip.hidden = true;
        elemento.classList.remove("is-active");
    }

    fetch(mapa.dataset.source, { headers: { Accept: "application/geo+json, application/json" } })
        .then(respuesta => {
            if (!respuesta.ok) throw new Error("No se pudo cargar la capa provincial.");
            return respuesta.json();
        })
        .then(geojson => {
            const features = geojson.features ?? [];
            const puntos = features.flatMap(feature => extraerPuntos(feature.geometry.coordinates));
            if (!puntos.length) throw new Error("La capa provincial no contiene geometrías.");

            const latitudMedia = puntos.reduce((suma, punto) => suma + punto[1], 0) / puntos.length;
            const factorLongitud = Math.cos(latitudMedia * Math.PI / 180);
            const transformados = puntos.map(([longitud, latitud]) => [longitud * factorLongitud, latitud]);
            const minX = Math.min(...transformados.map(punto => punto[0]));
            const maxX = Math.max(...transformados.map(punto => punto[0]));
            const minY = Math.min(...transformados.map(punto => punto[1]));
            const maxY = Math.max(...transformados.map(punto => punto[1]));
            const margen = 8;
            const ancho = 304;
            const alto = 314;
            const escala = Math.min(ancho / (maxX - minX), alto / (maxY - minY));
            const dibujadoAncho = (maxX - minX) * escala;
            const dibujadoAlto = (maxY - minY) * escala;
            const offsetX = margen + (ancho - dibujadoAncho) / 2;
            const offsetY = margen + (alto - dibujadoAlto) / 2;
            const proyectar = ([longitud, latitud]) => [
                offsetX + (longitud * factorLongitud - minX) * escala,
                offsetY + (maxY - latitud) * escala
            ];
            const maximo = Math.max(0, ...[...conteos.values()].map(dato => dato.cantidad));

            for (const feature of features) {
                const nombreFuente = feature.properties?.NPROVINCIA ?? feature.properties?.provincia ?? "";
                const dato = conteos.get(normalizar(nombreFuente)) ?? {
                    nombre: nombreFuente,
                    cantidad: 0,
                    porcentaje: 0
                };
                const elemento = document.createElementNS("http://www.w3.org/2000/svg", "path");
                elemento.setAttribute("d", geometriaAPath(feature.geometry, proyectar));
                elemento.setAttribute("class", "province-map__shape");
                elemento.setAttribute("fill", colorPara(dato.cantidad, maximo));
                elemento.setAttribute("aria-label", `${dato.nombre}: ${dato.cantidad.toLocaleString("es-CR")} incidentes`);
                elemento.setAttribute("tabindex", "0");
                const titulo = document.createElementNS("http://www.w3.org/2000/svg", "title");
                titulo.textContent = `${dato.nombre}: ${dato.cantidad.toLocaleString("es-CR")} incidentes`;
                elemento.append(titulo);
                elemento.addEventListener("pointerenter", () => mostrarDetalle(elemento, dato));
                elemento.addEventListener("pointerleave", () => ocultarDetalle(elemento));
                elemento.addEventListener("focus", () => mostrarDetalle(elemento, dato));
                elemento.addEventListener("blur", () => ocultarDetalle(elemento));
                svg.append(elemento);
            }
        })
        .catch(() => {
            mapa.classList.add("province-map--error");
            mapa.setAttribute("aria-label", "No fue posible cargar el mapa provincial. Los datos por provincia siguen disponibles en la lista.");
        });
})();
