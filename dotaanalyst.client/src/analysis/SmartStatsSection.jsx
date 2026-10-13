import { Link } from 'react-router';
import { formatNumber } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { standingDetail, standingOf } from '../ui/standingLevels';
import ComparisonCard from './ComparisonCard';
import { INSIGHT_TITLES, insightText, plain, ROLES } from './insights';
import { metricLabel } from './metrics';
import { INSIGHT_TABS, TAB_NAMES } from './statThemes';
import { useTabLink } from './tabLink';

// The smart stats in words: the highlight cards of the overview, the tips of "Como melhorar" (each linking to the tab
// whose numbers back it) and the deaths by region and phase of "Posicionamento". The numbers themselves live once,
// in the tab of their subject (ThemeStatsCard).

const capitalize = text => text.charAt(0).toUpperCase() + text.slice(1);

function cell(value, format) {
    return value === null || value === undefined ? '—' : format(value);
}

const REGIONS = [
    { key: 'ownBase', label: 'Sua base' },
    { key: 'topLane', label: 'Rota de cima' },
    { key: 'middleLane', label: 'Rota do meio' },
    { key: 'bottomLane', label: 'Rota de baixo' },
    { key: 'ownJungle', label: 'Sua selva' },
    { key: 'river', label: 'Rio' },
    { key: 'enemyJungle', label: 'Selva inimiga' },
    { key: 'enemyBase', label: 'Base inimiga' },
];

const PHASES = [
    { key: 'earlyGame', label: 'Começo (0–10 min)' },
    { key: 'midGame', label: 'Meio (10–20 min)' },
    { key: 'lateGame', label: 'Fim (20+ min)' },
];

const LANES = { top: 'rota de cima', middle: 'rota do meio', bottom: 'rota de baixo', jungle: 'selva', roaming: 'roaming' };

// ----- The three highlight cards -----

function Icon({ kind }) {
    const paths = {
        strong: 'M7 11v8H4v-8h3zm2 8h7.4a2 2 0 0 0 2-1.6l1-5A2 2 0 0 0 17.4 10H14l.6-3.2A1.7 1.7 0 0 0 13 5l-4 6v8z',
        warning: 'M12 4l9 16H3L12 4zm0 6v4m0 3h.01',
        lane: 'M4 19h16M7 16V9m5 7V5m5 11v-4',
    };
    return (
        <svg className="card-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path d={paths[kind]} />
        </svg>
    );
}

function HighlightCard({ tone, icon, title, text, detail, footerLabel, footerValue }) {
    return (
        <article className={`card highlight-card tone-${tone}`}>
            <div>
                <h3 className="highlight-title"><Icon kind={icon} /> {title}</h3>
                <p className="highlight-text">{text}</p>
                {detail && <p className="highlight-detail">{detail}</p>}
            </div>
            {footerLabel && (
                <p className="highlight-footer">
                    <span>{footerLabel}</span>
                    <strong className="mono">{footerValue}</strong>
                </p>
            )}
        </article>
    );
}

function StrongPoint({ performance }) {
    if (!performance || performance.metrics.length === 0) {
        return null;
    }
    const byName = new Map(performance.metrics.map(metric => [metric.metric, metric]));
    const best = byName.get(performance.strengths[0])
        ?? [...performance.metrics].sort((a, b) => b.percentile - a.percentile)[0];
    const isStrength = best.percentile >= 0.7;
    return (
        <HighlightCard
            tone="win"
            icon="strong"
            title={isStrength ? 'Ponto forte' : 'Seu melhor número'}
            text={`${metricLabel(best.metric)}: ${standingOf(best.percentile).label.toLowerCase()}.`}
            detail={`${capitalize(standingDetail(best.percentile, `jogadores de ${performance.heroName}`))}.${performance.strengths.length > 1
                ? ` Também acima da maioria: ${performance.strengths.slice(1, 3).map(metricLabel).join(' e ')}.`
                : ''}`}
            footerLabel={`Contra jogadores de ${performance.heroName}`}
            footerValue={standingOf(best.percentile).label}
        />
    );
}

function AttentionPoint({ performance, smartStats }) {
    const insight = smartStats?.insights.find(candidate => insightText(candidate, smartStats));
    if (insight) {
        return (
            <HighlightCard
                tone="loss"
                icon="warning"
                title="Ponto de atenção"
                text={INSIGHT_TITLES[insight.kind] ?? 'Ponto de atenção'}
                detail={insightText(insight, smartStats)}
                footerLabel={`Comparado com ${smartStats.counterpartHeroName}`}
                footerValue="mesma posição"
            />
        );
    }

    const worst = performance && performance.metrics.find(metric => metric.metric === performance.weaknesses[0]);
    if (!worst) {
        return null;
    }
    return (
        <HighlightCard
            tone="loss"
            icon="warning"
            title="Ponto de atenção"
            text={`${metricLabel(worst.metric)}: ${standingOf(worst.percentile).label.toLowerCase()}.`}
            detail={`${capitalize(standingDetail(worst.percentile, `jogadores de ${performance.heroName}`))}: o seu número mais fraco nesta partida.`}
            footerLabel={`Contra jogadores de ${performance.heroName}`}
            footerValue={standingOf(worst.percentile).label}
        />
    );
}

