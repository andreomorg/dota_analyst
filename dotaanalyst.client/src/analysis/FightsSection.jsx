import { formatDuration } from '../matches/format';

// The teamfights of the match (3+ hero deaths at most 15 s apart, decided by the API) and the player's part in them,
// as a list of moments (the Stitch "Momentos críticos" card).

function plural(count, one, many) {
    return `${count} ${count === 1 ? one : many}`;
}

// The text always says the result; the color only reinforces it.
function resultOf(fight, isRadiant) {
    if (!fight.winner) {
        return { label: 'Empate', tone: 'even' };
    }
    if (isRadiant === null) {
        return { label: fight.winner === 'radiant' ? 'Radiant venceu' : 'Dire venceu', tone: 'even' };
    }
    return fight.playersTeamWon
        ? { label: 'Seu time venceu', tone: 'win' }
        : { label: 'Seu time perdeu', tone: 'loss' };
}

function participation(fight) {
    const parts = [];
    if (fight.kills > 0) {
        parts.push(plural(fight.kills, 'abate', 'abates'));
    }
    if (fight.assists > 0) {
        parts.push(plural(fight.assists, 'assistência', 'assistências'));
    }
    if (fight.died) {
        parts.push('morreu');
    }
    return parts.length > 0 ? parts.join(', ') : 'não participou';
}

function tookPart(fight) {
    return fight.died || fight.kills > 0 || fight.assists > 0;
}

function timeOf(fight) {
    return fight.startSeconds === fight.endSeconds
        ? formatDuration(fight.startSeconds)
        : `${formatDuration(fight.startSeconds)}–${formatDuration(fight.endSeconds)}`;
}

// fights comes from the API (null until the replay is read: then nothing shows). isRadiant: the player's team,
// null when the page has no point-of-view player (the fights are then told by Radiant/Dire).
function FightsSection({ fights, isRadiant }) {
    if (!fights) {
        return null;
    }

    const hasPlayer = isRadiant !== null;
    const won = fights.filter(fight => fight.playersTeamWon === true).length;
    const lost = fights.filter(fight => fight.playersTeamWon === false).length;
    const joined = fights.filter(tookPart).length;

    return (
        <div className="card fights-card">
            <div className="card-head">
                <h3 id="fights-title">Lutas</h3>
                <span className="card-aside mono">{plural(fights.length, 'luta', 'lutas')}</span>
            </div>
            {fights.length === 0
                ? <p className="section-note">Nenhuma luta nesta partida (3 ou mais mortes com até 15 s entre uma e outra).</p>
                : (
                    <>
                        {hasPlayer && (
                            <p className="section-intro">
                                Seu time venceu <strong className="text-win">{won}</strong> e perdeu{' '}
                                <strong className="text-loss">{lost}</strong>; você participou de <strong>{joined}</strong>.
                            </p>
                        )}
                        <ol className="fight-list">
                            {fights.map(fight => {
                                const result = resultOf(fight, isRadiant);
                                const [mine, theirs] = isRadiant === false
                                    ? [fight.direDeaths, fight.radiantDeaths]
                                    : [fight.radiantDeaths, fight.direDeaths];
                                return (
                                    <li key={fight.startSeconds} className={`fight tone-${result.tone}${hasPlayer && tookPart(fight) ? ' is-you' : ''}`}>
                                        <div className="fight-head">
                                            <span className="fight-time mono">{timeOf(fight)}</span>
                                            <span className={`fight-result text-${result.tone}`}>{result.label}</span>
                                        </div>
                                        <p className="fight-deaths">
                                            Mortes: <span className="mono">{mine} × {theirs}</span>
                                            {' '}({hasPlayer ? 'seu time × inimigo' : 'Radiant × Dire'})
                                        </p>
                                        {hasPlayer && <p className="fight-you">Você: {participation(fight)}</p>}
                                    </li>
                                );
                            })}
                        </ol>
                        {hasPlayer && (
                            <p className="section-note">
                                3 ou mais mortes com até 15 s entre uma e outra. Assistências segundo o registro de combate do
                                replay: podem ser menos que as do placar do jogo.
                            </p>
                        )}
                    </>
                )}
        </div>
    );
}

export default FightsSection;
