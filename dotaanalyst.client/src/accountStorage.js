// Temporary: the account ID is typed by the user until Steam login exists.
const STORAGE_KEY = 'dotaanalyst.accountId';

export function readSavedAccountId() {
    try {
        return localStorage.getItem(STORAGE_KEY) ?? '';
    } catch {
        return '';
    }
}

export function saveAccountId(accountId) {
    try {
        localStorage.setItem(STORAGE_KEY, accountId);
    } catch {
        // Storage unavailable (e.g. private window): the user just types the ID again next time.
    }
}
