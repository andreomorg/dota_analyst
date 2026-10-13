import { formatDuration, formatNumber } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { standingDetail, standingOf } from '../ui/standingLevels';
import ComparisonCard from './ComparisonCard';

const capitalize = text => text.charAt(0).toUpperCase() + text.slice(1);
import { INSIGHT_TITLES, insightText, lifetime, percent, plain, ROLES } from './insights';
import { metricLabel } from './metrics';

// "Dicas e oportunidades de melhoria" (the Stitch smart-stats section): three highlight cards (strong point, point
// of attention, the lane), the API's findings, then the numbers next to the enemy of the same role, the match
// average and other players of the same hero (pro and high rank).

const GROUPS = [
    {
        title: 'Posicionamento',
        rows: [
            { key: 'deaths', label: 'Mortes', format: plain },
            { key: 'isolatedDeaths', label: 'Mortes sem aliado por perto', format: plain },
            { key: 'deathsOnEnemySide', label: 'Mortes no lado inimigo', format: plain },
            { key: 'deathsByRegion.enemyJungle', label: 'Mortes na selva inimiga', format: plain },
            { key: 'deathsByRegion.river', label: 'Mortes no rio', format: plain },
            { key: 'aloneOnEnemySideShare', label: 'Tempo sozinho no lado inimigo', format: percent },
            { key: 'enemySideShare', label: 'Tempo no lado inimigo', format: percent },
            { key: 'quickRedeaths', label: 'Mortes logo depois de voltar', format: plain },
        ],
    },
    {
        title: 'Lutas',
        rows: [
            {
                key: 'fightsNearby',
                label: 'Lutas do time em que esteve perto',
                // "x de y" (the average row has fractions: one decimal).
                value: summary => summary?.teamFights === null || summary?.teamFights === undefined
                    ? null
                    : `${plain(summary.fightsNearby)} de ${plain(summary.teamFights)}`,
            },
            { key: 'killParticipation', label: 'Participação em abates', format: percent },
            { key: 'leadLostPerDeath', label: 'Vantagem do time perdida por morte', format: value => formatNumber(Math.round(value)) },
        ],
    },
    {
        title: 'Economia',
        rows: [
            { key: 'lastHitsAt10', label: 'Last hits aos 10:00', format: plain },
            { key: 'netWorthAt10', label: 'Patrimônio aos 10:00', format: plain },
            { key: 'netWorthAt20', label: 'Patrimônio aos 20:00', format: plain },
            { key: 'firstBigItemSeconds', label: 'Primeiro item grande', format: value => formatDuration(Math.round(value)) },
            { key: 'netWorthShare', label: 'Parte do patrimônio do time', format: percent },
            { key: 'laneDamageDealt', label: 'Dano causado em heróis até 10:00', format: plain },
            { key: 'laneDamageTaken', label: 'Dano recebido de heróis até 10:00', format: plain },
        ],
    },
    {
        title: 'Visão',
        rows: [
            { key: 'observersPlaced', label: 'Observers colocados', format: plain },
            { key: 'sentriesPlaced', label: 'Sentries colocados', format: plain },
            { key: 'observerAverageLifetimeSeconds', label: 'Vida média dos observers', format: lifetime },
            { key: 'wardsLostToEnemy', label: 'Seus wards destruídos pelo inimigo', format: plain },
            { key: 'enemyWardsDestroyed', label: 'Wards inimigos destruídos', format: plain },
            { key: 'teamObserverUptime', label: 'Tempo com observer do time', format: percent },
        ],
    },
];

// key may go one level down ("deathsByRegion.river"); missing on metrics computed before the regions.
function valueOf(summary, key) {
    return key.split('.').reduce((value, part) => value?.[part], summary);
}

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

// ----- Tables -----

function PhaseDeathTable({ player, counterpart, matchAverage, counterpartName }) {
    const series = [
        { key: 'player', label: 'Você', values: player },
        { key: 'counterpart', label: counterpartName, values: counterpart },
        { key: 'average', label: 'Média da partida', values: matchAverage },
    ];
    if (!series.some(item => item.values)) {
        return null;
    }

    return (
        <div className="table-scroll">
            <table className="data-table">
                <caption className="visually-hidden">
                    Mortes em cada região por fase: você, {counterpartName} e a média da partida
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
    );
}

