import test from 'node:test';
import assert from 'node:assert/strict';
import { pollingState } from '../src/analysis/analysisPolling.js';

test('OpenDota still polls when our replay is ready', () => {
    const result = pollingState({ isParsed: false, replayStatus: 'ready', openDotaProcessing: { status: 'processing' } }, 0);
    assert.equal(result.keepPolling, true);
    assert.equal(result.intervalMs, 15000);
    assert.equal(result.replayPollingStopped, false);
});
test('Go and OpenDota have independent limits', () => {
    const data = { isParsed: false, replayStatus: 'processing', openDotaProcessing: { status: 'queued' } };
    assert.equal(pollingState(data, 5 * 60000).keepPolling, true);
    assert.equal(pollingState(data, 5 * 60000).replayPollingStopped, true);
    assert.equal(pollingState(data, 10 * 60000).keepPolling, false);
    assert.equal(pollingState(data, 10 * 60000).openDotaPollingStopped, true);
});
test('Parsed data stops external polling and old API remains compatible', () => {
    assert.equal(pollingState({ isParsed: true, replayStatus: 'ready', openDotaProcessing: { status: 'processing' } }, 0).keepPolling, false);
    assert.equal(pollingState({ isParsed: false, replayStatus: 'ready' }, 0).keepPolling, false);
});
test('Already parsed legacy match polls enrichment without replay parsing', () => {
    assert.equal(pollingState({ isParsed: true, replayStatus: 'ready', openDotaProcessing: { status: 'refreshing', enrichmentOnly: true } }, 0).keepPolling, true);
});
