import { formatDuration, formatNumber, toPercent } from '../matches/format';

// Where the player could improve (analysis.smartStats): the API's findings in words, then the numbers next to the
// enemy of the same role and the match average. The rules and the numbers come from the API (Domain); only the
// words are here.

const ROLES = {
    1: 'carry (posição 1)',
    2: 'mid (posição 2)',
    3: 'offlane (posição 3)',
    4: 'suporte (posição 4)',
    5: 'suporte (posição 5)',
};

const percent = value => `${toPercent(value)}%`;
const lifetime = value => formatDuration(Math.round(value));
const plain = value => formatNumber(Math.round(value * 10) / 10);

// One sentence per finding; counterpart = the enemy of the same role.
function insightText(insight, stats) {
    const them = stats.counterpartHeroName;
    switch (insight.kind) {
        case 'isolatedDeaths':
            return `${insight.value} das suas ${stats.player.deaths} mortes foram sem nenhum aliado por perto (${them}: ${insight.counterpart}). Antes de avançar, ande com o time ou tenha visão da área.`;
        case 'aloneOnEnemySide':
            return `Você passou ${percent(insight.value)} do tempo sozinho no lado inimigo do mapa (${them}: ${percent(insight.counterpart)}) e morreu ${stats.player.deathsOnEnemySide} vezes lá. Avance com o time ou com visão.`;
        case 'behindAtTen':
            return `Aos 10:00 você tinha ${formatNumber(insight.value)} de patrimônio, ${formatNumber(insight.counterpart - insight.value)} a menos que ${them} (${formatNumber(insight.counterpart)}). Vale rever a fase de rotas: last hits, trocas e mortes.`;
        case 'fewObservers':
            return `Você colocou ${insight.value} observers; ${them} colocou ${insight.counterpart}. Como suporte, visão é parte do seu trabalho.`;
        case 'deathsInEnemyJungle':
            return `Você morreu ${insight.value} vezes na selva inimiga (${them}: ${insight.counterpart ?? 0} na sua). Entrar lá sem visão ou sem o time costuma custar caro.`;
        case 'shortObserverLife':
            return `Seus observers duraram em média ${lifetime(insight.value)} (o máximo é 6:00): o inimigo os encontrou cedo. Evite lugares óbvios e coloque sentries junto.`;
        case 'lowFightPresence':
            return `Você esteve perto de ${percent(insight.value)} das lutas do time (${them}: ${percent(insight.counterpart)}). Fique mais perto do time quando uma luta é provável.`;
        case 'quickRedeaths':
            return `Você morreu ${insight.value} vezes logo depois de voltar (até 1:30 depois de renascer). Antes de voltar para a luta, espere o time ou recupere o mapa.`;
        case 'lostLaneTrades':
            return `Na fase de rotas você recebeu ${formatNumber(insight.value)} de dano de heróis (${them}: ${formatNumber(insight.counterpart)}) e causou ${formatNumber(stats.player.laneDamageDealt)}. Você perdeu as trocas: cuidado com o posicionamento perto dos creeps inimigos.`;
        case 'lowObserverUptime':
            return `Seu time teve observer no mapa em ${percent(insight.value)} da partida; o inimigo, em ${percent(insight.counterpart)}. Como suporte, mantenha sempre um observer ativo.`;
        case 'gapGrewAfterLane':
            return `Aos 20:00 você tinha ${formatNumber(insight.value)} de patrimônio contra ${formatNumber(insight.counterpart)} de ${them}, e a diferença cresceu depois da fase de rotas. Reveja onde você farmou entre 10 e 20 minutos.`;
        default:
            return null;
    }
}

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
    {
        title: 'Movimento',
        rows: [
            { key: 'estimatedMovementDistance', label: 'Deslocamento estimado', format: value => `${formatNumber(Math.round(value))} unidades de mapa` },
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

function movementTotal(movement) {
    return movement
        ? Object.values(movement).reduce((total, value) => total + value, 0)
        : null;
}

// Where the time went: the player and the enemy of the same role, region by region, on one scale (0-100%).
// Colour only tells the two apart (legend); the values are written.
function RegionChart({ player, counterpart, counterpartName, title, note }) {
    if (!player || !counterpart) {
        return null;
    }

    return (
        <div className="region-chart">
            <h3>{title ?? 'Onde você passou o tempo'}</h3>
            <ul className="chart-legend">
                <li><span className="legend-swatch series-1" aria-hidden="true" /> Você</li>
                <li><span className="legend-swatch series-2" aria-hidden="true" /> {counterpartName}</li>
            </ul>
            <ol className="bar-list">
                {REGIONS.map(region => (
                    <li key={region.key} className="region-row">
                        <span className="bar-label">{region.label}</span>
                        <span className="region-bars">
                            {[{ who: 'Você', value: player[region.key], slot: 1 }, { who: counterpartName, value: counterpart[region.key], slot: 2 }].map(bar => (
                                <span key={bar.slot} className="region-bar">
                                    <span className="bar-track" aria-hidden="true">
                                        <span className={`bar-fill series-${bar.slot}`} style={{ width: `${bar.value * 100}%` }} />
                                    </span>
                                    <span className="bar-value">
                                        <span className="visually-hidden">{bar.who}: </span>{percent(bar.value)}
                                    </span>
                                </span>
                            ))}
                        </span>
                    </li>
                ))}
            </ol>
            <p className="laning-note">{note ?? (
                <>
                    Parte do tempo vivo (desde 0:00) em cada região do mapa. Rotas: uma faixa em volta do caminho de cada
                    rota; rio: a água do minimapa do jogo.
                </>
            )}</p>
        </div>
    );
}

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
        <div className="phase-deaths">
            <h3>Mortes por região e fase</h3>
            <div className="table-scroll">
                <table className="smart-stats-table phase-deaths-table">
                    <caption className="visually-hidden">
                        Mortes em cada região por fase: você, {counterpartName} e a média da partida
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Fase</th>
                            <th scope="col">Jogador</th>
                            {REGIONS.map(region => <th key={region.key} scope="col">{region.label}</th>)}
                        </tr>
                    </thead>
                    <tbody>
                        {PHASES.flatMap(phase => series.map(item => (
                            <tr key={`${phase.key}-${item.key}`}>
                                <th scope="row">{phase.label}</th>
                                <th scope="row">{item.label}</th>
                                {REGIONS.map(region => (
                                    <td key={region.key}>{cell(item.values?.[phase.key]?.[region.key], plain)}</td>
                                ))}
                            </tr>
                        )))}
                    </tbody>
                </table>
            </div>
            <p className="laning-note">
                Contagem de eventos de morte do replay por coordenada; a média considera os dez heróis da partida.
            </p>
        </div>
    );
}

