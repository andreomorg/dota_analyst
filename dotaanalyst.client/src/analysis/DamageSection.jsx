import { formatNumber } from '../matches/format';
import { inflictorName } from './inflictors';

// The player's damage to and from enemy heroes, read from the replay: one measure per list, horizontal bars with
// the value written next to them (the bar only helps to compare).

const TOP_INFLICTORS = 8;

function sum(rows) {
    return rows.reduce((total, row) => total + row.amount, 0);
}

function BarList({ title, rows, max, labelOf, emptyText }) {
    return (
        <div className="bar-group">
            <h3>{title}</h3>
            {rows.length === 0
                ? <p className="laning-note">{emptyText}</p>
                : (
                    <ol className="bar-list">
                        {/* Index keys: the list never reorders, and two heroes can share "Unknown hero". */}
                        {rows.map((row, index) => (
                            <li key={index} className="bar-row">
                                <span className="bar-label">{labelOf(row)}</span>
                                <span className="bar-track" aria-hidden="true">
                                    <span className="bar-fill" style={{ width: `${max > 0 ? (row.amount / max) * 100 : 0}%` }} />
                                </span>
                                <span className="bar-value">{formatNumber(row.amount)}</span>
                            </li>
                        ))}
                    </ol>
                )}
        </div>
    );
}

// The biggest inflictors, then the rest added up.
function topInflictors(rows, heroName) {
    const named = rows.map(row => ({ label: inflictorName(row.inflictor, heroName), amount: row.amount }));
    if (named.length <= TOP_INFLICTORS) {
        return named;
    }

    const rest = named.slice(TOP_INFLICTORS - 1);
    return [...named.slice(0, TOP_INFLICTORS - 1), { label: `Outras (${rest.length})`, amount: sum(rest) }];
}

// damage comes from the API (null until the replay is read, or without a point-of-view player: then nothing shows).
function DamageSection({ damage, heroName }) {
    if (!damage) {
        return null;
    }

    const dealt = sum(damage.toHeroes);
    const taken = sum(damage.fromHeroes);
    // The same scale for dealt and taken, so the two lists can be compared.
    const heroMax = Math.max(0, ...damage.toHeroes.map(row => row.amount), ...damage.fromHeroes.map(row => row.amount));
    const inflictors = topInflictors(damage.byInflictor, heroName);

    return (
        <section className="damage-section" aria-labelledby="damage-title">
            <h2 id="damage-title">Dano em heróis</h2>
            <p className="laning-intro">
                Você causou {formatNumber(dealt)} de dano em heróis inimigos e recebeu {formatNumber(taken)} deles
                (a partida inteira, com ilusões e invocações contando para o dono).
            </p>
            <div className="bar-groups">
                <BarList
                    title="Causado em cada herói"
                    rows={damage.toHeroes}
                    max={heroMax}
                    labelOf={row => row.heroName}
                    emptyText="Você não causou dano em heróis inimigos."
                />
                <BarList
                    title="Recebido de cada herói"
                    rows={damage.fromHeroes}
                    max={heroMax}
                    labelOf={row => row.heroName}
                    emptyText="Você não recebeu dano de heróis inimigos."
                />
            </div>
            {inflictors.length > 0 && (
                <BarList
                    title="Causado por habilidade e item"
                    rows={inflictors}
                    max={inflictors.reduce((max, row) => Math.max(max, row.amount), 0)}
                    labelOf={row => row.label}
                />
            )}
        </section>
    );
}

export default DamageSection;
