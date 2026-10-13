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

function cell(value, format) {
    return value === null || value === undefined ? '—' : format(value);
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
                                    <td>{cell(player[row.key], row.format)}</td>
                                    <td>{cell(counterpart[row.key], row.format)}</td>
                                    <td>{cell(matchAverage[row.key], row.format)}</td>
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
