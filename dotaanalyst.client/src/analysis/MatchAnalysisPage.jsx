import { useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { readSavedAccountId } from '../accountStorage';
import { loginUrl } from '../api/auth';
import StatePanel, { StateIcon } from '../ui/StatePanel';
import { useCurrentUser } from '../auth/useCurrentUser';
import SectionHead from '../ui/SectionHead';
import AbilityBuildSection from './AbilityBuildSection';
import { DamageHeroesCard, DamageSourcesCard } from './DamageSection';
import DeathMap from './DeathMap';
import FightsSection from './FightsSection';
import HeatmapSection from './HeatmapSection';
import ItemBuildSection from './ItemBuildSection';
import LaningSection, { ReplayStatusBanner } from './LaningSection';
import MatchHeader from './MatchHeader';
import ObjectivesSection, { ObjectiveCards } from './ObjectivesSection';
import PerformanceSummary from './PerformanceSummary';
import SmartStatsSection, { HighlightCards, PhaseDeathsCard } from './SmartStatsSection';
import StatChart from './StatChart';
import TabIcon from './TabIcon';
import { useTabLink } from './tabLink';
import TeamTable from './TeamTable';
import ThemeStatsCard from './ThemeStatsCard';
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
            <p className="loading-line"><span className="loading-dot" aria-hidden="true" /> Carregando os dados da partida {matchId}...</p>
            <div aria-hidden="true" className="skeleton-stack">
                <span className="skeleton skeleton-banner" />
                <div className="skeleton-tiles">
                    <span className="skeleton" />
                    <span className="skeleton" />
                    <span className="skeleton" />
                </div>
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
                <StatePanel
                    icon="user"
                    tone="you"
                    title={`Partida ${matchId}`}
                    titleAs="h1"
                    actions={(
                        <>
                            <Link className="button-secondary" to="/">Voltar para as partidas</Link>
                            <a className="button-secondary button-steam" href={loginUrl(`/matches/${matchId}`)}>Entrar com a Steam</a>
                        </>
                    )}
                >
                    <p>Para ver a análise, entre com a Steam ou informe o seu account ID do Dota na lista de partidas.</p>
                </StatePanel>
            </main>
        );
    }

    if (!isValidMatchId(matchId)) {
        return (
            <main className="page container">
                <BackLink />
                <StatePanel
                    icon="invalid"
                    tone="loss"
                    title="Partida inválida"
                    titleAs="h1"
                    actions={<Link className="button-secondary" to="/">← Voltar para a busca</Link>}
                >
                    <p role="alert">O número da partida deve conter só dígitos.</p>
                </StatePanel>
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
                    <div className="error-panel error-panel-wide" role="alert">
                        <span className="error-panel-icon"><StateIcon icon="alert" /></span>
                        <div>
                            <h1>Partida {matchId}</h1>
                            <p>{state.message}</p>
                        </div>
                        <button className="button-secondary button-retry" type="button" onClick={state.retry}>
                            <span aria-hidden="true">↻</span> Tentar novamente
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
                        <TabIcon tab={tab.key} />
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
    const tips = smartStats?.insights.length;
    const plural = (count, one, many) => `${count} ${count === 1 ? one : many}`;
    const items = [
        has('tips') && {
            key: 'tips',
            label: 'Como melhorar',
            sub: smartStats
                ? (tips > 0 ? `${plural(tips, 'dica', 'dicas')} contra ${smartStats.counterpartHeroName} e outros jogadores do herói` : 'Nada se destacou como ponto a melhorar')
                : `${plural(performance?.weaknesses.length ?? 0, 'ponto fraco', 'pontos fracos')} nos percentis do OpenDota`,
            cta: 'Abrir dicas',
        },
        has('laning') && {
            key: 'laning',
            label: 'Fase de rotas',
            sub: `${outcome ?? 'Sem disputa'}: ${plural(laning.player.lastHits, 'last hit', 'last hits')} aos 10:00`,
            cta: 'Ver a rota',
        },
        has('positioning') && {
            key: 'positioning',
            label: 'Posicionamento',
            sub: `Mapa de calor, wards e ${deaths ? plural(deaths.length, 'morte', 'mortes') : 'mortes'}`,
            cta: 'Ver o mapa',
        },
        has('fights') && {
            key: 'fights',
            label: 'Lutas e objetivos',
            sub: fights ? `${won} de ${plural(fights.length, 'luta vencida', 'lutas vencidas')}; você participou de ${joined}` : 'Dano e objetivos da partida',
            cta: 'Ver as lutas',
        },
        has('build') && {
            key: 'build',
            label: 'Build',
            sub: lastItem ? `${plural(itemBuild.items.length, 'item', 'itens')}; último: ${lastItem.name}` : 'Itens e habilidades na ordem',
            cta: 'Ver a build',
        },
        has('players') && !has('build') && {
            key: 'players',
            label: 'Jogadores',
            sub: 'Os 10 jogadores e qualquer estatística minuto a minuto',
            cta: 'Ver os jogadores',
        },
    ].filter(Boolean);
    if (items.length === 0) {
        return null;
    }

    return (
        <section className="section" aria-labelledby="shortcuts-title">
            <SectionHead id="shortcuts-title" title="Aprofundar a análise desta partida" icon="forward">
                <span className="mono">Uma aba para cada pergunta</span>
            </SectionHead>
            <ul className="shortcut-grid">
                {items.map(item => (
                    <li key={item.key}>
                        <Link className="shortcut" to={tabLink(item.key)}>
                            <span className="shortcut-icons" aria-hidden="true">
                                <TabIcon tab={item.key} className="shortcut-icon" />
                                <svg className="shortcut-chevron" viewBox="0 0 24 24"><path d="M9 18l6-6-6-6" /></svg>
                            </span>
                            <strong>{item.label}</strong>
                            <span className="shortcut-sub">{item.sub}</span>
                            <span className="shortcut-cta mono" aria-hidden="true">{item.cta} →</span>
                        </Link>
                    </li>
                ))}
            </ul>
        </section>
    );
}

// How the lane chart names each hero of the lane (timeline.lane[].side).
const LANE_SIDES = { player: 'você', ally: 'aliado', opponent: 'adversário' };

function AnalysisContent({ analysis, accountId, replayPollingStopped }) {
    const [searchParams] = useSearchParams();
    const { performance } = analysis;
    const tabs = tabsOf(analysis);
    const asked = searchParams.get('tab') ?? 'overview';
    const requested = OLD_TABS[asked] ?? asked;
    // A tab this match doesn't have (or not yet: replay still being read) shows the overview.
    const active = tabs.some(tab => tab.key === requested) ? requested : 'overview';
    const ready = analysis.replayStatus === 'ready';
    const smartStats = ready ? analysis.smartStats : null;
    const heroName = performance?.heroName;
    const stats = ready ? analysis.stats : null;
    const laneHeroes = (analysis.timeline?.lane ?? []).map(hero => ({ heroId: hero.heroId, side: LANE_SIDES[hero.side] }));
    // The comparison chart of the players tab starts with you and the enemy of your role (or the first enemy).
    const me = stats?.heroes.find(hero => hero.isCurrentPlayer);
    // The point-of-view player's row of the scoreboard (items at the end).
    const pointOfView = [...analysis.radiant, ...analysis.dire].find(player => player.isCurrentPlayer);
    const rivalHeroId = analysis.smartStats?.counterpartHeroId ?? stats?.heroes.find(hero => me && hero.team !== me.team)?.heroId;
    const compareHeroes = [me?.heroId, rivalHeroId].filter(heroId => heroId !== undefined).map(heroId => ({ heroId }));
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

            <div className="container analysis-sections" data-analysis-tab={active}>
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
                        {performance && <PerformanceSummary analysis={analysis} />}
                        <HighlightCards
                            performance={performance}
                            laning={ready ? analysis.laning : null}
                            smartStats={ready ? analysis.smartStats : null}
                        />
                        {ready && (
                            <TimelineSection
                                timeline={analysis.timeline}
                                objectives={analysis.objectives}
                                durationSeconds={analysis.durationSeconds}
                                radiantWon={analysis.radiantWon}
                            />
                        )}
                        <Shortcuts analysis={analysis} tabs={tabs} />
                    </>
                )}

                {active === 'tips' && (
                    <SmartStatsSection performance={performance} smartStats={smartStats} tabs={tabs} />
                )}

                {active === 'laning' && (
                    <>
                        <LaningSection laning={analysis.laning} isRadiant={performance?.isRadiant} />
                        <div className="split split-top">
                            {laneHeroes.length > 0 && (
                                <StatChart
                                    stats={stats}
                                    heroes={laneHeroes}
                                    upTo={15 * 60}
                                    defaultStat="lastHits"
                                    title="Evolução dos heróis da rota (0–15 min)"
                                    titleId="lane-chart-title"
                                    note={`Minuto a minuto, entre os ${laneHeroes.length} heróis da rota`}
                                />
                            )}
                            <HeatmapSection
                                heatmaps={analysis.heatmaps}
                                deaths={analysis.deaths}
                                smartStats={analysis.smartStats}
                                laning={analysis.laning}
                                variant="laning"
                            />
                        </div>
                        <ThemeStatsCard
                            themes={['laning']}
                            smartStats={smartStats}
                            stats={stats}
                            heroName={heroName}
                            title="Sua rota contra outros jogadores"
                        />
                    </>
                )}

                {active === 'positioning' && (
                    <>
                        <HeatmapSection
                            heatmaps={analysis.heatmaps}
                            deaths={analysis.deaths}
                            smartStats={analysis.smartStats}
                            laning={analysis.laning}
                            variant="afterLaning"
                        />
                        <div className="split split-top map-pair">
                            <DeathMap deaths={analysis.deaths} />
                            <WardMap wards={analysis.wards} uptime={smartStats?.player.teamObserverUptime} />
                        </div>
                        <ThemeStatsCard themes={['vision', 'deaths']} smartStats={smartStats} heroName={heroName} title="Visão e sobrevivência" />
                        <PhaseDeathsCard smartStats={smartStats} />
                    </>
                )}

                {active === 'fights' && (
                    <>
                        <ObjectiveCards objectives={analysis.objectives} fights={analysis.fights} />
                        <FightsSection
                            fights={analysis.fights}
                            objectives={analysis.objectives}
                            durationSeconds={analysis.durationSeconds}
                            isRadiant={performance ? performance.isRadiant : null}
                        />
                        {analysis.damage && (
                            <div className="split split-top">
                                <DamageHeroesCard damage={analysis.damage} players={[...analysis.radiant.map(player => ({ ...player, team: 'radiant' })), ...analysis.dire.map(player => ({ ...player, team: 'dire' }))]} />
                                <DamageSourcesCard damage={analysis.damage} heroName={performance?.heroName} />
                            </div>
                        )}
                        <div className={smartStats && analysis.objectives ? 'split split-top split-7-5' : 'stack'}>
                            <ObjectivesSection objectives={analysis.objectives} durationSeconds={analysis.durationSeconds} />
                            <ThemeStatsCard themes={['fights']} smartStats={smartStats} heroName={heroName} title="Seus números nas lutas" compact />
                        </div>
                    </>
                )}

                {active === 'build' && (
                    <>
                        <ItemBuildSection
                            itemBuild={analysis.itemBuild}
                            firstBigItemSeconds={smartStats?.player.firstBigItemSeconds}
                            neutral={pointOfView?.neutralItem ? { id: pointOfView.neutralItemId, name: pointOfView.neutralItem } : null}
                        />
                        <ThemeStatsCard themes={['economy']} smartStats={smartStats} stats={stats} heroName={heroName} title="Economia: seus números contra outros jogadores" />
                        <AbilityBuildSection abilityBuild={analysis.abilityBuild} heroName={performance?.heroName} durationSeconds={analysis.durationSeconds} />
                    </>
                )}

                {active === 'players' && (
                    <>
                        <h2 className="visually-hidden">Os 10 jogadores</h2>
                        <TeamTable name="Radiant" side="radiant" players={analysis.radiant} won={radiantWon} score={analysis.radiantScore} />
                        <TeamTable name="Dire" side="dire" players={analysis.dire} won={direWon} score={analysis.direScore} />
                        <StatChart
                            stats={stats}
                            heroes={compareHeroes}
                            pickHeroes
                            title="Compare qualquer estatística"
                            titleId="compare-title"
                            note="Escolha a estatística e até 5 heróis. Cada ponto é um minuto do relógio do jogo; o último é o fim da partida."
                        />
                    </>
                )}
            </div>
        </>
    );
}

export default MatchAnalysisPage;
