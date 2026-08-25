(function () {
    'use strict';

    const MEDALS = ['🥇', '🥈', '🥉'];

    const QUIZ_PHASE_KEY = {
        waiting: 'phaseReady', armed:   'phaseArmed', buzzed: 'phaseBuzzed',
        correct: 'phaseCorrect', wrong: 'phaseWrong', over:   'phaseOver',
    };

    const MUSIC_PHASE_KEY = {
        idle:    'phaseReady',        waiting: 'phaseReady', playing: 'musicPhasePlaying',
        buzzed:  'phaseBuzzed',       reveal:  'musicPhaseReveal', over: 'phaseOver',
    };

    function $(id) { return document.getElementById(id); }

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function show(el, visible) {
        if (el) el.classList.toggle('d-none', !visible);
    }

    /* Screen routing */
    function showScreen(id) {
        const s = window.appState;
        if (!$(id)) return;
        s.screen = id;
        if (id === 'screen-quiz' || id === 'screen-music') s.lastGameScreen = id;
        render();
        const main = $('main-content');
        if (main) main.scrollTop = 0;
    }

    /* Connection indicator */
    function setConnState(connected) {
        window.appState.connected = connected;
        const dot = $('conn-dot');
        if (dot) {
            dot.classList.toggle('bg-success', connected);
            dot.classList.toggle('bg-danger', !connected);
            dot.classList.toggle('ok', connected);
        }
        const txt = $('conn-text');
        if (txt) txt.textContent = window.i18n.t(connected ? 'connConnected' : 'connReconnecting');
    }

    /* Toast */
    let toastTimer;
    function toast(msg) {
        const el = $('toast');
        if (!el) return;
        el.textContent = msg;
        el.classList.add('show');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => el.classList.remove('show'), 2800);
    }

    /* Focus preservation */
    //Re-rendering the register list while somebody is typing a name would drop the caret, so we put it back afterwards.
    function withPreservedFocus(fn) {
        const active = document.activeElement;
        const key    = active && active.dataset ? active.dataset.renameId : null;
        const start  = key ? active.selectionStart : null;
        const end    = key ? active.selectionEnd   : null;

        fn();

        if (!key) return;
        const restored = document.querySelector(`[data-rename-id="${key}"]`);
        if (!restored) return;
        restored.focus();
        try { restored.setSelectionRange(start, end); } catch (e) { /* unsupported type */ }
    }

    /* Player panels */
    function panelBg(phase, playerId, focusId, mode) {
        const isFocus = !!focusId && playerId === focusId;
        switch (phase) {
            case 'buzzed':
            case 'reveal':  return isFocus ? 'text-bg-info' : 'text-bg-light opacity-50';
            case 'correct': return isFocus ? 'text-bg-success' : 'text-bg-light';
            case 'wrong':   return isFocus ? 'text-bg-danger'
                                           : (mode === 'music' ? 'text-bg-success' : 'text-bg-light');
            default:        return 'text-bg-light';
        }
    }

    function panelIcon(phase, playerId, focusId, mode) {
        const isFocus = !!focusId && playerId === focusId;
        switch (phase) {
            case 'armed':
            case 'playing': return 'my_location';
            case 'buzzed':
            case 'reveal':  return isFocus ? 'notifications_active' : 'hourglass_empty';
            case 'correct': return isFocus ? 'check_circle' : '';
            case 'wrong':   return isFocus ? 'cancel' : (mode === 'music' ? 'add_circle' : '');
            default:        return 'radio_button_unchecked';
        }
    }

    function renderPlayers(containerId, mode, phase, focusId) {
        const wrap = $(containerId);
        if (!wrap) return;
        const s = window.appState;

        wrap.innerHTML = s.activePlayers().map(p => {
            const icon = panelIcon(phase, p.id, focusId, mode);
            return `
            <div class="player-panel card border rounded-4 ${panelBg(phase, p.id, focusId, mode)}"
                 id="${mode}-panel-${escHtml(p.id)}">
                <div class="card-body d-flex flex-column align-items-center justify-content-center gap-2">
                    <div class="player-name text-truncate w-100">${escHtml(p.name)}</div>
                    <div class="player-score score-badge" id="${mode}-score-${escHtml(p.id)}">${s.scoreOf(p, mode)}</div>
                    <div class="player-icon">${icon ? `<span class="material-symbols-outlined">${icon}</span>` : ''}</div>
                </div>
            </div>`;
        }).join('');
    }

    /** Replay the score bump animation for the given players. */
    function bumpScores(ids, mode) {
        (ids || []).forEach(id => {
            const el = $(`${mode}-score-${id}`);
            if (!el) return;
            el.classList.remove('bump');
            void el.offsetWidth;      // force reflow so the animation restarts
            el.classList.add('bump');
        });
    }

    /* Klaxio (classic) */
    function renderQuiz() {
        const s        = window.appState;
        const hasSeats = s.players.length > 0;

        show($('quiz-empty'), !hasSeats);
        show($('quiz-stage'), hasSeats);
        if (!hasSeats) return;

        renderPlayers('quiz-players', 'quiz', s.quiz.phase, s.quiz.winnerId);

        const label = $('quiz-phase-label');
        label.className   = `phase-label small fw-semibold my-2 ${s.quiz.phase}`;
        label.textContent = window.i18n.t(QUIZ_PHASE_KEY[s.quiz.phase] || 'phaseReady');
    }

    /* Klaxio Music */
    /** A song round is in flight - "idle" and "over" both mean "no game on the board". */
    function musicRunning() {
        const phase = window.appState.music.phase;
        return phase !== 'idle' && phase !== 'over';
    }

    function renderMusic() {
        const s          = window.appState;
        const phase      = s.music.phase;
        const running    = musicRunning();
        const hasList    = !!s.config.playlistUrl;
        const enoughSeats = s.activePlayers().length >= 2;

        show($('music-empty-playlist'), !running && !hasList);
        show($('music-empty-players'),  !running && hasList && !enoughSeats);
        show($('music-ready'),          !running && hasList && enoughSeats);
        show($('music-stage'),          running);

        if (!running) {
            $('music-ready-hint').textContent =
                window.i18n.t('musicReadyHint', s.config.rounds, s.activePlayers().length);
            return;
        }

        renderPlayers('music-players', 'music', phase, s.music.buzzerId);

        const label = $('music-phase-label');
        label.className   = `phase-label small fw-semibold my-2 ${phase}`;
        label.textContent = window.i18n.t(MUSIC_PHASE_KEY[phase] || 'phaseReady');
    }

    /* Track bar (the Klaxio Music navbar) */
    function renderTrackBar() {
        const s       = window.appState;
        const visible = s.screen === 'screen-music' && musicRunning();
        show($('track-bar'), visible);
        if (!visible) return;

        $('round-badge').textContent = window.i18n.t('musicRoundBadge', s.music.round, s.music.totalRounds);

        const track   = s.currentTrack;
        const unknown = window.i18n.t('musicUnknown');
        const dash    = '—';

        if (!track) {
            $('track-title').textContent  = dash;
            $('track-artist').textContent = dash;
        } else if (s.trackRevealed) {
            $('track-title').textContent  = track.title  || dash;
            $('track-artist').textContent = track.artist || dash;
        } else {
            // A song is running: never leak title, artist or cover art.
            $('track-title').textContent  = unknown;
            $('track-artist').textContent = unknown;
        }

        const showCover = !!(track && s.trackRevealed && track.thumb);
        show($('track-thumb'), showCover);
        show($('track-thumb-mask'), !showCover);
        if (showCover) $('track-thumb').src = track.thumb;
    }

    function setProgress(pct) {
        const el = $('progress-fill');
        if (el) el.style.width = `${pct}%`;
    }

    function setWave(on) {
        const el = $('wave-bars');
        if (el) el.classList.toggle('show', on);
    }

    function showYtError(msg) {
        $('yt-error-text').textContent = msg;
        $('yt-error').classList.add('show');
        setTimeout(() => $('yt-error').classList.remove('show'), 6000);
    }

    /* Overlays*/
    function renderOverlays() {
        const s       = window.appState;
        const onMusic = s.screen === 'screen-music';
        const phase   = s.music.phase;
        const buzzer  = s.findPlayer(s.music.buzzerId);

        const buzzedOv = $('buzzed-overlay');
        const buzzed   = onMusic && phase === 'buzzed';
        buzzedOv.classList.toggle('show', buzzed);
        if (buzzed) $('buzzed-name').textContent = buzzer ? buzzer.name : '—';

        const revealOv = $('reveal-overlay');
        const reveal   = onMusic && phase === 'reveal';
        revealOv.classList.toggle('show', reveal);
        if (reveal) {
            const track = s.currentTrack || {};
            const dash  = '—';
            $('reveal-buzzer-name').textContent = buzzer ? buzzer.name : dash;
            $('reveal-title').textContent       = track.title  || dash;
            $('reveal-artist').textContent      = track.artist || dash;

            const img = $('reveal-thumb');
            if (track.thumb) { img.src = track.thumb; img.classList.remove('d-none'); }
            else             { img.classList.add('d-none'); }
        }
    }

    /* Players and Buttons screen */
    function renderRegister() {
        const s    = window.appState;
        const list = $('register-list');

        $('register-count').textContent =
            window.i18n.t('registerCount', s.activePlayers().length, s.players.length);

        show($('register-empty'), s.players.length === 0);

        list.innerHTML = s.players.map(p => `
            <div class="register-row card border rounded-4" id="register-row-${escHtml(p.id)}">
                <div class="card-body d-flex align-items-center gap-2 py-2">
                    <span class="material-symbols-outlined ${p.active ? 'text-success' : 'text-body-secondary'} fs-1"
                          aria-hidden="true">${p.active ? 'person' : 'person_off'}</span>

                    <div class="flex-grow-1 min-w-0">
                        <input type="text" class="form-control form-control-sm"
                               data-rename-id="${escHtml(p.id)}" value="${escHtml(p.name)}"
                               aria-label="${escHtml(window.i18n.t('registerName'))}">
                        <div class="player-button small text-body-secondary mt-1 text-truncate">
                            ${escHtml(p.button)}${p.active ? '' : ' · ' + escHtml(window.i18n.t('statusInactive'))}
                        </div>
                    </div>

                    <button type="button" class="btn btn-sm ${p.active ? 'btn-outline-secondary' : 'btn-outline-success'} icon-link py-3"
                            data-toggle-id="${escHtml(p.id)}"
                            title="${escHtml(window.i18n.t(p.active ? 'registerDeactivate' : 'registerActivate'))}"
                            aria-label="${escHtml(window.i18n.t(p.active ? 'registerDeactivate' : 'registerActivate'))}">
                        <span class="material-symbols-outlined fs-6" aria-hidden="true">${p.active ? 'person_off' : 'person'}</span>
                    </button>

                    <button type="button" class="btn btn-sm btn-outline-danger icon-link py-3"
                            data-remove-id="${escHtml(p.id)}"
                            title="${escHtml(window.i18n.t('registerRemove'))}"
                            aria-label="${escHtml(window.i18n.t('registerRemove'))}">
                        <span class="material-symbols-outlined fs-6" aria-hidden="true">delete</span>
                    </button>
                </div>
            </div>`).join('');
    }

    /** Briefly outline the seat that already owns a pressed button. */
    function highlightPlayer(id) {
        const row = $(`register-row-${id}`);
        if (!row) return;
        row.classList.add('highlight');
        setTimeout(() => row.classList.remove('highlight'), 1200);
    }

    /* Playlist settings */
    function fillMusicSettings() {
        const s = window.appState;
        $('playlist-url').value      = s.config.playlistUrl || s.loadPlaylistUrl();
        $('rounds-slider').value     = s.config.rounds;
        $('rounds-val').textContent  = s.config.rounds;
    }

    function showPlaylistOk(text) {
        $('playlist-ok-text').textContent = text;
        show($('playlist-ok'), true);
        show($('playlist-err'), false);
    }

    function showPlaylistError(msg) {
        $('playlist-err').textContent = msg;
        show($('playlist-err'), true);
        show($('playlist-ok'), false);
    }

    function clearPlaylistFeedback() {
        show($('playlist-ok'), false);
        show($('playlist-err'), false);
    }

    /* Results */
    /**
     * Dense ranking (1, 1, 3, ...) over the players of one game.
     * Somebody who is sitting out only shows up if they scored before that.
     */
    function rankPlayers(mode) {
        const s      = window.appState;
        const sorted = s.players
            .filter(p => p.active || s.scoreOf(p, mode) > 0)
            .sort((a, b) => s.scoreOf(b, mode) - s.scoreOf(a, mode));

        let rank = 1;
        return sorted.map((p, i) => {
            if (i > 0 && s.scoreOf(p, mode) < s.scoreOf(sorted[i - 1], mode)) rank = i + 1;
            return { player: p, rank, score: s.scoreOf(p, mode) };
        });
    }

    function renderResults() {
        const s      = window.appState;
        const mode   = s.resultsMode;
        const ranked = rankPlayers(mode);

        renderWinnerLine(ranked);
        renderPodium(ranked);
        renderRanking(ranked);

        const tracks = mode === 'music' ? s.playedTracks : [];
        show($('results-tracks-wrap'), tracks.length > 0);
        if (tracks.length) renderTrackHistory(tracks);
    }

    function renderWinnerLine(ranked) {
        const el      = $('results-winner');
        const winners = ranked.filter(r => r.rank === 1);

        if (!winners.length || winners[0].score === 0) {
            el.textContent = window.i18n.t('resultsNoScore');
            return;
        }
        const names = winners.map(r => r.player.name);
        el.textContent = names.length === 1
            ? window.i18n.t('resultsWinner', names[0])
            : window.i18n.t('resultsWinners', names.join(', '));
    }

    function renderPodium(ranked) {
        const cont = $('podium');

        // Group by rank so players on equal scores share one step
        const byRank = new Map();
        ranked.forEach(r => {
            if (!byRank.has(r.rank)) byRank.set(r.rank, []);
            byRank.get(r.rank).push(r);
        });

        // Visual order: silver | gold | bronze | everyone else
        const order = [2, 1, 3].filter(r => byRank.has(r));
        Array.from(byRank.keys()).filter(r => r > 3).sort((a, b) => a - b).forEach(r => order.push(r));

        cont.innerHTML = order.map(rank => {
            const group = byRank.get(rank);
            const cls   = rank <= 3 ? `rank-${rank}` : 'rank-n';
            const medal = MEDALS[rank - 1] || rank;
            const names = group.map(r => `<div class="podium-name">${escHtml(r.player.name)}</div>`).join('');
            return `
                <div class="podium-bar ${cls}">
                    <div class="podium-medal" aria-hidden="true">${medal}</div>
                    ${names}
                    <div class="podium-score">${group[0].score}</div>
                </div>`;
        }).join('');
    }

    function renderRanking(ranked) {
        const pts = window.i18n.t('rankPoints');
        $('ranking').innerHTML = ranked.map(r => `
            <div class="rank-row ${r.rank === 1 && r.score > 0 ? 'is-winner' : ''}">
                <span class="rank-pos">${r.rank}</span>
                <span class="fw-semibold flex-grow-1 text-truncate">${escHtml(r.player.name)}</span>
                <span class="fw-bold">${r.score}<span class="small text-body-secondary ms-1">${escHtml(pts)}</span></span>
            </div>`).join('');
    }

    function renderTrackHistory(tracks) {
        $('results-tracks').innerHTML = tracks.map((t, i) => `
            <div class="track-row d-flex align-items-center gap-2 py-1">
                <span class="text-body-secondary small" style="width:1.25rem">${i + 1}</span>
                ${t.thumb ? `<img src="${escHtml(t.thumb)}" alt="" onerror="this.remove()">` : ''}
                <div class="min-w-0">
                    <div class="small fw-semibold text-truncate">${escHtml(t.title)}</div>
                    <div class="small text-body-secondary text-truncate">${escHtml(t.artist)}</div>
                </div>
            </div>`).join('');
    }

    /* Chrome: nav highlight and action bar */
    function renderNav() {
        const s = window.appState;
        document.querySelectorAll('[data-nav]').forEach(btn => {
            if (!btn.classList.contains('drawer-nav-item')) return;
            if (btn.dataset.nav === s.screen) btn.setAttribute('aria-current', 'page');
            else                              btn.removeAttribute('aria-current');
        });
    }

    function renderFooter() {
        const s       = window.appState;
        const onQuiz  = s.screen === 'screen-quiz'  && s.players.length > 0;
        const onMusic = s.screen === 'screen-music' && musicRunning();

        show($('footer-quiz'), onQuiz);
        show($('footer-music'), onMusic);
        show($('app-footer'), onQuiz || onMusic);
        document.body.classList.toggle('with-footer', onQuiz || onMusic);

        if (onQuiz) {
            const phase   = s.quiz.phase;
            const hasSeat = s.activePlayers().length > 0;
            $('btn-arm').disabled     = !hasSeat || ['armed', 'buzzed', 'over'].includes(phase);
            $('btn-correct').disabled = phase !== 'buzzed';
            $('btn-wrong').disabled   = phase !== 'buzzed';
            $('btn-reset').disabled   = ['waiting', 'armed', 'over'].includes(phase);
            $('btn-finish').disabled  = phase === 'over';
        }

        if (onMusic) {
            const phase = s.music.phase;
            $('btn-play').disabled         = phase !== 'waiting';
            $('btn-skip').disabled         = phase !== 'playing';
            $('btn-music-finish').disabled = phase === 'over';
        }
    }

    function renderScreens() {
        const s = window.appState;
        document.querySelectorAll('.screen').forEach(sec => {
            sec.classList.toggle('active', sec.id === s.screen);
        });
    }

    /* Full repaint */
    function render() {
        withPreservedFocus(() => {
            const s = window.appState;
            renderScreens();
            renderNav();
            renderTrackBar();
            renderFooter();

            switch (s.screen) {
                case 'screen-quiz':     renderQuiz();     break;
                case 'screen-music':    renderMusic();    break;
                case 'screen-register': renderRegister(); break;
                case 'screen-results':  renderResults();  break;
            }

            renderOverlays();
        });
    }

    // setConnState owns #conn-text, so it has to be re-run after a language switch.
    window.i18n.onChange(() => setConnState(window.appState.connected));

    window.ui = {
        render,
        showScreen,
        setConnState,
        toast,
        bumpScores,
        highlightPlayer,
        fillMusicSettings,
        showPlaylistOk,
        showPlaylistError,
        clearPlaylistFeedback,
        setProgress,
        setWave,
        showYtError,
        escHtml,
    };
})();
