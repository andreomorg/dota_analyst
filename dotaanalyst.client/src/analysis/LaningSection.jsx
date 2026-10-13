import { formatDuration, formatNumber, formatSignedNumber } from '../matches/format';
import { HeroIcon } from '../heroes/HeroImage';
import { YouChip } from '../ui/Chips';
import SectionHead from '../ui/SectionHead';

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

function PlayerRow({ player, isCurrentPlayer }) {
    return (
        <tr className={isCurrentPlayer ? 'is-you' : undefined}>
            <th scope="row">
                <span className="hero-cell">
                    <HeroIcon heroId={player.heroId} />
                    {player.heroName}
                    {isCurrentPlayer && <YouChip />}
                </span>
            </th>
            <td className="num-col mono">{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
            <td className="num-col mono">{formatNumber(player.netWorth)}</td>
            <td className="num-col mono">{formatNumber(player.experience)}</td>
            <td className="num-col mono">{formatNumber(player.deaths)}</td>
        </tr>
    );
}

const COLUMN_COUNT = 5;

function GroupHeader({ title }) {
    return (
        <tr className="group-row">
            <th scope="rowgroup" colSpan={COLUMN_COUNT}>{title}</th>
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
            <table className="data-table">
                <caption className="visually-hidden">
                    Last hits, denies, patrimônio, experiência e mortes aos {time}
                </caption>
                <thead>
                    <tr>
                        <th scope="col">Herói</th>
                        <th scope="col" className="num-col">LH / DN</th>
                        <th scope="col" className="num-col">Patrimônio</th>
                        <th scope="col" className="num-col">Experiência</th>
                        <th scope="col" className="num-col">Mortes</th>
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
        <section className="section" aria-labelledby="laning-title">
            <SectionHead id="laning-title" title="Fase de rotas">
                <span>Aos {time} de jogo, segundo o replay</span>
            </SectionHead>
            <div className="card">
                <dl className="fact-row">
                    <div className="tile">
                        <dt className="stat-label">Rota</dt>
                        <dd>{laneLabel(laning.lane)}</dd>
                    </div>
                    {outcome && (
                        <div className="tile">
                            <dt className="stat-label">Resultado</dt>
                            <dd className={outcome.className}>{outcome.label}</dd>
                        </div>
                    )}
                    {hasDifference && (
                        <div className="tile">
                            <dt className="stat-label">Diferença de patrimônio</dt>
                            <dd className={`mono ${laning.netWorthDifference >= 0 ? 'text-win' : 'text-loss'}`}>
                                {formatSignedNumber(laning.netWorthDifference)}
                            </dd>
                        </div>
                    )}
                </dl>

                {hasDifference && (
                    <p className="section-note">
                        Patrimônio somado do seu lado na rota menos o dos adversários, aos {time}.
                    </p>
                )}
                {!outcome && noLaneText && <p className="section-note">{noLaneText}</p>}

                <LaningTable laning={laning} time={time} isLane={isLane} />
            </div>
        </section>
    );
}

// The laning phase section; laning comes from the API (null without a point-of-view player or before the replay is
// read: then nothing shows).
function LaningSection({ laning }) {
    return laning ? <LaningSummary laning={laning} /> : null;
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
            <div className={`replay-status replay-status-processing${pollingStopped ? ' replay-status-delayed' : ''}`}>
                <span className="replay-status-indicator" aria-hidden="true" />
                <div>
                    <h2>Analisando o replay</h2>
                    <p>{processingText}</p>
                </div>
            </div>
        );
        announcement = processingText;
    } else if (STATUS_TEXT[replayStatus]) {
        content = (
            <div className={`replay-status replay-status-${replayStatus}`}>
                <div>
                    <h2>{STATUS_TITLE[replayStatus]}</h2>
                    <p>{STATUS_TEXT[replayStatus]}</p>
                </div>
            </div>
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
