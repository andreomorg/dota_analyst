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

// The short form of the references: "à frente de 76 em cada 100" / "86 em cada 100 foram melhores".
export function standingPer100(share) {
    const ahead = toPercent(share);
    return ahead >= 50 ? `à frente de ${ahead} em cada 100` : `${100 - ahead} em cada 100 foram melhores`;
}

// The verdict without "da média" for tight spots ("Muito acima", "Na média").
export function shortLabel(level) {
    return level.label.replace(' da média', '');
}

// "Você foi melhor que 76% dos jogadores de Lich" / "86% dos jogadores de Lich foram melhores que você";
// who: "jogadores de Lich" (default "jogadores").
export function standingDetail(share, who = 'jogadores') {
    const ahead = toPercent(share);
    return ahead >= 50
        ? `Você foi melhor que ${ahead}% dos ${who}`
        : `${100 - ahead}% dos ${who} foram melhores que você`;
}
