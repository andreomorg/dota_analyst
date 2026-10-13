import { useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { readSavedAccountId } from '../accountStorage';
import { useCurrentUser } from '../auth/useCurrentUser';
import SectionHead from '../ui/SectionHead';
import DamageSection from './DamageSection';
import DeathMap from './DeathMap';
import FightsSection from './FightsSection';
import HeatmapSection from './HeatmapSection';
import ItemBuildSection from './ItemBuildSection';
import LaningSection, { ReplayStatusBanner } from './LaningSection';
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
            <h1 className="visually-hidden">Partida {matchId}</h1>
            <p className="loading-line"><span className="spinner" aria-hidden="true" /> Carregando os dados da partida {matchId}...</p>
            <div aria-hidden="true" className="skeleton-stack">
                <span className="skeleton skeleton-banner" />
                <div className="skeleton-tiles">
                    <span className="skeleton" />
                    <span className="skeleton" />
                    <span className="skeleton" />
                    <span className="skeleton" />
                </div>
                <span className="skeleton skeleton-block" />
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
            <main className="page container">
                <BackLink />
                <div className="empty-state">
                    <h1>Partida {matchId}</h1>
                    <p>
                        Para ver a análise, entre com a Steam ou informe o seu account ID do Dota na{' '}
                        <Link to="/">lista de partidas</Link>.
                    </p>
                </div>
            </main>
        );
    }

    if (!isValidMatchId(matchId)) {
        return (
            <main className="page container">
                <BackLink />
                <div className="empty-state">
                    <h1>Partida inválida</h1>
                    <p className="alert" role="alert">O número da partida deve conter só dígitos.</p>
                </div>
            </main>
        );
    }

    return (
        <main className="analysis-page">
            {state.status === 'loading' && (
                <div className="page container">
                    <BackLink />
                    <MatchAnalysisSkeleton matchId={matchId} />
                </div>
            )}

            {state.status === 'error' && (
                <div className="page container">
                    <BackLink />
                    <div className="alert alert-block" role="alert">
                        <h1>Partida {matchId}</h1>
                        <p>{state.message}</p>
                        <button className="button-secondary" type="button" onClick={state.retry}>
                            Tentar novamente
                        </button>
                    </div>
                </div>
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

// The section links under the match header; the one of the section on screen is highlighted (scroll spy).
function SectionNav({ sections }) {
    const [active, setActive] = useState(sections[0]?.id);
    const barRef = useRef(null);
    const ids = sections.map(section => section.id).join(',');

    useEffect(() => {
        const elements = ids.split(',').map(id => document.getElementById(id)).filter(Boolean);
        if (elements.length === 0) {
            return undefined;
        }
        // The last section whose heading has passed under the sticky bars (about 160 px from the top); at the very
        // bottom of the page, the last section.
        let frame = 0;
        function update() {
            frame = 0;
            const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4;
            const passed = elements.filter(element => element.getBoundingClientRect().top <= 160);
            const current = atBottom ? elements.at(-1) : passed.at(-1) ?? elements[0];
            setActive(current.id);
        }
        function onScroll() {
            if (!frame) {
                frame = requestAnimationFrame(update);
            }
        }
        update();
        window.addEventListener('scroll', onScroll, { passive: true });
        return () => {
            window.removeEventListener('scroll', onScroll);
            cancelAnimationFrame(frame);
        };
    }, [ids]);

    // On a narrow screen the links scroll sideways: keep the active one in view.
    useEffect(() => {
        const bar = barRef.current;
        const link = bar?.querySelector('[aria-current="true"]');
        if (bar && link && (link.offsetLeft < bar.scrollLeft || link.offsetLeft + link.offsetWidth > bar.scrollLeft + bar.clientWidth)) {
            bar.scrollTo({ left: link.offsetLeft - 16 });
        }
    }, [active]);

    return (
        <nav className="analysis-nav" aria-label="Seções da análise">
            <div className="container analysis-nav-inner" ref={barRef}>
                {sections.map(section => (
                    <a
                        key={section.id}
                        href={`#${section.id}`}
                        aria-current={active === section.id ? 'true' : undefined}
                    >
                        {section.label}
                    </a>
                ))}
            </div>
        </nav>
    );
}

function AnalysisContent({ analysis, accountId, replayPollingStopped }) {
    const { performance } = analysis;
    const ready = analysis.replayStatus === 'ready';
    // null stays null for both teams: an unknown winner highlights nobody.
    const radiantWon = analysis.radiantWon ?? null;
    const direWon = radiantWon === null ? null : !radiantWon;
    const hasMaps = ready && ((analysis.heatmaps?.heroes?.length ?? 0) > 0 || Boolean(analysis.deaths) || (analysis.wards?.length ?? 0) > 0);
    const hasFights = ready && (Boolean(analysis.fights) || Boolean(analysis.damage));
    const sections = [
        { id: 'performance-title', label: 'Visão geral', visible: Boolean(performance) },
        { id: 'smart-stats-title', label: 'Dicas', visible: Boolean(performance || analysis.smartStats) },
        { id: 'laning-title', label: 'Fase de rotas', visible: ready && Boolean(analysis.laning) },
        { id: 'item-build-title', label: 'Itens', visible: ready && Boolean(analysis.itemBuild) },
        { id: 'timeline-title', label: 'Economia', visible: ready && (analysis.timeline?.seconds?.length ?? 0) > 1 },
        { id: 'objectives-title', label: 'Objetivos', visible: ready && Boolean(analysis.objectives) },
        { id: 'fights-damage-title', label: 'Lutas e dano', visible: hasFights },
        { id: 'maps-title', label: 'Mapas', visible: hasMaps },
        { id: 'teams-title', label: 'Os 10 jogadores', visible: true },
    ].filter(section => section.visible);

    return (
        <>
            <MatchHeader analysis={analysis} />
            <SectionNav sections={sections} />

            <div className="container analysis-sections">
                {!analysis.isParsed && (
                    <p className="notice">
                        O OpenDota ainda não processou o replay desta partida: os percentis de desempenho podem faltar.
                    </p>
                )}

                <ReplayStatusBanner replayStatus={analysis.replayStatus} pollingStopped={replayPollingStopped} />

                {performance
                    ? <PerformanceSummary analysis={analysis} />
                    : (
                        <p className="notice">
                            O jogador {accountId} não aparece nesta partida, por isso não há análise de desempenho.
                            Abra a partida pela lista de partidas desse jogador.
                        </p>
                    )}

                <SmartStatsSection performance={performance} laning={analysis.laning} smartStats={ready ? analysis.smartStats : null} />

                {ready && <LaningSection laning={analysis.laning} />}

                {ready && <ItemBuildSection itemBuild={analysis.itemBuild} />}

                {ready && <TimelineSection timeline={analysis.timeline} objectives={analysis.objectives} />}

                {ready && <ObjectivesSection objectives={analysis.objectives} />}

                {hasFights && (
                    <section className="section" aria-labelledby="fights-damage-title">
                        <SectionHead id="fights-damage-title" title="Lutas e dano" />
                        <div className="split split-5-7">
                            <FightsSection fights={analysis.fights} isRadiant={performance ? performance.isRadiant : null} />
                            <DamageSection damage={analysis.damage} heroName={performance?.heroName} />
                        </div>
                    </section>
                )}

                {hasMaps && (
                    <section className="section" aria-labelledby="maps-title">
                        <SectionHead id="maps-title" title="Mapas da partida">
                            <span>No minimapa do jogo, com as posições do replay</span>
                        </SectionHead>
                        <div className="stack">
                            <HeatmapSection
                                heatmaps={analysis.heatmaps}
                                deaths={analysis.deaths}
                                smartStats={analysis.smartStats}
                                laning={analysis.laning}
                            />
                            <div className="split">
                                <DeathMap deaths={analysis.deaths} />
                                <WardMap wards={analysis.wards} />
                            </div>
                        </div>
                    </section>
                )}

                <section className="section" aria-labelledby="teams-title">
                    <SectionHead id="teams-title" title="Os 10 jogadores" />
                    <div className="stack">
                        <TeamTable name="Radiant" side="radiant" players={analysis.radiant} won={radiantWon} score={analysis.radiantScore} />
                        <TeamTable name="Dire" side="dire" players={analysis.dire} won={direWon} score={analysis.direScore} />
                    </div>
                </section>
            </div>
        </>
    );
}

export default MatchAnalysisPage;
