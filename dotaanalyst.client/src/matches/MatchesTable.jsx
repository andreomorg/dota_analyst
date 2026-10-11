import { formatDate, formatDuration } from './format';

function resultOf(match) {
    if (match.won === null) {
        return { label: '—', className: 'result-unknown' };
    }
    return match.won
        ? { label: 'Vitória', className: 'result-win' }
        : { label: 'Derrota', className: 'result-loss' };
}

function MatchesTable({ matches }) {
    return (
        <table className="matches-table">
            <thead>
                <tr>
                    <th>Herói</th>
                    <th>Resultado</th>
                    <th>K / D / A</th>
                    <th>Duração</th>
                    <th>Data</th>
                </tr>
            </thead>
            <tbody>
                {matches.map(match => {
                    const result = resultOf(match);
                    return (
                        <tr key={match.matchId}>
                            <td>{match.heroName}</td>
                            <td className={result.className}>{result.label}</td>
                            <td>{match.kills} / {match.deaths} / {match.assists}</td>
                            <td>{formatDuration(match.durationSeconds)}</td>
                            <td>{formatDate(match.startTime)}</td>
                        </tr>
                    );
                })}
            </tbody>
        </table>
    );
}

export default MatchesTable;
