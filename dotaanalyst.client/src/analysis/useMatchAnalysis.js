import { useEffect, useState } from 'react';
import { fetchMatchAnalysis } from '../api/matches';

// While the API says the replay is being analyzed, the analysis is fetched again every 10 s,
// for at most ~5 min (the replay parser usually answers well before that).
const REPLAY_POLL_INTERVAL_MS = 10_000;
const REPLAY_POLL_LIMIT_MS = 5 * 60_000;

// Loads the analysis of a match and, while replayStatus is "processing", keeps it up to date.
// Returns { status: 'loading' } | { status: 'error', message } |
// { status: 'success', analysis, replayPollingStopped } (true = gave up waiting for the replay).
export function useMatchAnalysis(accountId, matchId, enabled) {
    const requestKey = `${accountId}/${matchId}`;

    // The result remembers which request produced it. If the URL changes (another match),
    // the key no longer matches and the page shows "loading" without a synchronous setState.
    // A refresh keeps the same key, so the page never goes back to "loading": only the data changes.
    const [result, setResult] = useState(null);

    useEffect(() => {
        if (!enabled) {
            return;
        }

        // One controller and one timer for the whole lifetime of this match on screen:
        // the cleanup cancels the request in flight and the next scheduled refresh.
        const controller = new AbortController();
        const startedAt = Date.now();
        let refreshTimer = null;

        // false = the next refresh would go past the time limit.
        function canRefreshAgain() {
            return Date.now() - startedAt + REPLAY_POLL_INTERVAL_MS <= REPLAY_POLL_LIMIT_MS;
        }

        function scheduleRefresh() {
            refreshTimer = setTimeout(() => load(true), REPLAY_POLL_INTERVAL_MS);
        }

        function load(isRefresh) {
            fetchMatchAnalysis(accountId, matchId, controller.signal)
                .then(analysis => {
                    if (controller.signal.aborted) {
                        return;
                    }
                    const processing = analysis.replayStatus === 'processing';
                    const keepPolling = processing && canRefreshAgain();
                    setResult({
                        key: requestKey,
                        status: 'success',
                        analysis,
                        replayPollingStopped: processing && !keepPolling,
                    });
                    if (keepPolling) {
                        scheduleRefresh();
                    }
                })
                .catch(error => {
                    if (controller.signal.aborted) {
                        return;
                    }
                    if (!isRefresh) {
                        setResult({ key: requestKey, status: 'error', message: error.message });
                        return;
                    }
                    // A failed refresh keeps the analysis already on screen and tries again next round.
                    if (canRefreshAgain()) {
                        scheduleRefresh();
                    } else {
                        setResult(previous => (previous?.key === requestKey
                            ? { ...previous, replayPollingStopped: true }
                            : previous));
                    }
                });
        }

        load(false);

        // Runs when the user leaves the page or opens another match: nothing updates the state afterwards.
        return () => {
            controller.abort();
            clearTimeout(refreshTimer);
        };
    }, [accountId, matchId, enabled, requestKey]);

    return result?.key === requestKey ? result : { status: 'loading' };
}
