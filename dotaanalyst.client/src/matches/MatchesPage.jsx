import { useEffect, useRef, useState } from 'react';
import { readSavedAccountId, saveAccountId } from '../accountStorage';
import { fetchRecentMatches } from '../api/matches';
import { useCurrentUser } from '../auth/useCurrentUser';
import MatchesTable from './MatchesTable';

// Only checks the format (account ID or SteamID64); the API decides if the number is valid.
function isValidAccountId(value) {
    return /^\d{1,17}$/.test(value) && Number(value) > 0;
}

function MatchesPage() {
    const { status: sessionStatus, user } = useCurrentUser();
    const [savedAccountId] = useState(readSavedAccountId);
    // null = the user hasn't typed anything yet, so the field shows the default account ID.
    const [accountIdInput, setAccountIdInput] = useState(null);
    // A search typed by the user. The counter gives every search a new key, so searching
    // the same ID again is a new request (it never resets, so an old result can't match a new search).
    const [typedSearch, setTypedSearch] = useState(null);
    const searchCount = useRef(0);
    const [inputError, setInputError] = useState(null);
    // The result remembers which search produced it (same idea as in MatchAnalysisPage).
    const [result, setResult] = useState(null);

    const sessionReady = sessionStatus !== 'loading';
    const ownAccountId = sessionStatus === 'authenticated' ? String(user.accountId) : null;
    // Logged in: your own matches. Anonymous: the last ID searched in this browser (as before the login existed).
    const defaultAccountId = ownAccountId ?? savedAccountId;

    // Waits for the session so it doesn't load the saved ID and then switch to the logged-in user's.
    const search = typedSearch
        ?? (sessionReady && defaultAccountId ? { accountId: defaultAccountId, key: `default/${defaultAccountId}` } : null);
    const inputValue = accountIdInput ?? (sessionReady ? defaultAccountId : '');
    const searchKey = search?.key;
    const searchAccountId = search?.accountId;

    useEffect(() => {
        if (!searchKey) {
            return;
        }

        const controller = new AbortController();
        fetchRecentMatches(searchAccountId, controller.signal)
            .then(matches => {
                if (!controller.signal.aborted) {
                    setResult({ key: searchKey, status: 'success', matches });
                }
            })
            .catch(error => {
                if (!controller.signal.aborted) {
                    setResult({ key: searchKey, status: 'error', message: error.message });
                }
            });

        // Cancels the request if a new search starts before this one finishes.
        return () => controller.abort();
    }, [searchKey, searchAccountId]);

    function handleSubmit(event) {
        event.preventDefault();
        const accountId = inputValue.trim();

        if (!isValidAccountId(accountId)) {
            setInputError('Digite o account ID do Dota ou o SteamID64 (só números).');
            return;
        }

        saveAccountId(accountId);
        setInputError(null);
        searchCount.current += 1;
        setTypedSearch({ accountId, key: `typed/${searchCount.current}` });
    }

    function showOwnMatches() {
        setInputError(null);
        setAccountIdInput(null);
        setTypedSearch(null);
    }

    let state;
    if (inputError) {
        state = { status: 'error', message: inputError };
    } else if (!search) {
        state = sessionReady ? { status: 'idle' } : { status: 'loading' };
    } else {
        state = result?.key === search.key ? result : { status: 'loading' };
    }

    const viewingSomeoneElse = ownAccountId !== null && typedSearch !== null && typedSearch.accountId !== ownAccountId;

    return (
        <main className="matches-page">
            <h1>{viewingSomeoneElse ? 'Partidas' : 'Minhas partidas'}</h1>

            <form className="search-form" onSubmit={handleSubmit}>
                <label htmlFor="account-id">
                    {ownAccountId ? 'Ver as partidas de outro jogador' : 'Account ID do Dota ou SteamID64'}
                </label>
                <input
                    id="account-id"
                    inputMode="numeric"
                    value={inputValue}
                    onChange={event => setAccountIdInput(event.target.value)}
                    placeholder="ex.: 87278757"
                />
                <button type="submit" disabled={state.status === 'loading'}>Buscar</button>
                {viewingSomeoneElse && (
                    <button type="button" onClick={showOwnMatches}>Ver minhas partidas</button>
                )}
            </form>

            {state.status === 'loading' && <p>Carregando partidas...</p>}
            {state.status === 'error' && <p className="error-message" role="alert">{state.message}</p>}
            {state.status === 'success' && state.matches.length === 0 && (
                <p>Nenhuma partida encontrada — o perfil pode estar privado.</p>
            )}
            {state.status === 'success' && state.matches.length > 0 && (
                <MatchesTable matches={state.matches} accountId={search.accountId} />
            )}
        </main>
    );
}

export default MatchesPage;