function PhaseRegionCharts({ player, counterpart, counterpartName }) {
    if (!player || !counterpart) {
        return null;
    }

    return (
        <>
            {PHASES.map(phase => (
                <RegionChart
                    key={phase.key}
                    player={player[phase.key]}
                    counterpart={counterpart[phase.key]}
                    counterpartName={counterpartName}
                    title={`Tempo por região — ${phase.label}`}
                    note="Parte das amostras em que o herói estava vivo nessa fase; cada fase é normalizada separadamente."
                />
            ))}
        </>
    );
}

// Gold per minute in the minutes spent mostly in each region (net worth gained ÷ those minutes): you and the enemy of
// the same role. Only regions where one of you spent a minute; the minutes are written next to the rate.
function FarmChart({ player, counterpart, counterpartName }) {
    const rate = (summary, key) => {
        const minutes = summary?.minutesByRegion?.[key];
        return minutes > 0 ? { perMinute: summary.netWorthGainByRegion[key] / minutes, minutes } : null;
    };
    const rows = REGIONS
        .map(region => ({ region, mine: rate(player, region.key), theirs: rate(counterpart, region.key) }))
        .filter(row => row.mine || row.theirs);
    if (rows.length === 0) {
        return null;
    }

    const max = Math.max(1, ...rows.flatMap(row => [row.mine?.perMinute ?? 0, row.theirs?.perMinute ?? 0]));
    return (
        <div className="region-chart farm-chart">
            <h3>Ganho de patrimônio por região</h3>
            <ul className="chart-legend">
                <li><span className="legend-swatch series-1" aria-hidden="true" /> Você</li>
                <li><span className="legend-swatch series-2" aria-hidden="true" /> {counterpartName}</li>
            </ul>
            <ol className="bar-list">
                {rows.map(({ region, mine, theirs }) => (
                    <li key={region.key} className="region-row">
                        <span className="bar-label">{region.label}</span>
                        <span className="region-bars">
                            {[{ who: 'Você', value: mine, slot: 1 }, { who: counterpartName, value: theirs, slot: 2 }].map(bar => (
                                <span key={bar.slot} className="region-bar">
                                    <span className="bar-track" aria-hidden="true">
                                        {bar.value && (
                                            <span
                                                className={`bar-fill series-${bar.slot}`}
                                                style={{ width: `${Math.max(0, bar.value.perMinute) / max * 100}%` }}
                                            />
                                        )}
                                    </span>
                                    <span className="bar-value">
                                        <span className="visually-hidden">{bar.who}: </span>
                                        {bar.value
                                            ? `${formatNumber(Math.round(bar.value.perMinute))}/min (${bar.value.minutes} min)`
                                            : '—'}
                                    </span>
                                </span>
                            ))}
                        </span>
                    </li>
                ))}
            </ol>
            <p className="laning-note">
                Cada minuto vai para a região onde o herói passou a maior parte dele; o ganho inclui ouro de abates e o
                ouro passivo. Mostra onde o seu farm rendeu mais, não onde você deveria estar.
            </p>
        </div>
    );
}

