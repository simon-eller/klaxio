(function () {
    'use strict';

    const send = (cmd, data) => window.wsClient.send(cmd, data);

    function arm()          { send('quiz_arm'); }
    function correct()      { send('quiz_correct'); }
    function wrong()        { send('quiz_wrong'); }
    function reset()        { send('quiz_reset'); }
    function restart()      { send('quiz_restart'); }
    function finish() {
        if (confirm(window.i18n.t('confirmFinish'))) send('quiz_finish');
    }
    function resetScores() {
        if (confirm(window.i18n.t('confirmReset'))) send('quiz_reset_scores');
    }

    /** Apply a quiz_state broadcast. */
    function handleState(msg) {
        const s    = window.appState;
        const prev = s.quiz.phase;

        s.players        = msg.players;
        s.quiz.phase     = msg.phase;
        s.quiz.winnerId  = msg.winnerId;

        const onQuizFlow = s.screen === 'screen-quiz' || s.screen === 'screen-results';

        if (msg.phase === 'over' && onQuizFlow) {
            s.resultsMode = 'quiz';
            window.ui.showScreen('screen-results');
        } else if (msg.phase !== 'over' && s.screen === 'screen-results' && s.resultsMode === 'quiz') {
            window.ui.showScreen('screen-quiz');
        } else {
            window.ui.render();
        }

        window.ui.bumpScores(msg.bump, 'quiz');

        if (msg.phase === 'buzzed' && prev !== 'buzzed' && msg.winnerName)
            window.ui.toast(window.i18n.t('toastBuzzed', msg.winnerName));

        if (msg.phase === 'wrong' && prev !== 'wrong')
            window.ui.toast(window.i18n.t('toastWrongOthers'));
    }

    /** Host shortcuts, active only while the Klaxio board is on screen. */
    function handleKey(e) {
        if (window.appState.screen !== 'screen-quiz') return;
        const phase = window.appState.quiz.phase;

        if (e.code === 'Space' || e.code === 'Enter') {
            e.preventDefault();
            if (phase === 'waiting' || phase === 'correct' || phase === 'wrong') arm();
            else if (phase === 'buzzed') reset();
            return;
        }

        const k = e.key.toLowerCase();
        if (phase === 'buzzed' && (k === '+' || k === '1' || k === 'c')) correct();
        else if (phase === 'buzzed' && (k === '-' || k === '0' || k === 'w')) wrong();
        else if (k === 'r') reset();
    }

    window.quiz = { arm, correct, wrong, reset, restart, finish, resetScores, handleState, handleKey };
})();
