import { useEffect, useRef, useState } from 'react';
import { toAccountId } from '../accountId';
import { readSavedAccountId, saveAccountId } from '../accountStorage';
import { fetchRecentMatches } from '../api/matches';
import { useCurrentUser } from '../auth/useCurrentUser';
import MatchesTable from './MatchesTable';

// Only checks the format (account ID or SteamID64); the API decides if the number is valid.
function isValidAccountId(value) {
    return /^\d{1,17}$/.test(value) && Number(value) > 0;
}

function MatchesSkeleton() {
    return (
        <div className="match-list" aria-hidden="true">
            {Array.from({ length: 5 }, (_, index) => <span className="skeleton match-row-skeleton" key={index} />)}
        </div>
    );
}

function SearchIcon() {
    return (
        <svg className="search-icon" viewBox="0 0 24 24" aria-hidden="true">
            <circle cx="10.5" cy="10.5" r="6" />
            <path d="M15 15l5 5" />
        </svg>
    );
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

    function retrySearch() {
        if (!searchAccountId) {
            return;
        }
        setInputError(null);
        searchCount.current += 1;
        setTypedSearch({ accountId: searchAccountId, key: `typed/${searchCount.current}` });
    }

    let state;
    if (inputError) {
        state = { status: 'error', message: inputError };
    } else if (!search) {
        state = sessionReady ? { status: 'idle' } : { status: 'loading' };
    } else {
        state = result?.key === search.key ? result : { status: 'loading' };
    }

    // Compared as account IDs, so typing your own SteamID64 still counts as "my matches".
    const viewingSomeoneElse =
        ownAccountId !== null && typedSearch !== null && toAccountId(typedSearch.accountId) !== ownAccountId;

    return (
        <main className="page container matches-page">
            <section className="matches-hero" aria-labelledby="matches-title">
                <p className="kicker">Dota 2 <span aria-hidden="true">//</span> histórico de partidas</p>
                <h1 id="matches-title">{viewingSomeoneElse ? 'Partidas' : 'Minhas partidas'}</h1>
                <p className="matches-description">
                    Os resultados recentes. Abra uma partida para ver a análise do replay: fase de rotas, mapa de
                    calor, lutas, itens e o que melhorar.
                </p>

                <form className="search-bar" onSubmit={handleSubmit}>
                    <label className="visually-hidden" htmlFor="account-id">
                        {ownAccountId ? 'Ver as partidas de outro jogador' : 'Account ID do Dota ou SteamID64'}
                    </label>
                    <div className="search-field">
                        <SearchIcon />
                        <input
                            id="account-id"
                            inputMode="numeric"
                            autoComplete="off"
                            value={inputValue}
                            onChange={event => {
                                setAccountIdInput(event.target.value);
                                setInputError(null);
                            }}
                            placeholder="Account ID do Dota ou SteamID64 (ex.: 87278757)"
                            aria-describedby={inputError ? 'account-id-error' : 'account-id-hint'}
                            aria-invalid={Boolean(inputError)}
                        />
                    </div>
                    <button className="button-primary" type="submit" disabled={state.status === 'loading'}>Buscar</button>
                    <p id="account-id-hint" className="search-hint">
                        {ownAccountId
                            ? 'Mostrando as suas partidas. Para ver as de outro jogador, informe o account ID ou o SteamID64 dele.'
                            : 'Informe o account ID do Dota ou o SteamID64, ou entre com a Steam para ver as suas.'}
                        {viewingSomeoneElse && (
                            <>
                                {' '}
                                <button type="button" className="link-button" onClick={showOwnMatches}>Ver minhas partidas</button>
                            </>
                        )}
                    </p>
                </form>
            </section>

            {inputError && <p id="account-id-error" className="alert" role="alert">{inputError}</p>}
            {state.status === 'loading' && (
                <div role="status" aria-live="polite" aria-busy="true">
                    <p className="loading-line"><span className="spinner" aria-hidden="true" /> Carregando partidas...</p>
                    <MatchesSkeleton />
                </div>
            )}
            {state.status === 'error' && !inputError && (
                <div className="alert" role="alert">
                    <p>{state.message}</p>
                    <button className="button-secondary" type="button" onClick={retrySearch}>Tentar novamente</button>
                </div>
            )}
            {state.status === 'idle' && (
                <p className="empty-state">Informe um ID acima para buscar as partidas recentes.</p>
            )}
            {state.status === 'success' && state.matches.length === 0 && (
                <div className="empty-state">
                    <h2>Nenhuma partida encontrada</h2>
                    <p>O perfil pode estar privado ou ainda não ter partidas disponíveis.</p>
                </div>
            )}
            {state.status === 'success' && state.matches.length > 0 && (
                <MatchesTable matches={state.matches} accountId={search.accountId} />
            )}
        </main>
    );
}

export default MatchesPage;
