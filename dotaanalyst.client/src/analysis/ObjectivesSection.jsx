import { formatDuration } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { objectiveFullName, teamName } from './objectives';

// Towers, barracks, Ancient, Roshan and Tormentor of both teams, read from the replay (the same objectives are
// marked on the lead chart of TimelineSection).

const COUNTED = [
    { kind: 'tower', label: 'Torres' },
    { kind: 'barracks', label: 'Barracks' },
    { kind: 'roshan', label: 'Roshan' },
    { kind: 'tormentor', label: 'Tormentor' },
];

// "Seu time" / "Inimigos" with a point-of-view player, else the team's name.
function takerName(objective) {
    if (objective.isPlayersTeam === true) {
        return 'Seu time';
    }
    if (objective.isPlayersTeam === false) {
        return 'Inimigos';
    }
    return teamName(objective.takenBy);
}

function Summary({ objectives }) {
    const hasPlayer = objectives.some(objective => objective.isPlayersTeam !== null && objective.isPlayersTeam !== undefined);
    const sides = hasPlayer
        ? [{ label: 'Seu time', test: o => o.isPlayersTeam === true }, { label: 'Inimigos', test: o => o.isPlayersTeam === false }]
        : [{ label: 'Radiant', test: o => o.takenBy === 'radiant' }, { label: 'Dire', test: o => o.takenBy === 'dire' }];

    // Denied buildings don't count as taken from the enemy.
    const rows = COUNTED
        .map(({ kind, label }) => ({
            label,
            counts: sides.map(side => objectives.filter(o => o.kind === kind && !o.denied && side.test(o)).length),
        }))
        .filter(row => row.counts.some(count => count > 0));

    return (
        <dl className="fact-row">
            {rows.map(row => (
                <div key={row.label} className="tile">
                    <dt className="stat-label">{row.label}</dt>
                    <dd className="objective-count">
                        <span className="mono text-win">{row.counts[0]}</span>
                        <span className="objective-vs">{sides[0].label} × {sides[1].label}</span>
                        <span className="mono text-loss">{row.counts[1]}</span>
                    </dd>
                </div>
            ))}
        </dl>
    );
}

// objectives comes from the API (null until the replay is read: then nothing shows).
function ObjectivesSection({ objectives }) {
    if (!objectives) {
        return null;
    }

    return (
        <section className="section" aria-labelledby="objectives-title">
            <SectionHead id="objectives-title" title="Objetivos" />
            <div className="card">
                {objectives.length === 0
                    ? <p className="section-note">Nenhuma torre, barracks ou Roshan caiu nesta partida.</p>
                    : (
                        <>
                            <Summary objectives={objectives} />
                            <details className="inline-details">
                                <summary>Ver os {objectives.length} objetivos em ordem</summary>
                                <div className="table-scroll">
                                    <table className="data-table">
                                        <caption className="visually-hidden">Objetivos da partida, em ordem</caption>
                                        <thead>
                                            <tr>
                                                <th scope="col">Tempo</th>
                                                <th scope="col">Objetivo</th>
                                                <th scope="col">Quem conquistou</th>
                                                <th scope="col">Último golpe</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {objectives.map((objective, index) => (
                                                <tr key={`${objective.seconds}-${index}`}>
                                                    <th scope="row" className="mono">{formatDuration(objective.seconds)}</th>
                                                    <td>
                                                        {objectiveFullName(objective)}
                                                        {objective.aegisHeroName && ` (Aegis: ${objective.aegisHeroName})`}
                                                    </td>
                                                    <td>
                                                        <span className={objective.isPlayersTeam === false ? 'text-loss' : objective.isPlayersTeam ? 'text-win' : undefined}>
                                                            {takerName(objective)}
                                                        </span>
                                                        {objective.denied && ' (negada)'}
                                                    </td>
                                                    <td>{objective.killerHeroName ?? 'Creeps ou outra unidade'}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            </details>
                        </>
                    )}
            </div>
        </section>
    );
}

export default ObjectivesSection;
