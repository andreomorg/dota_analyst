import { useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { readSavedAccountId } from '../accountStorage';
import { useCurrentUser } from '../auth/useCurrentUser';
import SectionHead from '../ui/SectionHead';
import AbilityBuildSection from './AbilityBuildSection';
import DamageSection from './DamageSection';
import DeathMap from './DeathMap';
import FightsSection from './FightsSection';
import HeatmapSection from './HeatmapSection';
import ItemBuildSection from './ItemBuildSection';
import LaningSection, { ReplayStatusBanner } from './LaningSection';
import MatchHeader from './MatchHeader';
import ObjectivesSection from './ObjectivesSection';
import PerformanceSummary from './PerformanceSummary';
import SmartStatsSection, { HighlightCards } from './SmartStatsSection';
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

// The tabs of the analysis, in priority order, each answering one question (each its own page: ?tab=key in the URL,
// so a tab can be linked or opened in a new tab); a tab without content for this match (replay not read yet, no
// point-of-view player) is left out.
// - overview: how did the match go?        - tips: what to change?
// - laning: how did the first 10 min go?    - positioning: where was I?
// - fights: how did the fights go?          - build: what did I buy and level?
// - players: and the other nine?
function tabsOf(analysis) {
    const ready = analysis.replayStatus === 'ready';
    return [
        { key: 'overview', label: 'Resumo', visible: true },
        { key: 'tips', label: 'Como melhorar', visible: Boolean(analysis.performance) || (ready && Boolean(analysis.smartStats)) },
        { key: 'laning', label: 'Fase de rotas', visible: ready && Boolean(analysis.laning) },
        { key: 'positioning', label: 'Posicionamento', visible: ready && ((analysis.heatmaps?.heroes?.length ?? 0) > 0 || Boolean(analysis.deaths) || (analysis.wards?.length ?? 0) > 0) },
        { key: 'fights', label: 'Lutas e objetivos', visible: ready && (Boolean(analysis.fights) || Boolean(analysis.damage) || Boolean(analysis.objectives)) },
        { key: 'build', label: 'Build', visible: ready && (Boolean(analysis.itemBuild) || (analysis.abilityBuild?.upgrades?.length ?? 0) > 0) },
        { key: 'players', label: 'Jogadores', visible: true },
    ].filter(tab => tab.visible);
}

// Links to the tabs of an earlier version still open the right page.
const OLD_TABS = { maps: 'positioning', items: 'build', abilities: 'build', economy: 'overview' };

function useTabLink() {
    const [searchParams] = useSearchParams();
    // Keeps ?player= (whose point of view) and only changes the tab.
    return key => {
        const params = new URLSearchParams(searchParams);
        if (key === 'overview') {
            params.delete('tab');
        } else {
            params.set('tab', key);
        }
        const search = params.toString();
        return { search: search ? `?${search}` : '' };
    };
}

function TabNav({ tabs, active }) {
    const tabLink = useTabLink();
    const barRef = useRef(null);

    // On a narrow screen the tabs scroll sideways: keep the active one in view.
    useEffect(() => {
        const bar = barRef.current;
        const link = bar?.querySelector('[aria-current="page"]');
        if (bar && link && (link.offsetLeft < bar.scrollLeft || link.offsetLeft + link.offsetWidth > bar.scrollLeft + bar.clientWidth)) {
            bar.scrollTo({ left: link.offsetLeft - 16 });
        }
    }, [active]);

    return (
        <nav className="analysis-nav" aria-label="Abas da análise" id="analysis-tabs">
            <div className="container analysis-nav-inner" ref={barRef}>
                {tabs.map(tab => (
                    <Link key={tab.key} to={tabLink(tab.key)} aria-current={active === tab.key ? 'page' : undefined}>
                        {tab.label}
                    </Link>
                ))}
            </div>
        </nav>
    );
}

// The overview's shortcuts: the key number of each other tab, linking to it.
function Shortcuts({ analysis, tabs }) {
    const tabLink = useTabLink();
    const has = key => tabs.some(tab => tab.key === key);
    const { laning, fights, itemBuild, deaths, smartStats, performance } = analysis;
    const outcome = { won: 'Ganhou a rota', even: 'Rota empatada', lost: 'Perdeu a rota' }[laning?.outcome];
    const won = fights?.filter(fight => fight.playersTeamWon === true).length;
    const joined = fights?.filter(fight => fight.died || fight.kills > 0 || fight.assists > 0).length;
    const lastItem = itemBuild?.items.at(-1);
    const items = [
        has('tips') && {
            key: 'tips',
            label: 'Como melhorar',
            value: smartStats ? `${smartStats.insights.length} ${smartStats.insights.length === 1 ? 'dica' : 'dicas'}` : `${performance?.weaknesses.length ?? 0} pontos fracos`,
            sub: smartStats ? `contra ${smartStats.counterpartHeroName} e outros jogadores do herói` : 'percentis do OpenDota',
        },
        has('laning') && {
            key: 'laning',
            label: 'Fase de rotas',
            value: outcome ?? 'Sem disputa',
            tone: { won: 'win', lost: 'loss' }[laning.outcome],
            sub: `${laning.player.lastHits} ${laning.player.lastHits === 1 ? 'last hit' : 'last hits'} aos 10:00`,
        },
        has('positioning') && {
            key: 'positioning',
            label: 'Posicionamento',
            value: deaths ? `${deaths.length} ${deaths.length === 1 ? 'morte' : 'mortes'}` : 'Mapa de calor',
            sub: 'mapa de calor, mortes e wards',
        },
        has('fights') && fights && {
            key: 'fights',
            label: 'Lutas e objetivos',
            value: `${won} de ${fights.length} vencidas`,
            sub: `você participou de ${joined}`,
        },
        has('build') && lastItem && {
            key: 'build',
            label: 'Build',
            value: `${itemBuild.items.length} itens`,
            sub: `último: ${lastItem.name}`,
        },
    ].filter(Boolean);
    if (items.length === 0) {
        return null;
    }

    return (
        <section className="section" aria-labelledby="shortcuts-title">
            <SectionHead id="shortcuts-title" title="Mais detalhes" />
            <ul className="shortcut-grid">
                {items.map(item => (
                    <li key={item.key}>
                        <Link className="card shortcut" to={tabLink(item.key)}>
                            <span className="stat-label">{item.label}</span>
                            <strong className={item.tone ? `text-${item.tone}` : undefined}>{item.value}</strong>
                            <span className="stat-sub">{item.sub}</span>
                            <span className="shortcut-arrow" aria-hidden="true">→</span>
                        </Link>
                    </li>
                ))}
            </ul>
        </section>
    );
}

function AnalysisContent({ analysis, accountId, replayPollingStopped }) {
    const [searchParams] = useSearchParams();
    const { performance } = analysis;
    const tabs = tabsOf(analysis);
    const asked = searchParams.get('tab') ?? 'overview';
    const requested = OLD_TABS[asked] ?? asked;
    // A tab this match doesn't have (or not yet: replay still being read) shows the overview.
    const active = tabs.some(tab => tab.key === requested) ? requested : 'overview';
    const ready = analysis.replayStatus === 'ready';
    // null stays null for both teams: an unknown winner highlights nobody.
    const radiantWon = analysis.radiantWon ?? null;
    const direWon = radiantWon === null ? null : !radiantWon;

    // A new tab starts at its top (just under the match header when the page was scrolled past it).
    useEffect(() => {
        const nav = document.getElementById('analysis-tabs');
        if (nav && window.scrollY > nav.offsetTop - 64) {
            window.scrollTo({ top: nav.offsetTop - 64 });
        }
    }, [active]);

    return (
        <>
            <MatchHeader analysis={analysis} />
            <TabNav tabs={tabs} active={active} />

            <div className="container analysis-sections">
                <ReplayStatusBanner replayStatus={analysis.replayStatus} pollingStopped={replayPollingStopped} />

                {active === 'overview' && (
                    <>
                        {!analysis.isParsed && (
                            <p className="notice">
                                O OpenDota ainda não processou o replay desta partida: os percentis de desempenho podem faltar.
                            </p>
                        )}
                        {!performance && (
                            <p className="notice">
                                O jogador {accountId} não aparece nesta partida, por isso não há análise de desempenho.
                                Abra a partida pela lista de partidas desse jogador.
                            </p>
                        )}
                        <HighlightCards
                            performance={performance}
                            laning={ready ? analysis.laning : null}
                            smartStats={ready ? analysis.smartStats : null}
                        />
                        {performance && <PerformanceSummary analysis={analysis} />}
                        {ready && <TimelineSection timeline={analysis.timeline} objectives={analysis.objectives} charts="lead" />}
                        <Shortcuts analysis={analysis} tabs={tabs} />
                    </>
                )}

                {active === 'tips' && (
                    <SmartStatsSection performance={performance} smartStats={ready ? analysis.smartStats : null} />
                )}

                {active === 'laning' && (
                    <>
                        <LaningSection laning={analysis.laning} />
                        <TimelineSection timeline={analysis.timeline} objectives={analysis.objectives} charts="lane" />
                        <section className="section" aria-labelledby="laning-heatmap-title">
                            <HeatmapSection
                                heatmaps={analysis.heatmaps}
                                deaths={analysis.deaths}
                                smartStats={analysis.smartStats}
                                laning={analysis.laning}
                                variant="laning"
                            />
                        </section>
                    </>
                )}

                {active === 'positioning' && (
                    <section className="section" aria-labelledby="maps-title">
                        <SectionHead id="maps-title" title="Posicionamento">
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

                {active === 'fights' && (
                    <>
                        {(analysis.fights || analysis.damage) && (
                            <section className="section" aria-labelledby="fights-damage-title">
                                <SectionHead id="fights-damage-title" title="Lutas e dano" />
                                <div className="split split-5-7">
                                    <FightsSection fights={analysis.fights} isRadiant={performance ? performance.isRadiant : null} />
                                    <DamageSection damage={analysis.damage} heroName={performance?.heroName} />
                                </div>
                            </section>
                        )}
                        <ObjectivesSection objectives={analysis.objectives} />
                    </>
                )}

                {active === 'build' && (
                    <>
                        <ItemBuildSection itemBuild={analysis.itemBuild} />
                        <AbilityBuildSection abilityBuild={analysis.abilityBuild} heroName={performance?.heroName} />
                    </>
                )}

                {active === 'players' && (
                    <section className="section" aria-labelledby="teams-title">
                        <SectionHead id="teams-title" title="Os 10 jogadores" />
                        <div className="stack">
                            <TeamTable name="Radiant" side="radiant" players={analysis.radiant} won={radiantWon} score={analysis.radiantScore} />
                            <TeamTable name="Dire" side="dire" players={analysis.dire} won={direWon} score={analysis.direScore} />
                        </div>
                    </section>
                )}
            </div>
        </>
    );
}

export default MatchAnalysisPage;
