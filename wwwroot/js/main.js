(function () {
    'use strict';

    function $(id) { return document.getElementById(id); }

    const send = (cmd, data) => window.wsClient.send(cmd, data);

    /* Navigation */
    /**
     * Switch screens and tell the server what the Echo Buttons should do:
     * register a seat, buzz for Klaxio, or buzz for Klaxio Music.
     */
    function navigate(id) {
        const s    = window.appState;
        const from = s.screen;

        if (from === 'screen-register' && id !== 'screen-register') {
            s.registerOpen = false;
            send('register_close');
        }

        switch (id) {
            case 'screen-register':
                s.registerOpen = true;
                send('register_open');
                break;
            case 'screen-quiz':
                s.mode = 'quiz';
                send('set_mode', { mode: 'quiz' });
                break;
            case 'screen-music':
                s.mode = 'music';
                send('set_mode', { mode: 'music' });
                break;
            case 'screen-music-settings':
                window.ui.clearPlaylistFeedback();
                window.ui.fillMusicSettings();
                break;
        }

        window.ui.showScreen(id);
        if (window.drawer) window.drawer.closeIfOverlay();
    }

    /** Where to land after connecting (or reconnecting). */
    function initialScreen() {
        const s = window.appState;

        // The registration screen opens by itself until there is a full roster.
        if (s.players.length < 2) return 'screen-register';

        if (s.mode === 'music') {
            if (s.music.phase === 'over') { s.resultsMode = 'music'; return 'screen-results'; }
            return 'screen-music';
        }
        if (s.quiz.phase === 'over') { s.resultsMode = 'quiz'; return 'screen-results'; }
        return 'screen-quiz';
    }

    /* Server events */
    function handleServerEvent(msg) {
        const s = window.appState;

        switch (msg.event) {
            case 'init':
                window.i18n.apply(msg.lang, msg.strings);
                s.players = msg.players;
                s.mode    = msg.mode;
                s.config  = msg.config;
                s.registerOpen = msg.registerOpen;

                s.quiz.phase    = msg.quiz.phase;
                s.quiz.winnerId = msg.quiz.winnerId;

                s.music.phase       = msg.music.phase;
                s.music.round       = msg.music.round;
                s.music.totalRounds = msg.music.totalRounds;
                s.music.buzzerId    = msg.music.buzzerId;

                window.ui.fillMusicSettings();

                // A reload during a music game loses the loaded playlist - fetch it again.
                if (s.music.phase !== 'idle' && s.config.playlistUrl) window.music.ensurePlaylist();

                navigate(initialScreen());
                break;

            case 'strings':
                window.i18n.apply(msg.lang, msg.strings);
                s.players = msg.players;
                window.ui.render();
                break;

            case 'player_registered':
                s.players = msg.players;
                window.ui.render();
                window.ui.highlightPlayer(msg.id);
                {
                    const p = s.findPlayer(msg.id);
                    if (p) window.ui.toast(window.i18n.t('toastRegistered', p.name));
                }
                break;

            case 'button_known':
                window.ui.highlightPlayer(msg.id);
                {
                    const p = s.findPlayer(msg.id);
                    if (p) window.ui.toast(window.i18n.t('toastButtonKnown', p.name));
                }
                break;

            case 'players_updated':
                s.players = msg.players;
                window.ui.render();
                break;

            case 'register_state':
                s.registerOpen = msg.open;
                s.players      = msg.players;
                window.ui.render();
                break;

            case 'mode_changed':
                s.mode = msg.mode;
                break;

            case 'quiz_state':
                window.quiz.handleState(msg);
                break;

            case 'music_state':
                window.music.handleState(msg);
                break;

            case 'music_config':
                window.music.handleConfig(msg);
                break;
        }
    }

    /* Wiring */
    function wireNavigation() {
        document.addEventListener('click', e => {
            const btn = e.target.closest('[data-nav]');
            if (btn) { navigate(btn.dataset.nav); return; }

            const lang = e.target.closest('[data-lang]');
            if (lang) send('lang_' + lang.dataset.lang);
        });
    }

    function wireRegisterScreen() {
        $('btn-register-done').addEventListener('click', () => navigate(window.appState.lastGameScreen));

        const list = $('register-list');

        list.addEventListener('change', e => {
            const input = e.target.closest('[data-rename-id]');
            if (input) send('rename_player', { id: input.dataset.renameId, name: input.value });
        });

        list.addEventListener('click', e => {
            const toggle = e.target.closest('[data-toggle-id]');
            if (toggle) { send('toggle_player', { id: toggle.dataset.toggleId }); return; }

            const remove = e.target.closest('[data-remove-id]');
            if (!remove) return;
            const player = window.appState.findPlayer(remove.dataset.removeId);
            if (!player) return;
            if (confirm(window.i18n.t('confirmRemove', player.name)))
                send('remove_player', { id: player.id });
        });
    }

    function wireQuizScreen() {
        $('btn-arm').addEventListener('click',     window.quiz.arm);
        $('btn-correct').addEventListener('click', window.quiz.correct);
        $('btn-wrong').addEventListener('click',   window.quiz.wrong);
        $('btn-reset').addEventListener('click',   window.quiz.reset);
        $('btn-scores').addEventListener('click',  window.quiz.resetScores);
        $('btn-finish').addEventListener('click',  window.quiz.finish);
    }

    function wireMusicScreen() {
        $('btn-music-start').addEventListener('click',  window.music.startGame);
        $('btn-play').addEventListener('click',         window.music.play);
        $('btn-skip').addEventListener('click',         window.music.skip);
        $('btn-music-finish').addEventListener('click', window.music.finish);

        $('btn-reveal').addEventListener('click',         window.music.reveal);
        $('btn-reveal-correct').addEventListener('click', window.music.correct);
        $('btn-reveal-wrong').addEventListener('click',   window.music.wrong);
        $('btn-reveal-close').addEventListener('click',   window.music.dismissSkipReveal);
        $('btn-reveal-next').addEventListener('click',    () => {
            window.music.dismissSkipReveal();
            window.music.play();
        });

        $('progress-wrap').addEventListener('click', e => {
            if (!window.ytPlayer.isPlaying()) return;
            const rect = e.currentTarget.getBoundingClientRect();
            window.ytPlayer.seekRelative((e.clientX - rect.left) / rect.width);
        });
    }

    function wireMusicSettings() {
        $('btn-validate').addEventListener('click',   window.music.validatePlaylist);
        $('btn-music-save').addEventListener('click', window.music.saveSettings);

        const slider = $('rounds-slider');
        slider.addEventListener('input', () => { $('rounds-val').textContent = slider.value; });

        $('playlist-url').addEventListener('input', window.ui.clearPlaylistFeedback);
    }

    function wireResultsScreen() {
        $('btn-results-again').addEventListener('click', () => {
            if (window.appState.resultsMode === 'music') window.music.restart();
            else                                         window.quiz.restart();
        });

        $('btn-results-back').addEventListener('click', () => {
            navigate(window.appState.resultsMode === 'music' ? 'screen-music' : 'screen-quiz');
        });
    }

    function wireKeyboard() {
        document.addEventListener('keydown', e => {
            if (e.target.closest('input, textarea, select, button, a, [contenteditable]')) return;
            window.quiz.handleKey(e);
            window.music.handleKey(e);
        });
    }

    function wireWebSocket() {
        window.wsClient.on('open', () => {
            window.ui.setConnState(true);
            window.ui.toast(window.i18n.t('toastConnected'));
        });
        window.wsClient.on('close',   () => window.ui.setConnState(false));
        window.wsClient.on('message', handleServerEvent);
    }

    // Screen changes that also have to tell the server what the buttons are for
    // must go through navigate() rather than ui.showScreen().
    window.nav = { go: navigate };

    document.addEventListener('DOMContentLoaded', () => {
        wireNavigation();
        wireRegisterScreen();
        wireQuizScreen();
        wireMusicScreen();
        wireMusicSettings();
        wireResultsScreen();
        wireKeyboard();
        wireWebSocket();

        window.music.wireYouTube();
        window.ui.render();
        window.wsClient.connect();
    });
})();
