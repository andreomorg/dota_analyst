import { formatNumber } from '../matches/format';

function teamStatus(won) {
    if (won === null || won === undefined) {
        return null;
    }
    return won
        ? { label: 'Vencedor', className: 'result-win' }
        : { label: 'Perdedor', className: 'result-loss' };
}

function ItemsCell({ items, neutralItem }) {
    if (items.length === 0 && !neutralItem) {
        return '—';
    }
    return (
        <>
            {items.join(', ')}
            {neutralItem && <span className="neutral-item">{items.length > 0 ? ' · ' : ''}Neutro: {neutralItem}</span>}
        </>
    );
}

// won: true/false for this team, or null when the winner is unknown.
function TeamTable({ name, players, won }) {
    const status = teamStatus(won);

    return (
        <section className={`team ${won ? 'team-won' : ''}`}>
            <h2>
                {name}
                {status && <span className={`team-status ${status.className}`}> · {status.label}</span>}
            </h2>
            <div className="table-scroll">
                <table className="team-table">
                    <caption className="visually-hidden">
                        Jogadores do time {name}{status ? ` (${status.label.toLowerCase()})` : ''}
                    </caption>
                    <thead>
                        <tr>
                            <th scope="col">Herói</th>
                            <th scope="col">K / D / A</th>
                            <th scope="col">LH / DN</th>
                            <th scope="col">GPM / XPM</th>
                            <th scope="col">Patrimônio</th>
                            <th scope="col">Dano a heróis</th>
                            <th scope="col">Itens</th>
                        </tr>
                    </thead>
                    <tbody>
                        {players.map((player, index) => (
                            // accountId is null for private profiles, so the position is part of the key.
                            <tr key={`${index}-${player.heroId}`} className={player.isCurrentPlayer ? 'current-player' : undefined}>
                                <th scope="row">
                                    {player.heroName}
                                    {player.isCurrentPlayer && <>{' '}<span className="you-badge">Você</span></>}
                                    <span className="hero-level"> · nível {player.level}</span>
                                </th>
                                <td>{player.kills} / {player.deaths} / {player.assists}</td>
                                <td>{formatNumber(player.lastHits)} / {formatNumber(player.denies)}</td>
                                <td>{formatNumber(player.goldPerMinute)} / {formatNumber(player.experiencePerMinute)}</td>
                                <td>{formatNumber(player.netWorth)}</td>
                                <td>{formatNumber(player.heroDamage)}</td>
                                <td className="items-cell">
                                    <ItemsCell items={player.items} neutralItem={player.neutralItem} />
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </section>
    );
}

export default TeamTable;
