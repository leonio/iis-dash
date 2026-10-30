// IIS Dash's small layer of browser behaviour. htmx does the requests and UIkit the dropdowns, the panel and alerts;
// this does what is purely local: the theme, opening the slide-in panel, the drop zone, and keeping URLs clean.
(function () {
  "use strict";

  const store = {
    get(key) { try { return localStorage.getItem(key); } catch (e) { return null; } },
    set(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* private mode */ } },
  };

  document.addEventListener("click", (event) => {
    const button = event.target.closest("button[data-theme-toggle]");
    if (!button) return;
    const dark = !document.documentElement.classList.contains("dark");
    document.documentElement.classList.toggle("dark", dark);
    store.set("theme", dark ? "dark" : "light");
  });

  // --- the slide-in panel -----------------------------------------------------------------------------------------
  // Links made with Ui.Sheet also carry hx-get into #sheet-body: htmx loads the page, the server leaves the page
  // around it out, and this opens the panel around it with the link's title. Each page still has its own URL.

  document.addEventListener("click", (event) => {
    const link = event.target.closest("a[data-sheet]");
    if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || !window.UIkit) return;
    document.getElementById("sheet-title").textContent = link.dataset.sheet;
    document.getElementById("sheet-body").innerHTML = '<div class="id-overlay-loading"><span class="spinner"></span></div>';
    document.getElementById("sheet-page").href = link.getAttribute("href");
    UIkit.modal(document.getElementById("sheet")).show();
  });

  // A link inside the panel that moves the page (a filter): the panel closes so the page shows.
  document.addEventListener("click", (event) => {
    const link = event.target.closest("#sheet-body a[hx-target='#app']");
    if (link && window.UIkit) UIkit.modal(document.getElementById("sheet")).hide();
  });

  document.addEventListener("hidden", (event) => {
    if (event.target.id === "sheet") document.getElementById("sheet-body").innerHTML = "";
  });

  // --- filters: empty fields are left out of the URL ----------------------------------------------------------------
  // The filter bar is a GET form; without this, every empty field would show in the address bar as "&method=".

  document.addEventListener("htmx:config:request", (event) => {
    const request = event.detail?.ctx?.request;
    if (!request || request.method !== "GET" || !(request.body instanceof FormData)) return;
    for (const key of [...new Set(request.body.keys())]) {
      if (request.body.getAll(key).every((value) => value === "")) request.body.delete(key);
    }
  });

  // Without htmx (or before it loads), the same for a plain submit.
  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (form.method.toLowerCase() !== "get" || form.hasAttribute("hx-get")) return;
    for (const field of form.elements) if (field.name && field.value === "") field.disabled = true;
  });

  // --- the drop zone ------------------------------------------------------------------------------------------------

  for (const type of ["dragenter", "dragover"]) {
    document.addEventListener(type, (event) => {
      event.target.closest?.(".id-drop")?.classList.add("id-dragging");
    });
  }
  for (const type of ["dragleave", "drop"]) {
    document.addEventListener(type, (event) => {
      event.target.closest?.(".id-drop")?.classList.remove("id-dragging");
    });
  }
})();
