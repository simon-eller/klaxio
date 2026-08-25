(function () {
    'use strict';

    let ws             = null;
    let reconnectTimer = null;

    const listeners = { open: [], close: [], message: [] };

    function emit(event, data) {
        (listeners[event] || []).forEach(fn => {
            try { fn(data); } catch (e) { console.error(`[ws] listener ${event} failed`, e); }
        });
    }

    function connect() {
        const proto = location.protocol === 'https:' ? 'wss' : 'ws';
        ws = new WebSocket(`${proto}://${location.host}/`);

        ws.addEventListener('open', () => {
            console.log('[ws] open');
            clearTimeout(reconnectTimer);
            emit('open');
        });

        ws.addEventListener('close', () => {
            console.log('[ws] closed - scheduling reconnect');
            emit('close');
            reconnectTimer = setTimeout(connect, 2000);
        });

        ws.addEventListener('error', () => {
            try { ws.close(); } catch (e) { /* already closing */ }
        });

        ws.addEventListener('message', ({ data }) => {
            try { emit('message', JSON.parse(data)); }
            catch (e) { console.error('[ws] invalid JSON', e); }
        });
    }

    window.wsClient = {
        connect,

        on(event, fn) {
            if (!listeners[event]) listeners[event] = [];
            listeners[event].push(fn);
        },

        send(cmd, data) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify(Object.assign({ cmd }, data || {})));
            } else {
                console.warn('[ws] cannot send - socket not open:', cmd);
            }
        },

        isOpen() { return !!ws && ws.readyState === WebSocket.OPEN; },
    };
})();
