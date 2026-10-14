import { formatDuration } from '../matches/format';
import { objectiveFullName } from './objectives';
import TabIcon from './TabIcon';

// The teamfights of the match (3+ hero deaths at most 15 s apart, decided by the API) and the player's part in them
// (Stitch reference "Lutas e objetivos"): a timeline with every fight, Roshan and building, then one card per fight.

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

// Fights, Roshans and buildings on one line over the match's minutes; every mark has its text for screen readers
// and a tooltip, and everything is also in the cards and the objectives' log below.
function EventStrip({ fights, objectives, durationSeconds, isRadiant }) {
    const end = Math.max(1, durationSeconds);
    const left = seconds => `${Math.min(100, Math.max(0, (seconds / end) * 100))}%`;
    const ticks = [];
    for (let minute = 0; minute * 60 < end - 120; minute += end > 2700 ? 10 : 5) {
        ticks.push(minute * 60);
    }
    const buildings = (objectives ?? []).filter(objective => objective.kind === 'tower' || objective.kind === 'barracks' || objective.kind === 'ancient');
    const roshans = (objectives ?? []).filter(objective => objective.kind === 'roshan' || objective.kind === 'tormentor');

    return (
        <div className="event-strip">
            <div className="event-axis mono" aria-hidden="true">
                {ticks.map(second => <span key={second} style={{ left: left(second) }}>{formatDuration(second)}</span>)}
                <span className="event-axis-end">{formatDuration(durationSeconds)} fim</span>
            </div>
            <ul className="event-marks">
                {buildings.map((objective, index) => (
                    <li
                        key={`b-${index}`}
                        className={`event-mark is-building ${objective.isPlayersTeam === false ? 'is-loss' : 'is-win'}`}
                        style={{ left: left(objective.seconds) }}
                        title={`${formatDuration(objective.seconds)} ${objectiveFullName(objective)}`}
                    >
                        <span className="visually-hidden">{formatDuration(objective.seconds)}: {objectiveFullName(objective)}</span>
                    </li>
                ))}
                {fights.map((fight, index) => {
                    const result = resultOf(fight, isRadiant);
                    return (
                        <li
                            key={`f-${fight.startSeconds}`}
                            className={`event-mark is-fight is-${result.tone}`}
                            style={{ left: left(fight.startSeconds) }}
                            title={`Luta ${index + 1} · ${formatDuration(fight.startSeconds)} · ${result.label}`}
                        >
                            <TabIcon tab="fights" className="event-icon" />
                            <span className="visually-hidden">Luta {index + 1} aos {formatDuration(fight.startSeconds)}: {result.label}</span>
                        </li>
                    );
                })}
                {roshans.map((objective, index) => (
                    <li
                        key={`r-${index}`}
                        className="event-mark is-roshan"
                        style={{ left: left(objective.seconds) }}
                        title={`${formatDuration(objective.seconds)} ${objectiveFullName(objective)}${objective.aegisHeroName ? ` · Aegis: ${objective.aegisHeroName}` : ''}`}
                    >
                        <TabIcon tab="star" className="event-icon" />
                        <span className="visually-hidden">{formatDuration(objective.seconds)}: {objectiveFullName(objective)}</span>
                    </li>
                ))}
            </ul>
        </div>
    );
}

// fights comes from the API (null until the replay is read: then nothing shows). isRadiant: the player's team,
// null when the page has no point-of-view player (the fights are then told by Radiant/Dire).
function FightsSection({ fights, objectives, durationSeconds, isRadiant }) {
    if (!fights) {
        return null;
    }

    const hasPlayer = isRadiant !== null;

    return (
        <section className="card fights-card" aria-labelledby="fights-title">
            <div className="card-split-head">
                <h2 id="fights-title" className="card-heading">
                    <TabIcon tab="trend" className="section-icon" />
                    Linha do tempo das lutas e objetivos
                </h2>
                <ul className="timeline-key mono">
                    <li className="text-win"><span className="key-dot" aria-hidden="true" /> Luta vencida</li>
                    <li className="text-loss"><span className="key-dot" aria-hidden="true" /> Luta perdida</li>
                    <li className="text-you"><span className="key-dot" aria-hidden="true" /> Roshan / Tormentor</li>
                    <li><span className="key-square" aria-hidden="true" /> Construção</li>
                </ul>
            </div>
            {fights.length === 0
                ? <p className="quiet-note">Nenhuma luta nesta partida (3 ou mais mortes com até 15 s entre uma e outra).</p>
                : (
                    <>
                        <EventStrip fights={fights} objectives={objectives} durationSeconds={durationSeconds} isRadiant={isRadiant} />
                        <ol className="fight-grid">
                            {fights.map((fight, index) => {
                                const result = resultOf(fight, isRadiant);
                                const [mine, theirs] = isRadiant === false
                                    ? [fight.direDeaths, fight.radiantDeaths]
                                    : [fight.radiantDeaths, fight.direDeaths];
                                return (
                                    <li key={fight.startSeconds} className={`fight-card tone-${result.tone}${hasPlayer && tookPart(fight) ? ' is-you' : ''}`}>
                                        <div className="fight-card-head">
                                            <span className={`fight-tag mono tag-${result.tone}`}>Luta {index + 1} · {formatDuration(fight.startSeconds)}</span>
                                            <span className={`fight-result mono text-${result.tone}`}>{result.label}</span>
                                        </div>
                                        <p className="fight-line">
                                            <TabIcon tab="skull" className="fight-icon" />
                                            Mortes: <strong className="mono">{mine} × {theirs}</strong>
                                            <span className="fight-muted">({hasPlayer ? 'seu time × inimigo' : 'Radiant × Dire'})</span>
                                        </p>
                                        <p className="fight-line">
                                            <TabIcon tab="trend" className="fight-icon" />
                                            Duração: <span className="mono">{fight.endSeconds - fight.startSeconds} s</span>
                                            <span className="fight-muted mono">({formatDuration(fight.startSeconds)}–{formatDuration(fight.endSeconds)})</span>
                                        </p>
                                        {hasPlayer && (
                                            <p className="fight-footer">
                                                <span>Você: <strong>{participation(fight)}</strong></span>
                                            </p>
                                        )}
                                    </li>
                                );
                            })}
                        </ol>
                        {hasPlayer && (
                            <p className="section-note">
                                Luta = 3 ou mais mortes com até 15 s entre uma e outra; vence o time com menos mortes. Assistências segundo o
                                registro de combate do replay: podem ser menos que as do placar do jogo.
                            </p>
                        )}
                    </>
                )}
        </section>
    );
}

export default FightsSection;
