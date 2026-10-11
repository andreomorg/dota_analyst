import { useEffect, useState } from 'react';
import { fetchRecentMatches } from '../api/matches';
import MatchesTable from './MatchesTable';

// Temporary: the account ID is typed by the user until Steam login exists.
const STORAGE_KEY = 'dotaanalyst.accountId';

function readSavedAccountId() {
    try {
        return localStorage.getItem(STORAGE_KEY) ?? '';
    } catch {
        return '';
    }
}

function saveAccountId(accountId) {
    try {
        localStorage.setItem(STORAGE_KEY, accountId);
    } catch {
        // Storage unavailable (e.g. private window): the user just types the ID again next time.
    }
}

// Only checks the format (account ID or SteamID64); the API decides if the number is valid.
function isValidAccountId(value) {
    return /^\d{1,17}$/.test(value) && Number(value) > 0;
}

function MatchesPage() {
    const [savedAccountId] = useState(readSavedAccountId);
    const [accountIdInput, setAccountIdInput] = useState(savedAccountId);
    // A new object on every search, so searching the same ID again still triggers the effect.
    const [search, setSearch] = useState(() => (savedAccountId ? { accountId: savedAccountId } : null));
    const [state, setState] = useState(() => (savedAccountId ? { status: 'loading' } : { status: 'idle' }));

    useEffect(() => {
        if (!search) {
            return;
        }

        const controller = new AbortController();
        fetchRecentMatches(search.accountId, controller.signal)
            .then(matches => {
                if (!controller.signal.aborted) {
                    setState({ status: 'success', matches });
                }
            })
            .catch(error => {
                if (!controller.signal.aborted) {
                    setState({ status: 'error', message: error.message });
                }
            });

        // Cancels the request if a new search starts before this one finishes.
        return () => controller.abort();
    }, [search]);

    function handleSubmit(event) {
        event.preventDefault();
        const accountId = accountIdInput.trim();

        if (!isValidAccountId(accountId)) {
            setState({ status: 'error', message: 'Digite o account ID do Dota ou o SteamID64 (só números).' });
            return;
        }

        saveAccountId(accountId);
        setState({ status: 'loading' });
        setSearch({ accountId });
    }

    return (
        <main className="matches-page">
            <h1>Minhas partidas</h1>

            <form className="search-form" onSubmit={handleSubmit}>
                <label htmlFor="account-id">Account ID do Dota ou SteamID64</label>
                <input
                    id="account-id"
                    inputMode="numeric"
                    value={accountIdInput}
                    onChange={event => setAccountIdInput(event.target.value)}
                    placeholder="ex.: 87278757"
                />
                <button type="submit" disabled={state.status === 'loading'}>Buscar</button>
            </form>

            {state.status === 'loading' && <p>Carregando partidas...</p>}
            {state.status === 'error' && <p className="error-message" role="alert">{state.message}</p>}
            {state.status === 'success' && state.matches.length === 0 && (
                <p>Nenhuma partida encontrada — o perfil pode estar privado.</p>
            )}
            {state.status === 'success' && state.matches.length > 0 && <MatchesTable matches={state.matches} />}
        </main>
    );
}

export default MatchesPage;