function MovementChart({ player, counterpart, counterpartName }) {
    const playerMovement = player?.estimatedMovementByRegion;
    const counterpartMovement = counterpart?.estimatedMovementByRegion;
    if (!playerMovement && !counterpartMovement) {
        return null;
    }

    const values = [playerMovement, counterpartMovement]
        .flatMap(movement => movement ? REGIONS.map(region => movement[region.key]) : [])
        .filter(Number.isFinite);
    const max = Math.max(1, ...values);
    const series = [
        { who: 'Você', movement: playerMovement, slot: 1 },
        { who: counterpartName, movement: counterpartMovement, slot: 2 },
    ];

    return (
        <div className="region-chart movement-chart">
            <h3>Deslocamento estimado por região</h3>
            <ul className="chart-legend">
                {series.map(bar => (
                    <li key={bar.slot}>
                        <span className={`legend-swatch series-${bar.slot}`} aria-hidden="true" /> {bar.who}
                        {bar.movement && ` · ${formatNumber(Math.round(movementTotal(bar.movement)))} unidades`}
                    </li>
                ))}
            </ul>
            <ol className="bar-list">
                {REGIONS.map(region => (
                    <li key={region.key} className="region-row">
                        <span className="bar-label">{region.label}</span>
                        <span className="region-bars">
                            {series.map(bar => {
                                const value = bar.movement?.[region.key];
                                return (
                                    <span key={bar.slot} className="region-bar">
                                        <span className="bar-track" aria-hidden="true">
                                            {Number.isFinite(value) && (
                                                <span
                                                    className={`bar-fill series-${bar.slot}`}
                                                    style={{ width: `${value / max * 100}%` }}
                                                />
                                            )}
                                        </span>
                                        <span className="bar-value">
                                            <span className="visually-hidden">{bar.who}: </span>
                                            {Number.isFinite(value) ? formatNumber(Math.round(value)) : '—'}
                                        </span>
                                    </span>
                                );
                            })}
                        </span>
                    </li>
                ))}
            </ol>
            <p className="laning-note">
                Estimativa em unidades do mapa: soma a distância em linha reta entre posições consecutivas do replay
                (amostradas a cada 2 s). Curvas podem ficar menores que o trajeto real; teleportes e Blinks ficam de
                fora.
            </p>
        </div>
    );
}

