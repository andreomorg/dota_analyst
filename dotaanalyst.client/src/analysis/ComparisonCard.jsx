import { useState } from 'react';
import { formatDuration, formatNumber } from '../matches/format';
import Standing from '../ui/Standing';
import { lifetime, percent, plain, ROLES } from './insights';
import { metricLabel } from './metrics';

// One card for every comparison of the player with other players of the same hero: OpenDota's percentiles (all of
// the hero's matches), our base of high-rank and pro matches (same hero and role) and the lane matchup (same hero
// against the same lane opponent). A segmented control picks the group; every group shows the same row: the metric,
// the player's value, the typical value (when the source has it) and the verdict (ui/Standing).

// lower: less is better (the API already turns the comparison around; the label tells the reader).
const COMPARED = {
    deathsPer30: { label: 'Mortes', format: plain, per30: true, lower: true },
    isolatedDeathsPer30: { label: 'Mortes sem aliado por perto', format: plain, per30: true, lower: true },
    enemyJungleDeathsPer30: { label: 'Mortes na selva inimiga', format: plain, per30: true, lower: true },
    aloneOnEnemySide: { label: 'Tempo sozinho no lado inimigo', format: percent, lower: true },
    netWorthAt10: { label: 'Patrimônio aos 10:00', format: plain },
    lastHitsAt10: { label: 'Last hits aos 10:00', format: plain },
    netWorthAt20: { label: 'Patrimônio aos 20:00', format: plain },
    firstBigItemSeconds: { label: 'Primeiro item grande', format: value => formatDuration(Math.round(value)), lower: true },
    laneDamageDealt: { label: 'Dano causado em heróis até 10:00', format: plain },
    laneDamageTaken: { label: 'Dano recebido de heróis até 10:00', format: plain, lower: true },
    fightPresence: { label: 'Presença nas lutas do time', format: percent },
    killParticipation: { label: 'Participação em abates', format: percent },
    quickRedeathsPer30: { label: 'Mortes logo depois de voltar', format: plain, per30: true, lower: true },
    leadLostPerDeath: { label: 'Vantagem do time perdida por morte', format: value => formatNumber(Math.round(value)), lower: true },
    observersPer30: { label: 'Observers colocados', format: plain, per30: true },
    sentriesPer30: { label: 'Sentries colocados', format: plain, per30: true },
    observerLifetime: { label: 'Vida média dos observers', format: lifetime },
    enemyWardsDestroyedPer30: { label: 'Wards inimigos destruídos', format: plain, per30: true },
    teamObserverUptime: { label: 'Tempo com observer do time', format: percent },
};

const SOURCES = { highRank: 'Alto nível', pro: 'Profissionais' };

// The groups the player can be compared with, in order; each with its rows, or a note when it is too small.
function groupsOf(performance, smartStats, heroName) {
    const groups = [];
    if (performance?.metrics.length > 0) {
        groups.push({
            key: 'opendota',
            label: 'Todas as partidas',
            hint: 'OpenDota',
            who: `jogadores de ${heroName}`,
            typical: false,
            rows: performance.metrics.map(metric => ({
                key: metric.metric,
                label: metricLabel(metric.metric),
                you: formatNumber(metric.value),
                share: metric.percentile,
            })),
        });
    }

    const base = (baselines, prefix, who) => (baselines ?? []).forEach(baseline => {
        const rows = Object.keys(COMPARED)
            .map(key => ({ key, metric: baseline.metrics.find(m => m.metric === key) }))
            .filter(entry => entry.metric)
            .map(({ key, metric }) => ({
                key,
                label: `${COMPARED[key].label}${COMPARED[key].per30 ? ' (por 30 min)' : ''}`,
                lower: COMPARED[key].lower,
                you: COMPARED[key].format(metric.value),
                typical: COMPARED[key].format(metric.median),
                share: metric.betterThan,
            }));
        groups.push({
            key: `${prefix}${baseline.source}`,
            label: `${prefix ? 'Contra ' + smartStats.laneMatchupHeroName + ' · ' : ''}${SOURCES[baseline.source] ?? baseline.source}`,
            hint: `${baseline.matches} ${baseline.matches === 1 ? 'partida' : 'partidas'}`,
            who,
            typical: true,
            rows,
            tooFew: rows.length === 0 ? baseline.matches : null,
        });
    });
    base(smartStats?.baselines, '', `jogadores de ${heroName}`);
    if (smartStats?.laneMatchupHeroName) {
        base(smartStats.laneMatchupBaselines, 'matchup-', `jogadores de ${heroName} contra ${smartStats.laneMatchupHeroName}`);
    }
    return groups;
}

