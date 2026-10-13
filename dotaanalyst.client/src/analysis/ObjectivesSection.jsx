import { formatDuration } from '../matches/format';
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
        ? [{ label: 'seu time', test: o => o.isPlayersTeam === true }, { label: 'inimigos', test: o => o.isPlayersTeam === false }]
        : [{ label: 'Radiant', test: o => o.takenBy === 'radiant' }, { label: 'Dire', test: o => o.takenBy === 'dire' }];

    // Denied buildings don't count as taken from the enemy.
    const rows = COUNTED
        .map(({ kind, label }) => ({
            label,
            counts: sides.map(side => objectives.filter(o => o.kind === kind && !o.denied && side.test(o)).length),
        }))
        .filter(row => row.counts.some(count => count > 0));

    return (
        <dl className="match-facts objective-facts">
            {rows.map(row => (
                <div key={row.label}>
                    <dt>{row.label}</dt>
                    <dd>{sides.map((side, i) => `${side.label} ${row.counts[i]}`).join(' × ')}</dd>
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
        <section className="objectives-section" aria-labelledby="objectives-title">
            <h2 id="objectives-title">Objetivos</h2>
            {objectives.length === 0
                ? <p className="laning-note">Nenhuma torre, barracks ou Roshan caiu nesta partida.</p>
                : (
                    <>
                        <Summary objectives={objectives} />
                        <div className="table-scroll">
                            <table className="objectives-table">
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
                                            <th scope="row">{formatDuration(objective.seconds)}</th>
                                            <td>
                                                {objectiveFullName(objective)}
                                                {objective.aegisHeroName && ` (Aegis: ${objective.aegisHeroName})`}
                                            </td>
                                            <td>
                                                <span className={objective.isPlayersTeam === false ? 'result-loss' : objective.isPlayersTeam ? 'result-win' : undefined}>
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
                    </>
                )}
        </section>
    );
}

export default ObjectivesSection;
