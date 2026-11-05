// Polling sencillo del seguimiento de una solicitud.
// - Una sola consulta a la vez (no se superponen).
// - Se detiene en estados terminales, al ocultar la pestaña y al abandonar la página.
// - Ante un error temporal conserva el último estado conocido, avisa y reintenta con espera creciente.
// - La cuenta regresiva es informativa: nunca decide que la reserva venció; eso lo informa el servidor.

(function () {
    'use strict';

    var root = document.querySelector('[data-tracking]');
    if (!root) return;

    var BASE_INTERVAL = 2000;
    var MAX_INTERVAL = 30000;

    var pollUrl = root.dataset.pollUrl;
    var panel = root.querySelector('[data-tracking-panel]');
    var alertBox = root.querySelector('[data-tracking-alert]');
    var live = root.querySelector('[data-tracking-live]');
    var refresh = root.querySelector('[data-tracking-refresh]');
    var mode = root.querySelector('[data-tracking-mode]');

    var interval = BASE_INTERVAL;
    var timer = null;
    var inFlight = null; // AbortController de la consulta en curso
    var stopped = false; // terminal, acceso denegado o página abandonada
    var countdownTimer = null;

    function current() { return panel.querySelector('[data-terminal]'); }
    function isTerminal() { var el = current(); return el && el.dataset.terminal === 'true'; }

    function showAlert(message) {
        alertBox.textContent = message;
        alertBox.classList.remove('d-none');
    }

    function hideAlert() {
        alertBox.textContent = '';
        alertBox.classList.add('d-none');
    }

    function setMode(text) { if (mode) mode.textContent = text; }

    function schedule() {
        clearTimeout(timer);
        if (stopped || isTerminal() || document.hidden) return;
        timer = setTimeout(poll, interval);
    }

    function stop(text) {
        stopped = true;
        clearTimeout(timer);
        if (text) setMode(text);
    }

    function poll() {
        if (inFlight || stopped) return;
        clearTimeout(timer);

        var controller = new AbortController();
        inFlight = controller;
        root.setAttribute('aria-busy', 'true');
        var previousState = current() && current().dataset.state;

        fetch(pollUrl, {
            headers: { 'X-Requested-With': 'fetch' },
            cache: 'no-store',
            credentials: 'same-origin',
            signal: controller.signal
        })
            .then(function (response) {
                if (response.ok) {
                    return response.text().then(function (html) {
                        panel.innerHTML = html;
                        hideAlert();
                        interval = BASE_INTERVAL;
                        startCountdowns();
                        var el = current();
                        if (el && el.dataset.state !== previousState && el.dataset.stateLabel) {
                            live.textContent = 'Estado actualizado: ' + el.dataset.stateLabel + '.';
                        }
                        if (isTerminal()) stop('Estado final: no hace falta seguir actualizando.');
                    });
                }

                if (response.status === 401) {
                    stop('Actualización detenida.');
                    showAlert('Tu sesión venció. Volvé a iniciar sesión para ver el estado; tu solicitud no se modifica.');
                    return;
                }
                if (response.status === 403 || response.status === 404) {
                    stop('Actualización detenida.');
                    showAlert(response.status === 403
                        ? 'No tenés acceso a esta solicitud.'
                        : 'No encontramos esta solicitud.');
                    return;
                }
                throw new Error('temporal');
            })
            .catch(function (error) {
                if (error && error.name === 'AbortError') return;
                interval = Math.min(interval * 2, MAX_INTERVAL);
                showAlert('No pudimos actualizar el estado. Te mostramos el último estado conocido; ' +
                    'volvemos a intentar en ' + Math.round(interval / 1000) + ' s o podés actualizar manualmente.');
            })
            .finally(function () {
                if (inFlight === controller) inFlight = null;
                root.removeAttribute('aria-busy');
                schedule();
            });
    }

    // ---- Cuenta regresiva informativa (corrige el desfase entre el reloj del servidor y el del navegador)
    function startCountdowns() {
        clearInterval(countdownTimer);
        var items = panel.querySelectorAll('[data-countdown-to]');
        if (items.length === 0) return;
        var loadedAt = Date.now();

        function tick() {
            items.forEach(function (el) {
                var remaining = Date.parse(el.dataset.countdownTo) - Date.parse(el.dataset.serverNow) - (Date.now() - loadedAt);
                if (remaining > 0) {
                    var total = Math.ceil(remaining / 1000);
                    var m = Math.floor(total / 60);
                    var s = String(total % 60).padStart(2, '0');
                    el.textContent = 'Quedan aproximadamente ' + m + ':' + s + '.';
                } else {
                    el.textContent = 'El plazo informado terminó; esperando el resultado del sistema.';
                }
            });
        }
        tick();
        countdownTimer = setInterval(tick, 1000);
    }

    // ---- Eventos
    if (refresh) {
        refresh.addEventListener('click', function (event) {
            event.preventDefault();
            // Consulta inmediata. Si el resultado no es terminal, el polling automático se reanuda.
            stopped = false;
            interval = BASE_INTERVAL;
            poll();
        });
    }

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) {
            clearTimeout(timer);
        } else if (!stopped && !isTerminal()) {
            poll();
        }
    });

    window.addEventListener('pagehide', function () {
        stop();
        clearInterval(countdownTimer);
        if (inFlight) inFlight.abort();
    });

    startCountdowns();
    if (isTerminal()) {
        stopped = true;
    } else {
        schedule();
    }
})();