function CounterpartTable({ smartStats }) {
    const { player, counterpart, matchAverage } = smartStats;
    return (
        <div className="card">
            <h3 className="card-title">Você, {smartStats.counterpartHeroName} e a média da partida</h3>
            <div className="table-scroll">
                <table className="data-table">
                    <caption className="visually-hidden">
                        Seus números, os de {smartStats.counterpartHeroName} e a média da partida
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Métrica</th>
                            <th scope="col" className="num-col">Você</th>
                            <th scope="col" className="num-col">{smartStats.counterpartHeroName}</th>
                            <th scope="col" className="num-col">Média da partida</th>
                        </tr>
                    </thead>
                    {GROUPS.map(group => (
                        <tbody key={group.title}>
                            <tr className="group-row">
                                <th scope="rowgroup" colSpan={4}>{group.title}</th>
                            </tr>
                            {group.rows.map(row => (
                                <tr key={row.key}>
                                    <th scope="row">{row.label}</th>
                                    {[player, counterpart, matchAverage].map((summary, index) => (
                                        <td key={index} className="num-col mono">
                                            {row.value ? (row.value(summary) ?? '—') : cell(valueOf(summary, row.key), row.format)}
                                        </td>
                                    ))}
                                </tr>
                            ))}
                        </tbody>
                    ))}
                </table>
            </div>
            <p className="section-note">
                &quot;Sem aliado por perto&quot;: nenhum herói do seu time a menos de 1.500 unidades do mapa.
                &quot;Lado inimigo&quot;: além do rio, no lado do outro time. O tempo no lado inimigo mostra mais quem estava
                ganhando do que um erro; o tempo sozinho lá é o que indica risco.
            </p>
        </div>
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

// The "Como melhorar" tab: every finding of the API, one card comparing the player with other players of the hero
// (OpenDota, our base, the lane matchup), then the numbers next to the enemy of the same role and the match average.
// smartStats is null until the replay is read, without a point-of-view player and for matches without role queue:
// then only OpenDota's comparison shows.
function SmartStatsSection({ performance, smartStats }) {
    if (!performance && !smartStats) {
        return null;
    }

    const texts = (smartStats?.insights ?? [])
        .map(insight => ({ insight, text: insightText(insight, smartStats) }))
        .filter(entry => entry.text);
    const hasPhaseMetrics = Boolean(
        smartStats?.player.deathsByRegionByPhase
        || smartStats?.counterpart.deathsByRegionByPhase
        || smartStats?.matchAverage.deathsByRegionByPhase,
    );

    return (
        <section className="section" aria-labelledby="smart-stats-title">
            <SectionHead id="smart-stats-title" title="Como melhorar">
                {smartStats && <span>Comparado com {smartStats.counterpartHeroName}, a mesma posição no time inimigo</span>}
            </SectionHead>

            {smartStats && (
                <>
                    <p className="section-intro">
                        Você jogou de {ROLES[smartStats.role] ?? `posição ${smartStats.role}`}. As dicas mais importantes vêm primeiro.
                    </p>
                    {texts.length === 0
                        ? <p className="empty-state">Nada se destacou como ponto a melhorar nesta partida.</p>
                        : (
                            <ol className="insight-list">
                                {texts.map(({ insight, text }) => (
                                    <li key={insight.kind} className="insight-item">
                                        <strong>{INSIGHT_TITLES[insight.kind] ?? 'Dica'}</strong>
                                        <span>{text}</span>
                                    </li>
                                ))}
                            </ol>
                        )}
                </>
            )}

            <div className="stack">
                <ComparisonCard performance={performance} smartStats={smartStats} />
                {smartStats && <CounterpartTable smartStats={smartStats} />}
                {hasPhaseMetrics && (
                    <details className="card">
                        <summary>
                            Mortes por região e fase da partida
                            <span className="summary-hint">0–10, 10–20 e 20+ min</span>
                        </summary>
                        <PhaseDeathTable
                            player={smartStats.player.deathsByRegionByPhase}
                            counterpart={smartStats.counterpart.deathsByRegionByPhase}
                            matchAverage={smartStats.matchAverage.deathsByRegionByPhase}
                            counterpartName={smartStats.counterpartHeroName}
                        />
                        <p className="section-note">
                            Contagem de eventos de morte do replay por coordenada; a média considera os dez heróis da partida.
                        </p>
                    </details>
                )}
            </div>
        </section>
    );
}

export default SmartStatsSection;
