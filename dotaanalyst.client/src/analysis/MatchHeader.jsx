import { formatDate, formatDuration, formatNumber } from '../matches/format';
import { HeroPortrait } from '../heroes/HeroImage';

// The result is from the user's point of view; without them in the match (or without a known winner) it is "—".
function resultOf(won) {
    if (won === null || won === undefined) {
        return { label: '—', className: 'result-unknown' };
    }
    return won
        ? { label: 'Vitória', className: 'result-win' }
        : { label: 'Derrota', className: 'result-loss' };
}

function winnerText(radiantWon) {
    if (radiantWon === null || radiantWon === undefined) {
        return 'Vencedor desconhecido';
    }
    return radiantWon ? 'Radiant venceu' : 'Dire venceu';
}

function MatchHeader({ analysis }) {
    const { performance } = analysis;
    const result = resultOf(performance?.won);

    return (
        <header id="match-overview" className="match-header">
            <h1>
                Partida {analysis.matchId}
                {' · '}
                <span className={result.className}>{result.label}</span>
            </h1>

            <p className="match-score">
                <span className="visually-hidden">Placar: </span>
                <span className="team-name">Radiant</span>
                {' '}
                <strong>{formatNumber(analysis.radiantScore)}</strong>
                {' × '}
                <strong>{formatNumber(analysis.direScore)}</strong>
                {' '}
                <span className="team-name">Dire</span>
            </p>

            <dl className="match-facts">
                <div>
                    <dt>Resultado</dt>
                    <dd>{winnerText(analysis.radiantWon)}</dd>
                </div>
                <div>
                    <dt>Duração</dt>
                    <dd>{formatDuration(analysis.durationSeconds)}</dd>
                </div>
                <div>
                    <dt>Data</dt>
                    <dd>{formatDate(analysis.startTime)}</dd>
                </div>
                {performance && (
                    <div>
                        <dt>Seu herói</dt>
                        <dd className="match-hero">
                            <HeroPortrait heroId={performance.heroId} />
                            <span>{performance.heroName} ({performance.isRadiant ? 'Radiant' : 'Dire'})</span>
                        </dd>
                    </div>
                )}
            </dl>
        </header>
    );
}

export default MatchHeader;
