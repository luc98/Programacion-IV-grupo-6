// JavaScript mínimo y progresivo: todo funciona sin JS; esto solo mejora la experiencia.

(function () {
    'use strict';

    // Evita envíos repetidos accidentales (doble clic). La protección real es la clave de idempotencia del servidor.
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form.matches('form[data-prevent-double-submit]')) return;

        if (form.dataset.submitting === 'true') {
            event.preventDefault();
            return;
        }
        form.dataset.submitting = 'true';
        form.setAttribute('aria-busy', 'true');
        form.querySelectorAll('button[type="submit"]').forEach(function (button) {
            // Se deshabilita después de que el navegador capture el botón presionado.
            setTimeout(function () { button.disabled = true; }, 0);
            if (button.dataset.submittingText) button.textContent = button.dataset.submittingText;
        });
    });

    // Si el usuario vuelve con "Atrás" (bfcache), el formulario debe poder usarse de nuevo.
    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) return;
        document.querySelectorAll('form[data-prevent-double-submit]').forEach(function (form) {
            delete form.dataset.submitting;
            form.removeAttribute('aria-busy');
            form.querySelectorAll('button[type="submit"]').forEach(function (button) { button.disabled = false; });
        });
    });

    // Indicador de carga para enlaces que recargan datos (p. ej. "Actualizar disponibilidad").
    document.addEventListener('click', function (event) {
        var link = event.target.closest('a[data-loading-text]');
        if (!link) return;
        link.textContent = link.dataset.loadingText;
        link.setAttribute('aria-busy', 'true');
        link.classList.add('disabled');
    });
})();
