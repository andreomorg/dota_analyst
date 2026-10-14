import { Link } from 'react-router';
import { formatNumber, formatSignedNumber } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { standingDetail, standingOf, standingPer100 } from '../ui/standingLevels';
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

// The subject of each tip (InsightKind), on its tag.
const INSIGHT_SUBJECTS = {
    behindAtTen: 'Fase de rotas',
    lostLaneTrades: 'Trocas na rota',
    isolatedDeaths: 'Posicionamento',
    aloneOnEnemySide: 'Posicionamento',
    deathsInEnemyJungle: 'Posicionamento',
    quickRedeaths: 'Mortes',
    fewObservers: 'Visão',
    shortObserverLife: 'Visão',
    lowObserverUptime: 'Visão',
    lowFightPresence: 'Lutas',
    gapGrewAfterLane: 'Economia',
};

const LANES = { top: 'rota de cima', middle: 'rota do meio', bottom: 'rota de baixo', jungle: 'selva', roaming: 'roaming' };

// ----- The three highlight cards -----

const BADGE_ICONS = {
    strong: 'M20 6L9 17l-5-5',
    warning: 'M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01',
    lane: 'M13 2L3 14h9l-1 8 10-12h-9l1-8z',
};

// A tag in the card's tone (green = good, red = attention), with its small icon.
export function Badge({ tone, icon, children }) {
    return (
        <span className={`badge badge-${tone}`}>
            {icon && <svg viewBox="0 0 24 24" aria-hidden="true"><path d={BADGE_ICONS[icon]} /></svg>}
            {children}
        </span>
    );
}

function HighlightCard({ tone, icon, badge, value, title, text, footerLabel, footerValue, footerTone }) {
    return (
        <article className="card highlight-card">
            <div>
                <div className="highlight-top">
                    <Badge tone={tone} icon={icon}>{badge}</Badge>
                    {value && <span className={`highlight-value mono text-${tone}`}>{value}</span>}
                </div>
                <h3 className="highlight-title">{title}</h3>
                <p className="highlight-text">{text}</p>
            </div>
            {footerLabel && (
                <p className="highlight-footer">
                    <span>{footerLabel}</span>
                    <strong className={`mono text-${footerTone ?? tone}`}>{footerValue}</strong>
                </p>
            )}
        </article>
    );
}

// "Muito acima da média (à frente de 97 em cada 100)".
function verdict(share) {
    return `${standingOf(share).label} (${standingPer100(share)})`;
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
            badge={isStrength ? 'Ponto forte' : 'Seu melhor número'}
            value={`${formatNumber(Math.round(best.value * 100) / 100)}${best.metric === 'towerDamage' ? '' : ' /min'}`}
            title={`${metricLabel(best.metric)}: ${standingOf(best.percentile).label.toLowerCase()}`}
            text={`${capitalize(standingDetail(best.percentile, `jogadores de ${performance.heroName}`))}.${performance.strengths.length > 1
                ? ` Também acima da maioria: ${performance.strengths.slice(1, 3).map(metricLabel).join(' e ')}.`
                : ''}`}
            footerLabel={`Contra jogadores de ${performance.heroName}`}
            footerValue={verdict(best.percentile)}
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
                badge="Atenção / oportunidade"
                title={INSIGHT_TITLES[insight.kind] ?? 'Ponto de atenção'}
                text={insightText(insight, smartStats)}
                footerLabel="Comparado com"
                footerValue={`${smartStats.counterpartHeroName} (mesma posição)`}
                footerTone="even"
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
            badge="Atenção / oportunidade"
            value={`${formatNumber(Math.round(worst.value * 100) / 100)}${worst.metric === 'towerDamage' ? '' : ' /min'}`}
            title={`${metricLabel(worst.metric)}: ${standingOf(worst.percentile).label.toLowerCase()}`}
            text={`${capitalize(standingDetail(worst.percentile, `jogadores de ${performance.heroName}`))}: o seu número mais fraco nesta partida.`}
            footerLabel={`Contra jogadores de ${performance.heroName}`}
            footerValue={verdict(worst.percentile)}
        />
    );
}

