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
        ],
    },
    {
        title: 'Economia',
        rows: [
            { key: 'lastHitsAt10', label: 'Last hits aos 10:00', format: plain },
            { key: 'netWorthAt10', label: 'Patrimônio aos 10:00', format: plain },
            { key: 'netWorthAt20', label: 'Patrimônio aos 20:00', format: plain },
            { key: 'netWorthShare', label: 'Parte do patrimônio do time', format: percent },
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

// Where the time went: the player and the enemy of the same role, region by region, on one scale (0-100%).
// Colour only tells the two apart (legend); the values are written.
function RegionChart({ player, counterpart, counterpartName }) {
    if (!player || !counterpart) {
        return null;
    }

    return (
        <div className="region-chart">
            <h3>Onde você passou o tempo</h3>
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
            <p className="laning-note">
                Parte do tempo vivo (desde 0:00) em cada região do mapa. Rotas: uma faixa em volta do caminho de cada
                rota; rio: a água do minimapa do jogo.
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
                &quot;Melhor que&quot;: em quantas partidas da base você foi melhor (em mortes, menos é melhor). Contagens
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
                                    <td>{cell(valueOf(player, row.key), row.format)}</td>
                                    <td>{cell(valueOf(counterpart, row.key), row.format)}</td>
                                    <td>{cell(valueOf(matchAverage, row.key), row.format)}</td>
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
