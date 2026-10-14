import { formatDuration } from '../matches/format';
import { inflictorName } from './inflictors';
import TabIcon from './TabIcon';
import abilityImages from './abilityImages.json';

// The player's skill build from the replay (Stitch reference "Build"), as the usual grid: one row per ability (in
// the order first learned), one column per hero level, the cell marked where that level went. Talents share one row
// and are listed under the grid with the level and time they were picked. The order seen, not a recommended build.

const TALENT_PREFIX = 'special_bonus';

function rowKey(abilityName) {
    return abilityName.startsWith(TALENT_PREFIX) ? TALENT_PREFIX : abilityName;
}

// "special_bonus_attack_damage_25" -> "Attack Damage 25"; the hero's own talents ("special_bonus_unique_…") have no
// readable name without an ability catalog, so they are only called that.
function talentName(abilityName) {
    const rest = abilityName.slice(TALENT_PREFIX.length + 1);
    if (!rest || rest.startsWith('unique')) {
        return 'Talento próprio do herói';
    }
    return inflictorName(rest);
}

// abilityBuild comes from the API (null until the replay is read or without a point-of-view player).
function AbilityBuildSection({ abilityBuild, heroName, durationSeconds }) {
    if (!abilityBuild) {
        return null;
    }

    const { upgrades } = abilityBuild;
    const rows = [];
    upgrades.forEach(upgrade => {
        const key = rowKey(upgrade.abilityName);
        if (!rows.some(row => row.key === key)) {
            rows.push({
                key,
                label: key === TALENT_PREFIX ? 'Talentos' : inflictorName(upgrade.abilityName, heroName),
            });
        }
    });
    // Talents last, as in the game's tree.
    rows.sort((a, b) => (a.key === TALENT_PREFIX) - (b.key === TALENT_PREFIX));
    const talents = upgrades
        .map((upgrade, index) => ({ ...upgrade, heroLevel: index + 1 }))
        .filter(upgrade => upgrade.abilityName.startsWith(TALENT_PREFIX));

    return (
        <section className="card ability-card" aria-labelledby="ability-build-title">
            <div className="card-split-head">
                <div>
                    <h2 id="ability-build-title" className="card-heading">
                        <TabIcon tab="table" className="section-icon" />
                        Evolução das habilidades e talentos (níveis 1 a {upgrades.length})
                    </h2>
                    <p className="card-subtitle">
                        Em que nível do herói cada habilidade subiu{durationSeconds ? `, ao longo dos ${formatDuration(durationSeconds)} de jogo` : ''}, segundo o replay
                    </p>
                </div>
                <ul className="timeline-key">
                    <li><span className="key-box" aria-hidden="true" /> Habilidade</li>
                    <li><span className="key-box is-talent" aria-hidden="true" /> Talento</li>
                </ul>
            </div>
            {upgrades.length === 0
                ? <p className="quiet-note">O replay não registrou evoluções de habilidade para este jogador.</p>
                : (
                    <div className="table-scroll" tabIndex={0} role="region" aria-label="Evolução de habilidades, tabela com rolagem">
                        <table className="ability-grid">
                            <caption className="visually-hidden">
                                Habilidades por nível do herói; cada célula marcada diz o horário em que o nível foi escolhido.
                            </caption>
                            <thead>
                                <tr>
                                    <th scope="col">Habilidade</th>
                                    {upgrades.map((upgrade, index) => (
                                        <th key={index} scope="col" className={`mono${upgrade.abilityName.startsWith(TALENT_PREFIX) ? ' is-talent-level' : ''}`}>{index + 1}</th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {rows.map(row => (
                                    <tr key={row.key} className={row.key === TALENT_PREFIX ? 'talent-row' : undefined}>
                                        <th scope="row">
                                            <span className="ability-name">
                                                {abilityImages[row.key]
                                                    ? <img className="ability-art" src={abilityImages[row.key]} alt="" aria-hidden="true" loading="lazy" />
                                                    : <span className={`ability-art${row.key === TALENT_PREFIX ? ' is-talent' : ''}`} aria-hidden="true">{row.key === TALENT_PREFIX && <TabIcon tab="star" />}</span>}
                                                <span>{row.label}</span>
                                            </span>
                                        </th>
                                        {upgrades.map((upgrade, index) => (
                                            rowKey(upgrade.abilityName) === row.key
                                                ? (
                                                    <td key={index} title={`Nível ${index + 1} do herói, ${formatDuration(upgrade.seconds)}`}>
                                                        <span className={`ability-cell is-taken${row.key === TALENT_PREFIX ? ' is-talent' : ''}`}>
                                                            <span className="mono" aria-hidden="true">{row.key === TALENT_PREFIX ? '★' : upgrade.level}</span>
                                                            <span className="visually-hidden">
                                                                {row.key === TALENT_PREFIX ? `talento ${talentName(upgrade.abilityName)}` : `nível ${upgrade.level}`} aos {formatDuration(upgrade.seconds)}
                                                            </span>
                                                        </span>
                                                    </td>
                                                )
                                                : <td key={index}><span className="ability-cell" /></td>
                                        ))}
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            {talents.length > 0 && (
                <ul className="talent-list">
                    {talents.map(talent => (
                        <li key={talent.heroLevel}>
                            <span className="talent-level mono">{talent.heroLevel}</span>
                            <span>
                                <strong>{talentName(talent.abilityName)}</strong>
                                <span className="talent-time mono"> aos {formatDuration(talent.seconds)}</span>
                            </span>
                        </li>
                    ))}
                </ul>
            )}
            <p className="section-note">
                Número na célula: o nível da habilidade; passe o mouse para ver o horário. É a ordem desta partida, não uma
                recomendação de build. Nomes de talentos pelo nome interno do jogo.
            </p>
        </section>
    );
}

export default AbilityBuildSection;
