import { formatDuration } from '../matches/format';
import { objectiveFullName, teamName } from './objectives';
import TabIcon from './TabIcon';

// Towers, barracks, Ancient, Roshan and Tormentor of both teams, read from the replay (Stitch reference "Lutas e
// objetivos"): four summary cards at the top of the tab and the full log in order. The same objectives are marked on
// the lead chart of TimelineSection and on the fights' timeline.

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

function sidesOf(objectives) {
    const hasPlayer = objectives.some(objective => objective.isPlayersTeam !== null && objective.isPlayersTeam !== undefined);
    return hasPlayer
        ? [{ label: 'seu time', test: o => o.isPlayersTeam === true }, { label: 'inimigos', test: o => o.isPlayersTeam === false }]
        : [{ label: 'Radiant', test: o => o.takenBy === 'radiant' }, { label: 'Dire', test: o => o.takenBy === 'dire' }];
}

function SummaryCard({ title, icon, children }) {
    return (
        <div className="card summary-card">
            <p className="summary-card-title">
                {title}
                <TabIcon tab={icon} className="summary-card-icon" />
            </p>
            {children}
        </div>
    );
}

function plural(count, one, many) {
    return `${count} ${count === 1 ? one : many}`;
}

// The four cards at the top of "Lutas e objetivos". objectives/fights come from the API (null until the replay is
// read); a card without data says so instead of a zero it doesn't know.
export function ObjectiveCards({ objectives, fights }) {
    if (!objectives && !fights) {
        return null;
    }
    const list = objectives ?? [];
    const sides = sidesOf(list);
    // Denied buildings don't count as taken from the enemy.
    const count = (kind, side) => list.filter(o => o.kind === kind && !o.denied && side.test(o)).length;
    const towers = sides.map(side => count('tower', side));
    const towerTotal = towers[0] + towers[1];
    const roshans = list.filter(o => o.kind === 'roshan');
    const tormentors = list.filter(o => o.kind === 'tormentor');
    const won = fights?.filter(fight => fight.playersTeamWon === true).length ?? 0;
    const joined = fights?.filter(fight => fight.died || fight.kills > 0 || fight.assists > 0).length ?? 0;

    return (
        <div className="summary-cards">
            {objectives && (
                <SummaryCard title="Torres derrubadas" icon="flag">
                    <p className="summary-card-value">
                        <strong className="mono text-win">{towers[0]}</strong> <span>{sides[0].label}</span>
                        <span className="summary-card-sep" aria-hidden="true">/</span>
                        <strong className="mono text-loss">{towers[1]}</strong> <span>{sides[1].label}</span>
                    </p>
                    {towerTotal > 0 && (
                        <span className="split-bar" aria-hidden="true">
                            <span style={{ width: `${(towers[0] / towerTotal) * 100}%` }} />
                        </span>
                    )}
                </SummaryCard>
            )}
            {objectives && (
                <SummaryCard title="Roshan" icon="star">
                    <p className="summary-card-value">
                        <strong className="mono">{roshans.length}</strong> <span>{roshans.length === 1 ? 'abate' : 'abates'}</span>
                    </p>
                    {roshans.length === 0
                        ? <p className="summary-card-line">Ninguém matou o Roshan.</p>
                        : (
                            <ul className="summary-card-list">
                                {roshans.map((roshan, index) => (
                                    <li key={index}>
                                        <span className="mono">{formatDuration(roshan.seconds)}</span>{' '}
                                        <span className={roshan.isPlayersTeam === false ? 'text-loss' : roshan.isPlayersTeam ? 'text-win' : undefined}>
                                            {takerName(roshan)}
                                        </span>
                                        {roshan.aegisHeroName && <> · Aegis: <strong className="text-you">{roshan.aegisHeroName}</strong></>}
                                    </li>
                                ))}
                            </ul>
                        )}
                </SummaryCard>
            )}
            {objectives && (
                <SummaryCard title="Tormentor" icon="positioning">
                    <p className="summary-card-value">
                        <strong className="mono">{tormentors.length}</strong> <span>{tormentors.length === 1 ? 'abate' : 'abates'}</span>
                    </p>
                    <p className="summary-card-line">
                        {tormentors.length === 0
                            ? 'Nenhum time matou o Tormentor.'
                            : sides.map(side => `${tormentors.filter(side.test).length} ${side.label}`).join(' · ')}
                    </p>
                </SummaryCard>
            )}
            {fights && (
                <SummaryCard title="Lutas do time" icon="fights">
                    <p className="summary-card-value">
                        <strong className="mono">{fights.length}</strong> <span>{fights.length === 1 ? 'luta' : 'lutas'}</span>
                    </p>
                    <p className="summary-card-line text-win">{plural(won, 'vencida', 'vencidas')} pelo seu time</p>
                    <p className="summary-card-line mono">você participou de {joined}</p>
                </SummaryCard>
            )}
        </div>
    );
}

// objectives comes from the API (null until the replay is read: then nothing shows). The whole log, in order.
function ObjectivesSection({ objectives, durationSeconds }) {
    if (!objectives) {
        return null;
    }

    return (
        <section className="card objectives-card" aria-labelledby="objectives-title">
            <div className="card-split-head">
                <h2 id="objectives-title" className="card-heading">
                    <TabIcon tab="flag" className="section-icon" />
                    Registro dos objetivos em ordem
                </h2>
                <span className="section-aside">Partida completa{durationSeconds ? ` (${formatDuration(durationSeconds)})` : ''}</span>
            </div>
            {objectives.length === 0
                ? <p className="quiet-note">Nenhuma torre, barracks ou Roshan caiu nesta partida.</p>
                : (
                    <ol className="objective-log" tabIndex={0} aria-label={`${objectives.length} objetivos, lista com rolagem`}>
                        {objectives.map((objective, index) => (
                            <li key={`${objective.seconds}-${index}`} className={`objective-entry kind-${objective.kind}`}>
                                <span className="objective-time mono">{formatDuration(objective.seconds)}</span>
                                <span className="objective-what">
                                    {objectiveFullName(objective)}
                                    {objective.aegisHeroName && <span className="text-you"> · Aegis: {objective.aegisHeroName}</span>}
                                    <span className="objective-killer">
                                        {objective.killerHeroName ? `Último golpe: ${objective.killerHeroName}` : 'Último golpe: creeps ou outra unidade'}
                                    </span>
                                </span>
                                <span className={`objective-taker mono ${objective.isPlayersTeam === false ? 'text-loss' : objective.isPlayersTeam ? 'text-win' : ''}`}>
                                    {takerName(objective)}{objective.denied ? ' (negada)' : ''}
                                </span>
                            </li>
                        ))}
                    </ol>
                )}
        </section>
    );
}

export default ObjectivesSection;
