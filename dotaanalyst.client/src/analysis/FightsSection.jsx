import { formatDuration } from '../matches/format';

// The teamfights of the match (3+ hero deaths at most 15 s apart, decided by the API) and the player's part in them.

function plural(count, one, many) {
    return `${count} ${count === 1 ? one : many}`;
}

// The text always says the result; the color only reinforces it.
function resultOf(fight, isRadiant) {
    if (!fight.winner) {
        return { label: 'Empate', className: 'result-even' };
    }
    if (isRadiant === null) {
        return { label: fight.winner === 'radiant' ? 'Radiant venceu' : 'Dire venceu', className: 'result-even' };
    }
    return fight.playersTeamWon
        ? { label: 'Seu time venceu', className: 'result-win' }
        : { label: 'Seu time perdeu', className: 'result-loss' };
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
    return parts.length > 0 ? parts.join(', ') : '—';
}

function tookPart(fight) {
    return fight.died || fight.kills > 0 || fight.assists > 0;
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
        <section className="fights-section" aria-labelledby="fights-title">
            <h2 id="fights-title">Lutas</h2>
            {fights.length === 0
                ? <p className="laning-note">Nenhuma luta nesta partida (3 ou mais mortes com até 15 s entre uma e outra).</p>
                : (
                    <>
                        <p className="laning-intro">
                            {plural(fights.length, 'luta', 'lutas')} (3 ou mais mortes com até 15 s entre uma e outra).
                            {hasPlayer && <> Seu time venceu {won} e perdeu {lost}; você participou de {joined}.</>}
                        </p>
                        <div className="table-scroll">
                            <table className="fights-table">
                                <caption className="visually-hidden">Lutas da partida, em ordem</caption>
                                <thead>
                                    <tr>
                                        <th scope="col">Tempo</th>
                                        <th scope="col">{hasPlayer ? 'Mortes (seu time × inimigo)' : 'Mortes (Radiant × Dire)'}</th>
                                        <th scope="col">Resultado</th>
                                        {hasPlayer && <th scope="col">Você</th>}
                                    </tr>
                                </thead>
                                <tbody>
                                    {fights.map(fight => {
                                        const result = resultOf(fight, isRadiant);
                                        const [mine, theirs] = isRadiant === false
                                            ? [fight.direDeaths, fight.radiantDeaths]
                                            : [fight.radiantDeaths, fight.direDeaths];
                                        return (
                                            <tr key={fight.startSeconds} className={hasPlayer && tookPart(fight) ? 'current-player' : undefined}>
                                                <th scope="row">
                                                    {fight.startSeconds === fight.endSeconds
                                                        ? formatDuration(fight.startSeconds)
                                                        : `${formatDuration(fight.startSeconds)}–${formatDuration(fight.endSeconds)}`}
                                                </th>
                                                <td>{mine} × {theirs}</td>
                                                <td><span className={result.className}>{result.label}</span></td>
                                                {hasPlayer && <td>{participation(fight)}</td>}
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                        {hasPlayer && (
                            <p className="laning-note">
                                Assistências segundo o registro de combate do replay: podem ser menos que as do placar do jogo.
                            </p>
                        )}
                    </>
                )}
        </section>
    );
}

export default FightsSection;
