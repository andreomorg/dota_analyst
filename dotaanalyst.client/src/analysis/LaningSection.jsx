import { formatDuration, formatNumber, formatSignedNumber } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';

const LANE_LABELS = {
    top: 'Rota de cima',
    middle: 'Rota do meio',
    bottom: 'Rota de baixo',
    jungle: 'Selva',
    roaming: 'Roaming',
};

// The API sends no outcome for jungle, roaming or a lane no enemy hero stayed in.
const NO_LANE_TEXT = {
    jungle: 'Você ficou na selva no começo da partida, então não há disputa de rota para comparar.',
    roaming: 'Você rodou pelo mapa (roaming) no começo da partida, então não há disputa de rota para comparar.',
};
const UNCONTESTED_LANE_TEXT = 'Nenhum herói inimigo ficou na sua rota, então não há disputa de rota para comparar.';
const LANES = ['top', 'middle', 'bottom'];

// The text always says the result; the color only reinforces it.
const OUTCOMES = {
    won: { label: 'Ganhou a rota', className: 'text-win' },
    even: { label: 'Rota empatada', className: 'text-even' },
    lost: { label: 'Perdeu a rota', className: 'text-loss' },
};

// Short lines for when there is no laning data; statuses not listed here (notNeeded, or no
// replayStatus at all) show nothing.
const STATUS_TEXT = {
    expired: 'O replay desta partida não está mais disponível na Valve, por isso não há fase de rotas, mapas nem lutas.',
    failed: 'Não foi possível analisar o replay desta partida.',
    unavailable: 'A análise do replay está indisponível no momento.',
};
const STATUS_TITLE = {
    expired: 'Replay indisponível',
    failed: 'Não foi possível analisar o replay',
    unavailable: 'Análise do replay indisponível',
};

// Falls back to the raw key so a lane added later on the back-end still shows up.
function laneLabel(lane) {
    return LANE_LABELS[lane] ?? lane;
}

