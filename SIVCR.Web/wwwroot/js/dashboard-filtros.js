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