function ComparisonCard({ performance, smartStats }) {
    const heroName = performance?.heroName ?? 'seu herói';
    const all = groupsOf(performance, smartStats, heroName);
    // A group with no match at all isn't worth a button: it becomes a line under the table.
    const groups = all.filter(group => group.tooFew !== 0);
    const empty = all.filter(group => group.tooFew === 0);
    // Start on the richest group of our base, else OpenDota's.
    const preferred = groups.find(group => group.key === 'highRank' && group.rows.length > 0)
        ?? groups.find(group => group.rows.length > 0);
    const [selectedKey, setSelectedKey] = useState(null);
    const selected = groups.find(group => group.key === selectedKey) ?? preferred ?? groups[0];
    if (!selected) {
        return null;
    }

    const role = smartStats ? ` como ${ROLES[smartStats.role] ?? `posição ${smartStats.role}`}` : '';
    return (
        <div className="card comparison-card">
            <div className="card-head">
                <h3>Você comparado com outros jogadores de {heroName}{role}</h3>
            </div>
            <div className="segmented comparison-groups" role="group" aria-label="Comparar com">
                {groups.map(group => (
                    <button key={group.key} type="button" aria-pressed={group.key === selected.key} onClick={() => setSelectedKey(group.key)}>
                        {group.label}
                        <span className="segmented-hint">{group.hint}</span>
                    </button>
                ))}
            </div>

            {selected.rows.length === 0
                ? (
                    <p className="section-note">
                        A base ainda tem {selected.tooFew} {selected.tooFew === 1 ? 'partida' : 'partidas'} destes{' '}
                        {selected.who}; são precisas 10 para comparar. Ela cresce sozinha com o coletor.
                    </p>
                )
                : (
                    <div className="table-scroll">
                        <table className="data-table baseline-table">
                            <caption className="visually-hidden">
                                Seus números comparados com {selected.who} ({selected.label})
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Métrica</th>
                                    <th scope="col" className="num-col">Você</th>
                                    {selected.typical && <th scope="col" className="num-col">Típico</th>}
                                    <th scope="col">Como você ficou</th>
                                </tr>
                            </thead>
                            <tbody>
                                {selected.rows.map(row => (
                                    <tr key={row.key}>
                                        <th scope="row">
                                            {row.label}
                                            {row.lower && <span className="indicator-hint">menos é melhor</span>}
                                        </th>
                                        <td className="num-col mono">{row.you}</td>
                                        {selected.typical && <td className="num-col mono">{row.typical}</td>}
                                        <td><Standing share={row.share} who={selected.who} /></td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            {empty.length > 0 && (
                <p className="section-note">
                    Ainda sem partidas na base para: {empty.map(group => group.label).join(', ')}. Ela cresce sozinha com o coletor.
                </p>
            )}
            <p className="section-note">
                &quot;Típico&quot; é o valor do meio entre os {selected.who} do grupo escolhido. &quot;Todas as partidas&quot; vem
                do OpenDota (qualquer nível); &quot;Alto nível&quot; e &quot;Profissionais&quot; vêm da nossa base, dos últimos 60
                dias, só com a mesma posição. Contagens por 30 minutos de jogo, porque as partidas têm durações diferentes.
            </p>
        </div>
    );
}

export default ComparisonCard;
