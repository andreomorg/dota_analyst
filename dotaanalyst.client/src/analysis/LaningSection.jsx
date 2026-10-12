import { formatDuration, formatNumber, formatSignedNumber } from '../matches/format';

const LANE_LABELS = {
    top: 'Top (rota de cima)',
    middle: 'Mid (rota do meio)',
    bottom: 'Bot (rota de baixo)',
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
    won: { label: 'Ganhou a rota', className: 'result-win' },
    even: { label: 'Rota empatada', className: 'result-even' },
    lost: { label: 'Perdeu a rota', className: 'result-loss' },
};

// Short lines for when there is no laning data; statuses not listed here (notNeeded, or no
// replayStatus at all) show nothing.
const STATUS_TEXT = {
    expired: 'O replay desta partida não está mais disponível na Valve, por isso não há análise da fase de rotas.',
    failed: 'Não foi possível analisar o replay desta partida.',
    unavailable: 'A análise do replay está indisponível no momento.',
};

// Falls back to the raw key so a lane added later on the back-end still shows up.
function laneLabel(lane) {
    return LANE_LABELS[lane] ?? lane;
}

function PlayerRow({ player, isCurrentPlayer }) {
    return (
        <tr className={isCurrentPlayer ? 'current-player' : undefined}>
            <th scope="row">
                {player.heroName}
                {isCurrentPlayer && <>{' '}<span className="you-badge">Você</span></>}
            </th>
            <td>{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
            <td>{formatNumber(player.netWorth)}</td>
            <td>{formatNumber(player.experience)}</td>
            <td>{formatNumber(player.deaths)}</td>
        </tr>
    );
}

const COLUMN_COUNT = 5;

function GroupHeader({ title }) {
    return (
        <tr className="laning-group">
            <th scope="colgroup" colSpan={COLUMN_COUNT}>{title}</th>
        </tr>
    );
}

function LaningTable({ laning, time, isLane }) {
    const allies = laning.allies ?? [];
    const opponents = laning.opponents ?? [];
    // In the jungle or roaming there is usually only the player: no sides to label.
    const showOpponents = opponents.length > 0 || isLane;

    return (
        <div className="table-scroll">
            <table className="laning-table">
                <caption className="visually-hidden">
                    Last hits, denies, patrimônio, experiência e mortes aos {time}
                </caption>
                <thead>
                    <tr>
                        <th scope="col">Herói</th>
                        <th scope="col">LH / DN</th>
                        <th scope="col">Patrimônio</th>
                        <th scope="col">Experiência</th>
                        <th scope="col">Mortes</th>
                    </tr>
                </thead>
                <tbody>
                    {showOpponents && <GroupHeader title="Seu lado na rota" />}
                    <PlayerRow player={laning.player} isCurrentPlayer />
                    {allies.map(ally => <PlayerRow key={ally.heroId} player={ally} />)}
                </tbody>
                {showOpponents && (
                    <tbody>
                        <GroupHeader title="Adversários na rota" />
                        {opponents.length === 0
                            ? (
                                <tr>
                                    <td colSpan={COLUMN_COUNT}>Nenhum adversário nesta rota.</td>
                                </tr>
                            )
                            : opponents.map(opponent => <PlayerRow key={opponent.heroId} player={opponent} />)}
                    </tbody>
                )}
            </table>
        </div>
    );
}

function LaningSummary({ laning }) {
    const time = formatDuration(laning.endsAtSeconds);
    const outcome = OUTCOMES[laning.outcome];
    const isLane = LANES.includes(laning.lane);
    const noLaneText = NO_LANE_TEXT[laning.lane] ?? (isLane ? UNCONTESTED_LANE_TEXT : undefined);
    const hasDifference = outcome && laning.netWorthDifference !== null && laning.netWorthDifference !== undefined;

    return (
        <section className="laning-summary" aria-labelledby="laning-title">
            <h2 id="laning-title">Fase de rotas</h2>
            <p className="laning-intro">Como estava a sua rota aos {time} de jogo, segundo o replay.</p>

            <dl className="match-facts laning-facts">
                <div>
                    <dt>Rota</dt>
                    <dd>{laneLabel(laning.lane)}</dd>
                </div>
                {outcome && (
                    <div>
                        <dt>Resultado</dt>
                        <dd><span className={outcome.className}>{outcome.label}</span></dd>
                    </div>
                )}
                {hasDifference && (
                    <div>
                        <dt>Diferença de patrimônio</dt>
                        <dd>{formatSignedNumber(laning.netWorthDifference)}</dd>
                    </div>
                )}
            </dl>

            {hasDifference && (
                <p className="laning-note">
                    Patrimônio somado do seu lado na rota menos o dos adversários, aos {time}.
                </p>
            )}
            {!outcome && noLaneText && <p className="laning-note">{noLaneText}</p>}

            <LaningTable laning={laning} time={time} isLane={isLane} />
        </section>
    );
}

// replayStatus and laning come from the API (null/absent on older responses: then nothing shows).
// pollingStopped: the page gave up waiting while the replay was still "processing".
function LaningSection({ replayStatus, laning, pollingStopped }) {
    if (replayStatus === 'ready') {
        // Without a point-of-view player there is no laning; the page already explains why.
        return laning ? <LaningSummary laning={laning} /> : null;
    }

    if (replayStatus === 'processing') {
        return (
            <p className="notice" role="status">
                {pollingStopped
                    ? 'A análise do replay está demorando mais que o normal. Recarregue a página mais tarde para ver a fase de rotas.'
                    : 'Analisando o replay… A fase de rotas aparece aqui em instantes.'}
            </p>
        );
    }

    const text = STATUS_TEXT[replayStatus];
    return text ? <p className="replay-status-note">{text}</p> : null;
}

export default LaningSection;
