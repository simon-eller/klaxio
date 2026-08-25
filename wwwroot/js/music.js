(function () {
    'use strict';

    const send = (cmd, data) => window.wsClient.send(cmd, data);

    /* Playlist handling */
    /** Make sure the configured playlist is loaded into the YouTube player. */
    async function ensurePlaylist() {
        const s = window.appState;
        if (window.ytPlayer.hasPlaylist()) return true;
        if (!s.config.playlistUrl) return false;
        try {
            await window.ytPlayer.loadPlaylist(s.config.playlistUrl);
            return true;
        } catch (e) {
            window.ui.toast(window.i18n.t(e.message));
            return false;
        }
    }

    async function validatePlaylist() {
        const url = document.getElementById('playlist-url').value.trim();
        if (!url) { window.ui.showPlaylistError(window.i18n.t('errNoUrl')); return; }
        try {
            const res = await window.ytPlayer.loadPlaylist(url);
            window.ui.showPlaylistOk(window.i18n.t('musicLoaded', res.count));
            window.appState.savePlaylistUrl(url);
        } catch (e) {
            window.ui.showPlaylistError(window.i18n.t(e.message));
        }
    }

    /** Persist the settings form and jump to the game board. */
    function saveSettings() {
        const url    = document.getElementById('playlist-url').value.trim();
        const rounds = parseInt(document.getElementById('rounds-slider').value, 10);

        if (!url) { window.ui.showPlaylistError(window.i18n.t('errNoUrl')); return; }

        // A different playlist invalidates the one currently loaded
        if (url !== window.appState.config.playlistUrl) window.ytPlayer.reset();

        window.appState.savePlaylistUrl(url);
        send('music_config', { playlistUrl: url, rounds });
        window.ui.toast(window.i18n.t('musicSaved'));
        window.ui.showScreen('screen-music');
    }

    /* Host actions */
    async function startGame() {
        if (!(await ensurePlaylist())) return;
        send('music_start');
    }

    function play()    { send('music_play'); }
    function skip()    { send('music_skip'); }
    function reveal()  { send('music_reveal'); }
    function correct() { send('music_correct'); }
    function wrong()   { send('music_wrong'); }
    function restart() { send('music_restart'); }
    function finish() {
        if (confirm(window.i18n.t('confirmFinish'))) send('music_finish');
    }

    /* Server events */
    function handleConfig(msg) {
        const s       = window.appState;
        const changed = s.config.playlistUrl !== msg.config.playlistUrl;

        s.config = msg.config;
        s.savePlaylistUrl(s.config.playlistUrl);

        // Another client picked a different playlist - drop the one we hold.
        if (changed) window.ytPlayer.reset();

        window.ui.render();
    }

    function handleState(msg) {
        const s    = window.appState;
        const prev = s.music.phase;

        s.players           = msg.players;
        s.music.phase       = msg.phase;
        s.music.round       = msg.round;
        s.music.totalRounds = msg.totalRounds;
        s.music.buzzerId    = msg.buzzerId;

        if (msg.outcome === 'start') {
            s.resetTracks();
            window.ui.setProgress(0);
            ensurePlaylist();
        }

        if (msg.phase === 'playing' && prev !== 'playing') startSong();
        if (msg.phase === 'buzzed'  && prev !== 'buzzed')  pauseForBuzz(msg);
        if (msg.phase === 'reveal'  && prev !== 'reveal')  revealTrack();

        if (msg.outcome === 'correct' || msg.outcome === 'wrong' || msg.outcome === 'skip')
            finishRound(msg.outcome, msg.buzzerName);

        if (msg.phase === 'over') {
            window.ytPlayer.stop();
            window.ui.setWave(false);
        }

        route(msg.phase);
        window.ui.bumpScores(msg.bump, 'music');
    }

    /** Keep the visible screen in sync with the music phase. */
    function route(phase) {
        const s          = window.appState;
        const onMusicFlow = s.screen === 'screen-music' || s.screen === 'screen-results';

        if (phase === 'over' && onMusicFlow) {
            s.resultsMode = 'music';
            window.ui.showScreen('screen-results');
        } else if (phase !== 'over' && s.screen === 'screen-results' && s.resultsMode === 'music') {
            window.ui.showScreen('screen-music');
        } else {
            window.ui.render();
        }
    }

    function startSong() {
        const s = window.appState;
        s.currentTrack  = null;
        s.trackRevealed = false;
        window.ui.setProgress(0);

        if (!window.ytPlayer.hasPlaylist()) {
            window.ui.toast(window.i18n.t('errNoPlaylist'));
            return;
        }
        window.ytPlayer.playCurrent();
    }

    function pauseForBuzz(msg) {
        window.ytPlayer.pause();
        window.ui.setWave(false);
        if (msg.buzzerName) window.ui.toast(window.i18n.t('toastBuzzed', msg.buzzerName));
    }

    function revealTrack() {
        const s = window.appState;

        // The meta event may not have fired yet - read it on demand.
        if (!s.currentTrack) s.currentTrack = window.ytPlayer.getMeta();
        s.trackRevealed = true;

        if (s.currentTrack && !s.currentTrack.logged) {
            s.playedTracks.push({
                title:  s.currentTrack.title,
                artist: s.currentTrack.artist,
                thumb:  s.currentTrack.thumb,
            });
            s.currentTrack.logged = true;
        }
    }

    function finishRound(outcome, buzzerName) {
        window.ytPlayer.stop();
        window.ui.setWave(false);

        if (outcome === 'correct')   window.ui.toast(window.i18n.t('toastMusicCorrect', buzzerName));
        else if (outcome === 'wrong') window.ui.toast(window.i18n.t('toastMusicWrong'));
        else                          window.ui.toast(window.i18n.t('toastMusicSkipped'));

        window.ytPlayer.advance(1);
        window.ui.setProgress(0);
    }

    /* YouTube Player wiring */
    function wireYouTube() {
        const yt = window.ytPlayer;

        yt.on('playing', () => window.ui.setWave(true));
        yt.on('paused',  () => window.ui.setWave(false));

        // Song ran out without anybody buzzing: move on automatically.
        yt.on('ended', () => {
            window.ui.setWave(false);
            if (window.appState.music.phase === 'playing') skip();
        });

        yt.on('progress', ({ pct }) => window.ui.setProgress(pct));

        // Metadata is kept on state but stays masked until the reveal.
        yt.on('meta', meta => {
            window.appState.currentTrack = Object.assign({ logged: false }, meta);
            window.ui.render();
        });

        yt.on('error', err => {
            window.ui.showYtError(window.i18n.t(err.key));
            setTimeout(() => {
                yt.advance(1);
                if (window.appState.music.phase === 'playing') yt.playCurrent();
            }, 1500);
        });
    }

    /* Host shortcuts */
    function handleKey(e) {
        if (window.appState.screen !== 'screen-music') return;
        const phase = window.appState.music.phase;

        if (e.code === 'Space' || e.code === 'Enter') {
            e.preventDefault();
            if (phase === 'waiting')     play();
            else if (phase === 'buzzed') reveal();
            return;
        }

        const k = e.key.toLowerCase();
        if (phase === 'reveal' && (k === '+' || k === '1' || k === 'c')) correct();
        else if (phase === 'reveal' && (k === '-' || k === '0' || k === 'w')) wrong();
        else if (phase === 'playing' && k === 's') skip();
        else if (phase === 'buzzed'  && k === 'r') reveal();
    }

    window.music = {
        ensurePlaylist, validatePlaylist, saveSettings, startGame,
        play, skip, reveal, correct, wrong, restart, finish,
        handleState, handleConfig, wireYouTube, handleKey,
    };
})();