function LanePoint({ laning, smartStats }) {
    if (!laning) {
        return null;
    }
    const opponentBest = laning.opponents.reduce((best, hero) => (hero.lastHits > (best?.lastHits ?? -1) ? hero : best), null);
    const outcome = { won: 'Ganhou a rota', even: 'Rota empatada', lost: 'Perdeu a rota' }[laning.outcome];
    const tone = { won: 'win', lost: 'loss' }[laning.outcome] ?? 'even';
    const difference = laning.opponents.length > 0 ? laning.netWorthDifference : null;
    const trades = smartStats?.player.laneDamageDealt !== null && smartStats?.player.laneDamageDealt !== undefined
        ? `Trocas até 10:00: causou ${formatNumber(smartStats.player.laneDamageDealt)} e recebeu ${formatNumber(smartStats.player.laneDamageTaken)} de dano de heróis.`
        : null;
    return (
        <HighlightCard
            tone={tone === 'even' ? 'win' : tone}
            icon="lane"
            badge="Fase de rotas"
            value={difference !== null && difference !== undefined ? `${formatSignedNumber(difference)} patrimônio` : null}
            title={`${formatNumber(laning.player.lastHits)} ${laning.player.lastHits === 1 ? 'last hit' : 'last hits'} e ${formatNumber(laning.player.denies)} ${laning.player.denies === 1 ? 'deny' : 'denies'} aos 10:00 na ${LANES[laning.lane] ?? laning.lane}`}
            text={[
                opponentBest && `O adversário com mais last hits, ${opponentBest.heroName}, tinha ${formatNumber(opponentBest.lastHits)}.`,
                trades,
            ].filter(Boolean).join(' ') || 'Sem adversário na sua rota.'}
            footerLabel="Resultado da rota"
            footerValue={outcome ?? '—'}
            footerTone={tone}
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
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Mortes por fase, tabela com rolagem">
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
            <SectionHead id="highlights-title" title="Destaques da partida" icon="pulse">
                <span className="mono">{smartStats ? 'Do replay e do OpenDota' : 'Do OpenDota'}</span>
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
        <>
            {smartStats && (
                <section className="section" aria-labelledby="smart-stats-title">
                    <SectionHead id="smart-stats-title" title="Diagnósticos Prioritários de Desempenho" icon="pulse">
                        <span className="mono">
                            {texts.length === 0 ? 'Nenhuma dica' : `${texts.length} ${texts.length === 1 ? 'dica' : 'dicas'}`}
                        </span>
                    </SectionHead>
                    <p className="section-intro tips-context">
                        Você jogou de {ROLES[smartStats.role] ?? `posição ${smartStats.role}`}, comparado com {smartStats.counterpartHeroName}, a
                        mesma posição no time inimigo. As dicas mais importantes vêm primeiro; os números de cada uma estão na aba do assunto.
                    </p>
                    {texts.length === 0
                        ? (
                            <div className="card quiet-state">
                                <p className="quiet-state-kicker mono">Sem pontos críticos</p>
                                <p>Nada se destacou como ponto a melhorar nesta partida.</p>
                            </div>
                        )
                        : (
                            <ol className="insight-list">
                                {texts.map(({ insight, text }, index) => {
                                    const tab = INSIGHT_TABS[insight.kind];
                                    return (
                                        <li key={insight.kind} className="card insight-item">
                                            <div>
                                                <div className="highlight-top">
                                                    <Badge tone="loss">{INSIGHT_SUBJECTS[insight.kind] ?? 'Atenção'}</Badge>
                                                    <span className="insight-rank mono">Dica {index + 1} de {texts.length}</span>
                                                </div>
                                                <h3 className="highlight-title">{INSIGHT_TITLES[insight.kind] ?? 'Dica'}</h3>
                                                <p className="insight-text">{text}</p>
                                            </div>
                                            <p className="insight-footer">
                                                <span className="mono">vs {smartStats.counterpartHeroName}</span>
                                                {tab && hasTab(tab) && (
                                                    <Link className="insight-link" to={tabLink(tab)}>
                                                        Ver os números em {TAB_NAMES[tab]} →
                                                    </Link>
                                                )}
                                            </p>
                                        </li>
                                    );
                                })}
                            </ol>
                        )}
                </section>
            )}

            <ComparisonCard performance={performance} />
        </>
    );
}

export default SmartStatsSection;
