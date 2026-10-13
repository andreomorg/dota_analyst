import { Link } from 'react-router';
import { formatDate, formatDuration } from './format';
import { HeroIcon } from '../heroes/HeroImage';

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
        <>
            <div className="table-scroll matches-table-scroll">
                <table className="matches-table">
                    <caption className="visually-hidden">Partidas recentes e resultados</caption>
                    <thead>
                        <tr>
                            <th scope="col">Herói</th>
                            <th scope="col">Resultado</th>
                            <th scope="col">K / D / A</th>
                            <th scope="col">Duração</th>
                            <th scope="col">Data</th>
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
                                        <Link
                                            className="hero-link"
                                            to={`/matches/${match.matchId}?player=${encodeURIComponent(accountId)}`}
                                        >
                                            <HeroIcon heroId={match.heroId} />
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
            <div className="matches-cards" aria-label="Partidas recentes">
                {matches.map(match => {
                    const result = resultOf(match);
                    return (
                        <Link
                            key={match.matchId}
                            className="match-card"
                            to={`/matches/${match.matchId}?player=${encodeURIComponent(accountId)}`}
                        >
                            <span className="match-card-header">
                                <span className="match-card-hero">
                                    <HeroIcon heroId={match.heroId} />
                                    {match.heroName}
                                </span>
                                <span className={`match-result ${result.className}`}>{result.label}</span>
                            </span>
                            <span className="match-card-stats">
                                <span>
                                    <span className="match-card-label">K / D / A</span>
                                    <span className="match-card-value">{match.kills} / {match.deaths} / {match.assists}</span>
                                </span>
                                <span>
                                    <span className="match-card-label">Duração</span>
                                    <span className="match-card-value">{formatDuration(match.durationSeconds)}</span>
                                </span>
                                <span>
                                    <span className="match-card-label">Data</span>
                                    <span className="match-card-value">{formatDate(match.startTime)}</span>
                                </span>
                            </span>
                            <span className="visually-hidden"> — ver análise da partida {match.matchId}</span>
                        </Link>
                    );
                })}
            </div>
        </>
    );
}

export default MatchesTable;
