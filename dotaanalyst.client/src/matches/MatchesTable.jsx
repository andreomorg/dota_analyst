import { Link } from 'react-router';
import { formatDate, formatDuration } from './format';

function resultOf(match) {
    if (match.won === null) {
        return { label: '—', className: 'result-unknown' };
    }
    return match.won
        ? { label: 'Vitória', className: 'result-win' }
        : { label: 'Derrota', className: 'result-loss' };
}

function MatchesTable({ matches, accountId }) {
    return (
        <div className="table-scroll">
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
                                <td>
                                    {/* A real link: reachable with Tab, opens with Enter, works with "open in new tab". */}
                                    {/* ?player= tells the analysis page whose matches these are (may not be the logged-in */}
                                    {/* user's). In the URL, not in router state, so it survives a new tab or a shared link. */}
                                    <Link to={`/matches/${match.matchId}?player=${encodeURIComponent(accountId)}`}>
                                        {match.heroName}
                                        <span className="visually-hidden"> — ver análise da partida</span>
                                    </Link>
                                </td>
                                <td className={result.className}>{result.label}</td>
                                <td>{match.kills} / {match.deaths} / {match.assists}</td>
                                <td>{formatDuration(match.durationSeconds)}</td>
                                <td>{formatDate(match.startTime)}</td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}

export default MatchesTable;