const COMPARED = {
    deathsPer30: { label: 'Mortes', format: plain, per30: true },
    isolatedDeathsPer30: { label: 'Mortes sem aliado por perto', format: plain, per30: true },
    enemyJungleDeathsPer30: { label: 'Mortes na selva inimiga', format: plain, per30: true },
    aloneOnEnemySide: { label: 'Tempo sozinho no lado inimigo', format: percent },
    netWorthAt10: { label: 'Patrimônio aos 10:00', format: plain },
    lastHitsAt10: { label: 'Last hits aos 10:00', format: plain },
    netWorthAt20: { label: 'Patrimônio aos 20:00', format: plain },
    observersPer30: { label: 'Observers colocados', format: plain, per30: true },
    sentriesPer30: { label: 'Sentries colocados', format: plain, per30: true },
    observerLifetime: { label: 'Vida média dos observers', format: lifetime },
    enemyWardsDestroyedPer30: { label: 'Wards inimigos destruídos', format: plain, per30: true },
    fightPresence: { label: 'Presença nas lutas do time', format: percent },
    killParticipation: { label: 'Participação em abates', format: percent },
    quickRedeathsPer30: { label: 'Mortes logo depois de voltar', format: plain, per30: true },
    laneDamageDealt: { label: 'Dano causado em heróis até 10:00', format: plain },
    laneDamageTaken: { label: 'Dano recebido de heróis até 10:00', format: plain },
    teamObserverUptime: { label: 'Tempo com observer do time', format: percent },
    leadLostPerDeath: { label: 'Vantagem do time perdida por morte', format: value => formatNumber(Math.round(value)) },
};

const SOURCES = { pro: 'Profissionais', highRank: 'Alto nível' };

// The same limits as the OpenDota percentiles of the page (PerformanceEvaluator): the text says it, the colour helps.
function betterThanClass(share) {
    if (share >= 0.7) {
        return 'result-win';
    }
    return share <= 0.3 ? 'result-loss' : undefined;
}

