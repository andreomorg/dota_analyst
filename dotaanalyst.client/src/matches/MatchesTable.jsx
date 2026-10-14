import { Link } from 'react-router';
import { formatCompactDate, formatDateTime, formatDuration } from './format';
import { HeroPortrait } from '../heroes/HeroImage';
import { ResultChip } from '../ui/Chips';
import Kda from '../ui/Kda';

// The recent matches as rows of cards (the Stitch home screen): hero, result, K/D/A, duration and date. Each row
// is one real link (Tab, Enter, "open in new tab"); the column titles are only visual, every value also has a
// hidden label for screen readers.
function MatchesTable({ matches, accountId }) {
    return (
        <section className="match-list-section" aria-label="Partidas recentes">
            <div className="match-list-mobile-head">
                <h2>Partidas recentes</h2>
                <span>{matches.length} exibidas</span>
            </div>
            <div className="match-list-head" aria-hidden="true">
                <span>Herói</span>
                <span>Resultado</span>
                <span>K / D / A</span>
                <span>Duração</span>
                <span>Data</span>
            </div>
            <ol className="match-list">
                {matches.map(match => (
                    <li key={match.matchId}>
                        {/* ?player= tells the analysis page whose matches these are (may not be the logged-in */}
                        {/* user's). In the URL, not in router state, so it survives a new tab or a shared link. */}
                        <Link className="match-row" to={`/matches/${match.matchId}?player=${encodeURIComponent(accountId)}`}>
                            <span className="match-row-hero">
                                <HeroPortrait heroId={match.heroId} />
                                <span className="match-row-names">
                                    <strong>{match.heroName}</strong>
                                    <span>{match.isRadiant ? 'Radiant' : 'Dire'} • <span className="mono">#{match.matchId}</span></span>
                                </span>
                            </span>
                            <span className="match-row-result">
                                <span className="visually-hidden">Resultado: </span>
                                <ResultChip won={match.won} />
                            </span>
                            <span className="match-row-kda">
                                <span className="match-cell-label" aria-hidden="true">K / D / A</span>
                                <span className="visually-hidden">Abates, mortes e assistências: </span>
                                <Kda kills={match.kills} deaths={match.deaths} assists={match.assists} />
                            </span>
                            <span className="match-row-duration">
                                <span className="match-cell-label" aria-hidden="true">Duração</span>
                                <span className="visually-hidden">Duração: </span>
                                <span className="match-duration-value mono"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8" /><path d="M12 8v4l3 2" /></svg>{formatDuration(match.durationSeconds)}</span>
                            </span>
                            <span className="match-row-date">
                                <span className="match-cell-label" aria-hidden="true">Data</span>
                                <span className="visually-hidden">Data: </span>
                                <time className="match-date-full" dateTime={match.startTime}>{formatDateTime(match.startTime)}</time>
                                <time className="match-date-mobile" dateTime={match.startTime} title={formatDateTime(match.startTime)}>{formatCompactDate(match.startTime)}</time>
                            </span>
                        </Link>
                    </li>
                ))}
            </ol>
        </section>
    );
}

export default MatchesTable;
