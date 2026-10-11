import { useEffect, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router';
import { readSavedAccountId } from '../accountStorage';
import { fetchMatchAnalysis } from '../api/matches';
import { useCurrentUser } from '../auth/useCurrentUser';
import MatchHeader from './MatchHeader';
import PerformanceSummary from './PerformanceSummary';
import TeamTable from './TeamTable';

const MAX_MATCH_ID = 9223372036854775807n; // long.MaxValue on the API

function isValidMatchId(value) {
    return /^\d{1,19}$/.test(value) && BigInt(value) > 0n && BigInt(value) <= MAX_MATCH_ID;
}

function BackLink() {
    return (
        <p className="back-link-row">
            <Link to="/">← Voltar para as partidas</Link>
        </p>
    );
}

function MatchAnalysisPage() {
    const { matchId } = useParams();
    const location = useLocation();
    const { status: sessionStatus, user } = useCurrentUser();
    const [savedAccountId] = useState(readSavedAccountId);

    // Whose point of view: the player whose list the user clicked (sent by MatchesTable),
    // else the logged-in user, else the ID saved in this browser. null = session still loading,
    // so no request goes out with the wrong ID.
    const linkedAccountId = location.state?.accountId;
    let accountId;
    if (typeof linkedAccountId === 'string' && linkedAccountId !== '') {
        accountId = linkedAccountId;
    } else if (sessionStatus === 'loading') {
        accountId = null;
    } else {
        accountId = sessionStatus === 'authenticated' ? String(user.accountId) : savedAccountId;
    }

    const canFetch = accountId !== null && accountId !== '' && isValidMatchId(matchId);
    const requestKey = `${accountId}/${matchId}`;

    // The result remembers which request produced it. If the URL changes (another match),
    // the key no longer matches and the page shows "loading" without a synchronous setState.
    const [result, setResult] = useState(null);

    useEffect(() => {
        if (!canFetch) {
            return;
        }

        const controller = new AbortController();
        fetchMatchAnalysis(accountId, matchId, controller.signal)
            .then(analysis => {
                if (!controller.signal.aborted) {
                    setResult({ key: requestKey, status: 'success', analysis });
                }
            })
            .catch(error => {
                if (!controller.signal.aborted) {
                    setResult({ key: requestKey, status: 'error', message: error.message });
                }
            });

        // Cancels the request if the user leaves the page (or opens another match) before it finishes.
        return () => controller.abort();
    }, [accountId, matchId, canFetch, requestKey]);

    if (accountId === '') {
        return (
            <main className="page analysis-page">
                <BackLink />
                <h1>Partida {matchId}</h1>
                <p>
                    Para ver a análise, entre com a Steam ou informe o seu account ID do Dota na{' '}
                    <Link to="/">lista de partidas</Link>.
                </p>
            </main>
        );
    }

    if (!isValidMatchId(matchId)) {
        return (
            <main className="page analysis-page">
                <BackLink />
                <h1>Partida inválida</h1>
                <p className="error-message" role="alert">O número da partida deve conter só dígitos.</p>
            </main>
        );
    }

    const state = result?.key === requestKey ? result : { status: 'loading' };

    return (
        <main className="page analysis-page">
            <BackLink />

            {state.status === 'loading' && (
                <>
                    <h1>Partida {matchId}</h1>
                    <p>Carregando análise...</p>
                </>
            )}

            {state.status === 'error' && (
                <>
                    <h1>Partida {matchId}</h1>
                    <p className="error-message" role="alert">{state.message}</p>
                </>
            )}

            {state.status === 'success' && <AnalysisContent analysis={state.analysis} />}
        </main>
    );
}

function AnalysisContent({ analysis }) {
    const { performance } = analysis;
    // null stays null for both teams: an unknown winner highlights nobody.
    const radiantWon = analysis.radiantWon ?? null;
    const direWon = radiantWon === null ? null : !radiantWon;

    return (
        <>
            <MatchHeader analysis={analysis} />

            {!analysis.isParsed && (
                <p className="notice">
                    Dados detalhados indisponíveis: o replay desta partida ainda não foi processado pelo OpenDota.
                </p>
            )}

            {performance
                ? <PerformanceSummary performance={performance} />
                : (
                    <p className="notice">
                        Você não aparece nesta partida, por isso não há análise do seu desempenho.
                        Confira se o account ID salvo na lista de partidas está correto.
                    </p>
                )}

            <div className="teams">
                <TeamTable name="Radiant" players={analysis.radiant} won={radiantWon} />
                <TeamTable name="Dire" players={analysis.dire} won={direWon} />
            </div>
        </>
    );
}

export default MatchAnalysisPage;