// The player against other players of the same hero and role, from the comparison base (pro and high-rank matches).
function BaselineTable({ baselines, role }) {
    if (!baselines) {
        return null;
    }

    const usable = baselines.filter(baseline => baseline.metrics.length > 0);
    const small = baselines.filter(baseline => baseline.metrics.length === 0);
    const metrics = Object.keys(COMPARED).filter(key => usable.some(baseline => baseline.metrics.some(m => m.metric === key)));
    const yours = key => usable.flatMap(baseline => baseline.metrics).find(m => m.metric === key)?.value;

    return (
        <div className="baseline-comparison">
            <h3>Comparado com outros jogadores do mesmo herói como {ROLES[role] ?? `posição ${role}`}</h3>
            {usable.length > 0 && (
                <div className="table-scroll">
                    <table className="smart-stats-table">
                        <caption className="visually-hidden">Seus números contra os de outros jogadores do mesmo herói e posição</caption>
                        <thead>
                            <tr>
                                <th scope="col">Métrica</th>
                                <th scope="col">Você</th>
                                {usable.map(baseline => (
                                    <th key={baseline.source} scope="col">
                                        {SOURCES[baseline.source] ?? baseline.source} (mediana, {baseline.matches} partidas)
                                    </th>
                                ))}
                            </tr>
                        </thead>
                        <tbody>
                            {metrics.map(key => (
                                <tr key={key}>
                                    <th scope="row">{COMPARED[key].label}{COMPARED[key].per30 ? ' (por 30 min)' : ''}</th>
                                    <td>{cell(yours(key), COMPARED[key].format)}</td>
                                    {usable.map(baseline => {
                                        const metric = baseline.metrics.find(m => m.metric === key);
                                        return (
                                            <td key={baseline.source}>
                                                {metric
                                                    ? (
                                                        <>
                                                            {COMPARED[key].format(metric.median)}{' · '}
                                                            <span className={betterThanClass(metric.betterThan)}>
                                                                melhor que {percent(metric.betterThan)}
                                                            </span>
                                                        </>
                                                    )
                                                    : '—'}
                                            </td>
                                        );
                                    })}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {small.map(baseline => (
                <p key={baseline.source} className="laning-note">
                    {SOURCES[baseline.source] ?? baseline.source}: a base ainda tem {baseline.matches}{' '}
                    {baseline.matches === 1 ? 'partida' : 'partidas'} deste herói nesta posição (são precisas 10 para comparar).
                    Ela cresce sozinha com o coletor.
                </p>
            ))}
            <p className="laning-note">
                &quot;Melhor que&quot;: em quantas partidas da base você foi melhor (em mortes, dano recebido e vantagem perdida, menos é melhor). Contagens
                por 30 minutos de jogo, porque as partidas têm durações diferentes. Partidas dos últimos 60 dias; nas
                profissionais a posição é deduzida pela rota e pelo patrimônio aos 10:00.
            </p>
        </div>
    );
}

// smartStats comes from the API (null until the replay is read, without a point-of-view player, or for a match
// without role queue: then nothing shows).
function SmartStatsSection({ smartStats }) {
    if (!smartStats) {
        return null;
    }

    const { player, counterpart, matchAverage, insights } = smartStats;
    const texts = insights.map(insight => insightText(insight, smartStats)).filter(Boolean);
    const hasPhaseMetrics = Boolean(
        player.timeByRegionByPhase
        || player.deathsByRegionByPhase
        || counterpart.timeByRegionByPhase
        || counterpart.deathsByRegionByPhase
        || matchAverage.deathsByRegionByPhase,
    );

    return (
        <section className="smart-stats" aria-labelledby="smart-stats-title">
            <h2 id="smart-stats-title">Como melhorar</h2>
            <p className="laning-intro">
                Você jogou de {ROLES[smartStats.role] ?? `posição ${smartStats.role}`}. Comparação com{' '}
                {smartStats.counterpartHeroName}, a mesma posição no time inimigo, e com a média dos 10 jogadores.
            </p>

            {texts.length === 0
                ? <p className="laning-note">Nada se destacou como ponto a melhorar nesta partida.</p>
                : (
                    <ul className="insight-list">
                        {texts.map(text => <li key={text}>{text}</li>)}
                    </ul>
                )}

            <BaselineTable baselines={smartStats.baselines} role={smartStats.role} />

            <RegionChart
                player={player.timeByRegion}
                counterpart={counterpart.timeByRegion}
                counterpartName={smartStats.counterpartHeroName}
            />
            <FarmChart player={player} counterpart={counterpart} counterpartName={smartStats.counterpartHeroName} />
            <MovementChart
                player={player}
                counterpart={counterpart}
                counterpartName={smartStats.counterpartHeroName}
            />

            {hasPhaseMetrics && (
                <details className="smart-stats-phase-details">
                    <summary>Ver tempo e mortes por fase da partida</summary>
                    <p className="laning-note">
                        Divisão do relógio de jogo: começo 0–10 min, meio 10–20 min e fim a partir de 20 min.
                    </p>
                    <PhaseRegionCharts
                        player={player.timeByRegionByPhase}
                        counterpart={counterpart.timeByRegionByPhase}
                        counterpartName={smartStats.counterpartHeroName}
                    />
                    <PhaseDeathTable
                        player={player.deathsByRegionByPhase}
                        counterpart={counterpart.deathsByRegionByPhase}
                        matchAverage={matchAverage.deathsByRegionByPhase}
                        counterpartName={smartStats.counterpartHeroName}
                    />
                </details>
            )}

            <div className="table-scroll">
                <table className="smart-stats-table">
                    <caption className="visually-hidden">
                        Seus números, os de {smartStats.counterpartHeroName} e a média da partida
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Métrica</th>
                            <th scope="col">Você</th>
                            <th scope="col">{smartStats.counterpartHeroName}</th>
                            <th scope="col">Média da partida</th>
                        </tr>
                    </thead>
                    {GROUPS.map(group => (
                        <tbody key={group.title}>
                            <tr className="laning-group">
                                <th scope="rowgroup" colSpan={4}>{group.title}</th>
                            </tr>
                            {group.rows.map(row => (
                                <tr key={row.key}>
                                    <th scope="row">{row.label}</th>
                                    {[player, counterpart, matchAverage].map((summary, index) => (
                                        <td key={index}>
                                            {row.value ? (row.value(summary) ?? '—') : cell(valueOf(summary, row.key), row.format)}
                                        </td>
                                    ))}
                                </tr>
                            ))}
                        </tbody>
                    ))}
                </table>
            </div>
            <p className="laning-note">
                &quot;Sem aliado por perto&quot;: nenhum herói do seu time a menos de 1.500 unidades do mapa.
                &quot;Lado inimigo&quot;: além do rio, no lado do outro time. O tempo no lado inimigo mostra mais quem estava
                ganhando do que um erro; o tempo sozinho lá é o que indica risco.
            </p>
        </section>
    );
}

export default SmartStatsSection;
