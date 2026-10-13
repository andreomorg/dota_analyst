import { formatDuration } from '../matches/format';
import SectionHead from '../ui/SectionHead';
import { inflictorName } from './inflictors';

// The player's skill build from the replay, as the usual grid: one row per ability (in the order first learned), one
// column per hero level, the cell marked where that level went. Talents share one row.

const TALENT_PREFIX = 'special_bonus';

function rowKey(abilityName) {
    return abilityName.startsWith(TALENT_PREFIX) ? TALENT_PREFIX : abilityName;
}

// abilityBuild comes from the API (null until the replay is read or without a point-of-view player).
function AbilityBuildSection({ abilityBuild, heroName }) {
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

    return (
        <section className="section" aria-labelledby="ability-build-title">
            <SectionHead id="ability-build-title" title="Habilidades">
                <span>Em que nível do herói cada habilidade subiu, segundo o replay</span>
            </SectionHead>
            <div className="card">
                {upgrades.length === 0
                    ? <p className="section-note">O replay não registrou evoluções de habilidade para este jogador.</p>
                    : (
                        <div className="table-scroll">
                            <table className="ability-grid">
                                <caption className="visually-hidden">
                                    Habilidades por nível do herói; cada célula marcada diz o horário em que o nível foi escolhido.
                                </caption>
                                <thead>
                                    <tr>
                                        <th scope="col">Habilidade</th>
                                        {upgrades.map((_, index) => <th key={index} scope="col" className="mono">{index + 1}</th>)}
                                    </tr>
                                </thead>
                                <tbody>
                                    {rows.map(row => (
                                        <tr key={row.key}>
                                            <th scope="row">{row.label}</th>
                                            {upgrades.map((upgrade, index) => (
                                                rowKey(upgrade.abilityName) === row.key
                                                    ? (
                                                        <td
                                                            key={index}
                                                            className={`ability-cell is-taken${row.key === TALENT_PREFIX ? ' is-talent' : ''}`}
                                                            title={`Nível ${index + 1} do herói, ${formatDuration(upgrade.seconds)}`}
                                                        >
                                                            <span className="mono">{row.key === TALENT_PREFIX ? 'T' : upgrade.level}</span>
                                                            <span className="visually-hidden">
                                                                {' '}aos {formatDuration(upgrade.seconds)}
                                                            </span>
                                                        </td>
                                                    )
                                                    : <td key={index} className="ability-cell" />
                                            ))}
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                <p className="section-note">
                    Número na célula: o nível da habilidade; passe o mouse para ver o horário. É a ordem desta partida,
                    não uma recomendação de build.
                </p>
            </div>
        </section>
    );
}

export default AbilityBuildSection;
