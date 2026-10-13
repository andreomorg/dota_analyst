import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { readSavedAccountId } from '../accountStorage';
import { useCurrentUser } from '../auth/useCurrentUser';
import DamageSection from './DamageSection';
import DeathMap from './DeathMap';
import FightsSection from './FightsSection';
import ItemBuildSection from './ItemBuildSection';
import LaningSection from './LaningSection';
import MatchHeader from './MatchHeader';
import ObjectivesSection from './ObjectivesSection';
import PerformanceSummary from './PerformanceSummary';
import SmartStatsSection from './SmartStatsSection';
import TeamTable from './TeamTable';
import TimelineSection from './TimelineSection';
import WardMap from './WardMap';
import { useMatchAnalysis } from './useMatchAnalysis';

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

function MatchAnalysisSkeleton({ matchId }) {
    return (
        <section className="analysis-loading" role="status" aria-live="polite" aria-busy="true">
            <h1>Partida {matchId}</h1>
            <p>Carregando os dados da partida...</p>
            <div className="skeleton-card skeleton-match" aria-hidden="true">
                <span className="skeleton-line skeleton-title" />
                <span className="skeleton-line skeleton-summary" />
                <span className="skeleton-line skeleton-facts" />
            </div>
            <div className="skeleton-grid" aria-hidden="true">
                <span className="skeleton-card" />
                <span className="skeleton-card" />
            </div>
        </section>
    );
}

function MatchAnalysisPage() {
    const { matchId } = useParams();
    const [searchParams] = useSearchParams();
    const { status: sessionStatus, user } = useCurrentUser();
    const [savedAccountId] = useState(readSavedAccountId);

    // Whose point of view: the player whose list the user clicked (?player=, set by MatchesTable),
    // else the logged-in user, else the ID saved in this browser. null = session still loading,
    // so no request goes out with the wrong ID.
    const linkedAccountId = searchParams.get('player')?.trim();
    let accountId;
    if (linkedAccountId) {
        accountId = linkedAccountId;
    } else if (sessionStatus === 'loading') {
        accountId = null;
    } else {
        accountId = sessionStatus === 'authenticated' ? String(user.accountId) : savedAccountId;
    }

    const canFetch = accountId !== null && accountId !== '' && isValidMatchId(matchId);
    const state = useMatchAnalysis(accountId, matchId, canFetch);

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

    return (
        <main className="page analysis-page">
            <BackLink />

            {state.status === 'loading' && (
                <MatchAnalysisSkeleton matchId={matchId} />
            )}

            {state.status === 'error' && (
                <>
                    <h1>Partida {matchId}</h1>
                    <p className="error-message" role="alert">{state.message}</p>
                    <button className="retry-button" type="button" onClick={state.retry}>
                        Tentar novamente
                    </button>
                </>
            )}

            {state.status === 'success' && (
                <AnalysisContent
                    analysis={state.analysis}
                    accountId={accountId}
                    replayPollingStopped={state.replayPollingStopped}
                />
            )}
        </main>
    );
}

function AnalysisContent({ analysis, accountId, replayPollingStopped }) {
    const { performance } = analysis;
    // null stays null for both teams: an unknown winner highlights nobody.
    const radiantWon = analysis.radiantWon ?? null;
    const direWon = radiantWon === null ? null : !radiantWon;
    const sections = [
        { id: 'performance-title', label: 'Desempenho', visible: Boolean(performance) },
        {
            id: 'laning-title',
            label: 'Fase de rotas',
            visible: analysis.replayStatus === 'ready' && Boolean(analysis.laning),
        },
        { id: 'smart-stats-title', label: 'Como melhorar', visible: Boolean(analysis.smartStats) },
        { id: 'item-build-title', label: 'Build de itens', visible: Boolean(analysis.itemBuild) },
        { id: 'timeline-title', label: 'Gráficos', visible: (analysis.timeline?.seconds?.length ?? 0) > 1 },
        { id: 'objectives-title', label: 'Objetivos', visible: Boolean(analysis.objectives) },
        { id: 'fights-title', label: 'Lutas', visible: Boolean(analysis.fights) },
        { id: 'damage-title', label: 'Dano', visible: Boolean(analysis.damage) },
        { id: 'death-map-title', label: 'Mortes', visible: Boolean(analysis.deaths) },
        { id: 'ward-map-title', label: 'Wards', visible: (analysis.wards?.length ?? 0) > 0 },
        { id: 'teams', label: 'Times', visible: true },
    ].filter(section => section.visible);

    return (
        <>
            <MatchHeader analysis={analysis} />
            <nav className="analysis-nav" aria-label="Seções da análise">
                <a href="#match-overview">Resumo</a>
                {sections.map(section => (
                    <a key={section.id} href={`#${section.id}`}>{section.label}</a>
                ))}
            </nav>

            <div className="analysis-sections">
                {!analysis.isParsed && (
                    <p className="notice">
                        Dados detalhados indisponíveis: o replay desta partida ainda não foi processado pelo OpenDota.
                    </p>
                )}

                {performance
                    ? <PerformanceSummary performance={performance} />
                    : (
                        <p className="notice">
                            O jogador {accountId} não aparece nesta partida, por isso não há análise de desempenho.
                            Abra a partida pela lista de partidas desse jogador.
                        </p>
                    )}

                <LaningSection
                    replayStatus={analysis.replayStatus}
                    laning={analysis.laning}
                    pollingStopped={replayPollingStopped}
                />

                {analysis.replayStatus === 'ready' && <SmartStatsSection smartStats={analysis.smartStats} />}

                {analysis.replayStatus === 'ready' && <ItemBuildSection itemBuild={analysis.itemBuild} />}

                {analysis.replayStatus === 'ready' && (
                    <TimelineSection timeline={analysis.timeline} objectives={analysis.objectives} />
                )}

                {analysis.replayStatus === 'ready' && <ObjectivesSection objectives={analysis.objectives} />}

                {analysis.replayStatus === 'ready' && (
                    <FightsSection fights={analysis.fights} isRadiant={performance ? performance.isRadiant : null} />
                )}

                {analysis.replayStatus === 'ready' && (
                    <DamageSection damage={analysis.damage} heroName={performance?.heroName} />
                )}

                {analysis.replayStatus === 'ready' && <DeathMap deaths={analysis.deaths} />}

                {analysis.replayStatus === 'ready' && <WardMap wards={analysis.wards} />}

                <div id="teams" className="teams">
                    <TeamTable name="Radiant" players={analysis.radiant} won={radiantWon} />
                    <TeamTable name="Dire" players={analysis.dire} won={direWon} />
                </div>
            </div>
        </>
    );
}

export default MatchAnalysisPage;