function LanePoint({ laning, smartStats }) {
    if (!laning) {
        return null;
    }
    const opponentBest = laning.opponents.reduce((best, hero) => (hero.lastHits > (best?.lastHits ?? -1) ? hero : best), null);
    const outcome = { won: 'Ganhou', even: 'Empatada', lost: 'Perdeu' }[laning.outcome];
    const trades = smartStats?.player.laneDamageDealt !== null && smartStats?.player.laneDamageDealt !== undefined
        ? `Trocas até 10:00: causou ${formatNumber(smartStats.player.laneDamageDealt)} e recebeu ${formatNumber(smartStats.player.laneDamageTaken)} de dano de heróis.`
        : null;
    return (
        <HighlightCard
            tone="neutral"
            icon="lane"
            title="Fase de rotas"
            text={`${formatNumber(laning.player.lastHits)} ${laning.player.lastHits === 1 ? 'last hit' : 'last hits'} e ${formatNumber(laning.player.denies)} ${laning.player.denies === 1 ? 'deny' : 'denies'} aos 10:00 na ${LANES[laning.lane] ?? laning.lane}.`}
            detail={[
                opponentBest && `O adversário com mais last hits, ${opponentBest.heroName}, tinha ${formatNumber(opponentBest.lastHits)}.`,
                trades,
            ].filter(Boolean).join(' ') || 'Sem adversário na sua rota.'}
            footerLabel="Resultado da rota"
            footerValue={outcome ?? '—'}
        />
    );
}

// ----- Deaths by region and phase (the "Posicionamento" tab) -----

export function PhaseDeathsCard({ smartStats }) {
    const series = smartStats && [
        { key: 'player', label: 'Você', values: smartStats.player.deathsByRegionByPhase },
        { key: 'counterpart', label: smartStats.counterpartHeroName, values: smartStats.counterpart.deathsByRegionByPhase },
        { key: 'average', label: 'Média da partida', values: smartStats.matchAverage.deathsByRegionByPhase },
    ];
    if (!series || !series.some(item => item.values)) {
        return null;
    }

    return (
        <details className="card">
            <summary>
                Onde e quando as mortes aconteceram
                <span className="summary-hint">por região do mapa, 0–10, 10–20 e 20+ min</span>
            </summary>
            <div className="table-scroll">
                <table className="data-table">
                    <caption className="visually-hidden">
                        Mortes em cada região por fase: você, {smartStats.counterpartHeroName} e a média da partida
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Fase</th>
                            <th scope="col">Jogador</th>
                            {REGIONS.map(region => <th key={region.key} scope="col" className="num-col">{region.label}</th>)}
                        </tr>
                    </thead>
                    <tbody>
                        {PHASES.flatMap(phase => series.map(item => (
                            <tr key={`${phase.key}-${item.key}`} className={item.key === 'player' ? 'is-you' : undefined}>
                                <th scope="row">{phase.label}</th>
                                <th scope="row">{item.label}</th>
                                {REGIONS.map(region => (
                                    <td key={region.key} className="num-col mono">{cell(item.values?.[phase.key]?.[region.key], plain)}</td>
                                ))}
                            </tr>
                        )))}
                    </tbody>
                </table>
            </div>
            <p className="section-note">
                Contagem de mortes do replay pela posição; a média considera os dez heróis da partida.
            </p>
        </details>
    );
}

// The three highlight cards (strong point, point of attention, the lane): the top of the overview. performance,
// laning and smartStats come from the API; each card shows only when it has something to say.
export function HighlightCards({ performance, laning, smartStats }) {
    if (!performance && !smartStats && !laning) {
        return null;
    }
    return (
        <section className="section" aria-labelledby="highlights-title">
            <SectionHead id="highlights-title" title="Destaques da partida">
                {smartStats && <span>Comparado com {smartStats.counterpartHeroName}, a mesma posição no time inimigo</span>}
            </SectionHead>
            <div className="highlight-grid">
                <StrongPoint performance={performance} />
                <AttentionPoint performance={performance} smartStats={smartStats} />
                <LanePoint laning={laning} smartStats={smartStats} />
            </div>
        </section>
    );
}

// The "Como melhorar" tab: every tip of the API, most important first, each with a link to the tab whose numbers
// back it; then the per-minute numbers against every match of the hero (OpenDota). smartStats is null until the
// replay is read, without a point-of-view player and for matches without role queue: then only OpenDota's part shows.
function SmartStatsSection({ performance, smartStats, tabs }) {
    const tabLink = useTabLink();
    if (!performance && !smartStats) {
        return null;
    }

    const texts = (smartStats?.insights ?? [])
        .map(insight => ({ insight, text: insightText(insight, smartStats) }))
        .filter(entry => entry.text);
    const hasTab = key => (tabs ?? []).some(tab => tab.key === key);

    return (
        <section className="section" aria-labelledby="smart-stats-title">
            <SectionHead id="smart-stats-title" title="Como melhorar">
                {smartStats && <span>Comparado com {smartStats.counterpartHeroName}, a mesma posição no time inimigo</span>}
            </SectionHead>

            {smartStats && (
                <>
                    <p className="section-intro">
                        Você jogou de {ROLES[smartStats.role] ?? `posição ${smartStats.role}`}. As dicas mais importantes vêm
                        primeiro; os números de cada uma estão na aba do assunto.
                    </p>
                    {texts.length === 0
                        ? <p className="empty-state">Nada se destacou como ponto a melhorar nesta partida.</p>
                        : (
                            <ol className="insight-list">
                                {texts.map(({ insight, text }) => {
                                    const tab = INSIGHT_TABS[insight.kind];
                                    return (
                                        <li key={insight.kind} className="insight-item">
                                            <strong>{INSIGHT_TITLES[insight.kind] ?? 'Dica'}</strong>
                                            <span>{text}</span>
                                            {tab && hasTab(tab) && (
                                                <Link className="insight-link" to={tabLink(tab)}>
                                                    Ver os números em {TAB_NAMES[tab]} →
                                                </Link>
                                            )}
                                        </li>
                                    );
                                })}
                            </ol>
                        )}
                </>
            )}

            <ComparisonCard performance={performance} />
        </section>
    );
}

export default SmartStatsSection;
