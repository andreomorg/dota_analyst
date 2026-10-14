export function pollingState(analysis, elapsedMs) {
    const replayActive = analysis.replayStatus === 'processing';
    const externalActive = (!analysis.isParsed || analysis.openDotaProcessing?.enrichmentOnly === true)
        && ['queued', 'processing', 'refreshing', 'deferred'].includes(analysis.openDotaProcessing?.status);
    const replayWaiting = replayActive && elapsedMs + 10_000 <= 5 * 60_000;
    const externalInterval = Math.max(10, Math.min(60, analysis.openDotaProcessing?.retryAfterSeconds ?? 15)) * 1000;
    const externalWaiting = externalActive && elapsedMs + externalInterval <= 10 * 60_000;
    return {
        keepPolling: replayWaiting || externalWaiting,
        intervalMs: replayWaiting ? Math.min(10_000, externalWaiting ? externalInterval : 10_000) : externalInterval,
        replayPollingStopped: replayActive && !replayWaiting,
        openDotaPollingStopped: externalActive && !externalWaiting,
    };
}