// side: 'ally' (green numbers for the lane's leader on your side) or 'enemy' (red).
function PlayerRow({ player, isCurrentPlayer, side, lead }) {
    return (
        <tr className={isCurrentPlayer ? 'is-you' : undefined}>
            <th scope="row">
                <span className="hero-cell">
                    <HeroIcon heroId={player.heroId} />
                    {player.heroName}
                    {isCurrentPlayer && <span className="tag-you">Você</span>}
                </span>
            </th>
            <td className={`num-col mono${lead ? ` lane-lead lane-${side}` : ''}`}>{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
            <td className={`num-col mono lane-${side}`}>{formatNumber(player.netWorth)}</td>
            <td className="num-col mono">{formatNumber(player.experience)} XP</td>
            <td className={`num-col mono${player.deaths > 0 ? ' text-loss' : ''}`}>{formatNumber(player.deaths)}</td>
        </tr>
    );
}

const COLUMN_COUNT = 5;

function GroupHeader({ title, side }) {
    return (
        <tr className={`group-row group-${side}`}>
            <th scope="rowgroup" colSpan={COLUMN_COUNT}>{title}</th>
        </tr>
    );
}

// The hero of a side with the most last hits (its numbers stand out, as in the reference).
function leaderOf(players) {
    return players.reduce((best, hero) => (hero.lastHits > (best?.lastHits ?? -1) ? hero : best), null)?.heroId;
}

function LaningTable({ laning, time, isLane, ourTeam, theirTeam }) {
    const allies = laning.allies ?? [];
    const opponents = laning.opponents ?? [];
    // In the jungle or roaming there is usually only the player: no sides to label.
    const showOpponents = opponents.length > 0 || isLane;
    const ourLeader = leaderOf([laning.player, ...allies]);
    const theirLeader = leaderOf(opponents);

    return (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Jogadores da rota, tabela com rolagem">
            <table className="data-table lane-table">
                <caption className="visually-hidden">
                    Last hits, denies, patrimônio, experiência e mortes aos {time}
                </caption>
                <thead>
                    <tr>
                        <th scope="col">Herói</th>
                        <th scope="col" className="num-col">LH / denies</th>
                        <th scope="col" className="num-col">Patrimônio</th>
                        <th scope="col" className="num-col">Experiência</th>
                        <th scope="col" className="num-col">Mortes na rota</th>
                    </tr>
                </thead>
                <tbody>
                    {showOpponents && <GroupHeader title={`Seu lado (${ourTeam})`} side="ally" />}
                    <PlayerRow player={laning.player} isCurrentPlayer side="ally" lead={ourLeader === laning.player.heroId} />
                    {allies.map(ally => <PlayerRow key={ally.heroId} player={ally} side="ally" lead={ourLeader === ally.heroId} />)}
                </tbody>
                {showOpponents && (
                    <tbody>
                        <GroupHeader title={`Adversários (${theirTeam})`} side="enemy" />
                        {opponents.length === 0
                            ? (
                                <tr>
                                    <td colSpan={COLUMN_COUNT}>Nenhum adversário nesta rota.</td>
                                </tr>
                            )
                            : opponents.map(opponent => <PlayerRow key={opponent.heroId} player={opponent} side="enemy" lead={theirLeader === opponent.heroId} />)}
                    </tbody>
                )}
            </table>
        </div>
    );
}

function LaningSummary({ laning, isRadiant }) {
    const time = formatDuration(laning.endsAtSeconds);
    const outcome = OUTCOMES[laning.outcome];
    const isLane = LANES.includes(laning.lane);
    const noLaneText = NO_LANE_TEXT[laning.lane] ?? (isLane ? UNCONTESTED_LANE_TEXT : undefined);
    const hasDifference = outcome && laning.netWorthDifference !== null && laning.netWorthDifference !== undefined;
    const ourTeam = isRadiant === false ? 'Dire' : 'Radiant';
    const theirTeam = isRadiant === false ? 'Radiant' : 'Dire';

    return (
        <section className="card lane-card" aria-labelledby="laning-title">
            <div className="lane-card-head">
                <div>
                    <p className="kicker">{isLane ? `Confronto da ${laneLabel(laning.lane).toLowerCase()}` : `Fase de rotas · ${laneLabel(laning.lane)}`}</p>
                    <h2 id="laning-title" className="lane-card-title">
                        {laning.opponents.length > 0 ? `${ourTeam} × ${theirTeam}` : 'Sua rota'} (0:00 – {time})
                    </h2>
                </div>
                {outcome && (
                    <dl className="lane-result">
                        <div>
                            <dt className="mono">Resultado da rota</dt>
                            <dd className={outcome.className}>{outcome.label}</dd>
                        </div>
                        {hasDifference && (
                            <div>
                                <dt className="mono">Diferença de patrimônio ({time})</dt>
                                <dd className={`mono ${laning.netWorthDifference >= 0 ? 'text-win' : 'text-loss'}`}>
                                    {formatSignedNumber(laning.netWorthDifference)}
                                </dd>
                            </div>
                        )}
                    </dl>
                )}
            </div>

            {!outcome && noLaneText && <p className="section-note">{noLaneText}</p>}

            <LaningTable laning={laning} time={time} isLane={isLane} ourTeam={ourTeam} theirTeam={theirTeam} />
            {hasDifference && (
                <p className="section-note">
                    Diferença = patrimônio somado do seu lado na rota menos o dos adversários, aos {time}, segundo o replay.
                </p>
            )}
        </section>
    );
}

// The laning phase section; laning comes from the API (null without a point-of-view player or before the replay is
// read: then nothing shows). isRadiant: the point-of-view player's team (names the sides).
function LaningSection({ laning, isRadiant }) {
    return laning ? <LaningSummary laning={laning} isRadiant={isRadiant} /> : null;
}

const REPLAY_ICONS = {
    clock: <><circle cx="12" cy="12" r="10" /><path d="M12 6v6l4 2" /></>,
    expired: <><circle cx="12" cy="12" r="10" /><path d="M4.9 4.9l14.2 14.2" /></>,
    unavailable: <path d="M1 1l22 22M16.7 11.1A10.9 10.9 0 0 1 19 12.5M5 12.6a10.9 10.9 0 0 1 5.2-2.5M10.7 5.1A16 16 0 0 1 22.6 9M1.4 9a15.9 15.9 0 0 1 4.7-2.9M8.5 16.1a5 5 0 0 1 7 0M12 20h.01" />,
    failed: <><rect x="3" y="3" width="18" height="18" rx="2" /><path d="M9 9l6 6M15 9l-6 6" /></>,
};

function ReplayIcon({ kind }) {
    return <svg className="replay-status-icon" viewBox="0 0 24 24" aria-hidden="true">{REPLAY_ICONS[kind]}</svg>;
}

// Where the reading of the replay stands, as a banner at the top of the analysis (nothing once it is ready).
// pollingStopped: the page gave up waiting while the replay was still "processing".
export function ReplayStatusBanner({ replayStatus, pollingStopped }) {
    const processingText = pollingStopped
        ? 'A análise do replay está demorando mais que o normal. Recarregue a página mais tarde para ver mapas, lutas e dicas.'
        : 'Analisando o replay… Fase de rotas, mapas, lutas e dicas aparecem aqui em instantes.';

    let content = null;
    let announcement = '';
    if (replayStatus === 'ready') {
        announcement = 'A análise do replay está pronta.';
    } else if (replayStatus === 'processing') {
        content = (
            <div className={`replay-status replay-status-${pollingStopped ? 'delayed' : 'processing'}`}>
                {pollingStopped
                    ? <ReplayIcon kind="clock" />
                    : <span className="replay-status-indicator" aria-hidden="true" />}
                <p><strong>{pollingStopped ? 'O replay está demorando.' : 'Analisando o replay.'}</strong> {processingText}</p>
            </div>
        );
        announcement = processingText;
    } else if (STATUS_TEXT[replayStatus]) {
        content = (
            <>
                <div className={`replay-status replay-status-${replayStatus}`}>
                    <ReplayIcon kind={replayStatus} />
                    <p><strong>{STATUS_TITLE[replayStatus]}.</strong> {STATUS_TEXT[replayStatus]} O placar e os números do OpenDota continuam disponíveis.</p>
                </div>
                {replayStatus === 'expired' && (
                    <p className="replay-status-strip mono">
                        <span>Dados disponíveis sem o replay</span>
                        <span className="text-win">Placar · itens finais · GPM/XPM · percentis do OpenDota</span>
                    </p>
                )}
            </>
        );
        announcement = STATUS_TEXT[replayStatus];
    }

    // The live region stays mounted while the status changes (processing → ready), so screen readers
    // announce the change; a region that appears together with its text is often not announced.
    return (
        <>
            <p className="visually-hidden" aria-live="polite">{announcement}</p>
            {content}
        </>
    );
}

export default LaningSection;
