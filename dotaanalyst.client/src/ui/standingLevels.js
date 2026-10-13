import { toPercent } from '../matches/format';

// How the player did against other players, from a share (0..1 = the part of the others the player did better than;
// the API already turns "less is better" metrics around): a verdict in words and "how many in 100".

export const LEVELS = [
    { min: 0.8, label: 'Muito acima da média', tone: 'great' },
    { min: 0.6, label: 'Acima da média', tone: 'good' },
    { min: 0.4, label: 'Na média', tone: 'even' },
    { min: 0.2, label: 'Abaixo da média', tone: 'bad' },
    { min: -Infinity, label: 'Muito abaixo da média', tone: 'awful' },
];

export function standingOf(share) {
    return LEVELS.find(level => share >= level.min);
}

// "à frente de 76 em cada 100" / "87 em cada 100 foram melhores"; who: "jogadores de Lich" (optional).
export function standingDetail(share, who) {
    const ahead = toPercent(share);
    const people = who ? ` ${who}` : '';
    return ahead >= 50
        ? `à frente de ${ahead} em cada 100${people}`
        : `${100 - ahead} em cada 100${people} foram melhores`;
}
