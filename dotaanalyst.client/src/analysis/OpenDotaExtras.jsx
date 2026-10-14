import { formatNumber } from '../matches/format';
import { inflictorName } from './inflictors';
import { hasOpenDotaExtras } from './externalExtrasVisibility';
function Totals({ title, rows }) {
    if (rows == null) return null;
    return <table><caption>{title}</caption><tbody>{rows.length ? rows.map(row => <tr key={row.key}>
        <th scope="row">{row.name === row.key ? inflictorName(row.key) : row.name}</th><td>{formatNumber(row.amount)}</td>
    </tr>) : <tr><td colSpan={2}>Sem eventos registrados.</td></tr>}</tbody></table>;
}
export default function OpenDotaExtras({ extras, section }) {
    if (!hasOpenDotaExtras(extras, section)) return null;
    const id = `opendota-extras-${section}`;
    const uses = [['ability', extras.abilityUses], ['item', extras.itemUses]].flatMap(([kind, rows]) =>
        (rows ?? []).map(row => ({ ...row, key: `${kind}:${row.key}`,
            name: row.name === row.key ? inflictorName(row.key) : row.name })));
    const economy = [
        ...(extras.actionsPerMinute == null ? [] : [{ key: 'apm', name: 'Ações por minuto', amount: extras.actionsPerMinute }]),
        ...(extras.goldSources ?? []).map(row => ({ ...row, key: `gold:${row.key}`, name: `Origem do ouro: código ${row.key}` })),
        ...(extras.experienceSources ?? []).map(row => ({ ...row, key: `xp:${row.key}`, name: `Origem da XP: código ${row.key}` })),
    ];
    return <section className="card external-data opendota-extras" aria-labelledby={id}>
        <h3 id={id}>Dados complementares do OpenDota</h3>
        <p className="section-note">Totais registrados pelo OpenDota; o recorte pode diferir do nosso replay.</p>
        {section === 'combat' ? <Totals title="Dano recebido por habilidade ou item" rows={extras.damageReceived} /> : <>
            <Totals title="Usos de habilidades e itens" rows={extras.abilityUses == null && extras.itemUses == null ? null : uses} />
            <Totals title="Ações e economia" rows={extras.actionsPerMinute == null && extras.goldSources == null && extras.experienceSources == null ? null : economy} />
            {extras.abilityUses == null && <p className="visually-hidden">Contagens de habilidades indisponíveis.</p>}
            {extras.itemUses == null && <p className="visually-hidden">Contagens de itens indisponíveis.</p>}
        </>}
    </section>;
}
